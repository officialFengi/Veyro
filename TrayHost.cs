using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Veyro;

internal sealed class TrayHost
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly ShortcutRegistration shortcut = new();
    private Process? worker;
    private long request;
    private string savedView = "null";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--mixer-ui" && Console.IsInputRedirected)
            new MixerWorker(args[1] == "bound").Run();
        else
            new TrayHost().Run();
    }

    private void Run()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Veyro.TrayIcon")!;
        using var icon = new Icon(stream, 32, 32);
        using var tray = new Forms.NotifyIcon { Text = "Veyro — микшер громкости", Icon = icon, Visible = true };
        var settings = Preferences.Load();
        if (settings.ShortcutKey != 0) shortcut.TrySet(settings.ShortcutKey, settings.ShortcutModifiers);
        shortcut.Pressed += () => Send("hotkey");
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) Send("toggle");
            if (e.Button == Forms.MouseButtons.Right) Send("menu");
        };
        try { Dispatcher.Run(); }
        finally
        {
            shortcut.Dispose();
            worker?.StandardInput.Close();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private Process StartWorker()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false), StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(start.FileName), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        start.ArgumentList.Add("--mixer-ui");
        start.ArgumentList.Add(shortcut.IsRegistered ? "bound" : "unbound");
        var child = new Process { StartInfo = start };
        child.OutputDataReceived += (_, e) => dispatcher.BeginInvoke(new Action(() => Receive(child, e.Data)));
        child.Start();
        worker = child;
        child.BeginOutputReadLine();
        child.StandardInput.WriteLine("view " + savedView);
        return child;
    }

    private void Send(string action)
    {
        // The UI can exit between a tray click and a pipe write. Retry that click once.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var child = worker ?? StartWorker();
                AllowSetForegroundWindow(child.Id);
                child.StandardInput.WriteLine($"show {++request} {action}");
                return;
            }
            catch (IOException) { worker = null; }
            catch (System.ComponentModel.Win32Exception error)
            {
                Forms.MessageBox.Show(error.Message, "Не удалось открыть Veyro");
                return;
            }
        }
    }

    private void Receive(Process child, string? line)
    {
        if (line is null)
        {
            if (ReferenceEquals(worker, child)) worker = null;
            child.Dispose();
            return;
        }
        if (!ReferenceEquals(worker, child)) return;
        var parts = line.Split(' ', 3);
        try
        {
            if (parts[0] == "bind" && parts.Length == 3 && int.TryParse(parts[1], out int ticket) &&
                int.TryParse(parts[2], out int binding))
            {
                int key = binding & 255;
                bool success = true;
                if (key == 0) shortcut.Clear();
                else success = shortcut.TrySet(key, (binding >> 8) & 15);
                child.StandardInput.WriteLine($"bound {ticket} {(success ? 1 : 0)}");
            }
            else if (parts[0] == "idle" && parts.Length == 3 && long.TryParse(parts[1], out long last) && last == request)
            {
                // A newer click invalidates an idle request. After acceptance, subsequent clicks get a new UI.
                savedView = parts[2];
                worker = null;
                child.StandardInput.Close();
            }
            else if (line == "exit") dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        }
        catch (IOException) { worker = null; }
    }
}
