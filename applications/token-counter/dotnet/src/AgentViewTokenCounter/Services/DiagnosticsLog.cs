using System.Text;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Append-only log at
/// <c>%APPDATA%\agentView-token-counter\diag.log</c>. Used by the
/// browser-cookie reader (which can fail for many subtle reasons:
/// profile path, locked DB, key decryption, host_key mismatch) so the
/// user can hand the log over when something does not work.
/// </summary>
/// <remarks>
/// Thread-safe via a single lock; we only append a few hundred lines
/// per session. The log is rotated when it grows past 256 KB - we
/// truncate the head so the latest events are always visible.
/// </remarks>
public sealed class DiagnosticsLog
{
    private const long MaxBytes = 256 * 1024;
    private readonly object _lock = new();

    public string Path { get; }

    public DiagnosticsLog()
    {
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "agentView-token-counter");
        Directory.CreateDirectory(dir);
        Path = System.IO.Path.Combine(dir, "diag.log");
    }

    public void Info(string message)  => Write("INFO",  message);
    public void Warn(string message)  => Write("WARN",  message);
    public void Error(string message) => Write("ERROR", message);
    public void Error(string message, Exception ex) =>
        Write("ERROR", message + " :: " + ex.GetType().Name + ": " + ex.Message);

    private void Write(string level, string message)
    {
        var line = $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {level,-5} {message}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                {
                    // Keep the tail. Simple rotation.
                    var keep = File.ReadAllText(Path, Encoding.UTF8);
                    if (keep.Length > 128 * 1024)
                    {
                        keep = keep[^(128 * 1024)..];
                    }
                    File.WriteAllText(Path, "... (rotated) ..." + Environment.NewLine + keep, Encoding.UTF8);
                }
                File.AppendAllText(Path, line, Encoding.UTF8);
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }
}
