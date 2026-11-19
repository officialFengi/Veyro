using System;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Veyro;

internal interface IMixerShortcut
{
    bool IsRegistered { get; }
    Task<bool> SetAsync(int key, int modifiers);
}

internal sealed class ShortcutRegistration : NativeWindow, IMixerShortcut, IDisposable
{
    private int registeredId;
    private int key;
    private int modifiers;
    public bool IsRegistered => registeredId != 0;
    internal event Action? Pressed;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr window, int id, int modifiers, int key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    public bool TrySet(int virtualKey, int modifierKeys)
    {
        if (IsRegistered && key == virtualKey && modifiers == modifierKeys) return true;
        if (Handle == IntPtr.Zero) CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
        int next = registeredId == 1 ? 2 : 1;
        if (!RegisterHotKey(Handle, next, modifierKeys | 0x4000, virtualKey)) return false;
        if (IsRegistered) UnregisterHotKey(Handle, registeredId);
        registeredId = next;
        key = virtualKey;
        modifiers = modifierKeys;
        return true;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && IsRegistered && message.WParam.ToInt32() == registeredId)
            Pressed?.Invoke();
        else base.WndProc(ref message);
    }

    public Task<bool> SetAsync(int key, int modifiers)
    {
        if (key != 0) return Task.FromResult(TrySet(key, modifiers));
        Clear();
        return Task.FromResult(true);
    }

    public void Clear() => Dispose();

    public void Dispose()
    {
        if (IsRegistered) UnregisterHotKey(Handle, registeredId);
        registeredId = 0;
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
