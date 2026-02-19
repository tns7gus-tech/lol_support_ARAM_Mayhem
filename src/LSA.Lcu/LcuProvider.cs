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
/// LCU ?곌껐 ?뺣낫 紐⑤뜽
/// </summary>
public class LcuConnectionInfo
{
    public int Port { get; set; }
    public string Password { get; set; } = ""; // 硫붾え由ъ뿉留?蹂닿?, 濡쒓렇/?붿뒪?????湲덉?
    public string Protocol { get; set; } = "https";
}

/// <summary>
/// Real LCU ?곌껐 Provider ??lockfile 湲곕컲 REST API + WebSocket WAMP
/// ?좑툘 POST/PATCH/DELETE ?붾뱶?ъ씤?몃뒗 ?덈? ?ъ슜?섏? ?딆쓬
/// 
/// Phase 2: WebSocket ?ㅼ떆媛??대깽??+ ?먮룞 ?ъ뿰寃?(吏??諛깆삤??
/// </summary>
public class LcuProvider : IGameStateProvider
{
    private readonly ILogger<LcuProvider> _logger;
    private readonly string? _configuredInstallPath;
    private readonly List<string> _connectionLog = new();
    private HttpClient? _httpClient;
    private ClientWebSocket? _webSocket;
    private LcuConnectionInfo? _connInfo;
    private bool _isConnected;
    private bool _isWebSocketConnected;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    private Task? _webSocketReceiveTask;

    // ?먮룞 ?ъ뿰寃??ㅼ젙
    private const int RECONNECT_BASE_DELAY_MS = 2000;
    private const int RECONNECT_MAX_DELAY_MS = 30000;
    private const int PROCESS_POLL_INTERVAL_MS = 5000;
    private int _reconnectAttempt;

    // 留덉?留??뚮젮吏??곹깭 (?대깽??以묐났 諛⑹???
    private GamePhase _lastKnownPhase = GamePhase.None;
    private int? _lastKnownChampionId;

    public bool IsConnected => _isConnected;
    public bool IsWebSocketConnected => _isWebSocketConnected;
    public string ProviderName => _isWebSocketConnected ? "LCU (WS)" : "LCU (REST)";
    public IReadOnlyList<string> ConnectionLog => _connectionLog.AsReadOnly();

    // Phase 2 ?대깽??
    public event Action<GamePhase>? OnPhaseChanged;
    public event Action<int?>? OnChampionChanged;
    public event Action<bool>? OnConnectionChanged;

    public LcuProvider(ILogger<LcuProvider> logger, string? installPath = null)
    {
        _logger = logger;
        _configuredInstallPath = string.IsNullOrWhiteSpace(installPath) ? null : installPath.Trim();
    }

    // ===================================================================
    // ?곌껐 愿由?
    // ===================================================================

    /// <summary>
    /// LCU ?곌껐 ?쒕룄 ??lockfile ?먯깋 ??HttpClient + WebSocket 援ъ꽦
    /// </summary>
    public async Task<bool> TryConnectAsync()
    {
        AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] TryConnect start");
        try
        {
            _connInfo = await FindLcuConnectionAsync();
            if (_connInfo == null)
            {
                _logger.LogWarning("LCU lockfile unavailable (not found or unreadable)");
                AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile unavailable");
                _isConnected = false;
                return false;
            }

            // REST HttpClient 생성 (Self-signed 인증서 허용)
            SetupHttpClient();

            // 연결 확인 + phase 조회 테스트
            var (phaseOk, phase) = await ProbePhaseAsync();
            if (!phaseOk)
            {
                AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] REST probe failed");
                _isConnected = false;
                return false;
            }

            _isConnected = true;
            _reconnectAttempt = 0;
            _logger.LogInformation("LCU REST 연결 성공 ? 현재 Phase: {Phase}", phase);
            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] REST connected, phase={phase}");

            OnConnectionChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LCU 연결 실패");
            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] connect exception: {ex.Message}");
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

        _logger.LogInformation("LCU ?곌껐 ?댁젣");
        OnConnectionChanged?.Invoke(false);
    }

    /// <summary>
    /// HttpClient ?ㅼ젙 ??Basic Auth (password??濡쒓렇 湲곕줉 湲덉?)
    /// </summary>
    private void SetupHttpClient()
    {
        if (_connInfo == null) return;

        _httpClient?.Dispose();
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
        // WebSocket??REST? ?숈씪?섍쾶 loopback留??덉슜
        var isLoopback = requestUri != null &&
                         (requestUri.IsLoopback ||
                          string.Equals(requestUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(requestUri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

        if (!isLoopback)
            return false;

        return errors == SslPolicyErrors.None || errors == SslPolicyErrors.RemoteCertificateChainErrors;
    }

    // ===================================================================
    // Phase 2: ?ㅼ떆媛?紐⑤땲?곕쭅 (WebSocket + ?꾨줈?몄뒪 媛먯떆)
    // ===================================================================

    /// <summary>
    /// 紐⑤땲?곕쭅 ?쒖옉 ??WebSocket ?곌껐 ?쒕룄 + ?꾨줈?몄뒪 媛먯떆 猷⑦봽
    /// </summary>
    public async Task StartMonitoringAsync(CancellationToken ct = default)
    {
        if (_monitorCts != null && !_monitorCts.IsCancellationRequested)
        {
            _logger.LogDebug("LCU monitoring is already running.");
            return;
        }

        _monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _monitorCts.Token;

        // WebSocket ?곌껐 ?쒕룄 (?ㅽ뙣?대룄 REST fallback?쇰줈 怨꾩냽)
        await TryConnectWebSocketAsync(token);

        // 諛깃렇?쇱슫??紐⑤땲?곕쭅 猷⑦봽 ?쒖옉
        _monitorTask = Task.Run(() => MonitoringLoopAsync(token), token);
    }

    /// <summary>
    /// 紐⑤땲?곕쭅 以묒? + 由ъ냼???댁젣
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
                        "??醫낅즺",
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

        if (_webSocketReceiveTask != null)
        {
            try
            {
                await _webSocketReceiveTask;
            }
            catch { }
            _webSocketReceiveTask = null;
        }

        if (_monitorTask != null)
        {
            try
            {
                await _monitorTask;
            }
            catch { }
            _monitorTask = null;
        }
    }

    /// <summary>
    /// WebSocket ?곌껐 ?쒕룄 ??LCU WAMP ?꾨줈?좎퐳
    /// wss://riot:{password}@127.0.0.1:{port}/
    /// </summary>
    private async Task<bool> TryConnectWebSocketAsync(CancellationToken ct)
    {
        if (_connInfo == null) return false;

        try
        {
            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();

            // Loopback ???+ self-signed 泥댁씤 ?ㅻ쪟留??덉슜
            var wsUri = new Uri($"wss://127.0.0.1:{_connInfo.Port}/");
            _webSocket.Options.RemoteCertificateValidationCallback = (_, _, _, errors) =>
                IsAllowedLcuTlsForWebSocket(wsUri, errors);

            // Basic Auth ?ㅻ뜑 ?ㅼ젙 (password??濡쒓렇 湲곕줉 湲덉?)
            var authBytes = Encoding.ASCII.GetBytes($"riot:{_connInfo.Password}");
            _webSocket.Options.SetRequestHeader("Authorization",
                $"Basic {Convert.ToBase64String(authBytes)}");

            await _webSocket.ConnectAsync(wsUri, ct);

            // WAMP Subscribe ??Phase 蹂寃??대깽??
            await WampSubscribeAsync("OnJsonApiEvent_lol-gameflow_v1_gameflow-phase", ct);

            // WAMP Subscribe ??ChampSelect ?몄뀡 蹂寃??대깽??
            await WampSubscribeAsync("OnJsonApiEvent_lol-champ-select_v1_session", ct);

            _isWebSocketConnected = true;
            _reconnectAttempt = 0;
            _logger.LogInformation("LCU WebSocket ?곌껐 ?깃났");
            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] websocket connected");

            // 硫붿떆吏 ?섏떊 猷⑦봽 ?쒖옉
            _webSocketReceiveTask = Task.Run(() => WebSocketReceiveLoopAsync(ct), ct);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("LCU WebSocket ?곌껐 ?ㅽ뙣 (REST fallback ?ъ슜): {Msg}", ex.Message);
            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] websocket connect failed: {ex.Message}");
            _isWebSocketConnected = false;
            _webSocket?.Dispose();
            _webSocket = null;
            return false;
        }
    }

    /// <summary>
    /// WAMP Subscribe 硫붿떆吏 ?꾩넚 ??[5, "topic"]
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

        _logger.LogDebug("WAMP 援щ룆: {Topic}", topic);
    }

    /// <summary>
    /// WebSocket 硫붿떆吏 ?섏떊 猷⑦봽 ??WAMP ?대깽???뚯떛
    /// </summary>
    private async Task WebSocketReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        using var ms = new MemoryStream();

        try
        {
            while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("LCU WebSocket ?쒕쾭?먯꽌 醫낅즺");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    ms.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                        continue;

                    var message = Encoding.UTF8.GetString(ms.ToArray());
                    ms.SetLength(0);
                    ProcessWampMessage(message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ?뺤긽 醫낅즺
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning("WebSocket ?섏떊 ?ㅻ쪟: {Msg}", ex.Message);
        }
        finally
        {
            _isWebSocketConnected = false;
            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] websocket disconnected");

            if (!ct.IsCancellationRequested)
            {
                _logger.LogInformation("WebSocket ?곌껐 ?딄? ???ъ뿰寃??쒕룄 ?덉젙");
            }
        }
    }

    /// <summary>
    /// WAMP ?대깽??硫붿떆吏 ?뚯떛 ??[8, "topic", { data }]
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
            _logger.LogDebug("WAMP 硫붿떆吏 ?뚯떛 ?ㅽ뙣 (臾댁떆): {Msg}", ex.Message);
        }
    }

    /// <summary>
    /// Phase 蹂寃??대깽??泥섎━ ??{ "data": "ChampSelect" }
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
            _logger.LogInformation("[WS] Phase 蹂寃???{Phase}", phase);
            OnPhaseChanged?.Invoke(phase);
        }
    }

    /// <summary>
    /// ChampSelect ?몄뀡 蹂寃??대깽??泥섎━ ??梨뷀뵾??ID 異붿텧
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
                            _logger.LogInformation("[WS] 梨뷀뵾??蹂寃???{ChampId}", champValue);
                            OnChampionChanged?.Invoke(champValue);
                        }
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("ChampSelect ?대깽???뚯떛 ?ㅽ뙣: {Msg}", ex.Message);
        }
    }

    /// <summary>
    /// 諛깃렇?쇱슫??紐⑤땲?곕쭅 猷⑦봽 ???꾨줈?몄뒪 媛먯떆 + ?먮룞 ?ъ뿰寃?
    /// </summary>
    private async Task MonitoringLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1) LCU ?꾨줈?몄뒪 ?앹〈 ?뺤씤
                var lcuRunning = IsLcuProcessRunning();

                if (!lcuRunning && _isConnected)
                {
                    // ?대씪?댁뼵??醫낅즺?????곌껐 ?댁젣
                    _logger.LogInformation("LeagueClientUx ?꾨줈?몄뒪 醫낅즺 媛먯?");
                    AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] LeagueClientUx not running");
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
                    // ?대씪?댁뼵???쒖옉?????ъ뿰寃??쒕룄
                    _logger.LogInformation("LeagueClientUx ?꾨줈?몄뒪 媛먯? ???ъ뿰寃??쒕룄");
                    await AttemptReconnectAsync(ct);
                }
                else if (_isConnected && !_isWebSocketConnected)
                {
                    // REST???댁븘?덉?留?WebSocket ?딄? ??WebSocket ?ъ뿰寃?
                    await TryConnectWebSocketAsync(ct);
                }

                // 2) ?대쭅 媛꾧꺽 ?湲?
                var delay = _isConnected ? PROCESS_POLL_INTERVAL_MS : GetReconnectDelay();
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("紐⑤땲?곕쭅 猷⑦봽 ?ㅻ쪟: {Msg}", ex.Message);
                await Task.Delay(PROCESS_POLL_INTERVAL_MS, ct);
            }
        }
    }

    /// <summary>
    /// ?먮룞 ?ъ뿰寃???REST + WebSocket
    /// </summary>
    private async Task AttemptReconnectAsync(CancellationToken ct)
    {
        _reconnectAttempt++;
        _logger.LogInformation("?ъ뿰寃??쒕룄 #{Attempt}", _reconnectAttempt);
        AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] reconnect attempt #{_reconnectAttempt}");

        try
        {
            _connInfo = await FindLcuConnectionAsync();
            if (_connInfo == null) return;

            SetupHttpClient();

            // REST ?곌껐 ?뺤씤
            var (phaseOk, phase) = await ProbePhaseAsync();
            if (!phaseOk)
            {
                AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] reconnect REST probe failed");
                _isConnected = false;
                return;
            }

            _isConnected = true;
            _reconnectAttempt = 0;
            _lastKnownPhase = phase;

            _logger.LogInformation("LCU ?ъ뿰寃??깃났 ??Phase: {Phase}", phase);
            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] reconnect success, phase={phase}");
            OnConnectionChanged?.Invoke(true);
            OnPhaseChanged?.Invoke(phase);

            // WebSocket ?ъ뿰寃??쒕룄
            await TryConnectWebSocketAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("?ъ뿰寃??ㅽ뙣 #{Attempt}: {Msg}", _reconnectAttempt, ex.Message);
            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] reconnect failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 吏??諛깆삤???쒕젅??怨꾩궛 (2s ??4s ??8s ??max 30s)
    /// </summary>
    private int GetReconnectDelay()
    {
        var delay = RECONNECT_BASE_DELAY_MS * (int)Math.Pow(2, Math.Min(_reconnectAttempt, 5));
        return Math.Min(delay, RECONNECT_MAX_DELAY_MS);
    }

    /// <summary>
    /// LeagueClientUx ?꾨줈?몄뒪 ?ㅽ뻾 ?щ? ?뺤씤
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
    // REST API 議고쉶 (湲곗〈 ??蹂寃??놁쓬)
    // ===================================================================

    /// <summary>
    /// GET /lol-gameflow/v1/gameflow-phase ??GamePhase
    /// </summary>
    public async Task<GamePhase> GetPhaseAsync()
    {
        var response = await SafeGetAsync("/lol-gameflow/v1/gameflow-phase");
        if (response == null) return GamePhase.None;

        var phaseStr = response.Trim('"');
        return ParsePhaseString(phaseStr);
    }

    /// <summary>
    /// GET /lol-champ-select/v1/session ????梨뷀뵾??ID
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
            _logger.LogError(ex, "ChampSelect ?몄뀡 ?뚯떛 ?ㅽ뙣");
        }

        return null;
    }

    /// <summary>
    /// ??梨뷀뵾??ID 紐⑸줉 (ChampSelect?먯꽌 媛?ν븷 ?뚮쭔)
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
            _logger.LogError(ex, "??梨뷀뵾???뚯떛 ?ㅽ뙣");
            return new();
        }
    }

    /// <summary>
    /// 寃뚯엫 紐⑤뱶 議고쉶
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
    // ?좏떥由ы떚
    // ===================================================================

    /// <summary>
    /// Phase 臾몄옄????GamePhase 蹂??(怨듯넻 ?ъ슜)
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
    /// ?덉쟾??GET ?붿껌 ???ㅽ뙣 ??null 諛섑솚 (???щ옒??諛⑹?)
    /// </summary>
    private async Task<string?> SafeGetAsync(string endpoint)
    {
        if (_httpClient == null) return null;

        try
        {
            var response = await _httpClient.GetAsync(endpoint);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("LCU ?붿껌 ?ㅽ뙣: {Endpoint} ??{StatusCode}",
                    endpoint, (int)response.StatusCode);
                AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] GET {endpoint} => {(int)response.StatusCode}");
                return null;
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("LCU ?듭떊 ?ㅻ쪟: {Endpoint} ??{Message}", endpoint, ex.Message);
            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] HTTP error {endpoint}: {ex.Message}");
            _isConnected = false;
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("LCU ?붿껌 ??꾩븘?? {Endpoint}", endpoint);
            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] timeout {endpoint}");
            return null;
        }
    }

    /// <summary>
    /// 연결 확인에 사용하는 phase probe. HTTP 성공 여부를 명확히 분리한다.
    /// </summary>
    private async Task<(bool Success, GamePhase Phase)> ProbePhaseAsync()
    {
        var response = await SafeGetAsync("/lol-gameflow/v1/gameflow-phase");
        if (response == null)
        {
            return (false, GamePhase.None);
        }

        var phase = ParsePhaseString(response.Trim('"'));
        return (true, phase);
    }

    /// <summary>
    /// lockfile ?먯깋 ??LeagueClientUx ?꾨줈?몄뒪?먯꽌 寃쎈줈 異붾줎
    /// </summary>
    private async Task<LcuConnectionInfo?> FindLcuConnectionAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1) LeagueClientUx ?꾨줈?몄뒪?먯꽌 ?ㅽ뻾 寃쎈줈 異붿텧
                AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] probe by process");
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
                            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile via process: {lockfilePath}");
                            var parsed = ParseLockfile(lockfilePath);
                            if (parsed != null) return parsed;
                            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile parse failed: {lockfilePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("?꾨줈?몄뒪 {ProcId} 寃쎈줈 異붿텧 ?ㅽ뙣: {Msg}", proc.Id, ex.Message);
                    }
                }

                // 2) ?쇰컲?곸씤 ?ㅼ튂 寃쎈줈 ?먯깋
                if (!string.IsNullOrWhiteSpace(_configuredInstallPath))
                {
                    var configuredPath = _configuredInstallPath!;
                    var configuredLockfilePath = Path.Combine(configuredPath, "lockfile");
                    if (File.Exists(configuredLockfilePath))
                    {
                        AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile via config: {configuredLockfilePath}");
                        var parsed = ParseLockfile(configuredLockfilePath);
                        if (parsed != null) return parsed;
                        AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile parse failed: {configuredLockfilePath}");
                    }
                    AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] config path miss: {configuredLockfilePath}");
                }

                var commonPaths = new[]
                {
                    @"C:\Riot Games\League of Legends\lockfile",
                    @"D:\Riot Games\League of Legends\lockfile",
                    @"E:\Riot Games\League of Legends\lockfile",
                    @"C:\Program Files\Riot Games\League of Legends\lockfile",
                    @"D:\Program Files\Riot Games\League of Legends\lockfile",
                    @"E:\Program Files\Riot Games\League of Legends\lockfile",
                };

                foreach (var path in commonPaths)
                {
                    if (File.Exists(path))
                    {
                        AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile via common path: {path}");
                        var parsed = ParseLockfile(path);
                        if (parsed != null) return parsed;
                        AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile parse failed: {path}");
                    }
                }

                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                        continue;

                    var root = drive.RootDirectory.FullName;
                    var candidatePaths = new[]
                    {
                        Path.Combine(root, "Riot Games", "League of Legends", "lockfile"),
                        Path.Combine(root, "Program Files", "Riot Games", "League of Legends", "lockfile"),
                    };

                    foreach (var candidate in candidatePaths)
                    {
                        if (File.Exists(candidate))
                        {
                            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile via drive scan: {candidate}");
                            var parsed = ParseLockfile(candidate);
                            if (parsed != null) return parsed;
                            AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile parse failed: {candidate}");
                        }
                    }
                }
                AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile probe exhausted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LCU lockfile ?먯깋 ?ㅽ뙣");
                AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile probe exception: {ex.Message}");
            }

            return null;
        });
    }

    /// <summary>
    /// lockfile ?뚯떛 ??"name:pid:port:password:protocol" ?뺤떇
    /// ?좑툘 password??硫붾え由ъ뿉留?蹂닿?
    /// </summary>
    private LcuConnectionInfo? ParseLockfile(string path)
    {
        const int maxAttempts = 5;
        const int retryDelayMs = 120;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // Riot client가 lockfile을 갱신 중일 때도 읽을 수 있도록 공유 모드로 연다.
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var content = sr.ReadToEnd().Trim();
                var parts = content.Split(':');
                if (parts.Length < 5)
                {
                    if (attempt < maxAttempts)
                    {
                        AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile malformed, retry {attempt}/{maxAttempts}");
                        Thread.Sleep(retryDelayMs);
                        continue;
                    }

                    AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile parse error: malformed");
                    return null;
                }

                if (!int.TryParse(parts[2], out var port))
                {
                    if (attempt < maxAttempts)
                    {
                        AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile port invalid, retry {attempt}/{maxAttempts}");
                        Thread.Sleep(retryDelayMs);
                        continue;
                    }

                    AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile parse error: invalid port");
                    return null;
                }

                _logger.LogInformation("lockfile 諛쒓껄 ??Port: {Port}", parts[2]);
                AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile parsed, port={parts[2]}");
                // password(parts[3])???덈? 濡쒓렇???④린吏 ?딆쓬

                return new LcuConnectionInfo
                {
                    Port = port,
                    Password = parts[3].Trim(),
                    Protocol = parts[4].Trim()
                };
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile busy, retry {attempt}/{maxAttempts}");
                Thread.Sleep(retryDelayMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "lockfile ?뚯떛 ?ㅽ뙣");
                AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile parse error: {ex.Message}");
                return null;
            }
        }

        AddConnectionLog($"[{DateTime.Now:HH:mm:ss}] lockfile parse error: busy timeout");
        return null;
    }

    private void AddConnectionLog(string message)
    {
        _connectionLog.Add(message);
        if (_connectionLog.Count > 50)
        {
            _connectionLog.RemoveAt(0);
        }
    }
}



