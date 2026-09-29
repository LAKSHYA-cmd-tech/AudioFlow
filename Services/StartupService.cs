using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AudioFlow.Services;

public interface IStartupService
{
    bool IsEnabled { get; set; }
    bool IsManagedByWindows => false;
}

public sealed class StartupService : IStartupService
{
    private const string KeyName = "AudioFlow";
    public bool IsManagedByWindows { get; } = PackageIdentity.IsPackaged();
    public bool IsEnabled
    {
        get { if (IsManagedByWindows) return false; using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue(KeyName) is not null; }
        set
        {
            if (IsManagedByWindows) throw new InvalidOperationException("Manage the packaged app in Windows Settings > Apps > Startup.");
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (value) key.SetValue(KeyName, $"\"{Environment.ProcessPath}\" --minimized"); else key.DeleteValue(KeyName, false);
        }
    }
}

public static class PackageIdentity
{
    public static bool IsPackaged()
    {
        uint length = 0;
        return InterpretResult(GetCurrentPackageFullName(ref length, IntPtr.Zero));
    }
    public static bool InterpretResult(int result) => result switch
    {
        0 or 122 => true,
        15700 => false,
        _ => throw new Win32Exception(result)
    };
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, IntPtr packageFullName);
}
