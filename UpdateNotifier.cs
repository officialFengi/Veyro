using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Veyro;

internal sealed class UpdateNotifier : IDisposable
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(30);
    private readonly string stamp = Path.Combine(Path.GetDirectoryName(Preferences.FilePath)!, "last-update-check");
    private readonly DispatcherTimer timer;
    private bool disposed;
    private bool checking;
    private bool reportResult;

    internal UpdateNotifier(Dispatcher dispatcher)
    {
        timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        timer.Tick += ScheduledCheck;
        Schedule(TimeSpan.FromSeconds(30));
    }

    private void ScheduledCheck(object? sender, EventArgs args) => _ = CheckAsync(false);
    internal void CheckNow() => _ = CheckAsync(true);

    private async Task CheckAsync(bool manual)
    {
        if (disposed) return;
        reportResult |= manual;
        if (checking) return;
        checking = true;
        timer.Stop();
        var delay = RetryInterval;
        try
        {
            UpdateCache.CleanWhenIdle();
            try
            {
                var elapsed = DateTime.UtcNow - File.GetLastWriteTimeUtc(stamp);
                if (!manual && elapsed >= TimeSpan.Zero && elapsed < CheckInterval)
                {
                    delay = CheckInterval - elapsed;
                    return;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            string tag;
            using (var child = Process.Start(TrayHost.ChildProcess("--check-update"))!)
            {
                tag = (await child.StandardOutput.ReadToEndAsync()).Trim();
                await child.WaitForExitAsync();
                if (disposed) return;
                if (child.ExitCode != 0)
                {
                    Report("Не удалось проверить обновления на GitHub. Попробуйте позже.");
                    return;
                }
            }
            if (tag.Length > 0)
            {
                var answer = Forms.MessageBox.Show(
                    $"Доступна новая версия Veyro {tag}. Скачать обновление и перезапустить Veyro? Ваши настройки сохранятся.",
                    "Обновление Veyro", Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Information);
                if (answer == Forms.DialogResult.Yes)
                {
                    var start = TrayHost.ChildProcess("--install-update");
                    start.ArgumentList.Add(tag);
                    try
                    {
                        using var download = Process.Start(start)!;
                        await download.WaitForExitAsync();
                        if (download.ExitCode != 0) return;
                    }
                    catch (Win32Exception error)
                    {
                        Forms.MessageBox.Show(error.Message, "Не удалось начать обновление Veyro");
                        return;
                    }
                }
            }
            else
            {
                var version = typeof(UpdateNotifier).Assembly.GetName().Version!.ToString(3);
                Report($"У вас актуальная версия Veyro {version}.");
            }
            delay = CheckInterval;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(stamp)!);
                File.WriteAllText(stamp, string.Empty);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Report("Не удалось проверить обновления. " + error.Message);
        }
        finally
        {
            reportResult = false;
            checking = false;
            if (!disposed) Schedule(delay);
        }
    }

    private void Report(string message)
    {
        if (reportResult && !disposed)
            Forms.MessageBox.Show(message, "Обновления Veyro", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Information);
    }

    private void Schedule(TimeSpan delay)
    {
        timer.Interval = delay;
        timer.Start();
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        timer.Tick -= ScheduledCheck;
    }
}
