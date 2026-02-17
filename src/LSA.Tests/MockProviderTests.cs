using LSA.Core;
using LSA.Data;
using LSA.Data.Models;
using LSA.Mock;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LSA.Tests;

/// <summary>
/// MockProvider 시나리오 테스트
/// Phase 순환, 이벤트 발생, 챔피언 변경 검증
/// </summary>
public class MockProviderTests
{
    private readonly MockProvider _provider;

    public MockProviderTests()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.SetMinimumLevel(LogLevel.Warning));

        var dataService = new DataService(loggerFactory.CreateLogger<DataService>());
        dataService.LoadKnowledgeBaseAsync().Wait();

        _provider = new MockProvider(dataService, loggerFactory.CreateLogger<MockProvider>());
        _provider.TryConnectAsync().Wait();
    }

    // ========================================================
    // 연결 테스트
    // ========================================================

    [Fact]
    public void MockProvider_AlwaysConnected()
    {
        Assert.True(_provider.IsConnected);
        Assert.False(_provider.IsWebSocketConnected); // Mock은 WS 미사용
        Assert.Equal("MOCK", _provider.ProviderName);
    }

    [Fact]
    public async Task TryConnectAsync_ReturnsTrue()
    {
        var result = await _provider.TryConnectAsync();
        Assert.True(result);
    }

    // ========================================================
    // Phase 순환 테스트
    // ========================================================

    [Fact]
    public async Task InitialPhase_FromMockFile()
    {
        var phase = await _provider.GetPhaseAsync();
        // mock_game_state.json에서 로드된 phase
        Assert.True(Enum.IsDefined(typeof(GamePhase), phase));
    }

    [Fact]
    public async Task CyclePhase_FollowsExpectedOrder()
    {
        // Phase를 None으로 리셋 (여러번 순환)
        while (true)
        {
            var p = await _provider.GetPhaseAsync();
            if (p == GamePhase.None) break;
            _provider.CyclePhase();
        }

        // None → ChampSelect
        _provider.CyclePhase();
        Assert.Equal(GamePhase.ChampSelect, await _provider.GetPhaseAsync());

        // ChampSelect → InProgress
        _provider.CyclePhase();
        Assert.Equal(GamePhase.InProgress, await _provider.GetPhaseAsync());

        // InProgress → EndOfGame
        _provider.CyclePhase();
        Assert.Equal(GamePhase.EndOfGame, await _provider.GetPhaseAsync());

        // EndOfGame → None
        _provider.CyclePhase();
        Assert.Equal(GamePhase.None, await _provider.GetPhaseAsync());
    }

    // ========================================================
    // 이벤트 발생 테스트
    // ========================================================

    [Fact]
    public void CyclePhase_FiresOnPhaseChanged()
    {
        GamePhase? receivedPhase = null;
        _provider.OnPhaseChanged += phase => receivedPhase = phase;

        _provider.CyclePhase();

        Assert.NotNull(receivedPhase);
    }

    [Fact]
    public void SetChampionId_FiresOnChampionChanged()
    {
        int? receivedChampId = null;
        _provider.OnChampionChanged += id => receivedChampId = id;

        _provider.SetChampionId(157); // Yasuo

        Assert.Equal(157, receivedChampId);
    }

    [Fact]
    public void SetChampionId_ZeroFiresNull()
    {
        int? receivedChampId = -1; // 초기값을 -1로 설정
        _provider.OnChampionChanged += id => receivedChampId = id;

        _provider.SetChampionId(0);

        Assert.Null(receivedChampId);
    }

    [Fact]
    public async Task TryConnect_FiresOnConnectionChanged()
    {
        bool? receivedStatus = null;
        _provider.OnConnectionChanged += connected => receivedStatus = connected;

        await _provider.TryConnectAsync();

        Assert.True(receivedStatus);
    }

    // ========================================================
    // 모니터링 라이프사이클 테스트
    // ========================================================

    [Fact]
    public async Task StartStopMonitoring_DoesNotThrow()
    {
        var cts = new CancellationTokenSource();

        await _provider.StartMonitoringAsync(cts.Token);
        await _provider.StopMonitoringAsync();

        // 예외 없이 완료되면 성공
        Assert.True(true);
    }

    // ========================================================
    // 데이터 조회 테스트
    // ========================================================

    [Fact]
    public async Task GetGameMode_ReturnsAramMayhem()
    {
        var mode = await _provider.GetGameModeAsync();
        Assert.Equal("ARAM_MAYHEM", mode);
    }

    [Fact]
    public async Task GetEnemyChampionIds_ReturnsList()
    {
        var enemies = await _provider.GetEnemyChampionIdsAsync();
        Assert.NotNull(enemies);
        Assert.IsType<List<int>>(enemies);
    }
}
