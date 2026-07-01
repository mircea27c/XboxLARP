namespace MonitorTopology;

public interface ILogSink
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class ConsoleLogSink : ILogSink
{
    public void Info(string message) => Console.WriteLine($"[INFO] {message}");
    public void Warn(string message) => Console.WriteLine($"[WARN] {message}");
    public void Error(string message) => Console.WriteLine($"[ERROR] {message}");
}
