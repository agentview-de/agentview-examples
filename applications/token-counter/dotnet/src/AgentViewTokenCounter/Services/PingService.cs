using AgentView.TokenCounter.Models;

namespace AgentView.TokenCounter.Services;

/// <summary>
/// Orchestrates a single sync cycle: fetch the live plan-usage from
/// claude.ai via the embedded WebView session, map it to the slot
/// shape, then PUT it to agentView.
/// </summary>
/// <remarks>
/// Stateless - all configuration is passed in as <see cref="AppConfig"/>.
/// The tray loop calls <see cref="RunOnceAsync"/> every
/// <see cref="AppConfig.PollIntervalSeconds"/> seconds.
/// </remarks>
public sealed class PingService
{
    private readonly IClaudeApiClient  _claude;
    private readonly IAgentViewClient  _agentView;
    private readonly DiagnosticsLog    _log;

    public PingService(
        IClaudeApiClient claude,
        IAgentViewClient agentView,
        DiagnosticsLog log)
    {
        _claude    = claude;
        _agentView = agentView;
        _log       = log;
    }

    public async Task<PingResult> RunOnceAsync(AppConfig config, CancellationToken ct = default)
    {
        if (config.Paused)
        {
            return PingResult.Paused();
        }

        if (string.IsNullOrWhiteSpace(config.ClaudeOrgId))
            return PingResult.Failure("Claude organisation ID is not set.");
        if (string.IsNullOrWhiteSpace(config.AgentViewApiKey))
            return PingResult.Failure("agentView API key is not set.");

        // 1) Fetch usage via the WebView session.
        ClaudeUsageResponse raw;
        try
        {
            raw = await _claude.FetchUsageAsync(config.ClaudeOrgId, ct).ConfigureAwait(true);
        }
        catch (ClaudeAiAuthException ex)
        {
            _log.Warn("claude.ai auth: " + ex.Message);
            return PingResult.Failure(ex.Message);
        }
        catch (Exception ex)
        {
            _log.Error("claude.ai fetch failed", ex);
            return PingResult.Failure($"claude.ai fetch failed: {ex.Message}");
        }

        // 2) Map + PUT to agentView. Pass label=null on the happy
        // path so user-edited labels in the portal are preserved. If
        // the slot doesn't exist yet (first run after a manual config
        // restore, or template that didn't pre-create it), retry once
        // with a sensible default label.
        var slot = BucketMapper.Map(raw, config.IncludeModelSplits);
        try
        {
            try
            {
                await _agentView.WriteSlotAsync(
                    config.AgentViewBaseUrl,
                    config.AgentViewSlotSlug,
                    config.AgentViewApiKey!,
                    slot,
                    groupId: config.AgentViewGroupId,
                    label:   null,
                    ct).ConfigureAwait(true);
            }
            catch (MissingSlotLabelException)
            {
                _log.Info("agentView slot does not exist yet — retrying with label.");
                await _agentView.WriteSlotAsync(
                    config.AgentViewBaseUrl,
                    config.AgentViewSlotSlug,
                    config.AgentViewApiKey!,
                    slot,
                    groupId: config.AgentViewGroupId,
                    label:   DefaultSlotLabel,
                    ct).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _log.Error("agentView slot write failed", ex);
            return PingResult.Failure(ex.Message);
        }

        return PingResult.Success(slot);
    }

    /// <summary>
    /// Label applied to the slot the first time it is created. The
    /// user can rename it in the agentView portal at any time —
    /// subsequent writes pass <c>label=null</c> so the rename sticks.
    /// </summary>
    private const string DefaultSlotLabel = "Claude plan usage";
}
