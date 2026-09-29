using System.IO;

namespace AudioFlow.Services;

public interface IDiagnosticLog
{
    void Write(string operation, Exception? error = null);
}

public sealed class DiagnosticLog : IDiagnosticLog
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _gate = new();
    private string? _lastEntry;
    private DateTime _lastWritten;
    public DiagnosticLog(string? directory = null, long maxBytes = 1_048_576)
    {
        _path = Path.Combine(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioFlow", "Logs"), "audioflow.log");
        _maxBytes = maxBytes;
    }
    public void Write(string operation, Exception? error = null)
    {
        try
        {
            lock (_gate)
            {
                var entry = error is null ? operation : $"{operation}: {error.GetType().Name} (0x{error.HResult:X8}) {error.Message}";
                var now = DateTime.UtcNow;
                if (entry == _lastEntry && now - _lastWritten < TimeSpan.FromSeconds(30)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length >= _maxBytes)
                    File.Move(_path, Path.ChangeExtension(_path, ".previous.log"), true);
                File.AppendAllText(_path, $"{now:O} {entry.Replace("\r", " ").Replace("\n", " ")}{Environment.NewLine}");
                _lastEntry = entry; _lastWritten = now;
            }
        }
        catch { /* Diagnostics must never prevent audio controls or shutdown from working. */ }
    }
}

public sealed class NullDiagnosticLog : IDiagnosticLog
{
    public void Write(string operation, Exception? error = null) { }
}
