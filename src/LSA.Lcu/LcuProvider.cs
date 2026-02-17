using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using LSA.Core;
using LSA.Data.Models;
using Microsoft.Extensions.Logging;

namespace LSA.Lcu;

/// <summary>
/// LCU 연결 정보 모델
/// </summary>
public class LcuConnectionInfo
{
    public int Port { get; set; }
    public string Password { get; set; } = ""; // 메모리에만 보관, 로그/디스크 저장 금지
    public string Protocol { get; set; } = "https";
}

/// <summary>
/// Real LCU 연결 Provider — lockfile 기반 REST API + WebSocket WAMP
/// ⚠️ POST/PATCH/DELETE 엔드포인트는 절대 사용하지 않음
/// 
/// Phase 2: WebSocket 실시간 이벤트 + 자동 재연결 (지수 백오프)
/// </summary>
public class LcuProvider : IGameStateProvider
{
    private readonly ILogger<LcuProvider> _logger;
    private HttpClient? _httpClient;
    private ClientWebSocket? _webSocket;
    private LcuConnectionInfo? _connInfo;
    private bool _isConnected;
    private bool _isWebSocketConnected;
    private CancellationTokenSource? _monitorCts;

    // 자동 재연결 설정
    private const int RECONNECT_BASE_DELAY_MS = 2000;
    private const int RECONNECT_MAX_DELAY_MS = 30000;
    private const int PROCESS_POLL_INTERVAL_MS = 5000;
    private int _reconnectAttempt;

    // 마지막 알려진 상태 (이벤트 중복 방지용)
    private GamePhase _lastKnownPhase = GamePhase.None;
    private int? _lastKnownChampionId;

    public bool IsConnected => _isConnected;
    public bool IsWebSocketConnected => _isWebSocketConnected;
    public string ProviderName => _isWebSocketConnected ? "LCU (WS)" : "LCU (REST)";

    // Phase 2 이벤트
    public event Action<GamePhase>? OnPhaseChanged;
    public event Action<int?>? OnChampionChanged;
    public event Action<bool>? OnConnectionChanged;

    public LcuProvider(ILogger<LcuProvider> logger)
    {
        _logger = logger;
    }

    // ===================================================================
    // 연결 관리
    // ===================================================================

    /// <summary>
    /// LCU 연결 시도 — lockfile 탐색 → HttpClient + WebSocket 구성
    /// </summary>
    public async Task<bool> TryConnectAsync()
    {
        try
        {
            _connInfo = await FindLcuConnectionAsync();
            if (_connInfo == null)
            {
                _logger.LogWarning("LCU lockfile을 찾을 수 없음");
                _isConnected = false;
                return false;
            }

            // REST HttpClient 생성 (Self-signed 인증서 허용)
            SetupHttpClient();

            // 연결 확인 — phase 조회 테스트
            var phase = await GetPhaseAsync();
            _isConnected = true;
            _reconnectAttempt = 0;
            _logger.LogInformation("LCU REST 연결 성공 — 현재 Phase: {Phase}", phase);

            OnConnectionChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LCU 연결 실패");
            _isConnected = false;
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        await StopMonitoringAsync();

        _httpClient?.Dispose();
        _httpClient = null;
        _connInfo = null;
        _isConnected = false;
        _isWebSocketConnected = false;

        _logger.LogInformation("LCU 연결 해제");
        OnConnectionChanged?.Invoke(false);
    }

    /// <summary>
    /// HttpClient 설정 — Basic Auth (password는 로그 기록 금지)
    /// </summary>
    private void SetupHttpClient()
    {
        if (_connInfo == null) return;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (request, _, _, errors) => IsAllowedLcuTlsRequest(request?.RequestUri, errors)
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri($"{_connInfo.Protocol}://127.0.0.1:{_connInfo.Port}"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        var authBytes = Encoding.ASCII.GetBytes($"riot:{_connInfo.Password}");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
    }


    private static bool IsAllowedLcuTlsRequest(Uri? requestUri, SslPolicyErrors errors)
    {
        var isLoopback = requestUri != null &&
                         (requestUri.IsLoopback ||
                          string.Equals(requestUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(requestUri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

        if (!isLoopback)
            return false;

        return errors == SslPolicyErrors.None || errors == SslPolicyErrors.RemoteCertificateChainErrors;
    }

    private static bool IsAllowedLcuTlsForWebSocket(Uri? requestUri, SslPolicyErrors errors)
    {
        // WebSocket도 REST와 동일하게 loopback만 허용
        var isLoopback = requestUri != null &&
                         (requestUri.IsLoopback ||
                          string.Equals(requestUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(requestUri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

        if (!isLoopback)
            return false;

        return errors == SslPolicyErrors.None || errors == SslPolicyErrors.RemoteCertificateChainErrors;
    }

    // ===================================================================
    // Phase 2: 실시간 모니터링 (WebSocket + 프로세스 감시)
    // ===================================================================

    /// <summary>
    /// 모니터링 시작 — WebSocket 연결 시도 + 프로세스 감시 루프
    /// </summary>
    public async Task StartMonitoringAsync(CancellationToken ct = default)
    {
        _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _monitorCts.Token;

        // WebSocket 연결 시도 (실패해도 REST fallback으로 계속)
        await TryConnectWebSocketAsync(token);

        // 백그라운드 모니터링 루프 시작
        _ = Task.Run(() => MonitoringLoopAsync(token), token);
    }

    /// <summary>
    /// 모니터링 중지 + 리소스 해제
    /// </summary>
    public async Task StopMonitoringAsync()
    {
        _monitorCts?.Cancel();

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "앱 종료",
                        CancellationToken.None);
                }
            }
            catch { }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
                _isWebSocketConnected = false;
            }
        }

        _monitorCts?.Dispose();
        _monitorCts = null;
    }

    /// <summary>
    /// WebSocket 연결 시도 — LCU WAMP 프로토콜
    /// wss://riot:{password}@127.0.0.1:{port}/
    /// </summary>
    private async Task<bool> TryConnectWebSocketAsync(CancellationToken ct)
    {
        if (_connInfo == null) return false;

        try
        {
            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();

            // Loopback 대상 + self-signed 체인 오류만 허용
            var wsUri = new Uri($"wss://127.0.0.1:{_connInfo.Port}/");
            _webSocket.Options.RemoteCertificateValidationCallback = (_, _, _, errors) =>
                IsAllowedLcuTlsForWebSocket(wsUri, errors);

            // Basic Auth 헤더 설정 (password는 로그 기록 금지)
            var authBytes = Encoding.ASCII.GetBytes($"riot:{_connInfo.Password}");
            _webSocket.Options.SetRequestHeader("Authorization",
                $"Basic {Convert.ToBase64String(authBytes)}");

            await _webSocket.ConnectAsync(wsUri, ct);

            // WAMP Subscribe — Phase 변경 이벤트
            await WampSubscribeAsync("OnJsonApiEvent_lol-gameflow_v1_gameflow-phase", ct);

            // WAMP Subscribe — ChampSelect 세션 변경 이벤트
            await WampSubscribeAsync("OnJsonApiEvent_lol-champ-select_v1_session", ct);

            _isWebSocketConnected = true;
            _reconnectAttempt = 0;
            _logger.LogInformation("LCU WebSocket 연결 성공");

            // 메시지 수신 루프 시작
            _ = Task.Run(() => WebSocketReceiveLoopAsync(ct), ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("LCU WebSocket 연결 실패 (REST fallback 사용): {Msg}", ex.Message);
            _isWebSocketConnected = false;
            _webSocket?.Dispose();
            _webSocket = null;
            return false;
        }
    }

    /// <summary>
    /// WAMP Subscribe 메시지 전송 — [5, "topic"]
    /// </summary>
    private async Task WampSubscribeAsync(string topic, CancellationToken ct)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        var msg = $"[5, \"{topic}\"]";
        var bytes = Encoding.UTF8.GetBytes(msg);
        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true, ct);

        _logger.LogDebug("WAMP 구독: {Topic}", topic);
    }

    /// <summary>
    /// WebSocket 메시지 수신 루프 — WAMP 이벤트 파싱
    /// </summary>
    private async Task WebSocketReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        try
        {
            while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("LCU WebSocket 서버에서 종료");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessWampMessage(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상 종료
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning("WebSocket 수신 오류: {Msg}", ex.Message);
        }
        finally
        {
            _isWebSocketConnected = false;

            if (!ct.IsCancellationRequested)
            {
                _logger.LogInformation("WebSocket 연결 끊김 — 재연결 시도 예정");
            }
        }
    }

    /// <summary>
    /// WAMP 이벤트 메시지 파싱 — [8, "topic", { data }]
    /// </summary>
    private void ProcessWampMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 3)
                return;

            var opcode = root[0].GetInt32();
            if (opcode != 8) return; // 8 = WAMP EVENT

            var topic = root[1].GetString() ?? "";
            var payload = root[2];

            switch (topic)
            {
                case "OnJsonApiEvent_lol-gameflow_v1_gameflow-phase":
                    HandlePhaseEvent(payload);
                    break;

                case "OnJsonApiEvent_lol-champ-select_v1_session":
                    HandleChampSelectEvent(payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("WAMP 메시지 파싱 실패 (무시): {Msg}", ex.Message);
        }
    }

    /// <summary>
    /// Phase 변경 이벤트 처리 — { "data": "ChampSelect" }
    /// </summary>
    private void HandlePhaseEvent(JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var dataProp))
            return;

        var phaseStr = dataProp.GetString()?.Trim('"') ?? "";
        var phase = ParsePhaseString(phaseStr);

        if (phase != _lastKnownPhase)
        {
            _lastKnownPhase = phase;
            _logger.LogInformation("[WS] Phase 변경 → {Phase}", phase);
            OnPhaseChanged?.Invoke(phase);
        }
    }

    /// <summary>
    /// ChampSelect 세션 변경 이벤트 처리 — 챔피언 ID 추출
    /// </summary>
    private void HandleChampSelectEvent(JsonElement payload)
    {
        try
        {
            if (!payload.TryGetProperty("data", out var data))
                return;

            if (!data.TryGetProperty("localPlayerCellId", out var cellIdProp))
                return;

            var myCellId = cellIdProp.GetInt64();

            if (!data.TryGetProperty("myTeam", out var myTeam))
                return;

            foreach (var player in myTeam.EnumerateArray())
            {
                if (player.TryGetProperty("cellId", out var cid) && cid.GetInt64() == myCellId)
                {
                    if (player.TryGetProperty("championId", out var champId))
                    {
                        var id = champId.GetInt32();
                        var champValue = id > 0 ? id : (int?)null;

                        if (champValue != _lastKnownChampionId)
                        {
                            _lastKnownChampionId = champValue;
                            _logger.LogInformation("[WS] 챔피언 변경 → {ChampId}", champValue);
                            OnChampionChanged?.Invoke(champValue);
                        }
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("ChampSelect 이벤트 파싱 실패: {Msg}", ex.Message);
        }
    }

    /// <summary>
    /// 백그라운드 모니터링 루프 — 프로세스 감시 + 자동 재연결
    /// </summary>
    private async Task MonitoringLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1) LCU 프로세스 생존 확인
                var lcuRunning = IsLcuProcessRunning();

                if (!lcuRunning && _isConnected)
                {
                    // 클라이언트 종료됨 — 연결 해제
                    _logger.LogInformation("LeagueClientUx 프로세스 종료 감지");
                    _isConnected = false;
                    _isWebSocketConnected = false;
                    _webSocket?.Dispose();
                    _webSocket = null;
                    _httpClient?.Dispose();
                    _httpClient = null;
                    _connInfo = null;
                    _lastKnownPhase = GamePhase.None;
                    _lastKnownChampionId = null;

                    OnPhaseChanged?.Invoke(GamePhase.None);
                    OnConnectionChanged?.Invoke(false);
                }
                else if (lcuRunning && !_isConnected)
                {
                    // 클라이언트 시작됨 — 재연결 시도
                    _logger.LogInformation("LeagueClientUx 프로세스 감지 — 재연결 시도");
                    await AttemptReconnectAsync(ct);
                }
                else if (_isConnected && !_isWebSocketConnected)
                {
                    // REST는 살아있지만 WebSocket 끊김 → WebSocket 재연결
                    await TryConnectWebSocketAsync(ct);
                }

                // 2) 폴링 간격 대기
                var delay = _isConnected ? PROCESS_POLL_INTERVAL_MS : GetReconnectDelay();
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("모니터링 루프 오류: {Msg}", ex.Message);
                await Task.Delay(PROCESS_POLL_INTERVAL_MS, ct);
            }
        }
    }

    /// <summary>
    /// 자동 재연결 — REST + WebSocket
    /// </summary>
    private async Task AttemptReconnectAsync(CancellationToken ct)
    {
        _reconnectAttempt++;
        _logger.LogInformation("재연결 시도 #{Attempt}", _reconnectAttempt);

        try
        {
            _connInfo = await FindLcuConnectionAsync();
            if (_connInfo == null) return;

            SetupHttpClient();

            // REST 연결 확인
            var phase = await GetPhaseAsync();
            _isConnected = true;
            _reconnectAttempt = 0;
            _lastKnownPhase = phase;

            _logger.LogInformation("LCU 재연결 성공 — Phase: {Phase}", phase);
            OnConnectionChanged?.Invoke(true);
            OnPhaseChanged?.Invoke(phase);

            // WebSocket 재연결 시도
            await TryConnectWebSocketAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("재연결 실패 #{Attempt}: {Msg}", _reconnectAttempt, ex.Message);
        }
    }

    /// <summary>
    /// 지수 백오프 딜레이 계산 (2s → 4s → 8s → max 30s)
    /// </summary>
    private int GetReconnectDelay()
    {
        var delay = RECONNECT_BASE_DELAY_MS * (int)Math.Pow(2, Math.Min(_reconnectAttempt, 5));
        return Math.Min(delay, RECONNECT_MAX_DELAY_MS);
    }

    /// <summary>
    /// LeagueClientUx 프로세스 실행 여부 확인
    /// </summary>
    private bool IsLcuProcessRunning()
    {
        try
        {
            return Process.GetProcessesByName("LeagueClientUx").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    // ===================================================================
    // REST API 조회 (기존 — 변경 없음)
    // ===================================================================

    /// <summary>
    /// GET /lol-gameflow/v1/gameflow-phase → GamePhase
    /// </summary>
    public async Task<GamePhase> GetPhaseAsync()
    {
        var response = await SafeGetAsync("/lol-gameflow/v1/gameflow-phase");
        if (response == null) return GamePhase.None;

        var phaseStr = response.Trim('"');
        return ParsePhaseString(phaseStr);
    }

    /// <summary>
    /// GET /lol-champ-select/v1/session → 내 챔피언 ID
    /// </summary>
    public async Task<int?> GetMyChampionIdAsync()
    {
        var response = await SafeGetAsync("/lol-champ-select/v1/session");
        if (response == null) return null;

        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (!root.TryGetProperty("localPlayerCellId", out var cellIdProp))
                return null;

            var myCellId = cellIdProp.GetInt64();

            if (!root.TryGetProperty("myTeam", out var myTeam))
                return null;

            foreach (var player in myTeam.EnumerateArray())
            {
                if (player.TryGetProperty("cellId", out var cid) && cid.GetInt64() == myCellId)
                {
                    if (player.TryGetProperty("championId", out var champId))
                    {
                        var id = champId.GetInt32();
                        return id > 0 ? id : null;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ChampSelect 세션 파싱 실패");
        }

        return null;
    }

    /// <summary>
    /// 적 챔피언 ID 목록 (ChampSelect에서 가능할 때만)
    /// </summary>
    public async Task<List<int>> GetEnemyChampionIdsAsync()
    {
        var response = await SafeGetAsync("/lol-champ-select/v1/session");
        if (response == null) return new();

        try
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (!root.TryGetProperty("theirTeam", out var theirTeam))
                return new();

            var ids = new List<int>();
            foreach (var player in theirTeam.EnumerateArray())
            {
                if (player.TryGetProperty("championId", out var champId))
                {
                    var id = champId.GetInt32();
                    if (id > 0) ids.Add(id);
                }
            }
            return ids;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "적 챔피언 파싱 실패");
            return new();
        }
    }

    /// <summary>
    /// 게임 모드 조회
    /// </summary>
    public async Task<string> GetGameModeAsync()
    {
        var response = await SafeGetAsync("/lol-gameflow/v1/session");
        if (response == null) return "UNKNOWN";

        try
        {
            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("gameData", out var gameData) &&
                gameData.TryGetProperty("queue", out var queue) &&
                queue.TryGetProperty("gameMode", out var mode))
            {
                return mode.GetString() ?? "UNKNOWN";
            }
        }
        catch { }

        return "UNKNOWN";
    }

    // ===================================================================
    // 유틸리티
    // ===================================================================

    /// <summary>
    /// Phase 문자열 → GamePhase 변환 (공통 사용)
    /// </summary>
    private static GamePhase ParsePhaseString(string phaseStr)
    {
        return phaseStr switch
        {
            "None" => GamePhase.None,
            "Lobby" => GamePhase.Lobby,
            "ChampSelect" => GamePhase.ChampSelect,
            "InProgress" or "GameStart" or "Reconnect" => GamePhase.InProgress,
            "EndOfGame" or "PreEndOfGame" or "WaitingForStats" => GamePhase.EndOfGame,
            _ => GamePhase.None
        };
    }

    /// <summary>
    /// 안전한 GET 요청 — 실패 시 null 반환 (앱 크래시 방지)
    /// </summary>
    private async Task<string?> SafeGetAsync(string endpoint)
    {
        if (_httpClient == null) return null;

        try
        {
            var response = await _httpClient.GetAsync(endpoint);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LCU 요청 실패: {Endpoint} → {StatusCode}",
                    endpoint, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("LCU 통신 오류: {Endpoint} — {Message}", endpoint, ex.Message);
            _isConnected = false;
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("LCU 요청 타임아웃: {Endpoint}", endpoint);
            return null;
        }
    }

    /// <summary>
    /// lockfile 탐색 — LeagueClientUx 프로세스에서 경로 추론
    /// </summary>
    private async Task<LcuConnectionInfo?> FindLcuConnectionAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1) LeagueClientUx 프로세스에서 실행 경로 추출
                var lcuProcesses = Process.GetProcessesByName("LeagueClientUx");
                foreach (var proc in lcuProcesses)
                {
                    try
                    {
                        var exePath = proc.MainModule?.FileName;
                        if (string.IsNullOrEmpty(exePath)) continue;

                        var dir = Path.GetDirectoryName(exePath);
                        if (dir == null) continue;

                        var lockfilePath = Path.Combine(dir, "lockfile");
                        if (File.Exists(lockfilePath))
                        {
                            return ParseLockfile(lockfilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("프로세스 {ProcId} 경로 추출 실패: {Msg}", proc.Id, ex.Message);
                    }
                }

                // 2) 일반적인 설치 경로 탐색
                var commonPaths = new[]
                {
                    @"C:\Riot Games\League of Legends\lockfile",
                    @"D:\Riot Games\League of Legends\lockfile",
                    @"C:\Program Files\Riot Games\League of Legends\lockfile",
                    @"D:\Program Files\Riot Games\League of Legends\lockfile",
                };

                foreach (var path in commonPaths)
                {
                    if (File.Exists(path))
                    {
                        return ParseLockfile(path);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LCU lockfile 탐색 실패");
            }

            return null;
        });
    }

    /// <summary>
    /// lockfile 파싱 — "name:pid:port:password:protocol" 형식
    /// ⚠️ password는 메모리에만 보관
    /// </summary>
    private LcuConnectionInfo? ParseLockfile(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            var parts = content.Split(':');
            if (parts.Length < 5) return null;

            _logger.LogInformation("lockfile 발견 — Port: {Port}", parts[2]);
            // password(parts[3])는 절대 로그에 남기지 않음

            return new LcuConnectionInfo
            {
                Port = int.Parse(parts[2]),
                Password = parts[3],
                Protocol = parts[4]
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "lockfile 파싱 실패");
            return null;
        }
    }
}
