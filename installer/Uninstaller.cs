using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
namespace VeyroSetup;
internal static class Uninstaller
{
    internal static void Remove(IProgress<string> progress)
    {
        using var mutex=new System.Threading.Mutex(false,@"Local\Veyro.Setup");
        if(!mutex.WaitOne(0))throw new IOException("Дождитесь завершения установки Veyro.");
        try { RemoveCore(progress); }
        finally { mutex.ReleaseMutex(); }
    }
    private static void RemoveCore(IProgress<string> progress)
    {
        using var key=Registry.CurrentUser.OpenSubKey(Installer.ProductKey);
        var directory=key?.GetValue("InstallLocation") as string;
        var self=Environment.ProcessPath!;
        if(string.IsNullOrWhiteSpace(directory)||!string.Equals(Path.GetFullPath(Path.Combine(directory,"Uninstall-Veyro.exe")),Path.GetFullPath(self),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Запустите удаление из папки установленного Veyro или настроек приложений Windows.");
        var app=Path.Combine(directory,"Veyro.exe");
        progress.Report("Закрываем приложение…");
        foreach(var process in Process.GetProcessesByName("Veyro"))
        {
            using(process)
            {
                string? path;
                try {path=process.MainModule?.FileName;}catch(System.ComponentModel.Win32Exception){continue;}
                if(!string.Equals(path,app,StringComparison.OrdinalIgnoreCase))continue;
                process.Kill();
                if(!process.WaitForExit(5000))throw new IOException("Закройте Veyro и повторите удаление.");
            }
        }
        progress.Report("Удаляем файлы и ярлыки…");
        File.Delete(app);
        foreach(var folder in new[]{Environment.SpecialFolder.Programs,Environment.SpecialFolder.DesktopDirectory})
        {
            var link=Path.Combine(Environment.GetFolderPath(folder),"Veyro.lnk");
            if(File.Exists(link))File.Delete(link);
        }
        File.Delete(Path.Combine(directory,"Uninstall-Veyro.ps1"));
        using var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true);
        if(run?.GetValue("Veyro") as string=='"'+app+'"')run.DeleteValue("Veyro",false);
        Registry.CurrentUser.DeleteSubKeyTree(Installer.ProductKey,false);
        // The running uninstaller is released only after its result window closes.
        static string Quote(string text)=>"'"+text.Replace("'","''")+"'";
        var script=$$"""
Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
$target = {{Quote(self)}}
$folder = {{Quote(directory)}}
if ([IO.Path]::GetDirectoryName($target) -eq $folder -and [IO.Path]::GetFileName($target) -eq 'Uninstall-Veyro.exe') {
    for ($attempt=0; $attempt -lt 20; $attempt++) {
        try { Remove-Item -LiteralPath $target -Force -ErrorAction Stop; break } catch { Start-Sleep -Milliseconds 500 }
    }
    if ((Get-ChildItem -LiteralPath $folder -Force | Measure-Object).Count -eq 0) { Remove-Item -LiteralPath $folder }
}
""";
        var executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe");
        var start=new ProcessStartInfo(executable)
        {
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        Process.Start(start)?.Dispose();
    }
}


