using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Veyro;

public partial class MainWindow
{
    private readonly IMixerShortcut shortcut;
    private bool recordingShortcut;

    internal void OpenFromShortcut()
    {
        if (recordingShortcut) CancelShortcutCapture();
        else if (themeStudio is not null) themeStudio.Activate();
        else OpenMixer();
    }

    private void UpdateShortcutLabel()
    {
        if (preferences.ShortcutKey == 0) { ShortcutButton.Content = "Назначить сочетание"; return; }
        var key = KeyInterop.KeyFromVirtualKey(preferences.ShortcutKey);
        var modifiers = (ModifierKeys)(preferences.ShortcutModifiers & 15);
        var prefix = new ModifierKeysConverter().ConvertToString(modifiers);
        ShortcutButton.Content = string.IsNullOrEmpty(prefix) ? key.ToString() : prefix + "+" + key;
    }

    private void StartShortcutCapture(object sender, RoutedEventArgs e)
    {
        ShortcutButton.Focus();
        recordingShortcut = true;
        ShortcutButton.Content = "Нажмите сочетание…";
        ShortcutHint.Text = "Например, Ctrl+Alt+M или F8. Esc — отмена.";
    }

    private void CancelShortcutCapture()
    {
        if (!recordingShortcut) return;
        recordingShortcut = false;
        ShortcutHint.Text = "";
        UpdateShortcutLabel();
    }

    private void ShortcutLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CancelShortcutCapture();

    private bool CaptureShortcut(KeyEventArgs e)
    {
        if (!recordingShortcut) return false;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { CancelShortcutCapture(); return true; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return true;
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None && key is not (>= Key.F1 and <= Key.F24))
        {
            ShortcutHint.Text = "Добавьте Ctrl, Alt, Shift или Win либо выберите функциональную клавишу.";
            return true;
        }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk != 0) _ = SaveShortcutAsync(vk, (int)modifiers);
        return true;
    }

    private bool assigningShortcut;
    private async Task SaveShortcutAsync(int key, int modifiers)
    {
        if (assigningShortcut) return;
        assigningShortcut = true;
        bool ok;
        try { ok = await shortcut.SetAsync(key, modifiers); }
        finally { assigningShortcut = false; }
        if (!ok)
        {
            ShortcutHint.Text = "Windows не удалось назначить это сочетание. Попробуйте другое.";
            return;
        }
        preferences = preferences with { ShortcutKey = key, ShortcutModifiers = modifiers };
        CancelShortcutCapture();
        ShortcutHint.Text = "";
        UpdateShortcutLabel();
        SavePreferences();
    }

    private async void ClearShortcut(object sender, RoutedEventArgs e) => await SaveShortcutAsync(0, 0);
}
