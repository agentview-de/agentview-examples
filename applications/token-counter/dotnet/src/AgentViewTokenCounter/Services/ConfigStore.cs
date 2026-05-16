using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentView.TokenCounter.Models;

// Allow the test project to reach internal members (the seam ctor).
[assembly: InternalsVisibleTo("AgentViewTokenCounter.Tests")]

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Loads / persists <see cref="AppConfig"/> in
/// <c>%APPDATA%\agentView-token-counter\config.json</c>.
/// </summary>
/// <remarks>
/// The single secret field (<see cref="AppConfig.AgentViewApiKey"/>)
/// is protected with Windows DPAPI (<see cref="ProtectedData"/>) at
/// the current-user scope so only this Windows account can read it
/// back. The protected blob is base64-encoded and prefixed with
/// <c>"DPAPI:"</c> so plain migration values are still accepted on
/// first load (one-way upgrade).
/// <para>
/// The Claude session cookie itself is not persisted here - it lives
/// in the WebView2 user-data folder, encrypted by Edge / WebView2
/// itself.
/// </para>
/// </remarks>
public sealed class ConfigStore
{
    private const string DpapiPrefix = "DPAPI:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Absolute path to the persisted config file. The parent
    /// directory is created lazily on first save.
    /// </summary>
    public string ConfigPath { get; }

    public ConfigStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "agentView-token-counter");
        Directory.CreateDirectory(dir);
        ConfigPath = Path.Combine(dir, "config.json");
    }

    /// <summary>
    /// Test-only constructor that uses a caller-supplied directory
    /// instead of <c>%APPDATA%\agentView-token-counter</c>. Keeps
    /// tests isolated from each other and from the production config.
    /// </summary>
    /// <param name="configDir">
    /// An existing, writable temporary directory. The constructor does
    /// NOT create it — callers are responsible for passing a valid path.
    /// </param>
    internal ConfigStore(string configDir)
    {
        ConfigPath = Path.Combine(configDir, "config.json");
    }

    /// <summary>Reads the config. Returns a fresh default if the file
    /// is missing.</summary>
    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AppConfig();
        }

        AppConfig? cfg = null;
        try
        {
            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        }
        catch (Exception)
        {
            // Corrupted config: fall through to default. The setup
            // wizard will run again on the next start.
        }

        cfg ??= new AppConfig();
        cfg.AgentViewApiKey = Unprotect(cfg.AgentViewApiKey);

        // Normalise: strip trailing slashes from the base URL.
        if (!string.IsNullOrEmpty(cfg.AgentViewBaseUrl))
        {
            cfg.AgentViewBaseUrl = cfg.AgentViewBaseUrl.TrimEnd('/');

            // Defense-in-depth: a hand-edited or tampered config.json
            // could downgrade the channel to http:// and put the
            // plaintext X-API-Key header on the wire. The Settings tab
            // already rejects that on save; reject it again on load so
            // a file edit can't sneak past the UI. Fall back to the
            // safe default rather than refusing to start.
            if (!cfg.AgentViewBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                cfg.AgentViewBaseUrl = "https://agentview.de";
            }
        }
        return cfg;
    }

    /// <summary>Writes the config atomically. Secrets are
    /// DPAPI-protected before they hit disk.</summary>
    public void Save(AppConfig config)
    {
        // Don't mutate the caller's instance: clone every persisted
        // field into a copy. A missing line here means that field
        // silently resets on every save — we lost AgentViewDisplayId /
        // AgentViewDisplayName this way once, which made the "Re-publish"
        // bullet read "Sign in first" after a restart even though the
        // setup was actually complete.
        var snapshot = new AppConfig
        {
            ClaudeOrgId           = config.ClaudeOrgId,
            AgentViewBaseUrl      = (config.AgentViewBaseUrl ?? "").TrimEnd('/'),
            AgentViewSlotSlug     = config.AgentViewSlotSlug,
            AgentViewApiKey       = Protect(config.AgentViewApiKey),
            AgentViewGroupId      = config.AgentViewGroupId,
            AgentViewDisplayId    = config.AgentViewDisplayId,
            AgentViewDisplayName  = config.AgentViewDisplayName,
            AgentViewUserEmail    = config.AgentViewUserEmail,
            PollIntervalSeconds   = config.PollIntervalSeconds,
            IncludeModelSplits    = config.IncludeModelSplits,
            AutoStart             = config.AutoStart,
            Paused                = config.Paused,
            SetupComplete         = config.SetupComplete,
        };

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    // ─── DPAPI helpers ──────────────────────────────────────────────

    private static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return null;
        }
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        return DpapiPrefix + Convert.ToBase64String(bytes);
    }

    private static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }
        if (!stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            // One-way migration: a key written by a pre-DPAPI build (or
            // hand-pasted into config.json) is accepted once, then
            // re-wrapped on the next Save. Trade-off worth knowing if
            // you copy this: the path stays permanently open, so an
            // attacker who can write config.json could drop a plaintext
            // key and have it accepted. A hardened build would instead
            // log a warning + force re-setup rather than silently
            // trusting an unwrapped value on every load.
            return stored;
        }
        try
        {
            var b64 = stored[DpapiPrefix.Length..];
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(b64),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Wrong user or corrupted blob: discard.
            return null;
        }
    }
}
