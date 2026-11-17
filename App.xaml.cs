using System;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Veyro;

public partial class App : Application
{
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private TrayMenuWindow? trayMenu;
    private MainWindow? mixer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        using var stream = GetResourceStream(new Uri("assets/app.ico", UriKind.Relative)).Stream;
        using var resourceIcon = new System.Drawing.Icon(stream);
        trayIcon = new System.Drawing.Icon(resourceIcon, 32, 32);
        tray = new Forms.NotifyIcon { Text = "Veyro — микшер громкости", Icon = trayIcon, Visible = true };
        tray.MouseClick += (_, mouse) =>
        {
            if (mouse.Button == Forms.MouseButtons.Left)
                Dispatcher.Invoke(() => GetMixer().ToggleMixer());
            if (mouse.Button == Forms.MouseButtons.Right)
                Dispatcher.Invoke(() =>
                {
                    trayMenu?.Close();
                    trayMenu = new TrayMenuWindow(
                        () => GetMixer().OpenMixer(),
                        () => { if (mixer is null) Shutdown(); else mixer.ExitMixer(); });
                    trayMenu.Closed += (_, _) => trayMenu = null;
                    trayMenu.Show();
                });
        };

    }

    private MainWindow GetMixer()
    {
        if (mixer is null)
        {
            mixer = new MainWindow();
            MainWindow = mixer;
        }
        return mixer;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose();
        trayMenu?.Close();
        trayIcon?.Dispose();
        base.OnExit(e);
    }
}




