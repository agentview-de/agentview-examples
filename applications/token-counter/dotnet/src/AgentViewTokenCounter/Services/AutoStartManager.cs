using Microsoft.Win32;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Registers / unregisters the app in
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> so it
/// starts on user login. No elevation needed - HKCU is per-user.
/// </summary>
public static class AutoStartManager
{
    private const string RunKey   = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "agentViewTokenCounter";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }

    public static void Enable(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("exePath is required.", nameof(exePath));
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                        ?? throw new InvalidOperationException("Cannot open HKCU Run key.");
        key.SetValue(ValueName, $"\"{exePath}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
