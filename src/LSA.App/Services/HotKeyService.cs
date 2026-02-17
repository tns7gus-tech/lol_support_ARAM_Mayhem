using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LSA.App.Services;

/// <summary>
/// 글로벌 핫키 서비스 — Win32 RegisterHotKey 기반
/// Ctrl+Shift+O: 오버레이 토글
/// Ctrl+Shift+C: 클릭 통과 토글
/// Ctrl+Shift+P: [개발용] Phase 순환
/// </summary>
public class HotKeyService : IDisposable
{
    // Win32 API
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // 핫키 ID 상수
    private const int HOTKEY_TOGGLE_OVERLAY = 9001;
    private const int HOTKEY_TOGGLE_CLICKTHROUGH = 9002;
    private const int HOTKEY_DEV_CYCLE_PHASE = 9003;

    // 수정자 키 플래그
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    // 가상 키 코드
    private const uint VK_O = 0x4F;
    private const uint VK_C = 0x43;
    private const uint VK_P = 0x50;

    private IntPtr _windowHandle;
    private HwndSource? _source;

    // 이벤트
    public event Action? OnToggleOverlay;
    public event Action? OnToggleClickThrough;
    public event Action? OnDevCyclePhase;

    /// <summary>
    /// 핫키 등록 시작 — Window Loaded 이후 호출
    /// </summary>
    public void Register(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(HwndHook);

        var mods = MOD_CTRL | MOD_SHIFT | MOD_NOREPEAT;
        RegisterHotKey(_windowHandle, HOTKEY_TOGGLE_OVERLAY, mods, VK_O);
        RegisterHotKey(_windowHandle, HOTKEY_TOGGLE_CLICKTHROUGH, mods, VK_C);
        RegisterHotKey(_windowHandle, HOTKEY_DEV_CYCLE_PHASE, mods, VK_P);
    }

    /// <summary>
    /// Win32 메시지 처리 후크
    /// </summary>
    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            switch (id)
            {
                case HOTKEY_TOGGLE_OVERLAY:
                    OnToggleOverlay?.Invoke();
                    handled = true;
                    break;
                case HOTKEY_TOGGLE_CLICKTHROUGH:
                    OnToggleClickThrough?.Invoke();
                    handled = true;
                    break;
                case HOTKEY_DEV_CYCLE_PHASE:
                    OnDevCyclePhase?.Invoke();
                    handled = true;
                    break;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source?.RemoveHook(HwndHook);
        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HOTKEY_TOGGLE_OVERLAY);
            UnregisterHotKey(_windowHandle, HOTKEY_TOGGLE_CLICKTHROUGH);
            UnregisterHotKey(_windowHandle, HOTKEY_DEV_CYCLE_PHASE);
        }
    }
}
