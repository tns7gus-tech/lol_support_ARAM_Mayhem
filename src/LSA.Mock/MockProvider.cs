using LSA.Core;
using LSA.Data;
using LSA.Data.Models;
using Microsoft.Extensions.Logging;

namespace LSA.Mock;

/// <summary>
/// Mock 게임 상태 제공자 — LoL 없는 환경에서 개발/테스트용
/// mock_game_state.json 기반 + 핫키로 phase/champion 변경 가능
/// Phase 2: 이벤트 기반 알림 구현
/// </summary>
public class MockProvider : IGameStateProvider
{
    private readonly DataService _dataService;
    private readonly ILogger<MockProvider> _logger;

    private MockGameState _state = new();
    private GamePhase _currentPhase = GamePhase.None;

    public bool IsConnected => true; // Mock은 항상 연결 상태
    public bool IsWebSocketConnected => false; // Mock은 WebSocket 없음
    public string ProviderName => "MOCK";

    // Phase 2 이벤트
    public event Action<GamePhase>? OnPhaseChanged;
    public event Action<int?>? OnChampionChanged;
    public event Action<bool>? OnConnectionChanged;

    public MockProvider(DataService dataService, ILogger<MockProvider> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    public async Task<bool> TryConnectAsync()
    {
        _state = await _dataService.LoadMockGameStateAsync();
        _currentPhase = ParsePhase(_state.Phase);
        _logger.LogInformation("[MOCK] 연결 완료 — Phase: {Phase}, ChampId: {ChampId}",
            _currentPhase, _state.MyChampionId);

        OnConnectionChanged?.Invoke(true);
        return true;
    }

    public Task DisconnectAsync()
    {
        _logger.LogInformation("[MOCK] 연결 해제");
        OnConnectionChanged?.Invoke(false);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Mock은 모니터링 불필요 — 핫키로 직접 Phase 변경
    /// </summary>
    public Task StartMonitoringAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("[MOCK] 모니터링 시작 (no-op — 핫키로 Phase 변경)");
        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync()
    {
        _logger.LogDebug("[MOCK] 모니터링 중지");
        return Task.CompletedTask;
    }

    public Task<GamePhase> GetPhaseAsync() => Task.FromResult(_currentPhase);

    public Task<int?> GetMyChampionIdAsync() =>
        Task.FromResult<int?>(_state.MyChampionId > 0 ? _state.MyChampionId : null);

    public Task<List<int>> GetEnemyChampionIdsAsync() =>
        Task.FromResult(_state.EnemyChampionIds);

    public Task<string> GetGameModeAsync() =>
        Task.FromResult(_state.Mode);

    /// <summary>
    /// [개발용] Phase 순환 — 핫키로 호출
    /// None → ChampSelect → InProgress → EndOfGame → None
    /// Phase 2: OnPhaseChanged 이벤트 발생
    /// </summary>
    public void CyclePhase()
    {
        _currentPhase = _currentPhase switch
        {
            GamePhase.None => GamePhase.ChampSelect,
            GamePhase.ChampSelect => GamePhase.InProgress,
            GamePhase.InProgress => GamePhase.EndOfGame,
            GamePhase.EndOfGame => GamePhase.None,
            _ => GamePhase.None
        };
        _logger.LogInformation("[MOCK] Phase 변경 → {Phase}", _currentPhase);

        // 이벤트 발생 — OverlayWindow가 구독
        OnPhaseChanged?.Invoke(_currentPhase);
    }

    /// <summary>
    /// [개발용] 챔피언 ID 변경
    /// Phase 2: OnChampionChanged 이벤트 발생
    /// </summary>
    public void SetChampionId(int championId)
    {
        _state.MyChampionId = championId;
        _logger.LogInformation("[MOCK] ChampionId 변경 → {ChampId}", championId);

        OnChampionChanged?.Invoke(championId > 0 ? championId : null);
    }

    /// <summary>
    /// 문자열 → GamePhase 변환
    /// </summary>
    private static GamePhase ParsePhase(string phase)
    {
        return phase?.ToLower() switch
        {
            "none" => GamePhase.None,
            "lobby" => GamePhase.Lobby,
            "champselect" => GamePhase.ChampSelect,
            "inprogress" => GamePhase.InProgress,
            "endofgame" => GamePhase.EndOfGame,
            _ => GamePhase.None
        };
    }
}
