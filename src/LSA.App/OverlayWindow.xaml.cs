using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using LSA.App.Services;
using LSA.Core;
using LSA.Data;
using LSA.Data.Models;
using LSA.Lcu;
using LSA.Mock;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace LSA.App;

/// <summary>
/// 오버레이 뷰모델 — 증강 추천 표시용
/// </summary>
public class AugmentViewModel
{
    public string AugmentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tier { get; set; } = "C";
    public string TagsText { get; set; } = "";
    public string ReasonText { get; set; } = "";
    public bool IsSelected { get; set; }

    /// <summary>티어별 색상 브러시</summary>
    public SolidColorBrush TierBrush => Tier switch
    {
        "S" => new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)), // 금
        "A" => new SolidColorBrush(Color.FromRgb(0x7B, 0x68, 0xEE)), // 보라
        "B" => new SolidColorBrush(Color.FromRgb(0x4E, 0xCD, 0xC4)), // 청록
        _ => new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80))    // 회색
    };
}

/// <summary>
/// 아이템 뷰모델
/// </summary>
public class ItemViewModel
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>
/// 오버레이 윈도우 — 투명/TopMost/드래그 가능
/// Phase 2: 이벤트 기반 업데이트 + 폴링 fallback + 연결 상태 표시
/// </summary>
public partial class OverlayWindow : Window
{
    // Win32 — 클릭 통과 모드
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    // 서비스
    private readonly ILoggerFactory _loggerFactory;
    private readonly DataService _dataService;
    private readonly RecommendationService _recommendationService;
    private readonly HotKeyService _hotKeyService;
    private IGameStateProvider? _provider;
    private MockProvider? _mockProvider; // Mock 전용 기능 접근용
    private CancellationTokenSource? _appCts;

    // 상태
    private bool _isClickThrough;
    private bool _isCollapsed;
    private GamePhase _currentPhase = GamePhase.None;
    private int? _currentChampionId;
    private RecommendationResult? _currentRecommendation;
    private readonly List<string> _selectedAugmentIds = new();

    // Phase 2: fallback 폴링 타이머 (간격 5초 — WebSocket 활성 시 보조 역할)
    private DispatcherTimer? _fallbackPollTimer;

    // 연결 상태 색상
    private static readonly SolidColorBrush _connGreen = new(Color.FromRgb(0x4C, 0xAF, 0x50));  // WebSocket
    private static readonly SolidColorBrush _connYellow = new(Color.FromRgb(0xFF, 0xC1, 0x07)); // REST
    private static readonly SolidColorBrush _connRed = new(Color.FromRgb(0xF4, 0x43, 0x36));    // 미연결
    private static readonly SolidColorBrush _connPurple = new(Color.FromRgb(0xAB, 0x47, 0xBC)); // Mock

    public OverlayWindow()
    {
        InitializeComponent();

        // 로거 팩토리 생성
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(LogLevel.Information)
                .AddConsole();
        });

        // 서비스 초기화
        _dataService = new DataService(_loggerFactory.CreateLogger<DataService>());
        _recommendationService = new RecommendationService(
            _dataService, _loggerFactory.CreateLogger<RecommendationService>());
        _hotKeyService = new HotKeyService();
    }

    /// <summary>
    /// 윈도우 로드 완료 — 서비스 초기화 + 핫키 등록 + Provider 연결 + 모니터링 시작
    /// </summary>
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _appCts = new CancellationTokenSource();

        // 설정 로드
        await _dataService.LoadConfigAsync();
        await _dataService.LoadKnowledgeBaseAsync();

        // 설정에서 위치 복원
        Left = _dataService.Config.Overlay.X;
        Top = _dataService.Config.Overlay.Y;

        // 핫키 등록
        _hotKeyService.OnToggleOverlay += ToggleOverlay;
        _hotKeyService.OnToggleClickThrough += ToggleClickThrough;
        _hotKeyService.OnDevCyclePhase += DevCyclePhase;
        _hotKeyService.Register(this);

        // Provider 연결 + 이벤트 구독
        await ConnectProviderAsync();

        // 모니터링 시작 (WebSocket + 프로세스 감시)
        if (_provider != null)
        {
            await _provider.StartMonitoringAsync(_appCts.Token);
        }

        // Fallback 폴링 타이머 (5초 간격 — WebSocket 보조)
        _fallbackPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _fallbackPollTimer.Tick += async (s, args) => await FallbackPollAsync();
        _fallbackPollTimer.Start();

        // 초기 상태 갱신
        await FallbackPollAsync();
    }

    /// <summary>
    /// Provider 연결 + 이벤트 구독 — LCU 시도 → 실패 시 Mock fallback
    /// </summary>
    private async Task ConnectProviderAsync()
    {
        // 먼저 Real LCU 시도
        var lcuProvider = new LcuProvider(_loggerFactory.CreateLogger<LcuProvider>());
        if (await lcuProvider.TryConnectAsync())
        {
            _provider = lcuProvider;
            SubscribeProviderEvents(_provider);
            MockBadge.Visibility = Visibility.Collapsed;
            UpdateConnectionUI(true, lcuProvider.IsWebSocketConnected);
            return;
        }

        // LCU 실패 → Mock 전환
        if (_dataService.Config.App.UseMockWhenLcuMissing)
        {
            _mockProvider = new MockProvider(_dataService, _loggerFactory.CreateLogger<MockProvider>());
            await _mockProvider.TryConnectAsync();
            _provider = _mockProvider;
            SubscribeProviderEvents(_provider);
            MockBadge.Visibility = Visibility.Visible;
            UpdateConnectionUI_Mock();
        }
        else
        {
            UpdateConnectionUI(false, false);
        }
    }

    /// <summary>
    /// Provider 이벤트 구독 — Phase/Champion/Connection 변경 즉시 반응
    /// </summary>
    private void SubscribeProviderEvents(IGameStateProvider provider)
    {
        provider.OnPhaseChanged += phase =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _currentPhase = phase;
                UpdatePhaseUI();
            });
        };

        provider.OnChampionChanged += champId =>
        {
            Dispatcher.BeginInvoke(async () =>
            {
                if (champId != _currentChampionId)
                {
                    _currentChampionId = champId;
                    await UpdateRecommendationsAsync();
                }
            });
        };

        provider.OnConnectionChanged += connected =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_mockProvider != null)
                {
                    UpdateConnectionUI_Mock();
                }
                else
                {
                    UpdateConnectionUI(connected, _provider?.IsWebSocketConnected ?? false);
                }

                if (!connected)
                {
                    _currentPhase = GamePhase.None;
                    UpdatePhaseUI();
                }
            });
        };
    }

    // ===== 연결 상태 UI =====

    /// <summary>
    /// 연결 상태 인디케이터 업데이트 — 🟢WS / 🟡REST / 🔴미연결
    /// </summary>
    private void UpdateConnectionUI(bool connected, bool isWebSocket)
    {
        if (!connected)
        {
            ConnIndicator.Fill = _connRed;
            ConnText.Text = "미연결";
        }
        else if (isWebSocket)
        {
            ConnIndicator.Fill = _connGreen;
            ConnText.Text = "WS";
        }
        else
        {
            ConnIndicator.Fill = _connYellow;
            ConnText.Text = "REST";
        }
    }

    /// <summary>
    /// Mock 모드 인디케이터 — 🟣
    /// </summary>
    private void UpdateConnectionUI_Mock()
    {
        ConnIndicator.Fill = _connPurple;
        ConnText.Text = "MOCK";
    }

    // ===== Fallback 폴링 =====

    /// <summary>
    /// Fallback 폴링 — WebSocket 보조 (5초 간격)
    /// WebSocket이 활성이면 연결 상태 UI만 갱신
    /// </summary>
    private async Task FallbackPollAsync()
    {
        if (_provider == null) return;

        try
        {
            // 연결 상태 UI 갱신
            if (_mockProvider != null)
            {
                UpdateConnectionUI_Mock();
            }
            else
            {
                UpdateConnectionUI(_provider.IsConnected, _provider.IsWebSocketConnected);
            }

            // WebSocket이 활성이면 데이터 폴링은 스킵 (이벤트로 이미 수신 중)
            if (_provider.IsWebSocketConnected) return;

            // REST fallback 폴링
            var phase = await _provider.GetPhaseAsync();
            var champId = await _provider.GetMyChampionIdAsync();

            if (phase != _currentPhase)
            {
                _currentPhase = phase;
                UpdatePhaseUI();
            }

            if (champId != _currentChampionId && champId.HasValue)
            {
                _currentChampionId = champId;
                await UpdateRecommendationsAsync();
            }
        }
        catch (Exception)
        {
            // 폴링 오류는 조용히 무시
        }
    }

    /// <summary>
    /// Phase에 따른 UI 전환
    /// </summary>
    private void UpdatePhaseUI()
    {
        var phaseText = _currentPhase switch
        {
            GamePhase.None => "대기 중...",
            GamePhase.Lobby => "로비 대기",
            GamePhase.ChampSelect => "🎯 챔피언 선택",
            GamePhase.InProgress => "⚔️ 게임 진행 중",
            GamePhase.EndOfGame => "게임 종료",
            _ => "알 수 없음"
        };
        PhaseText.Text = phaseText;

        ContentPanel.Visibility = _currentPhase switch
        {
            GamePhase.ChampSelect or GamePhase.InProgress => Visibility.Visible,
            _ => Visibility.Visible // MVP에서는 항상 표시
        };
    }

    /// <summary>
    /// 추천 데이터 갱신
    /// </summary>
    private async Task UpdateRecommendationsAsync()
    {
        if (_currentChampionId == null) return;

        List<string>? enemyTags = null;
        try
        {
            var enemyIds = await _provider!.GetEnemyChampionIdsAsync();
            if (enemyIds.Any())
            {
                enemyTags = DeriveEnemyTags(enemyIds);
            }
        }
        catch { }

        _currentRecommendation = _recommendationService.GetRecommendations(
            _currentChampionId.Value, enemyTags);

        ChampionText.Text = _currentRecommendation.ChampionName;

        UpdateAugmentUI(_currentRecommendation.Augments.Take(8).ToList());
        UpdateItemUI(_currentRecommendation.Items);

        _selectedAugmentIds.Clear();
        AugmentSelectHint.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 적 챔피언 ID → 태그 변환 (knowledge_base 기반)
    /// </summary>
    private List<string> DeriveEnemyTags(List<int> enemyIds)
    {
        var tags = new List<string>();
        var kb = _dataService.KnowledgeBase;

        foreach (var id in enemyIds)
        {
            if (kb.Champions.TryGetValue(id.ToString(), out var champ))
            {
                foreach (var role in champ.Roles)
                {
                    var tag = role.ToLower() switch
                    {
                        "tank" => "tank",
                        "fighter" => "tank",
                        "mage" => "burst",
                        "assassin" => "burst",
                        "support" => "heal",
                        "marksman" => "dps",
                        _ => null
                    };
                    if (tag != null && !tags.Contains(tag))
                        tags.Add(tag);
                }
            }
        }

        return tags;
    }

    /// <summary>
    /// 증강 UI 업데이트
    /// </summary>
    private void UpdateAugmentUI(List<AugmentRecommendation> augments)
    {
        var viewModels = augments.Select(a => new AugmentViewModel
        {
            AugmentId = a.AugmentId,
            Name = a.Name,
            Tier = a.Tier,
            TagsText = string.Join(" · ", a.Tags),
            ReasonText = string.Join(" | ", a.Reasons.Take(2))
        }).ToList();

        AugmentList.ItemsSource = viewModels;
    }

    /// <summary>
    /// 아이템 UI 업데이트
    /// </summary>
    private void UpdateItemUI(List<ItemRecommendation> items)
    {
        CoreItemList.ItemsSource = items.Where(i => i.IsCore)
            .Select(i => new ItemViewModel { ItemId = i.ItemId, Name = i.Name, Reason = i.Reason })
            .ToList();

        SituationalItemList.ItemsSource = items.Where(i => !i.IsCore)
            .Select(i => new ItemViewModel { ItemId = i.ItemId, Name = i.Name, Reason = i.Reason })
            .ToList();
    }

    /// <summary>
    /// 증강 클릭 — "현재 3개 증강 선택" 기능
    /// </summary>
    private void Augment_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AugmentViewModel vm)
        {
            if (_selectedAugmentIds.Contains(vm.AugmentId))
            {
                _selectedAugmentIds.Remove(vm.AugmentId);
            }
            else if (_selectedAugmentIds.Count < 3)
            {
                _selectedAugmentIds.Add(vm.AugmentId);
            }

            if (_selectedAugmentIds.Count == 3 && _currentRecommendation != null)
            {
                var filtered = _recommendationService.FilterShownAugments(
                    _currentRecommendation, _selectedAugmentIds);
                UpdateAugmentUI(filtered);
                AugmentSelectHint.Visibility = Visibility.Collapsed;
            }
            else if (_selectedAugmentIds.Count < 3)
            {
                AugmentSelectHint.Visibility = Visibility.Visible;
            }
        }
    }

    // ===== 핫키 핸들러 =====

    /// <summary>Ctrl+Shift+O — 오버레이 표시/숨김 토글</summary>
    private void ToggleOverlay()
    {
        Dispatcher.Invoke(() =>
        {
            Visibility = Visibility == Visibility.Visible
                ? Visibility.Hidden
                : Visibility.Visible;
        });
    }

    /// <summary>Ctrl+Shift+C — 클릭 통과 토글</summary>
    private void ToggleClickThrough()
    {
        Dispatcher.Invoke(() =>
        {
            _isClickThrough = !_isClickThrough;
            var hwnd = new WindowInteropHelper(this).Handle;
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            if (_isClickThrough)
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
            else
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
        });
    }

    /// <summary>Ctrl+Shift+P — [개발용] Mock Phase 순환</summary>
    private void DevCyclePhase()
    {
        if (_mockProvider != null)
        {
            // Phase 2: Mock CyclePhase()가 이벤트를 발생시키므로
            // 별도 PollGameState 호출 불필요 — 이벤트 구독이 처리함
            _mockProvider.CyclePhase();
        }
    }

    /// <summary>접기/펼치기 버튼</summary>
    private void CollapseBtn_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = !_isCollapsed;
        ContentPanel.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseBtn.Content = _isCollapsed ? "+" : "—";
        Height = _isCollapsed ? 80 : 600;
    }

    /// <summary>드래그 이동</summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    /// <summary>창 닫힐 때 — 모니터링 중지 + 위치 저장 + 리소스 해제</summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        // 앱 취소 토큰 해제
        _appCts?.Cancel();

        // 모니터링 중지
        if (_provider != null)
        {
            await _provider.StopMonitoringAsync();
        }

        // 위치 저장
        _dataService.Config.Overlay.X = Left;
        _dataService.Config.Overlay.Y = Top;
        await _dataService.SaveConfigAsync();

        _fallbackPollTimer?.Stop();
        _hotKeyService.Dispose();

        if (_provider != null)
        {
            await _provider.DisconnectAsync();
        }

        _appCts?.Dispose();

        base.OnClosing(e);
    }
}
