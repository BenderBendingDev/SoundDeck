using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SoundDeck.Core;
using System.Runtime.InteropServices;

namespace SoundDeck.Audio;

public sealed class AudioDeviceService : IAudioDeviceService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly NotificationClient _notification;

    public AudioDeviceService()
    {
        _notification = new NotificationClient(() => DevicesChanged?.Invoke(this, EventArgs.Empty));
        _enumerator.RegisterEndpointNotificationCallback(_notification);
    }

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<AudioDevice> GetCaptureDevices() => GetDevices(DataFlow.Capture);

    public IReadOnlyList<AudioDevice> GetRenderDevices() => GetDevices(DataFlow.Render);

    public AudioDevice? FindVirtualCableInput() =>
        GetRenderDevices().FirstOrDefault(device =>
            device.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ||
            device.Name.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase));

    private IReadOnlyList<AudioDevice> GetDevices(DataFlow flow)
    {
        string? defaultId = null;
        try
        {
            defaultId = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID;
        }
        catch (COMException)
        {
            // Windows has no active default endpoint.
        }

        return _enumerator
            .EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(device => new AudioDevice(
                device.ID,
                device.FriendlyName,
                device.ID == defaultId,
                device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
                device.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name)
            .ToArray();
    }

    public void Dispose()
    {
        _enumerator.UnregisterEndpointNotificationCallback(_notification);
        _enumerator.Dispose();
    }

    private sealed class NotificationClient(Action changed) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => changed();
        public void OnDeviceAdded(string pwstrDeviceId) => changed();
        public void OnDeviceRemoved(string deviceId) => changed();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => changed();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => changed();
    }
}
