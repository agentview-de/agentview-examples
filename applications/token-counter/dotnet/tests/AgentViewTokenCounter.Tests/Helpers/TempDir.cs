namespace AgentViewTokenCounter.Tests.Helpers;

/// <summary>
/// Creates a unique temporary directory and deletes it on dispose.
/// Keeps ConfigStore integration tests isolated from %APPDATA% and
/// from each other.
/// </summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "avtest_" + System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; don't fail the test suite on a
            // stale lock.
        }
    }
}
