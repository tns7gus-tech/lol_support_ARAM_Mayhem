using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using LSA.Data.Models;

namespace LSA.App.Services;

/// <summary>
/// 글로벌 핫키 서비스 — Win32 RegisterHotKey 기반
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
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CTRL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    // 기본값
    private const string DEFAULT_TOGGLE_OVERLAY = "Ctrl+Shift+O";
    private const string DEFAULT_TOGGLE_CLICKTHROUGH = "Ctrl+Shift+C";
    private const string DEFAULT_DEV_CYCLE_PHASE = "Ctrl+Shift+P";

    private IntPtr _windowHandle;
    private HwndSource? _source;

    // 이벤트
    public event Action? OnToggleOverlay;
    public event Action? OnToggleClickThrough;
    public event Action? OnDevCyclePhase;

    /// <summary>
    /// 핫키 등록 시작 — Window Loaded 이후 호출
    /// </summary>
    public void Register(Window window, HotkeyConfig? config = null)
    {
        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(HwndHook);

        RegisterWithFallback(HOTKEY_TOGGLE_OVERLAY, config?.ToggleOverlay, DEFAULT_TOGGLE_OVERLAY);
        RegisterWithFallback(HOTKEY_TOGGLE_CLICKTHROUGH, config?.ToggleClickThrough, DEFAULT_TOGGLE_CLICKTHROUGH);
        RegisterWithFallback(HOTKEY_DEV_CYCLE_PHASE, config?.DevCyclePhase, DEFAULT_DEV_CYCLE_PHASE);
    }

    private void RegisterWithFallback(int id, string? configured, string fallback)
    {
        var target = TryParseHotKey(configured, out var mods, out var vk)
            ? configured!
            : fallback;

        if (!TryParseHotKey(target, out mods, out vk))
            return;

        RegisterHotKey(_windowHandle, id, mods | MOD_NOREPEAT, vk);
    }

    private static bool TryParseHotKey(string? hotkey, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(hotkey))
            return false;

        var tokens = hotkey
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
            return false;

        foreach (var token in tokens[..^1])
        {
            switch (token.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                    modifiers |= MOD_CTRL;
                    break;
                case "SHIFT":
                    modifiers |= MOD_SHIFT;
                    break;
                case "ALT":
                    modifiers |= MOD_ALT;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    return false;
            }
        }

        var keyToken = tokens[^1].ToUpperInvariant();
        var converter = new KeyConverter();

        var keyObj = converter.ConvertFromString(keyToken);
        if (keyObj is not Key key)
            return false;

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0)
            return false;

        virtualKey = (uint)vk;
        return true;
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
