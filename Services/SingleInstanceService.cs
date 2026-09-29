using System.Security.Cryptography;
using System.Text;

namespace AudioFlow.Services;

/// <summary>One AudioFlow per user/session; another launch requests that its window reopen.</summary>
public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;
    public bool IsPrimary { get; }

    public SingleInstanceService(Action activate, bool requestActivation = true, string? instanceName = null)
    {
        var userKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Environment.UserDomainName + "/" + Environment.UserName)))[..24];
        var name = instanceName ?? "Local\\AudioFlow." + userKey;
        _mutex = new Mutex(false, name + ".lock");
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".activate");
        try { IsPrimary = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { IsPrimary = true; }
        if (IsPrimary)
            _registration = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => activate(), null, Timeout.Infinite, false);
        else if (requestActivation)
            _activation.Set();
    }

    // Dispose on the thread that constructed the primary instance (the WPF UI thread).
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _registration?.Unregister(null);
        if (IsPrimary) _mutex.ReleaseMutex();
        _activation.Dispose();
        _mutex.Dispose();
    }
}
