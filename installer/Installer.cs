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
    internal static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Veyro");

    internal static string? InstalledDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ProductKey);
        if (key?.GetValue("InstallLocation") is string directory && Path.IsPathFullyQualified(directory)
            && File.Exists(Path.Combine(directory, "Veyro.exe")))
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return File.Exists(Path.Combine(DefaultDirectory, "Veyro.exe")) ? DefaultDirectory : null;
    }

    internal static string Install(string directory, bool updating, bool autostart, bool desktop, IProgress<string> progress)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory.Trim()));
        if (directory == Path.GetPathRoot(directory)) throw new IOException("Выберите отдельную папку для приложения.");
        using var mutex = new System.Threading.Mutex(false, @"Local\Veyro.Setup");
        if (!mutex.WaitOne(0)) throw new IOException("Другая установка Veyro уже выполняется.");
        try { return InstallCore(directory, updating, autostart, desktop, progress); }
        finally { mutex.ReleaseMutex(); }
    }

    private static string InstallCore(string directory, bool updating, bool autostart, bool desktop, IProgress<string> progress)
    {
        var previousDirectory = InstalledDirectory();
        if (updating != (previousDirectory is not null) ||
            (updating && !string.Equals(previousDirectory, directory, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Состояние установки изменилось. Откройте установщик снова.");
        using var oldProduct = Registry.CurrentUser.OpenSubKey(ProductKey);
        var appPath = Path.Combine(directory, "Veyro.exe");
        var uninstallPath = Path.Combine(directory, "Uninstall-Veyro.exe");
        var startLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Veyro.lnk");
        var desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Veyro.lnk");
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, ".veyro-" + Guid.NewGuid().ToString("N"));
        var backups = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var changedFiles = new Stack<string>();
        var registering = false;
        var registryValues = oldProduct?.GetValueNames().ToDictionary(name => name,
            name => (oldProduct.GetValue(name), oldProduct.GetValueKind(name)));
        using var run = updating ? null : Registry.CurrentUser.CreateSubKey(RunKey);
        var oldRun = run?.GetValue("Veyro");
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var path in updating ? new[] { appPath, uninstallPath } : new[] { appPath, uninstallPath, startLink, desktopLink })
            {
                var backup = File.Exists(path) ? Path.Combine(staging, Guid.NewGuid().ToString("N")) : null;
                if (backup is not null) File.Copy(path, backup);
                backups[path] = backup;
            }
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
            changedFiles.Push(appPath);
            File.Move(Path.Combine(staging, "Uninstall-Veyro.exe"), uninstallPath, true);
            changedFiles.Push(uninstallPath);
            if (!updating)
            {
                progress.Report("Добавляем ярлыки и параметры запуска…");
                changedFiles.Push(startLink);
                CreateShortcut(startLink, appPath);
                changedFiles.Push(desktopLink);
                if (desktop) CreateShortcut(desktopLink, appPath);
                else if (File.Exists(desktopLink)) File.Delete(desktopLink);
                if (autostart) run!.SetValue("Veyro", '"' + appPath + '"');
                else run!.DeleteValue("Veyro", false);
            }
            using var product = Registry.CurrentUser.CreateSubKey(ProductKey);
            registering = true;
            product.SetValue("DisplayName", "Veyro");
            product.SetValue("DisplayVersion", typeof(Installer).Assembly.GetName().Version!.ToString(3));
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
            foreach (var path in changedFiles)
            {
                if (backups[path] is string backup) File.Copy(backup, path, true);
                else File.Delete(path);
            }
            if (run is not null)
            {
                if (oldRun is not null) run.SetValue("Veyro", oldRun);
                else run!.DeleteValue("Veyro", false);
            }
            if (!registering) throw;
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

