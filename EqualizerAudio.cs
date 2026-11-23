using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Veyro;

internal sealed class EqualizerRoute : IDisposable
{
    private sealed class CableCapture(MMDevice device) : WasapiCapture(device, false, 20)
    {
        protected override AudioClientStreamFlags GetAudioClientStreamFlags() =>
            base.GetAudioClientStreamFlags() | AudioClientStreamFlags.Loopback;
    }

    private readonly CableCapture capture;
    private readonly WasapiOut playback;
    private readonly EqualizerSamples samples;
    private volatile bool stopped;
    public bool Stopped => stopped;
    public string CableId { get; }
    public string OutputId { get; }

    public EqualizerRoute(MMDevice cable, MMDevice output, EqualizerProfile profile)
    {
        CableId = cable.ID;
        OutputId = output.ID;
        capture = new CableCapture(cable);
        try
        {
            var mix = capture.WaveFormat;
            capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);
            var buffer = new BufferedWaveProvider(capture.WaveFormat) {
                BufferDuration = TimeSpan.FromMilliseconds(140), DiscardOnBufferOverflow = true
            };
            samples = new EqualizerSamples(buffer.ToSampleProvider(), profile);
            playback = new WasapiOut(output, AudioClientShareMode.Shared, true, 35);
            try
            {
                capture.DataAvailable += (_, e) => buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                capture.RecordingStopped += (_, _) => stopped = true;
                playback.PlaybackStopped += (_, _) => stopped = true;
                playback.Init(samples.ToWaveProvider());
                capture.StartRecording();
                playback.Play();
            }
            catch { playback.Dispose(); throw; }
        }
        catch { capture.Dispose(); throw; }
    }

    public void Update(EqualizerProfile profile) => samples.Update(profile);
    public void Dispose()
    {
        try { capture.Dispose(); }
        finally { playback.Dispose(); }
    }
}

public partial class MainWindow
{
    private readonly Dictionary<string, EqualizerRoute> eqRoutes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> eqStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer eqMonitor = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool HasEqualizerRoutes => preferences.Equalizers!.Values.Any(p => p.CableId is not null);

    private static bool IsCable(MMDevice device)
    {
        string name = device.FriendlyName;
        return name.Contains("Virtual Cable", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Virtual Audio Cable", StringComparison.OrdinalIgnoreCase)
            || (name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)
                && name.Contains("Cable", StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanIsolate(MMDevice device) => IsCable(device) ||
        device.FriendlyName.Contains("S/PDIF", StringComparison.OrdinalIgnoreCase) ||
        device.FriendlyName.Contains("SPDIF", StringComparison.OrdinalIgnoreCase);

    private static bool AudioUnavailable(Exception ex) => ex is COMException or InvalidOperationException
        or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or Win32Exception;

    private void InitializeEqualizer()
    {
        eqSave.Tick += (_, _) => FlushEqualizerSave();
        eqMonitor.Tick += (_, _) => RefreshEqualizerRoutes();
        if (HasEqualizerRoutes) RefreshEqualizerRoutes();
    }

    private void StopRoute(string key)
    {
        if (eqRoutes.Remove(key, out var old))
            try { old.Dispose(); } catch (Exception ex) when (AudioUnavailable(ex)) { }
    }

    private void StopEqualizer()
    {
        eqMonitor.Stop();
        foreach (string key in eqRoutes.Keys.ToArray()) StopRoute(key);
    }

    private void RefreshEqualizerRoutes()
    {
        foreach (string key in eqRoutes.Keys.ToArray())
            if (!preferences.Equalizers!.TryGetValue(key, out var p) || p.CableId is null) StopRoute(key);
        eqStates.Clear();
        if (!HasEqualizerRoutes) { eqMonitor.Stop(); UpdateEqualizerStatus(); return; }
        eqMonitor.Start();
        try
        {
            using var output = audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            foreach (var (key, profile) in preferences.Equalizers!)
            {
                if (profile.CableId is null) continue;
                try
                {
                    using var cable = audio.GetDevice(profile.CableId);
                    string? wait = cable.State != DeviceState.Active ? "Выделенный выход отключён"
                        : !CanIsolate(cable) || cable.ID == output.ID || IsCable(output) ? "Выберите наушники или колонки выходом Windows"
                        : CheckCableOwner(cable, key);
                    if (wait is not null)
                    {
                        StopRoute(key);
                        eqStates[key] = wait;
                        continue;
                    }
                    if (!eqRoutes.TryGetValue(key, out var route) || route.Stopped ||
                        route.OutputId != output.ID || route.CableId != cable.ID)
                    {
                        StopRoute(key);
                        eqRoutes[key] = new EqualizerRoute(cable, output, profile);
                    }
                    else route.Update(profile);
                    eqStates[key] = (profile.Enabled ? "Обработка" : "Обход") + "  ·  " + output.FriendlyName;
                }
                catch (Exception ex) when (AudioUnavailable(ex))
                {
                    StopRoute(key);
                    eqStates[key] = "Аудиомаршрут недоступен. Проверьте кабель и выход Windows.";
                }
            }
        }
        catch (Exception ex) when (AudioUnavailable(ex))
        {
            foreach (string key in eqRoutes.Keys.ToArray()) StopRoute(key);
            foreach (var (key, profile) in preferences.Equalizers!)
                if (profile.CableId is not null) eqStates[key] = "Нет доступного устройства вывода";
        }
        UpdateEqualizerStatus();
    }

    private static string? CheckCableOwner(MMDevice cable, string key)
    {
        var manager = cable.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        if (sessions is null) return "Windows не предоставила аудиосеансы этого выхода.";
        bool owner = false;
        for (int i = 0; i < sessions.Count; i++)
        {
            using var session = sessions[i];
            if (session.State != AudioSessionState.AudioSessionStateActive) continue;
            uint pid = session.GetProcessID;
            if (pid == 0 || session.IsSystemSoundsSession) return "Выделенный выход занят системным звуком";
            using var process = Process.GetProcessById((int)pid);
            if (!string.Equals(process.MainModule?.FileName, key, StringComparison.OrdinalIgnoreCase))
                return "Выделенный выход занят другим приложением";
            owner = true;
        }
        return owner ? null : "Ожидание звука на " + cable.FriendlyName;
    }
}
