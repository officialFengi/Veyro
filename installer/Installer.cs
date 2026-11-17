using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VeyroSetup;

internal static class Installer
{
    internal const string ProductKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Veyro";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string DefaultDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProductKey);
        return key?.GetValue("InstallLocation") as string ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Veyro");
    }

    internal static string Install(string directory, bool autostart, bool desktop, IProgress<string> progress)
    {
        directory = Path.GetFullPath(directory.Trim());
        if (directory == Path.GetPathRoot(directory)) throw new IOException("Выберите отдельную папку для приложения.");
        using var mutex = new System.Threading.Mutex(false, @"Local\Veyro.Setup");
        if (!mutex.WaitOne(0)) throw new IOException("Другая установка Veyro уже выполняется.");
        try { return InstallCore(directory, autostart, desktop, progress); }
        finally { mutex.ReleaseMutex(); }
    }

    private static string InstallCore(string directory, bool autostart, bool desktop, IProgress<string> progress)
    {
        var previousDirectory = DefaultDirectory();
        using var oldProduct = Registry.CurrentUser.OpenSubKey(ProductKey);
        if (oldProduct is not null && !string.Equals(Path.GetFullPath(previousDirectory), directory, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Veyro уже установлен. Для обновления используйте папку " + previousDirectory);
        var appPath = Path.Combine(directory, "Veyro.exe");
        var uninstallPath = Path.Combine(directory, "Uninstall-Veyro.exe");
        var startLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Veyro.lnk");
        var desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Veyro.lnk");
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, ".veyro-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var backups = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[] { appPath, uninstallPath, startLink, desktopLink })
        {
            var backup = File.Exists(path) ? Path.Combine(staging, Guid.NewGuid().ToString("N")) : null;
            if (backup is not null) File.Copy(path, backup);
            backups[path] = backup;
        }
        var registryValues = oldProduct?.GetValueNames().ToDictionary(name => name,
            name => (oldProduct.GetValue(name), oldProduct.GetValueKind(name)));
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        var oldRun = run.GetValue("Veyro");
        try
        {
            progress.Report("Распаковываем приложение…");
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Veyro.Payload")!)
            using (var zip = new ZipArchive(stream))
            {
                var payload = zip.GetEntry("Veyro.exe") ?? throw new IOException("В установщике отсутствует приложение.");
                payload.ExtractToFile(Path.Combine(staging, "Veyro.exe"));
                var removal = zip.GetEntry("Uninstall-Veyro.exe") ?? throw new IOException("В установщике отсутствует программа удаления.");
                removal.ExtractToFile(Path.Combine(staging, "Uninstall-Veyro.exe"));
            }
            progress.Report("Обновляем файлы Veyro…");
            foreach (var process in Process.GetProcessesByName("Veyro"))
            {
                using (process)
                {
                    string? processPath;
                    try { processPath = process.MainModule?.FileName; }
                    catch (System.ComponentModel.Win32Exception) { continue; }
                    if (!string.Equals(processPath, appPath, StringComparison.OrdinalIgnoreCase)) continue;
                    process.Kill();
                    if (!process.WaitForExit(5000)) throw new IOException("Закройте Veyro и повторите установку.");
                }
            }
            File.Move(Path.Combine(staging, "Veyro.exe"), appPath, true);
            File.Move(Path.Combine(staging, "Uninstall-Veyro.exe"), uninstallPath, true);
            progress.Report("Добавляем ярлыки и параметры запуска…");
            CreateShortcut(startLink, appPath);
            if (desktop) CreateShortcut(desktopLink, appPath);
            else if (File.Exists(desktopLink)) File.Delete(desktopLink);
            if (autostart) run.SetValue("Veyro", '"' + appPath + '"');
            else run.DeleteValue("Veyro", false);
            using var product = Registry.CurrentUser.CreateSubKey(ProductKey);
            product.SetValue("DisplayName", "Veyro");
            product.SetValue("DisplayVersion", "1.0.0");
            product.SetValue("Publisher", "Fengible");
            product.SetValue("InstallLocation", directory);
            product.SetValue("DisplayIcon", appPath);
            var command = "\"" + uninstallPath + "\"";
            product.SetValue("UninstallString", command);
            product.SetValue("QuietUninstallString", command + " --quiet");
            product.SetValue("NoModify", 1, RegistryValueKind.DWord);
            product.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            product.SetValue("EstimatedSize", (int)((new FileInfo(appPath).Length + new FileInfo(uninstallPath).Length) / 1024), RegistryValueKind.DWord);
            return appPath;
        }
        catch
        {
            foreach (var pair in backups)
            {
                if (pair.Value is not null) File.Copy(pair.Value, pair.Key, true);
                else if (File.Exists(pair.Key)) File.Delete(pair.Key);
            }
            if (oldRun is not null) run.SetValue("Veyro", oldRun);
            else run.DeleteValue("Veyro", false);
            if (registryValues is null) Registry.CurrentUser.DeleteSubKeyTree(ProductKey, false);
            else
            {
                using var product = Registry.CurrentUser.CreateSubKey(ProductKey);
                foreach (var name in product.GetValueNames()) product.DeleteValue(name);
                foreach (var pair in registryValues) product.SetValue(pair.Key, pair.Value.Item1!, pair.Value.Item2);
            }
            throw;
        }
        finally
        {
            // Only generated staging files are removed; never recurse into the chosen install directory.
            foreach (var file in Directory.EnumerateFiles(staging)) File.Delete(file);
            Directory.Delete(staging);
        }
    }

    private static void CreateShortcut(string path, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var type = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic shortcut = shell.CreateShortcut(path);
        try
        {
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.IconLocation = target + ",0";
            shortcut.Description = "Veyro — микшер громкости";
            shortcut.Save();
        }
        finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    }

}

