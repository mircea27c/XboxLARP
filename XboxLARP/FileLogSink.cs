using MonitorTopology;

namespace XboxLARP;

/// <summary>Writes to a rolling log file and mirrors to the console, so post-hoc diagnosis
/// of a flaky apply doesn't depend on having a console attached at the time.</summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private readonly bool _echoToConsole;

    public FileLogSink(string path, bool echoToConsole = true)
    {
        _echoToConsole = echoToConsole;
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true,
        };
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_gate)
        {
            _writer.WriteLine(line);
            if (_echoToConsole)
                Console.WriteLine(line);
        }
    }

    public void Dispose() => _writer.Dispose();
}
