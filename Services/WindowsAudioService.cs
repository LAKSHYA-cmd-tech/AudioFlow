using System.Diagnostics;
using System.Runtime.InteropServices;
using AudioFlow.Models;

namespace AudioFlow.Services;

public sealed class WindowsAudioService : IAudioService, IAudioChangeNotifier
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly IDiagnosticLog _log;
    private readonly object _gate = new();

    // Kept alive so notifications stay registered for the lifetime of the service.
    private readonly Dictionary<string, IAudioSessionManager2> _sessionManagers = new(StringComparer.OrdinalIgnoreCase);
    private ComCallback<DeviceNotificationClient>? _deviceCallback;
    private ComCallback<VolumeNotificationClient>? _volumeCallback;
    private IAudioEndpointVolume? _masterVolume;
    private string? _masterVolumeDeviceId;

    private readonly Debouncer _topologyChanged;
    private readonly Debouncer _masterChanged;
    private volatile bool _disposed;

    public event Action? Changed;
    public event Action? MasterStateChanged;

    public WindowsAudioService(IDiagnosticLog? log = null)
    {
        _log = log ?? new NullDiagnosticLog();
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _topologyChanged = new Debouncer(() => { if (!_disposed) Changed?.Invoke(); }, 200);
        _masterChanged = new Debouncer(() => { if (!_disposed) MasterStateChanged?.Invoke(); }, 120);
        try
        {
            _deviceCallback = new ComCallback<DeviceNotificationClient>(
                new DeviceNotificationClient(_topologyChanged.Signal), typeof(IMMNotificationClient));
            _volumeCallback = new ComCallback<VolumeNotificationClient>(
                new VolumeNotificationClient(_masterChanged.Signal), typeof(IAudioEndpointVolumeCallback));
            _enumerator.RegisterEndpointNotificationCallback(_deviceCallback.Pointer);
        }
        catch (Exception ex)
        {
            // Notifications are an optimisation. The caller's polling timer still keeps the UI correct.
            _log.Write("audio.notify.register", ex);
        }
    }

    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        var result = new List<AudioDevice>();
        _enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out var collection);
        try
        {
            string? defaultId = null;
            try
            {
                _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var defaultDevice);
                try { defaultDevice.GetId(out var defaultIdPtr); defaultId = ReadOwnedString(defaultIdPtr); }
                finally { Marshal.ReleaseComObject(defaultDevice); }
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070490)) { }
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                try
                {
                    device.GetId(out var idPtr);
                    var id = ReadOwnedString(idPtr);
                    result.Add(new(id ?? "", ReadFriendlyName(device) ?? "Audio output", id == defaultId));
                }
                finally { Marshal.ReleaseComObject(device); }
            }
            return result;
        }
        finally { Marshal.ReleaseComObject(collection); }
    }

    public IReadOnlyList<AudioSession> GetSessions()
    {
        var result = new List<AudioSession>();
        var outputs = GetOutputDevices();
        SyncSessionManagers(outputs);
        Exception? failure = null;
        var readableOutputs = 0;
        foreach (var output in outputs)
        {
        try
        {
        VisitSessions(output.Id, control =>
        {
            control.GetState(out var state);
            if (state == 2) return; // Expired sessions no longer have a controllable client.
            var control2 = (IAudioSessionControl2)control;
            control2.GetProcessId(out var pid);
            if (pid == 0) return;
            control2.GetSessionInstanceIdentifier(out var rawIdPtr);
            var rawId = ReadOwnedString(rawIdPtr);
            if (string.IsNullOrWhiteSpace(rawId)) return;
            var id = output.Id + "|" + rawId;
            var name = GetProcessName(pid);
            control2.GetDisplayName(out var displayPtr);
            var display = ReadOwnedString(displayPtr);
            var simple = (ISimpleAudioVolume)control;
            simple.GetMasterVolume(out var volume); simple.GetMute(out var muted);
            var session = new AudioSession {
                Id = id, ProcessId = (int)pid, ProcessName = name, DisplayName = FriendlyDisplayName(display, name),
                OutputDeviceId = output.Id, OutputDeviceName = output.Name,
                VolumeChanged = (_, v) => SetVolume(output.Id, rawId, v),
                MuteChanged = (_, m) => SetMute(output.Id, rawId, m)
            };
            session.Update(volume, muted, state == 1);
            result.Add(session);
        }, tolerateEndedSessions: true);
        readableOutputs++;
        }
        catch (COMException ex)
        {
            failure ??= ex;
            _log.Write("audio.endpoint.read", ex);
            // A USB or Bluetooth endpoint can return with the same device ID while its
            // previous session manager COM object has become invalid.
            EvictSessionManager(output.Id);
        }
        }
        if (outputs.Count > 0 && readableOutputs == 0 && failure is not null)
            throw new InvalidOperationException("No output could be read. AudioFlow will retry.", failure);
        return result.OrderBy(x => x.DisplayName).ThenBy(x => x.OutputDeviceName).ToList();
    }

    public void SetDefaultDevice(string deviceId)
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            policy.SetDefaultEndpoint(deviceId, ERole.Console);
            policy.SetDefaultEndpoint(deviceId, ERole.Multimedia);
            policy.SetDefaultEndpoint(deviceId, ERole.Communications);
        }
        finally { Marshal.ReleaseComObject(policy); }
    }

    public MasterAudioState GetMasterState()
    {
        return WithMasterVolume(volume =>
        {
            volume.GetMasterVolumeLevelScalar(out var level);
            volume.GetMute(out var muted);
            return new MasterAudioState(level, muted);
        });
    }

    public void SetMasterVolume(float volume) => WithMasterVolume(endpoint =>
        endpoint.SetMasterVolumeLevelScalar(Math.Clamp(volume, 0, 1), IntPtr.Zero));

    public void SetMasterMute(bool muted) => WithMasterVolume(endpoint =>
        endpoint.SetMute(muted, IntPtr.Zero));

    private T WithMasterVolume<T>(Func<IAudioEndpointVolume, T> action)
    {
        var endpoint = GetMasterVolume();
        try { return action(endpoint); }
        catch (COMException)
        {
            // A device can reconnect under the same ID while the old endpoint object
            // is still cached. Re-open it on the next refresh or control attempt.
            EvictMasterVolume(endpoint);
            throw;
        }
    }

    private void WithMasterVolume(Action<IAudioEndpointVolume> action) =>
        WithMasterVolume(endpoint => { action(endpoint); return true; });

    /// <summary>
    /// Returns the shared endpoint volume for the current default device, registering for change
    /// notifications the first time a device is seen. The caller must not release the result.
    /// </summary>
    private IAudioEndpointVolume GetMasterVolume()
    {
        _enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out var device);
        try
        {
            device.GetId(out var deviceIdPtr);
            var deviceId = ReadOwnedString(deviceIdPtr) ?? "";
            lock (_gate)
            {
                if (_masterVolume is not null &&
                    string.Equals(_masterVolumeDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                    return _masterVolume;

                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, 23, IntPtr.Zero, out var instance);
                var endpoint = (IAudioEndpointVolume)instance;
                if (_masterVolume is not null)
                {
                    try { _masterVolume.UnregisterControlChangeNotify(VolumeCallbackPointer); }
                    catch (Exception ex) { _log.Write("audio.master.unregister", ex); }
                    try { Marshal.ReleaseComObject(_masterVolume); }
                    catch (Exception ex) { _log.Write("audio.master.release", ex); }
                }
                _masterVolume = endpoint;
                _masterVolumeDeviceId = deviceId;
                if (VolumeCallbackPointer != IntPtr.Zero)
                {
                    try { endpoint.RegisterControlChangeNotify(VolumeCallbackPointer); }
                    catch (Exception ex) { _log.Write("audio.master.notify", ex); }
                }
                return endpoint;
            }
        }
        finally { Marshal.ReleaseComObject(device); }
    }

    private void EvictMasterVolume(IAudioEndpointVolume failedEndpoint)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_masterVolume, failedEndpoint)) return;
            try { if (VolumeCallbackPointer != IntPtr.Zero) failedEndpoint.UnregisterControlChangeNotify(VolumeCallbackPointer); }
            catch (Exception ex) { _log.Write("audio.master.unregister", ex); }
            try { Marshal.ReleaseComObject(failedEndpoint); }
            catch (Exception ex) { _log.Write("audio.master.release", ex); }
            _masterVolume = null;
            _masterVolumeDeviceId = null;
        }
    }

    private void SetVolume(string deviceId, string id, float value) => WithSession(deviceId, id, v => v.SetMasterVolume(Math.Clamp(value, 0, 1), IntPtr.Zero));
    private void SetMute(string deviceId, string id, bool muted) => WithSession(deviceId, id, v => v.SetMute(muted, IntPtr.Zero));
    private void WithSession(string deviceId, string id, Action<ISimpleAudioVolume> action)
    {
        var found = false;
        VisitSessions(deviceId, control =>
        {
            ((IAudioSessionControl2)control).GetSessionInstanceIdentifier(out var currentPtr);
            if (ReadOwnedString(currentPtr) == id) { action((ISimpleAudioVolume)control); found = true; }
        });
        if (!found) throw new InvalidOperationException("This audio session has ended. Refresh and try again.");
    }

    /// <summary>
    /// Reads a string that Core Audio allocated with CoTaskMemAlloc and releases it. Declaring these
    /// as plain <c>out string</c> would make the marshaller treat the pointer as a BSTR, which is both
    /// the wrong free and a leak on a path that runs on every refresh.
    /// </summary>
    private static string? ReadOwnedString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    private void VisitSessions(string deviceId, Action<IAudioSessionControl> visit, bool tolerateEndedSessions = false)
    {
        var manager = GetSessionManager(deviceId);
        manager.GetSessionEnumerator(out var sessions);
        try
        {
            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                try
                {
                sessions.GetSession(i, out var control);
                try { visit(control); }
                finally { Marshal.ReleaseComObject(control); }
                }
                catch (COMException ex) when (tolerateEndedSessions) { _log.Write("audio.session.ended-during-read", ex); }
            }
        }
        finally { Marshal.ReleaseComObject(sessions); }
    }

    private IAudioSessionManager2 GetSessionManager(string deviceId)
    {
        lock (_gate)
        {
            if (_sessionManagers.TryGetValue(deviceId, out var cached)) return cached;
        }
        _enumerator.GetDevice(deviceId, out var device);
        IAudioSessionManager2 manager;
        try
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var obj);
            manager = (IAudioSessionManager2)obj;
        }
        finally { Marshal.ReleaseComObject(device); }
        lock (_gate)
        {
            if (_sessionManagers.TryGetValue(deviceId, out var raced))
            {
                try { Marshal.ReleaseComObject(manager); } catch { }
                return raced;
            }
            _sessionManagers[deviceId] = manager;
            return manager;
        }
    }

    /// <summary>Drops cached session managers for outputs that are no longer present.</summary>
    private void SyncSessionManagers(IReadOnlyList<AudioDevice> devices)
    {
        var live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices) live.Add(device.Id);
        List<IAudioSessionManager2> dropped;
        lock (_gate)
        {
            var stale = _sessionManagers.Keys.Where(id => !live.Contains(id)).ToList();
            dropped = new List<IAudioSessionManager2>(stale.Count);
            foreach (var id in stale)
            {
                dropped.Add(_sessionManagers[id]);
                _sessionManagers.Remove(id);
            }
        }
        foreach (var manager in dropped)
        {
            try { Marshal.ReleaseComObject(manager); }
            catch (Exception ex) { _log.Write("audio.session.release", ex); }
        }
    }

    private void EvictSessionManager(string deviceId)
    {
        IAudioSessionManager2? stale;
        lock (_gate)
        {
            if (!_sessionManagers.Remove(deviceId, out stale)) return;
        }
        try { Marshal.ReleaseComObject(stale); }
        catch (Exception ex) { _log.Write("audio.session.release", ex); }
    }

    private IntPtr VolumeCallbackPointer => _volumeCallback?.Pointer ?? IntPtr.Zero;

    private static string GetProcessName(uint pid) { try { using var process = Process.GetProcessById((int)pid); return process.ProcessName; } catch { return $"App {pid}"; } }
    private static string FriendlyDisplayName(string? display, string process) =>
        !string.IsNullOrWhiteSpace(display) && !display.StartsWith('@') ? display
        : process.Length == 0 ? "App" : char.ToUpperInvariant(process[0]) + process[1..];
    private static string? ReadFriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(0, out var store);
        var key = new PropertyKey { FormatId = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
        try
        {
            store.GetValue(ref key, out var value);
            try { return value.GetString(); }
            finally { value.Clear(); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Changed = null;
        MasterStateChanged = null;
        // Order matters: stop the callbacks that can still fire, then release what they reference.
        if (_deviceCallback is not null)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback.Pointer); }
            catch (Exception ex) { _log.Write("audio.notify.unregister", ex); }
        }
        lock (_gate)
        {
            if (_masterVolume is not null)
            {
                try { if (VolumeCallbackPointer != IntPtr.Zero) _masterVolume.UnregisterControlChangeNotify(VolumeCallbackPointer); }
                catch (Exception ex) { _log.Write("audio.master.unregister", ex); }
                try { Marshal.ReleaseComObject(_masterVolume); }
                catch (Exception ex) { _log.Write("audio.master.release", ex); }
                _masterVolume = null;
                _masterVolumeDeviceId = null;
            }
            foreach (var manager in _sessionManagers.Values)
            {
                try { Marshal.ReleaseComObject(manager); }
                catch (Exception ex) { _log.Write("audio.session.release", ex); }
            }
            _sessionManagers.Clear();
        }
        // Only now is it safe to stop the timers and free the managed callback references.
        _topologyChanged.Dispose();
        _masterChanged.Dispose();
        _deviceCallback?.Dispose();
        _volumeCallback?.Dispose();
        try { Marshal.ReleaseComObject(_enumerator); }
        catch (Exception ex) { _log.Write("audio.enumerator.release", ex); }
    }

    /// <summary>Holds a managed callback alive and exposes the COM interface pointer Windows needs.</summary>
    private sealed class ComCallback<T> : IDisposable where T : class
    {
        private GCHandle _handle;
        private IntPtr _pointer;
        public ComCallback(T instance, Type interfaceType)
        {
            _handle = GCHandle.Alloc(instance, GCHandleType.Normal);
            try { _pointer = Marshal.GetComInterfaceForObject(instance, interfaceType); }
            catch { _handle.Free(); _handle = default; throw; }
        }
        public IntPtr Pointer => _pointer;
        public void Dispose()
        {
            if (_pointer != IntPtr.Zero) { Marshal.Release(_pointer); _pointer = IntPtr.Zero; }
            if (_handle.IsAllocated) _handle.Free();
        }
    }

    /// <summary>Collapses a burst of notifications into one callback, so drags cannot flood the UI.</summary>
    private sealed class Debouncer : IDisposable
    {
        private readonly Action _action;
        private readonly int _milliseconds;
        private readonly System.Threading.Timer? _timer;
        private int _pending;
        private int _stopped;
        public Debouncer(Action action, int milliseconds)
        {
            _action = action;
            _milliseconds = milliseconds;
            _timer = new System.Threading.Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        public void Signal()
        {
            // A Core Audio callback can still arrive while the service is shutting down, so this
            // must never throw on a COM thread. Swallowing here loses at most one pending refresh.
            if (Volatile.Read(ref _stopped) != 0) return;
            if (Interlocked.Exchange(ref _pending, 1) == 0)
            {
                try { _timer?.Change(TimeSpan.FromMilliseconds(_milliseconds), Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { }
            }
        }
        private void Flush()
        {
            // Cleared before invoking so a change arriving during the callback schedules another pass.
            Interlocked.Exchange(ref _pending, 0);
            if (Volatile.Read(ref _stopped) != 0) return;
            _action();
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
            _timer?.Dispose();
        }
    }
}

internal enum EDataFlow { Render, Capture, All }
internal enum ERole { Console, Multimedia, Communications }
[Flags] internal enum DeviceState : uint { Active = 1 }

[ComVisible(true)]
internal sealed class DeviceNotificationClient : IMMNotificationClient
{
    private readonly Action _onChanged;
    public DeviceNotificationClient(Action onChanged) => _onChanged = onChanged;
    public void OnDeviceStateChanged(string deviceId, uint newState) => _onChanged();
    public void OnDeviceAdded(string deviceId) => _onChanged();
    public void OnDeviceRemoved(string deviceId) => _onChanged();
    public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string defaultDeviceId) => _onChanged();
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) => _onChanged();
}

[ComVisible(true)]
internal sealed class VolumeNotificationClient : IAudioEndpointVolumeCallback
{
    private readonly Action _onChanged;
    public VolumeNotificationClient(Action onChanged) => _onChanged = onChanged;
    public void OnNotify() => _onChanged();
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class MMDeviceEnumeratorComObject { }
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    void RegisterEndpointNotificationCallback(IntPtr client);
    void UnregisterEndpointNotificationCallback(IntPtr client);
}
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
internal interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("C5EA8D77-8E22-4944-97D6-04AF9BCC6D12")]
internal interface IAudioEndpointVolumeCallback
{
    void OnNotify();
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
internal interface IMMDeviceCollection { void GetCount(out uint count); void Item(uint index, out IMMDevice device); }
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("D666063F-1587-4E43-81F1-B948E807363F")]
internal interface IMMDevice
{
    void Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    void OpenPropertyStore(uint access, out IPropertyStore properties);
    void GetId(out IntPtr id);
    void GetState(out DeviceState state);
}
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal interface IPropertyStore { void GetCount(out uint count); void GetAt(uint index, out PropertyKey key); void GetValue(ref PropertyKey key, out PropVariant value); void SetValue(ref PropertyKey key, ref PropVariant value); void Commit(); }
[StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid FormatId; public uint PropertyId; }
[StructLayout(LayoutKind.Explicit, Size = 24)] internal struct PropVariant
{
    [FieldOffset(0)] private ushort type; [FieldOffset(8)] private IntPtr pointer;
    // 31 = VT_LPWSTR, 8 = VT_BSTR. Some drivers report friendly names as BSTR.
    public string? GetString() => (type == 31 || type == 8) && pointer != IntPtr.Zero ? Marshal.PtrToStringUni(pointer) : null;
    public void Clear() => PropVariantClear(ref this);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}

// IAudioSessionManager2 as implemented by the object IMMDevice::Activate returns for this IID.
// Verified at runtime: GetSessionEnumerator answers at vtable slot 2, so the manager exposes only
// its own three methods here. Declaring the inherited IAudioSessionControl methods ahead of them
// shifts every call and access-violates. IAudioSessionControl2 below does need the full set,
// because session controls really do carry the inherited vtable.
//
// Consequence: the manager offers no way to register an IAudioSessionNotification, so AudioFlow
// detects new audio sessions from the timer rather than from a callback.
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
internal interface IAudioSessionManager2
{
    void GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IAudioSessionControl control);
    void GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out ISimpleAudioVolume volume);
    void GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
}
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
internal interface IAudioSessionEnumerator { void GetCount(out int count); void GetSession(int index, out IAudioSessionControl control); }
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
internal interface IAudioSessionControl
{
    void GetState(out int state); void GetDisplayName(out IntPtr name); void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, Guid context);
    void GetIconPath(out IntPtr path); void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, Guid context); void GetGroupingParam(out Guid grouping); void SetGroupingParam(Guid grouping, Guid context);
    void RegisterAudioSessionNotification(IntPtr client); void UnregisterAudioSessionNotification(IntPtr client);
}
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
internal interface IAudioSessionControl2
{
    void GetState(out int state); void GetDisplayName(out IntPtr name); void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, Guid context);
    void GetIconPath(out IntPtr path); void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, Guid context); void GetGroupingParam(out Guid grouping); void SetGroupingParam(Guid grouping, Guid context);
    void RegisterAudioSessionNotification(IntPtr client); void UnregisterAudioSessionNotification(IntPtr client);
    void GetSessionIdentifier(out IntPtr id); void GetSessionInstanceIdentifier(out IntPtr id); void GetProcessId(out uint processId); [PreserveSig] int IsSystemSoundsSession(); void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
internal interface ISimpleAudioVolume { void SetMasterVolume(float level, IntPtr context); void GetMasterVolume(out float level); void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context); void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute); }

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IntPtr notify);
    void UnregisterControlChangeNotify(IntPtr notify);
    void GetChannelCount(out uint count);
    void SetMasterVolumeLevel(float levelDb, IntPtr context);
    void SetMasterVolumeLevelScalar(float level, IntPtr context);
    void GetMasterVolumeLevel(out float levelDb);
    void GetMasterVolumeLevelScalar(out float level);
    void SetChannelVolumeLevel(uint channel, float levelDb, IntPtr context);
    void SetChannelVolumeLevelScalar(uint channel, float level, IntPtr context);
    void GetChannelVolumeLevel(uint channel, out float levelDb);
    void GetChannelVolumeLevelScalar(uint channel, out float level);
    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] internal class PolicyConfigClient { }
[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
internal interface IPolicyConfig
{
    void GetMixFormat(string deviceId, IntPtr format); void GetDeviceFormat(string deviceId, bool defaultFormat, IntPtr format); void ResetDeviceFormat(string deviceId);
    void SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat); void GetProcessingPeriod(string deviceId, bool defaultPeriod, IntPtr period, IntPtr minPeriod);
    void SetProcessingPeriod(string deviceId, IntPtr period); void GetShareMode(string deviceId, IntPtr mode); void SetShareMode(string deviceId, IntPtr mode);
    void GetPropertyValue(string deviceId, ref PropertyKey key, out PropVariant value); void SetPropertyValue(string deviceId, ref PropertyKey key, ref PropVariant value);
    void SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role); void SetEndpointVisibility(string deviceId, bool visible);
}
