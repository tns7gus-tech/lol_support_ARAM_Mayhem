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
using Microsoft.Win32;

namespace LSA.App;

/// <summary>
/// ?ㅻ쾭?덉씠 酉곕え????利앷컯 異붿쿇 ?쒖떆??
/// </summary>
public class AugmentViewModel
{
    private static readonly SolidColorBrush STierBrush = CreateFrozenBrush(0xFF, 0xD7, 0x00);
    private static readonly SolidColorBrush ATierBrush = CreateFrozenBrush(0x7B, 0x68, 0xEE);
    private static readonly SolidColorBrush BTierBrush = CreateFrozenBrush(0x4E, 0xCD, 0xC4);
    private static readonly SolidColorBrush CTierBrush = CreateFrozenBrush(0x80, 0x80, 0x80);

    public string AugmentId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tier { get; set; } = "C";
    public string TagsText { get; set; } = "";
    public string ReasonText { get; set; } = "";
    public bool IsSelected { get; set; }

    /// <summary>?곗뼱蹂??됱긽 釉뚮윭??/summary>
    public SolidColorBrush TierBrush => Tier switch
    {
        "S" => STierBrush,
        "A" => ATierBrush,
        "B" => BTierBrush,
        _ => CTierBrush
    };

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// ?꾩씠??酉곕え??
/// </summary>
public class ItemViewModel
{
    public int ItemId { get; set; }
    public string Name { get; set; } = "";
    public string Reason { get; set; } = "";
}

/// <summary>
/// ?ㅻ쾭?덉씠 ?덈룄?????щ챸/TopMost/?쒕옒洹?媛??
/// Phase 2: ?대깽??湲곕컲 ?낅뜲?댄듃 + ?대쭅 fallback + ?곌껐 ?곹깭 ?쒖떆
/// </summary>
public partial class OverlayWindow : Window
{
    // Win32 ???대┃ ?듦낵 紐⑤뱶
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const double DefaultLeft = 80;
    private const double DefaultTop = 80;

    // ?쒕퉬??
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OverlayWindow> _logger;
    private readonly DataService _dataService;
    private readonly RecommendationService _recommendationService;
    private readonly HotKeyService _hotKeyService;
    private IGameStateProvider? _provider;
    private Action<GamePhase>? _onPhaseChangedHandler;
    private Action<int?>? _onChampionChangedHandler;
    private Action<bool>? _onConnectionChangedHandler;
    private MockProvider? _mockProvider; // Mock ?꾩슜 湲곕뒫 ?묎렐??
    private CancellationTokenSource? _appCts;

    // ?곹깭
    private bool _isClickThrough;
    private bool _isCollapsed;
    private GamePhase _currentPhase = GamePhase.None;
    private int? _currentChampionId;
    private RecommendationResult? _currentRecommendation;
    private bool _freezeRecommendationsInGame;
    private readonly List<string> _selectedAugmentIds = new();
    private readonly List<string> _connectionLogs = new();
    private const int MaxConnectionLogLines = 50;
    private int _lastLcuLogCount;

    // Phase 2: fallback ?대쭅 ??대㉧ (媛꾧꺽 5珥???WebSocket ?쒖꽦 ??蹂댁“ ??븷)
    private DispatcherTimer? _fallbackPollTimer;

    // ?곌껐 ?곹깭 ?됱긽
    private static readonly SolidColorBrush _connGreen = new(Color.FromRgb(0x4C, 0xAF, 0x50));  // WebSocket
    private static readonly SolidColorBrush _connYellow = new(Color.FromRgb(0xFF, 0xC1, 0x07)); // REST
    private static readonly SolidColorBrush _connRed = new(Color.FromRgb(0xF4, 0x43, 0x36));    // 誘몄뿰寃?
    private static readonly SolidColorBrush _connPurple = new(Color.FromRgb(0xAB, 0x47, 0xBC)); // Mock

    public OverlayWindow()
    {
        InitializeComponent();

        // 濡쒓굅 ?⑺넗由??앹꽦
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(LogLevel.Information)
                .AddConsole();
        });

        // ?쒕퉬??珥덇린??
        _dataService = new DataService(_loggerFactory.CreateLogger<DataService>());
        _recommendationService = new RecommendationService(
            _dataService, _loggerFactory.CreateLogger<RecommendationService>());
        _hotKeyService = new HotKeyService();
        _logger = _loggerFactory.CreateLogger<OverlayWindow>();
    }

    /// <summary>
    /// ?덈룄??濡쒕뱶 ?꾨즺 ???쒕퉬??珥덇린??+ ?ロ궎 ?깅줉 + Provider ?곌껐 + 紐⑤땲?곕쭅 ?쒖옉
    /// </summary>
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _appCts = new CancellationTokenSource();
        AppendConnectionLog("App started");
        SyncClickThroughStateFromWindowStyle();
        UpdateClickThroughUI();

        // ?ㅼ젙 濡쒕뱶
        await _dataService.LoadConfigAsync();
        await _dataService.LoadKnowledgeBaseAsync();

        // ?ㅼ젙?먯꽌 ?꾩튂 蹂듭썝
        Left = _dataService.Config.Overlay.X;
        Top = _dataService.Config.Overlay.Y;
        EnsureWindowInsideVirtualBounds(logReason: "startup");
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // ?ロ궎 ?깅줉
        _hotKeyService.OnToggleOverlay += ToggleOverlay;
        _hotKeyService.OnToggleClickThrough += ToggleClickThrough;
        _hotKeyService.OnDevCyclePhase += DevCyclePhase;
        _hotKeyService.Register(this, _dataService.Config.Hotkeys);

        // Provider ?곌껐 + ?대깽??援щ룆
        await ConnectProviderAsync();

        // 紐⑤땲?곕쭅 ?쒖옉 (WebSocket + ?꾨줈?몄뒪 媛먯떆)
        if (_provider != null)
        {
            await _provider.StartMonitoringAsync(_appCts.Token);
        }

        // Fallback ?대쭅 ??대㉧ (5珥?媛꾧꺽 ??WebSocket 蹂댁“)
        _fallbackPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _fallbackPollTimer.Tick += async (s, args) => await FallbackPollAsync();
        _fallbackPollTimer.Start();

        // 珥덇린 ?곹깭 媛깆떊
        await FallbackPollAsync();
        EnsureOverlayOnTop();
        AppendConnectionLog("Overlay topmost reinforce enabled");
    }

    /// <summary>
    /// Provider ?곌껐 + ?대깽??援щ룆 ??LCU ?쒕룄 ???ㅽ뙣 ??Mock fallback
    /// </summary>
    private async Task ConnectProviderAsync()
    {
        AppendConnectionLog("Trying LCU provider...");
        if (!string.IsNullOrWhiteSpace(_dataService.Config.Lol.InstallPath))
        {
            AppendConnectionLog($"Config path: {_dataService.Config.Lol.InstallPath}");
        }

        var lcuProvider = new LcuProvider(
            _loggerFactory.CreateLogger<LcuProvider>(),
            _dataService.Config.Lol.InstallPath);
        _lastLcuLogCount = 0;

        if (await lcuProvider.TryConnectAsync())
        {
            AppendLcuLogs(lcuProvider);
            AppendConnectionLog("LCU connected");
            _provider = lcuProvider;
            SubscribeProviderEvents(_provider);
            MockBadge.Visibility = Visibility.Collapsed;
            UpdateConnectionUI(true, lcuProvider.IsWebSocketConnected);
            return;
        }

        AppendLcuLogs(lcuProvider);
        AppendConnectionLog("LCU connect failed");

        if (_dataService.Config.App.UseMockWhenLcuMissing)
        {
            _mockProvider = new MockProvider(_dataService, _loggerFactory.CreateLogger<MockProvider>());
            await _mockProvider.TryConnectAsync();
            _provider = _mockProvider;
            SubscribeProviderEvents(_provider);
            MockBadge.Visibility = Visibility.Visible;
            UpdateConnectionUI_Mock();
            AppendConnectionLog("Fallback to MOCK");
        }
        else
        {
            UpdateConnectionUI(false, false);
            AppendConnectionLog("MOCK fallback disabled");
        }
    }
    /// <summary>
    /// Provider ?대깽??援щ룆 ??Phase/Champion/Connection 蹂寃?利됱떆 諛섏쓳
    /// </summary>
    private void SubscribeProviderEvents(IGameStateProvider provider)
    {
        _onPhaseChangedHandler = phase =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplyPhaseUpdate(phase);
            });
        };

        _onChampionChangedHandler = champId =>
        {
            Dispatcher.BeginInvoke(async () =>
            {
                if (champId != _currentChampionId)
                {
                    if (_freezeRecommendationsInGame)
                    {
                        return;
                    }

                    _currentChampionId = champId;
                    if (_currentChampionId.HasValue)
                    {
                        await UpdateRecommendationsAsync();
                    }
                    else
                    {
                        ClearRecommendationsUI();
                    }
                }
            });
        };

        _onConnectionChangedHandler = connected =>
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
                    ApplyPhaseUpdate(GamePhase.None);
                }
            });
        };

        provider.OnPhaseChanged += _onPhaseChangedHandler;
        provider.OnChampionChanged += _onChampionChangedHandler;
        provider.OnConnectionChanged += _onConnectionChangedHandler;
    }

    private void UnsubscribeProviderEvents(IGameStateProvider provider)
    {
        if (_onPhaseChangedHandler != null)
            provider.OnPhaseChanged -= _onPhaseChangedHandler;

        if (_onChampionChangedHandler != null)
            provider.OnChampionChanged -= _onChampionChangedHandler;

        if (_onConnectionChangedHandler != null)
            provider.OnConnectionChanged -= _onConnectionChangedHandler;

        _onPhaseChangedHandler = null;
        _onChampionChangedHandler = null;
        _onConnectionChangedHandler = null;
    }

    // ===== ?곌껐 ?곹깭 UI =====

    /// <summary>
    /// ?곌껐 ?곹깭 ?몃뵒耳?댄꽣 ?낅뜲?댄듃 ???윟WS / ?윞REST / ?뵶誘몄뿰寃?
    /// </summary>
    private void UpdateConnectionUI(bool connected, bool isWebSocket)
    {
        if (!connected)
        {
            ConnIndicator.Fill = _connRed;
            ConnText.Text = "Disconnected";
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
    /// Mock 紐⑤뱶 ?몃뵒耳?댄꽣 ???윢
    /// </summary>
    private void UpdateConnectionUI_Mock()
    {
        ConnIndicator.Fill = _connPurple;
        ConnText.Text = "MOCK";
    }

    private void AppendLcuLogs(LcuProvider provider)
    {
        var logs = provider.ConnectionLog;
        if (logs.Count < _lastLcuLogCount)
        {
            _lastLcuLogCount = 0;
        }

        for (var i = _lastLcuLogCount; i < logs.Count; i++)
        {
            AppendConnectionLog(logs[i]);
        }

        _lastLcuLogCount = logs.Count;
    }

    private void AppendConnectionLog(string text)
    {
        _connectionLogs.Add(text);
        if (_connectionLogs.Count > MaxConnectionLogLines)
        {
            _connectionLogs.RemoveAt(0);
        }

        ConnectionLogTextBox.Text = string.Join(Environment.NewLine, _connectionLogs);
        ConnectionLogTextBox.CaretIndex = ConnectionLogTextBox.Text.Length;
        ConnectionLogTextBox.ScrollToEnd();
    }

    // ===== Fallback ?대쭅 =====

    /// <summary>
    /// Fallback ?대쭅 ??WebSocket 蹂댁“ (5珥?媛꾧꺽)
    /// WebSocket???쒖꽦?대㈃ ?곌껐 ?곹깭 UI留?媛깆떊
    /// </summary>
    private async Task FallbackPollAsync()
    {
        if (_provider == null) return;
        EnsureOverlayOnTop();

        if (_provider is LcuProvider lcuProvider)
        {
            AppendLcuLogs(lcuProvider);
        }

        try
        {
            // ?곌껐 ?곹깭 UI 媛깆떊
            if (_mockProvider != null)
            {
                UpdateConnectionUI_Mock();
            }
            else
            {
                UpdateConnectionUI(_provider.IsConnected, _provider.IsWebSocketConnected);
            }

            // WebSocket???쒖꽦?대㈃ ?곗씠???대쭅? ?ㅽ궢 (?대깽?몃줈 ?대? ?섏떊 以?
            if (_provider.IsWebSocketConnected) return;

            // REST fallback ?대쭅
            var phase = await _provider.GetPhaseAsync();

            if (phase != _currentPhase)
            {
                ApplyPhaseUpdate(phase);
            }

            if (_freezeRecommendationsInGame)
            {
                return;
            }

            var champId = await _provider.GetMyChampionIdAsync();
            if (champId != _currentChampionId && champId.HasValue)
            {
                _currentChampionId = champId;
                await UpdateRecommendationsAsync();
            }
            else if (champId != _currentChampionId)
            {
                _currentChampionId = champId;
                ClearRecommendationsUI();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fallback ?대쭅 以??ㅻ쪟");
        }
    }

    /// <summary>
    /// Phase???곕Ⅸ UI ?꾪솚
    /// </summary>
    private void ApplyPhaseUpdate(GamePhase phase)
    {
        _currentPhase = phase;

        if (phase == GamePhase.InProgress && _currentRecommendation != null && !_freezeRecommendationsInGame)
        {
            _freezeRecommendationsInGame = true;
            AppendConnectionLog("Recommendation freeze ON (in-game)");
        }
        else if (phase is GamePhase.ChampSelect or GamePhase.Lobby or GamePhase.EndOfGame)
        {
            if (_freezeRecommendationsInGame)
            {
                AppendConnectionLog("Recommendation freeze OFF");
            }
            _freezeRecommendationsInGame = false;
        }

        UpdatePhaseUI();
    }

    /// <summary>
    /// Phase???곕Ⅸ UI ?꾪솚
    /// </summary>
    private void UpdatePhaseUI()
    {
        var phaseText = _currentPhase switch
        {
            GamePhase.None => "대기 중...",
            GamePhase.Lobby => "Lobby",
            GamePhase.ChampSelect => "챔피언 선택",
            GamePhase.InProgress => "In Progress",
            GamePhase.EndOfGame => "게임 종료",
            _ => "알 수 없음"
        };
        PhaseText.Text = phaseText;

        ContentPanel.Visibility = _currentPhase switch
        {
            GamePhase.ChampSelect or GamePhase.InProgress => Visibility.Visible,
            _ => Visibility.Visible // MVP?먯꽌????긽 ?쒖떆
        };
    }

    /// <summary>
    /// 異붿쿇 ?곗씠??媛깆떊
    /// </summary>
    private async Task UpdateRecommendationsAsync()
    {
        if (_currentChampionId == null || _freezeRecommendationsInGame) return;

        List<string>? enemyTags = null;
        try
        {
            var enemyIds = await _provider!.GetEnemyChampionIdsAsync();
            if (enemyIds.Any())
            {
                enemyTags = DeriveEnemyTags(enemyIds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "??梨뷀뵾??議고쉶 ?ㅽ뙣 ??enemyTags ?앸왂");
        }

        _currentRecommendation = _recommendationService.GetRecommendations(
            _currentChampionId.Value, enemyTags);

        ChampionText.Text = _currentRecommendation.ChampionName;

        UpdateAugmentUI(_currentRecommendation.Augments.Take(8).ToList());
        UpdateItemUI(_currentRecommendation.Items);

        _selectedAugmentIds.Clear();
        AugmentSelectHint.Visibility = Visibility.Visible;
    }

    private void ClearRecommendationsUI()
    {
        _currentRecommendation = null;
        ChampionText.Text = "-";
        AugmentList.ItemsSource = null;
        CoreItemList.ItemsSource = null;
        SituationalItemList.ItemsSource = null;
        _selectedAugmentIds.Clear();
        AugmentSelectHint.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// ??梨뷀뵾??ID ???쒓렇 蹂??(knowledge_base 湲곕컲)
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
    /// 利앷컯 UI ?낅뜲?댄듃
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
    /// ?꾩씠??UI ?낅뜲?댄듃
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
    /// 利앷컯 ?대┃ ??"?꾩옱 3媛?利앷컯 ?좏깮" 湲곕뒫
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

    // ===== ?ロ궎 ?몃뱾??=====

    /// <summary>Ctrl+Shift+O ???ㅻ쾭?덉씠 ?쒖떆/?④? ?좉?</summary>
    private void ToggleOverlay()
    {
        Dispatcher.Invoke(() =>
        {
            Visibility = Visibility == Visibility.Visible
                ? Visibility.Hidden
                : Visibility.Visible;

            if (Visibility == Visibility.Visible)
            {
                EnsureWindowInsideVirtualBounds(logReason: "show");
                EnsureOverlayOnTop();
            }
        });
    }

    /// <summary>Ctrl+Shift+C ???대┃ ?듦낵 ?좉?</summary>
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

            EnsureOverlayOnTop();
            UpdateClickThroughUI();
            AppendConnectionLog($"Click-through {(_isClickThrough ? "ON" : "OFF")}");
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            EnsureWindowInsideVirtualBounds(logReason: "display changed");
            EnsureOverlayOnTop();
        });
    }

    private void EnsureWindowInsideVirtualBounds(string? logReason = null)
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var virtualHeight = SystemParameters.VirtualScreenHeight;

        var maxLeft = virtualLeft + Math.Max(0, virtualWidth - width);
        var maxTop = virtualTop + Math.Max(0, virtualHeight - height);

        var currentLeft = double.IsFinite(Left) ? Left : DefaultLeft;
        var currentTop = double.IsFinite(Top) ? Top : DefaultTop;

        var clampedLeft = Math.Clamp(currentLeft, virtualLeft, maxLeft);
        var clampedTop = Math.Clamp(currentTop, virtualTop, maxTop);

        if (!AreClose(Left, clampedLeft) || !AreClose(Top, clampedTop))
        {
            Left = clampedLeft;
            Top = clampedTop;
            if (!string.IsNullOrWhiteSpace(logReason))
            {
                AppendConnectionLog($"Overlay repositioned ({logReason})");
            }
        }
    }

    private static bool AreClose(double a, double b)
    {
        return Math.Abs(a - b) < 0.5;
    }

    private void EnsureOverlayOnTop()
    {
        if (Visibility != Visibility.Visible)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(
            hwnd,
            HWND_TOPMOST,
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private void SyncClickThroughStateFromWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            _isClickThrough = false;
            return;
        }

        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        _isClickThrough = (exStyle & WS_EX_TRANSPARENT) != 0;
    }

    private void UpdateClickThroughUI()
    {
        ClickThroughText.Text = _isClickThrough ? "CT ON" : "CT OFF";
        ClickThroughText.Foreground = _isClickThrough ? _connGreen : _connRed;
    }

    /// <summary>Ctrl+Shift+P ??[媛쒕컻?? Mock Phase ?쒗솚</summary>
    private void DevCyclePhase()
    {
        if (_mockProvider != null)
        {
            // Phase 2: Mock CyclePhase()媛 ?대깽?몃? 諛쒖깮?쒗궎誘濡?
            // 蹂꾨룄 PollGameState ?몄텧 遺덊븘?????대깽??援щ룆??泥섎━??
            _mockProvider.CyclePhase();
        }
    }

    /// <summary>?묎린/?쇱튂湲?踰꾪듉</summary>
    private void CollapseBtn_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = !_isCollapsed;
        ContentPanel.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CollapseBtn.Content = _isCollapsed ? "+" : "-";
        Height = _isCollapsed ? 80 : 600;
    }

    private void ExitBtn_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>?쒕옒洹??대룞</summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    /// <summary>李??ロ옄 ????紐⑤땲?곕쭅 以묒? + ?꾩튂 ???+ 由ъ냼???댁젣</summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        // ??痍⑥냼 ?좏겙 ?댁젣
        _appCts?.Cancel();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        // 紐⑤땲?곕쭅 以묒?
        if (_provider != null)
        {
            await _provider.StopMonitoringAsync();
            UnsubscribeProviderEvents(_provider);
        }

        // ?꾩튂 ???
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




