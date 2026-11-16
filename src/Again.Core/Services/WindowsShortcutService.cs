using System.Windows.Input;
using Again.Core.Native;
using Microsoft.Extensions.Logging;

namespace Again.Core.Services;

public enum ShortcutAction
{
    CaptureWorkspace = 1,
    RestoreLastUsed = 2,
    ToggleShowHide = 3,
    QuickSelector = 4
}

public interface IShortcutManagementService : IDisposable
{
    void Initialize(IntPtr windowHandle);
    bool RegisterShortcut(ShortcutAction action, string shortcutKeyString);
    void UnregisterShortcut(ShortcutAction action);
    void UnregisterAll();
    void ProcessHotkeyMessage(int hotkeyId);
    event Action<ShortcutAction>? ShortcutTriggered;
}

public class WindowsShortcutService : IShortcutManagementService
{
    private IntPtr _windowHandle = IntPtr.Zero;
    private readonly Dictionary<ShortcutAction, int> _actionToId = new();
    private readonly Dictionary<int, ShortcutAction> _idToAction = new();
    private readonly ILogger<WindowsShortcutService>? _logger;
    private int _nextId = 9000;

    public event Action<ShortcutAction>? ShortcutTriggered;

    public WindowsShortcutService(ILogger<WindowsShortcutService>? logger = null)
    {
        _logger = logger;
    }

    public void Initialize(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _logger?.LogInformation("Shortcut service initialized with window handle {Hwnd}", windowHandle);
    }

    public bool RegisterShortcut(ShortcutAction action, string shortcutKeyString)
    {
        if (_windowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(shortcutKeyString))
            return false;

        UnregisterShortcut(action);

        if (!TryParseShortcut(shortcutKeyString, out uint modifiers, out uint vk))
        {
            _logger?.LogWarning("Invalid shortcut string format: {Shortcut}", shortcutKeyString);
            return false;
        }

        int id = _nextId++;
        modifiers |= NativeMethods.MOD_NOREPEAT;

        bool registered = NativeMethods.RegisterHotKey(_windowHandle, id, modifiers, vk);
        if (registered)
        {
            _actionToId[action] = id;
            _idToAction[id] = action;
            _logger?.LogInformation("Registered global shortcut {Shortcut} for action {Action} (ID: {Id})", 
                shortcutKeyString, action, id);
            return true;
        }
        else
        {
            _logger?.LogWarning("Failed to register global hotkey {Shortcut} for action {Action}", 
                shortcutKeyString, action);
            return false;
        }
    }

    public void UnregisterShortcut(ShortcutAction action)
    {
        if (_actionToId.TryGetValue(action, out int id))
        {
            if (_windowHandle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_windowHandle, id);
            }
            _actionToId.Remove(action);
            _idToAction.Remove(id);
        }
    }

    public void UnregisterAll()
    {
        if (_windowHandle == IntPtr.Zero) return;

        foreach (var id in _actionToId.Values)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, id);
        }
        _actionToId.Clear();
        _idToAction.Clear();
    }

    public void ProcessHotkeyMessage(int hotkeyId)
    {
        if (_idToAction.TryGetValue(hotkeyId, out var action))
        {
            _logger?.LogInformation("Global hotkey triggered: {Action}", action);
            ShortcutTriggered?.Invoke(action);
        }
    }

    public static bool TryParseShortcut(string text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        string keyPart = parts[^1];

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var part = parts[i].ToLowerInvariant();
            if (part is "ctrl" or "control")
                modifiers |= NativeMethods.MOD_CONTROL;
            else if (part is "alt")
                modifiers |= NativeMethods.MOD_ALT;
            else if (part is "shift")
                modifiers |= NativeMethods.MOD_SHIFT;
            else if (part is "win" or "windows")
                modifiers |= NativeMethods.MOD_WIN;
        }

        if (Enum.TryParse<Key>(keyPart, ignoreCase: true, out var key))
        {
            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            return vk != 0;
        }

        if (keyPart.Length == 1 && char.IsLetterOrDigit(keyPart[0]))
        {
            vk = char.ToUpperInvariant(keyPart[0]);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        UnregisterAll();
    }
}
