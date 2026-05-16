using System.Text.Json.Serialization;

namespace AgentView.TokenCounter.Models;

/// <summary>
/// The user-editable configuration persisted between runs.
/// </summary>
/// <remarks>
/// Stored as JSON at <c>%APPDATA%\agentView-token-counter\config.json</c>.
/// The single sensitive value (<see cref="AgentViewApiKey"/>) is
/// wrapped via Windows DPAPI before it hits disk; see
/// <c>Services.ConfigStore</c>.
/// <para>
/// The Claude session itself lives in the WebView2 user-data folder
/// (<c>%APPDATA%\agentView-token-counter\WebView2</c>), not here -
/// the cookies are managed by the embedded browser engine.
/// </para>
/// </remarks>
public sealed class AppConfig
{
    /// <summary>
    /// The claude.ai organisation UUID. Auto-detected from the
    /// session in the WebView the first time the user logs in.
    /// </summary>
    public string? ClaudeOrgId { get; set; }

    /// <summary>
    /// The agentView instance base URL, default <c>https://agentview.de</c>.
    /// Trailing slash is stripped on save.
    /// </summary>
    public string AgentViewBaseUrl { get; set; } = "https://agentview.de";

    /// <summary>
    /// The agentView data-slot slug to write into. Default
    /// <c>token-usage</c> matches the slot the
    /// <c>token-counter-standalone</c> display template installs.
    /// </summary>
    public string AgentViewSlotSlug { get; set; } = "token-usage";

    /// <summary>
    /// Scoped API key (<c>avk_...</c>) issued in the agentView portal.
    /// Should be limited to <c>allowedSlotSlugs: ["token-usage"]</c>
    /// and capability <c>slot.write</c>. DPAPI-protected on disk.
    /// </summary>
    public string? AgentViewApiKey { get; set; }

    /// <summary>
    /// Optional group ID to scope the slot write to a group-scope
    /// slot. <c>null</c> writes to the API key owner's personal scope.
    /// </summary>
    public string? AgentViewGroupId { get; set; }

    /// <summary>
    /// The agentView display ID we wrote the Token Counter template to.
    /// Stored so the settings dialog can show the user which display is
    /// being driven, and so the setup wizard can skip the picker on
    /// subsequent runs.
    /// </summary>
    public string? AgentViewDisplayId { get; set; }

    /// <summary>
    /// Human-readable name of the display (cached at setup time, for
    /// the settings dialog). Best-effort; refreshed when settings
    /// re-fetch the list.
    /// </summary>
    public string? AgentViewDisplayName { get; set; }

    /// <summary>
    /// Email of the agentView account the user logged in with. Cached
    /// so the main window can show "Syncing · email@..." in the
    /// header without re-probing /auth/me on every open. Refreshed
    /// whenever the Setup tab confirms the agentView session.
    /// <para>
    /// PII note: this is stored in plaintext in config.json (it is not
    /// a secret, only the API key is DPAPI-wrapped). It never leaves
    /// the machine. If you fork this for a privacy-regulated context,
    /// either drop the cache and always re-probe /auth/me, or wrap it
    /// like <see cref="AgentViewApiKey"/>.
    /// </para>
    /// </summary>
    public string? AgentViewUserEmail { get; set; }

    /// <summary>
    /// Polling interval. The Token Counter display polls its slot
    /// every 120 seconds, so anything below ~60 s is wasted writes.
    /// Default 120 s.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 120;

    /// <summary>
    /// Include weekly Opus / Sonnet model-split buckets if their
    /// utilization is greater than zero.
    /// </summary>
    public bool IncludeModelSplits { get; set; } = true;

    /// <summary>
    /// Whether the app registers itself in <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>
    /// so it starts on login. Toggled in the tray menu.
    /// </summary>
    public bool AutoStart { get; set; }

    /// <summary>
    /// When the user paused the pinger via the tray menu, store the
    /// flag so it survives restarts. The background loop checks this
    /// on every tick.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>
    /// True when the app has been through the initial setup wizard.
    /// First-run if false.
    /// </summary>
    public bool SetupComplete { get; set; }
}
