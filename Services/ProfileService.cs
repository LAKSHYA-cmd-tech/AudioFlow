using AudioFlow.Models;

namespace AudioFlow.Services;

public sealed class ProfileService(IAudioService audio)
{
    public string? Apply(AudioProfile profile)
    {
        if (!profile.HasSavedMix) return "This profile has no saved mix yet. Adjust the controls, then select Save current mix.";
        var devices = audio.GetOutputDevices();
        if (!string.IsNullOrWhiteSpace(profile.OutputDeviceId))
        {
            var target = devices.FirstOrDefault(d => d.Id == profile.OutputDeviceId);
            if (target is null) return "Saved output is disconnected; the profile will resume when it reconnects.";
            if (!target.IsDefault) audio.SetDefaultDevice(target.Id);
        }
        if (!devices.Any()) return "No output is connected; the profile is waiting.";
        if (profile.MasterVolume is float volume) audio.SetMasterVolume(volume);
        if (profile.MasterMuted is bool muted) audio.SetMasterMute(muted);
        // Fetch after switching: sessions from the previous output are no longer valid.
        foreach (var session in audio.GetSessions()) ApplySession(profile, session);
        return null;
    }

    public void Capture(AudioProfile profile)
    {
        var output = audio.GetOutputDevices().FirstOrDefault(d => d.IsDefault)
            ?? throw new InvalidOperationException("Connect an output device before saving a mix.");
        var master = audio.GetMasterState();
        var sessions = audio.GetSessions();
        // Preserve settings for apps that are currently closed; update only the live apps.
        static string Key(string process, string? device) => process.ToUpperInvariant() + "|" + (device ?? "");
        var settings = profile.Applications.GroupBy(a => Key(a.ProcessName, a.OutputDeviceId))
            .ToDictionary(g => g.Key, g => g.Last());
        foreach (var session in sessions.GroupBy(s => Key(s.ProcessName, s.OutputDeviceId)).Select(g => g.First()))
            settings[Key(session.ProcessName, session.OutputDeviceId)] = new VolumeSetting
            {
                ProcessName = session.ProcessName, Volume = session.Volume, Muted = session.IsMuted,
                OutputDeviceId = string.IsNullOrEmpty(session.OutputDeviceId) ? null : session.OutputDeviceId
            };
        profile.OutputDeviceId = output.Id;
        profile.MasterVolume = master.Volume;
        profile.MasterMuted = master.IsMuted;
        profile.Applications = new(settings.Values);
        profile.NotifySaved();
    }

    public static void ApplySession(AudioProfile profile, AudioSession session)
    {
        var matches = profile.Applications.Where(s =>
            s.ProcessName.Equals(session.ProcessName, StringComparison.OrdinalIgnoreCase));
        // New saves are per output; existing profiles without device IDs keep their old behavior.
        var saved = matches.FirstOrDefault(s => s.OutputDeviceId == session.OutputDeviceId)
            ?? matches.FirstOrDefault(s => string.IsNullOrEmpty(s.OutputDeviceId));
        if (saved is null) return;
        session.Volume = saved.Volume;
        session.IsMuted = saved.Muted;
    }
}
