using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace VeyroSetup;
public partial class SetupWindow : Window
{
    private bool installing;
    private bool installed;
    private string? installedPath;
    public SetupWindow()
    {
        InitializeComponent();
        InstallPath.Text = Installer.DefaultDirectory();
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
        Heading.Text = "Устанавливаем Veyro";
        Description.Text = "Это займёт немного времени.";
        InstallButton.Content = "Установка…";
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
            installedPath = await Task.Run(() => Installer.Install(directory, autostart, desktop, progress));
            installed = true;
            Heading.Text = "Veyro установлен";
            Description.Text = "Откройте микшер через значок в области уведомлений.";
            Options.Visibility = Visibility.Collapsed;
            Progress.IsIndeterminate = false;
            Progress.Value = 100;
            ProgressText.Text = autostart ? "Автозапуск Windows включён" : "Автозапуск Windows выключен";
            InstallButton.Content = "Запустить Veyro  →";
            Footer.Text = "Удаление доступно в настройках приложений Windows";
        }
        catch (Exception error)
        {
            ErrorText.Text = "Установка не завершена. " + error.Message;
            ProgressPanel.Visibility = Visibility.Collapsed;
            Options.Visibility = Visibility.Visible;
            Heading.Text = "Повторить установку";
            Description.Text = "Проверьте параметры и попробуйте ещё раз.";
            InstallButton.Content = "Установить Veyro →";
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

