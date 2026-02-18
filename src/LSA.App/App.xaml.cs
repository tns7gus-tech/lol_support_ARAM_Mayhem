using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace LSA.App;

/// <summary>
/// 앱 진입점 — 단일 인스턴스 보장 + 트레이 아이콘 + 오버레이 실행
/// </summary>
public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Global\LSA.App.Singleton";
    private Mutex? _singleInstanceMutex;
    private Forms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        var createdNew = false;
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "LSA가 이미 실행 중입니다. 시스템 트레이 아이콘을 확인하세요.",
                "LSA",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        Exit += OnAppExit;

        var window = new OverlayWindow();
        MainWindow = window;
        InitializeTrayIcon();
        window.Show();
    }

    private void InitializeTrayIcon()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        var icon = !string.IsNullOrWhiteSpace(exePath)
            ? Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application
            : SystemIcons.Application;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("오버레이 열기/숨기기", null, (_, _) => ToggleMainWindowVisibility());
        menu.Items.Add("종료", null, (_, _) => ShutdownFromTray());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "LSA 실행 중",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreMainWindow();
    }

    private void ToggleMainWindowVisibility()
    {
        if (MainWindow == null) return;

        MainWindow.Dispatcher.Invoke(() =>
        {
            if (MainWindow.Visibility == Visibility.Visible)
            {
                MainWindow.Hide();
            }
            else
            {
                MainWindow.Show();
                MainWindow.Activate();
            }
        });
    }

    private void RestoreMainWindow()
    {
        if (MainWindow == null) return;

        MainWindow.Dispatcher.Invoke(() =>
        {
            if (MainWindow.Visibility != Visibility.Visible)
            {
                MainWindow.Show();
            }
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        });
    }

    private void ShutdownFromTray()
    {
        Current.Shutdown();
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        System.Windows.MessageBox.Show(
            $"예기치 않은 오류가 발생했습니다.\n{args.Exception.Message}",
            "LSA 오류",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        args.Handled = true;
    }

    private void OnAppExit(object? sender, ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
    }
}
