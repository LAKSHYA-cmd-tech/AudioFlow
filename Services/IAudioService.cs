using AudioFlow.Models;

namespace AudioFlow.Services;

public interface IAudioService : IDisposable
{
    IReadOnlyList<AudioDevice> GetOutputDevices();
    IReadOnlyList<AudioSession> GetSessions();
    MasterAudioState GetMasterState();
    void SetMasterVolume(float volume);
    void SetMasterMute(bool muted);
    void SetDefaultDevice(string deviceId);
}

/// <summary>
/// Optional capability for audio backends that can report changes instead of waiting to be polled.
/// Kept separate from <see cref="IAudioService"/> so other backends stay valid without implementing it.
/// </summary>
public interface IAudioChangeNotifier
{
    /// <summary>Devices or audio sessions appeared or disappeared. Requires a full refresh.</summary>
    event Action? Changed;

    /// <summary>The default device's volume or mute changed, usually from another application.</summary>
    event Action? MasterStateChanged;
}
