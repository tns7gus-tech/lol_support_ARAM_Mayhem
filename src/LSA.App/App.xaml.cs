using System.Windows;
using Microsoft.Extensions.Logging;

namespace LSA.App;

/// <summary>
/// 앱 진입점 — 서비스 초기화 + 오버레이 윈도우 실행
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 전역 예외 처리 — 앱 크래시 방지 (게임 영향 0)
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"예기치 않은 오류가 발생했습니다.\n{args.Exception.Message}",
                "LSA 오류",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            args.Handled = true;
        };
    }
}
