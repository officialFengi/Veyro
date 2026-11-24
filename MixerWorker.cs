using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Veyro;

// Only the transient UI process loads WPF windows, decoded images and Core Audio.
internal sealed class MixerWorker(bool registered) : IMixerShortcut
{
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer idle = new();
    private readonly StreamWriter reply = new(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
    private readonly App app = new();
    private MainWindow? mixer;
    private TrayMenuWindow? menu;
    private UpdateOfferWindow? updateResult;
    private MixerViewState? savedView;
    private long lastRequest;
    private int bindingTicket;
    private TaskCompletionSource<bool>? bindingReply;
    public bool IsRegistered { get; private set; } = registered;

    internal void Run()
    {
        app.InitializeComponent();
        if (System.Linq.Enumerable.Any(Preferences.Load().Equalizers!.Values, p => p.CableId is not null)) GetMixer();
        idle.Tick += (_, _) => RequestUnload();
        // A pipe read must not occupy the WPF dispatcher, including during a binding change.
        _ = Task.Run(ReadCommands);
        app.Run();
        reply.Dispose();
    }

    private async Task ReadCommands()
    {
        try
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                var parts = line.Split(' ', 3);
                await dispatcher.InvokeAsync(() => Handle(parts));
            }
        }
        catch (IOException) { }
        finally
        {
            await dispatcher.InvokeAsync(() =>
            {
                bindingReply?.TrySetResult(false);
                idle.Stop();
                mixer?.CloseForRelease();
                app.Shutdown();
            });
        }
    }

    private void Handle(string[] parts)
    {
        if (parts[0] == "bound" && parts.Length == 3 && int.TryParse(parts[1], out int ticket))
        {
            if (ticket == bindingTicket) bindingReply?.TrySetResult(parts[2] == "1");
            return;
        }
        if (parts[0] == "view" && parts.Length >= 2)
        {
            savedView = JsonSerializer.Deserialize<MixerViewState>(string.Join(" ", parts, 1, parts.Length - 1));
            return;
        }
        if (parts.Length != 3 || parts[0] != "show" || !long.TryParse(parts[1], out lastRequest)) return;
        idle.Stop();
        if (parts[2] is "update-current" or "update-check-failed")
        {
            ShowUpdateResult(parts[2] == "update-current");
            return;
        }
        if (parts[2] == "menu")
        {
            menu?.Close();
            menu = new TrayMenuWindow(mixer?.CurrentSettings ?? Preferences.Load(), () => GetMixer().OpenMixer(),
                () => Write("check-updates"), () => Write("exit"));
            menu.Closed += (_, _) => { menu = null; ScheduleUnload(); };
            menu.Show();
            return;
        }
        if (parts[2] == "hotkey") GetMixer().OpenFromShortcut();
        else GetMixer().ToggleMixer();
        ScheduleUnload();
    }

    private MainWindow GetMixer()
    {
        if (mixer is null)
        {
            mixer = new MainWindow(this, Preferences.Load());
            mixer.RestoreView(savedView);
            mixer.IsVisibleChanged += (_, _) => ScheduleUnload();
            app.MainWindow = mixer;
        }
        return mixer;
    }

    private void ScheduleUnload()
    {
        idle.Stop();
        if (!CanUnload) return;
        idle.Interval = TimeSpan.FromSeconds(mixer is null ? 5 : 30);
        idle.Start();
    }

    private bool CanUnload => menu is null && updateResult is null && (mixer is null || mixer.CanUnload);

    private void RequestUnload()
    {
        idle.Stop();
        if (CanUnload) Write($"idle {lastRequest} " + JsonSerializer.Serialize(mixer?.CaptureView() ?? savedView));
    }

    private void ShowUpdateResult(bool current)
    {
        if (updateResult is null)
        {
            updateResult = UpdateOfferWindow.CreateResult(current, mixer?.CurrentSettings);
            updateResult.Closed += (_, _) =>
            {
                updateResult = null;
                if (mixer is null) RequestUnload();
                else ScheduleUnload();
            };
            updateResult.Show();
        }
        else
        {
            updateResult.SetResult(current);
            updateResult.Activate();
        }
    }

    private void Write(string message)
    {
        try { reply.WriteLine(message); }
        catch (IOException) { bindingReply?.TrySetResult(false); app.Shutdown(); }
    }

    public async Task<bool> SetAsync(int key, int modifiers)
    {
        bindingReply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Write($"bind {++bindingTicket} {(modifiers << 8) | key}");
        bool ok = await bindingReply.Task;
        bindingReply = null;
        if (ok) IsRegistered = key != 0;
        return ok;
    }
}
