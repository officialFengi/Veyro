using System;
using System.Threading;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Veyro;

// Windows may notify from different threads. Coalesce changes and touch WPF only on its dispatcher.
internal sealed class AudioChanges(Dispatcher dispatcher, Action<bool> refresh) : IMMNotificationClient, IAudioSessionEventsHandler
{
    private int pending;
    private void Queue(bool topology)
    {
        if (Interlocked.Or(ref pending, topology ? 2 : 1) != 0) return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            refresh((Interlocked.Exchange(ref pending, 0) & 2) != 0)));
    }
    public void OnEndpointVolume(AudioVolumeNotificationData _) => Queue(false);
    public void OnSessionCreated(object sender, IAudioSessionControl session) => Queue(true);
    public void OnVolumeChanged(float volume, bool mute) => Queue(false);
    public void OnStateChanged(AudioSessionState state) => Queue(true);
    public void OnSessionDisconnected(AudioSessionDisconnectReason reason) => Queue(true);
    public void OnDisplayNameChanged(string name) => Queue(true);
    public void OnIconPathChanged(string path) { }
    public void OnChannelVolumeChanged(uint count, IntPtr levels, uint channel) { }
    public void OnGroupingParamChanged(ref Guid grouping) { }
    public void OnDeviceStateChanged(string id, DeviceState state) => Queue(true);
    public void OnDeviceAdded(string id) => Queue(true);
    public void OnDeviceRemoved(string id) => Queue(true);
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) Queue(true);
    }
    public void OnPropertyValueChanged(string id, PropertyKey key) { }
}
