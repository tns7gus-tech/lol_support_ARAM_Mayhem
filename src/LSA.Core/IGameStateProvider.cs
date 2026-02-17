using LSA.Data.Models;

namespace LSA.Core;

/// <summary>
/// 게임 상태 제공자 인터페이스 — Mock/Real LCU 구현 추상화
/// Phase 2: 이벤트 기반 알림 + 모니터링 라이프사이클 추가
/// </summary>
public interface IGameStateProvider
{
    // ===== 데이터 조회 (기존) =====

    /// <summary>현재 게임 페이즈 조회</summary>
    Task<GamePhase> GetPhaseAsync();

    /// <summary>내 챔피언 ID 조회 (ChampSelect 이후)</summary>
    Task<int?> GetMyChampionIdAsync();

    /// <summary>적 챔피언 ID 목록 조회 (가능할 때만)</summary>
    Task<List<int>> GetEnemyChampionIdsAsync();

    /// <summary>현재 게임 모드 조회 (ARAM, ARENA 등)</summary>
    Task<string> GetGameModeAsync();

    // ===== 연결 상태 (기존) =====

    /// <summary>연결 상태 확인</summary>
    bool IsConnected { get; }

    /// <summary>Provider 이름 (UI 표시용)</summary>
    string ProviderName { get; }

    /// <summary>연결 시도</summary>
    Task<bool> TryConnectAsync();

    /// <summary>연결 해제</summary>
    Task DisconnectAsync();

    // ===== Phase 2: 이벤트 기반 알림 =====

    /// <summary>Phase 변경 이벤트 (WebSocket 또는 Mock에서 발생)</summary>
    event Action<GamePhase>? OnPhaseChanged;

    /// <summary>챔피언 변경 이벤트</summary>
    event Action<int?>? OnChampionChanged;

    /// <summary>연결 상태 변경 이벤트 (true=연결, false=끊김)</summary>
    event Action<bool>? OnConnectionChanged;

    // ===== Phase 2: 모니터링 라이프사이클 =====

    /// <summary>
    /// 실시간 모니터링 시작 — WebSocket 구독 or 폴링 시작
    /// CancellationToken으로 안전한 종료 지원
    /// </summary>
    Task StartMonitoringAsync(CancellationToken ct = default);

    /// <summary>모니터링 중지 + 리소스 해제</summary>
    Task StopMonitoringAsync();

    /// <summary>WebSocket 연결 여부 (REST fallback 구분용)</summary>
    bool IsWebSocketConnected { get; }
}
