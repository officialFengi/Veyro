using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace VeyroSetup;
public partial class SetupWindow : Window
{
    private readonly bool updating;
    private bool installing;
    private bool installed;
    private string? installedPath;
    public SetupWindow()
    {
        InitializeComponent();
        var existing = Installer.InstalledDirectory();
        updating = existing is not null;
        InstallPath.Text = existing ?? Installer.DefaultDirectory;
        if (updating)
        {
            Title = "Обновление Veyro";
            ModeLabel.Text = "Обновление";
            Heading.Text = "Обновить установленный Veyro";
            Description.Text = $"Папка приложения — {existing}\nНастройки, автозапуск и ярлыки сохранятся.";
            InstallButton.Content = "Обновить Veyro  →";
            Options.Visibility = Visibility.Collapsed;
        }
        Closing += (_, e) => e.Cancel = installing;
    }
    private void DragHeader(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is System.Windows.Controls.TextBlock) DragMove(); }
    private void CloseSetup(object sender, RoutedEventArgs e) => Close();
    private void BrowseFolder(object sender, RoutedEventArgs e)
    {
        using var picker = new Forms.FolderBrowserDialog { Description = "Папка установки Veyro", UseDescriptionForTitle = true, SelectedPath = InstallPath.Text };
        if (picker.ShowDialog() == Forms.DialogResult.OK) InstallPath.Text = picker.SelectedPath;
    }
    private async void Install(object sender, RoutedEventArgs e)
    {
        if (installed)
        {
            try { Process.Start(new ProcessStartInfo(installedPath!) { UseShellExecute = true }); Close(); }
            catch (Exception error) { ErrorText.Text = error.Message; }
            return;
        }
        installing = true;
        CloseButton.IsEnabled = false;
        InstallButton.IsEnabled = false;
        Options.Visibility = Visibility.Collapsed;
        Heading.Text = updating ? "Обновляем Veyro" : "Устанавливаем Veyro";
        Description.Text = "Это займёт немного времени.";
        InstallButton.Content = updating ? "Обновление…" : "Установка…";
        Progress.IsIndeterminate = false;
        Progress.Value = 12;
        ProgressPanel.Visibility = Visibility.Visible;
        ErrorText.Text = "";
        var directory = InstallPath.Text;
        var autostart = Autostart.IsChecked == true;
        var desktop = DesktopShortcut.IsChecked == true;
        var progress = new Progress<string>(text =>
        {
            ProgressText.Text = text;
            Progress.Value = text.StartsWith("Распаковываем") ? 20 : text.StartsWith("Обновляем") ? 60 : 85;
        });
        try
        {
            installedPath = await Task.Run(() => Installer.Install(directory, updating, autostart, desktop, progress));
            installed = true;
            Heading.Text = updating ? "Veyro обновлён" : "Veyro установлен";
            Description.Text = "Откройте микшер через значок в области уведомлений.";
            Options.Visibility = Visibility.Collapsed;
            Progress.IsIndeterminate = false;
            Progress.Value = 100;
            ProgressText.Text = updating ? "Настройки сохранены" : autostart ? "Автозапуск Windows включён" : "Автозапуск Windows выключен";
            InstallButton.Content = "Запустить Veyro  →";
            Footer.Text = "Удаление доступно в настройках приложений Windows";
        }
        catch (Exception error)
        {
            ErrorText.Text = (updating ? "Обновление не завершено. " : "Установка не завершена. ") + error.Message;
            ProgressPanel.Visibility = Visibility.Collapsed;
            Options.Visibility = updating ? Visibility.Collapsed : Visibility.Visible;
            Heading.Text = updating ? "Повторить обновление" : "Повторить установку";
            Description.Text = "Проверьте параметры и попробуйте ещё раз.";
            InstallButton.Content = updating ? "Обновить Veyro →" : "Установить Veyro →";
        }
        finally
        {
            installing = false;
            InstallButton.IsEnabled = true;
            Options.IsEnabled = true;
            CloseButton.IsEnabled = true;
        }
    }
}

