using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace Veyro;

internal sealed class UpdateDownloadWindow : Forms.Form
{
    private readonly Forms.Label status = new() { Dock = Forms.DockStyle.Top, Height = 30, Text = "Проверяем релиз…" };
    private readonly Forms.ProgressBar bar = new() { Dock = Forms.DockStyle.Top, Height = 18 };
    private readonly CancellationTokenSource cancel = new();
    private bool finished;
    private int result;

    internal static int Run(string tag)
    {
        using var mutex = new Mutex(false, UpdateCache.MutexName);
        if (!UpdateCache.Acquire(mutex)) return 0;
        try
        {
            Forms.Application.EnableVisualStyles();
            using var form = new UpdateDownloadWindow(tag);
            Forms.Application.Run(form);
            return form.result;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private UpdateDownloadWindow(string tag)
    {
        Text = $"Обновление Veyro {tag}";
        Font = SystemFonts.MessageBoxFont;
        AutoScaleMode = Forms.AutoScaleMode.Dpi;
        FormBorderStyle = Forms.FormBorderStyle.FixedDialog;
        StartPosition = Forms.FormStartPosition.CenterScreen;
        MaximizeBox = false;
        ClientSize = new Size(390, 125);
        Padding = new Forms.Padding(16);
        var button = new Forms.Button { Text = "Отмена", AutoSize = true, Dock = Forms.DockStyle.Right };
        var actions = new Forms.Panel { Height = 30, Dock = Forms.DockStyle.Bottom };
        actions.Controls.Add(button);
        Controls.Add(actions);
        Controls.Add(bar);
        Controls.Add(status);
        button.Click += (_, _) => Close();
        FormClosing += (_, e) =>
        {
            if (finished) return;
            e.Cancel = true;
            button.Enabled = false;
            cancel.Cancel();
        };
        Shown += async (_, _) =>
        {
            bool launched = false;
            try
            {
                var asset = await ReleaseCheck.LatestAsync(cancel.Token);
                if (asset?.Tag != tag) throw new IOException("Релиз изменился. Veyro проверит обновления ещё раз.");
                Directory.CreateDirectory(UpdateCache.DirectoryPath);
                var progress = new Progress<int>(percent => { bar.Value = percent; status.Text = $"Скачиваем обновление… {percent}%"; });
                await DownloadAsync(asset, progress, cancel.Token);
                status.Text = "Проверяем установщик…";
                File.Move(UpdateCache.Partial, UpdateCache.Setup, true);
                using var verified = File.OpenRead(UpdateCache.Setup);
                var hash = await SHA256.HashDataAsync(verified, cancel.Token);
                if (!CryptographicOperations.FixedTimeEquals(hash, asset.Sha256))
                    throw new InvalidDataException("Контрольная сумма не совпала. Обновление остановлено.");
                var info = FileVersionInfo.GetVersionInfo(UpdateCache.Setup);
                var version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);
                if (version != asset.Version || info.OriginalFilename != "Veyro-Setup.dll")
                    throw new InvalidDataException("Установщик не соответствует версии релиза.");
                cancel.Token.ThrowIfCancellationRequested();
                var setup = new ProcessStartInfo(UpdateCache.Setup) { UseShellExecute = false };
                setup.ArgumentList.Add("--update");
                setup.ArgumentList.Add(AppContext.BaseDirectory);
                using var installer = Process.Start(setup)!;
                launched = true;
                button.Enabled = false;
                status.Text = "Запускаем установку…";
                // A self-contained installer can fail during runtime extraction, before it opens a window.
                try { await Task.Run(() => installer.WaitForInputIdle(60000)); }
                catch (InvalidOperationException) when (installer.HasExited) { }
                if (installer.HasExited && installer.ExitCode != 0)
                    throw new IOException("Установщик не смог запуститься. Проверьте свободное место на системном диске.");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (Exception error)
            {
                result = 1;
                Forms.MessageBox.Show(this, error.Message, "Не удалось обновить Veyro", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Error);
            }
            finally
            {
                UpdateCache.Delete(UpdateCache.Partial);
                if (!launched) UpdateCache.Delete(UpdateCache.Setup);
                finished = true;
                Close();
            }
        };
    }

    private static async Task DownloadAsync(ReleaseCheck.Asset asset, IProgress<int> progress, CancellationToken token)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var url = $"https://github.com/officialFengi/Veyro/releases/download/{Uri.EscapeDataString(asset.Tag)}/{ReleaseCheck.InstallerName}";
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var network = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var file = new FileStream(UpdateCache.Partial, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int previous = -1;
        while (true)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            int read = await network.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > asset.Size) throw new InvalidDataException("Размер загрузки превышает размер установщика.");
            await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            int percent = (int)(total * 100.0 / asset.Size);
            if (percent != previous) { progress.Report(percent); previous = percent; }
        }
        if (total != asset.Size) throw new EndOfStreamException("Загрузка прервалась. Обновление не применено.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) cancel.Dispose();
        base.Dispose(disposing);
    }
}
