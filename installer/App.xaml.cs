using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
namespace VeyroSetup;
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (System.Reflection.Assembly.GetExecutingAssembly().GetName().Name == "Uninstall-Veyro")
        {
            if(e.Args.Contains("--quiet"))
            {
                try { await Task.Run(() => Uninstaller.Remove(new Progress<string>())); Shutdown(0); }
                catch { Shutdown(1); }
            }
            else new UninstallWindow().Show();
        }
        else new SetupWindow().Show();
    }
}
