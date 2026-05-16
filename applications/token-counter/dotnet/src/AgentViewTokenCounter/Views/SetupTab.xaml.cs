using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AgentView.TokenCounter.Models;
using AgentView.TokenCounter.Services;

namespace AgentView.TokenCounter.Views;

/// <summary>
/// "Setup" tab of the main window. Three-step wizard
/// (sign in to Claude, sign in to agentView, publish the display)
/// laid out as three side-by-side cards plus a full-width
/// "Pair a screen" card below.
/// </summary>
/// <remarks>
/// This class is a thin view shell. All orchestration and business
/// logic lives in <see cref="SetupCoordinator"/> — it has no WPF
/// types so it is fully unit-testable. The code-behind here only:
/// <list type="bullet">
///   <item>Wires button-click events to coordinator calls.</item>
///   <item>Reflects coordinator result records back into visual state.</item>
///   <item>Manages the pair-code input boxes (pure UI concern).</item>
/// </list>
/// </remarks>
public partial class SetupTab : UserControl
{
    private const string CreateNewToken = "+ Create a new display…";

    private readonly AppConfig          _config;
    private readonly WebViewSession     _session;
    private readonly SetupCoordinator   _coordinator;
    private readonly DiagnosticsLog     _log;

    private List<ClaudeOrganization> _detectedOrgs     = new();
    private List<AgentViewDisplay>   _detectedDisplays = new();
    private bool                     _installComplete;
    private TextBox[]                _pairBoxes = Array.Empty<TextBox>();

    /// <summary>Raised when "Publish" succeeds and config has been saved.</summary>
    public event EventHandler? InstallCompleted;

    /// <summary>Raised when the agentView user email is detected /
    /// changes (so the main window header can refresh).</summary>
    public event EventHandler<string>? UserEmailUpdated;

    public SetupTab(
        AppConfig config,
        WebViewSession session,
        SetupCoordinator coordinator,
        DiagnosticsLog log)
    {
        InitializeComponent();

        _config      = config;
        _session     = session;
        _coordinator = coordinator;
        _log         = log;
        _pairBoxes   = new[] { PairChar0, PairChar1, PairChar2, PairChar3, PairChar4, PairChar5 };

        OrgPicker.SelectionChanged     += (_, _) => UpdateInstallEnabled();
        DisplayPicker.SelectionChanged += (_, _) => UpdateInstallEnabled();

        // Keyboard shortcuts on the pair codeboxes: Backspace on empty box → focus previous.
        foreach (var box in _pairBoxes)
        {
            box.PreviewKeyDown += OnPairKeyDown;
        }

        // If the user has already finished setup on a previous run
        // (config has a display id + slug), reflect that visually so
        // the Setup tab doesn't read like a brand-new wizard.
        if (!string.IsNullOrEmpty(_config.AgentViewDisplayId))
        {
            _installComplete = true;
            UpdateStepPill(3, StepState.Done);
            InstallButton.Content = "Re-publish";

            var displayLabel = !string.IsNullOrEmpty(_config.AgentViewDisplayName)
                ? _config.AgentViewDisplayName
                : _config.AgentViewDisplayId;
            ShowInstallDetails(
                slot:        _config.AgentViewSlotSlug,
                publishedAt: "waiting for next sync…",
                apiKey:      _config.AgentViewApiKey);
            SetPill(InstallPillText, InstallBullet, "Published", PillState.Success);
            InstallStatus.Text = "Previously published to " + displayLabel + " · waiting for next sync to confirm…";
        }

        UpdatePairEnabled();
        Loaded += async (_, _) => await OnLoadedAsync();
    }

    private async Task OnLoadedAsync()
    {
        try
        {
            await _session.InitialiseAsync().ConfigureAwait(true);
        }
        catch (WebView2RuntimeMissingException ex)
        {
            ShowWebView2Missing(ex);
            return;
        }
        catch (Exception ex)
        {
            _log.Error("WebView init failed", ex);
            SetCaption(InstallStatus, "Could not start the embedded browser: " + ex.Message, isError: true);
            return;
        }

        await RefreshClaudeStateAsync().ConfigureAwait(true);
        await RefreshAgentStateAsync().ConfigureAwait(true);
    }

    private async void OnClaudeLoginClicked(object sender, RoutedEventArgs e)
    {
        ClaudeLoginButton.IsEnabled = false;
        SetPill(ClaudePillText, ClaudeBullet, "Opening…", PillState.Pending);
        try
        {
            await _session.InitialiseAsync().ConfigureAwait(true);
            var login = new LoginWindow(
                _session,
                title:      "Token Counter — Sign in to Claude",
                header:     "Sign in to Claude",
                subtitle:   "This window closes automatically once you are signed in.",
                loginUrl:   "https://claude.ai/login",
                isLoggedIn: () => _coordinator.RefreshClaudeStateAsync()
                                              .ContinueWith(t => t.Result.LoggedIn,
                                                  System.Threading.Tasks.TaskScheduler.Current))
            {
                Owner = Window.GetWindow(this),
            };
            login.ShowDialog();
            await RefreshClaudeStateAsync().ConfigureAwait(true);
        }
        catch (WebView2RuntimeMissingException ex) { ShowWebView2Missing(ex); }
        catch (Exception ex)
        {
            _log.Error("Claude login failed", ex);
            SetPill(ClaudePillText, ClaudeBullet, "Sign-in failed", PillState.Danger);
            SetCaption(OrgStatus, ex.Message, isError: true);
        }
        finally { ClaudeLoginButton.IsEnabled = true; }
    }

    private async void OnClaudeSignOutClicked(object sender, RoutedEventArgs e)
    {
        ClaudeSignOutLine.IsEnabled = false;
        SetPill(ClaudePillText, ClaudeBullet, "Signing out…", PillState.Pending);
        try
        {
            await _coordinator.SignOutClaudeAsync().ConfigureAwait(true);
            await RefreshClaudeStateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("Claude sign-out failed", ex);
            SetCaption(OrgStatus, "Sign-out failed: " + ex.Message, isError: true);
        }
        finally { ClaudeSignOutLine.IsEnabled = true; }
    }

    private async void OnAgentSignOutClicked(object sender, RoutedEventArgs e)
    {
        AgentSignOutLine.IsEnabled = false;
        SetPill(AgentPillText, AgentBullet, "Signing out…", PillState.Pending);
        try
        {
            await _coordinator.SignOutAgentViewAsync().ConfigureAwait(true);

            // agentView sign-out wipes the published-display state too,
            // so roll the wizard's step 3 + header back to "fresh".
            _installComplete = false;
            InstallButton.Content = "Publish";
            UpdateStepPill(3, StepState.Pending);
            SetPill(InstallPillText, InstallBullet, "Not published", PillState.Pending);
            InstallStatus.Text = "Sign in to Claude and agentView first";
            InstallDetails.Visibility = Visibility.Collapsed;
            UserEmailUpdated?.Invoke(this, string.Empty);   // clear header email
            UpdatePairEnabled();

            await RefreshAgentStateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("agentView sign-out failed", ex);
            SetCaption(DisplayStatus, "Sign-out failed: " + ex.Message, isError: true);
        }
        finally { AgentSignOutLine.IsEnabled = true; }
    }

    private async Task RefreshClaudeStateAsync()
    {
        var result = await _coordinator.RefreshClaudeStateAsync().ConfigureAwait(true);

        // Sign-out link is only meaningful while a session exists.
        ClaudeSignOutLine.Visibility =
            result.LoggedIn ? Visibility.Visible : Visibility.Collapsed;

        if (!result.LoggedIn)
        {
            SetPill(ClaudePillText, ClaudeBullet, "Not signed in", PillState.Pending);
            OrgPicker.IsEnabled   = false;
            OrgPicker.ItemsSource = null;
            _detectedOrgs.Clear();
            UpdateStepPill(1, StepState.Pending);
            UpdateInstallEnabled();
            return;
        }

        SetPill(ClaudePillText, ClaudeBullet, "Signed in", PillState.Success);
        UpdateStepPill(1, StepState.Done);

        _detectedOrgs = new List<ClaudeOrganization>(result.Organizations);

        if (result.ErrorMessage is not null)
        {
            OrgPicker.IsEnabled = false;
            SetCaption(OrgStatus, result.ErrorMessage, isError: true);
            UpdateInstallEnabled();
            return;
        }

        var labels = _detectedOrgs.Select(o =>
        {
            var tier = o.FriendlyTier();
            return tier is null ? o.Name : $"{o.Name}  ·  {tier}";
        }).ToList();
        OrgPicker.ItemsSource = labels;
        OrgPicker.IsEnabled   = _detectedOrgs.Count > 0;
        if (_detectedOrgs.Count > 0)
        {
            OrgPicker.SelectedIndex = 0;
            SetCaption(OrgStatus, _detectedOrgs.Count == 1
                ? "Auto-selected."
                : $"{_detectedOrgs.Count} organisations — pick one.");
        }
        else
        {
            SetCaption(OrgStatus, "Signed in, but no organisations on this account.", isError: true);
        }
        UpdateInstallEnabled();
    }

    /// <summary>
    /// Alternative agentView "login" path: the user mints an
    /// <c>avk_…</c> key in the dashboard and pastes it. Two flavours,
    /// controlled by the radio buttons in the modal:
    /// <list type="bullet">
    ///   <item><see cref="ApiKeyMode.FullSetup"/> — key has display.*
    ///   capabilities. Flip the API client to header auth and run the
    ///   rest of the wizard on top of the key.</item>
    ///   <item><see cref="ApiKeyMode.SlotOnly"/> — key is slot-write
    ///   only. Persist creds, mark Setup complete, skip steps 2/3.</item>
    /// </list>
    /// </summary>
    private async void OnUseApiKeyClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new ApiKeyLoginWindow
        {
            Owner = Window.GetWindow(this),
        };
        var ok = dlg.ShowDialog();
        if (ok != true || !dlg.Confirmed)
        {
            return;
        }

        if (dlg.Mode == ApiKeyMode.SlotOnly)
        {
            _coordinator.ApplySlotOnlyApiKey(dlg.ApiKey, dlg.SlotSlug, dlg.GroupId);

            SetPill(AgentPillText, AgentBullet, "Using API key", PillState.Success);
            AgentSignOutLine.Visibility = Visibility.Visible;
            UpdateStepPill(2, StepState.Done);
            SetCaption(DisplayStatus,
                "Using a slot-write key — the bridge writes to slot \"" + dlg.SlotSlug +
                "\". Display + HTML setup is on you, do it in the agentView dashboard.");

            SetPill(InstallPillText, InstallBullet, "Skipped (manual setup)", PillState.Pending);
            InstallStatus.Text = "You provided your own API key — provision the display + HTML in the agentView dashboard.";
            InstallCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        await ApplyFullSetupApiKeyAsync(dlg.ApiKey, dlg.SlotSlug, dlg.GroupId)
            .ConfigureAwait(true);
    }

    private async Task ApplyFullSetupApiKeyAsync(string apiKey, string slotSlug, string? groupId)
    {
        SetPill(AgentPillText, AgentBullet, "Verifying API key…", PillState.Pending);
        SetCaption(DisplayStatus, "Checking the key and loading your displays…");

        var result = await _coordinator.ApplyFullSetupApiKeyAsync(apiKey, slotSlug, groupId)
            .ConfigureAwait(true);

        if (!result.LoggedIn)
        {
            SetPill(AgentPillText, AgentBullet, "Key rejected", PillState.Danger);
            SetCaption(DisplayStatus, result.ErrorMessage ?? "Key validation failed.", isError: true);
            return;
        }

        ApplyEmailIfChanged(result.UserEmail);
        PopulateDisplayPicker(result.Displays);
        SetPill(AgentPillText, AgentBullet, "Signed in via API key", PillState.Success);
        AgentSignOutLine.Visibility = Visibility.Visible;
        UpdateStepPill(2, StepState.Done);
        SetCaption(DisplayStatus, result.Displays.Count switch
        {
            0 => "No existing displays — a new one will be created on Publish.",
            1 => "Auto-selected. You can also create a new one.",
            _ => $"{result.Displays.Count} displays — pick one, or create a new one.",
        });
        UpdateInstallEnabled();
    }

    private async void OnAgentLoginClicked(object sender, RoutedEventArgs e)
    {
        AgentLoginButton.IsEnabled = false;
        SetPill(AgentPillText, AgentBullet, "Opening…", PillState.Pending);
        try
        {
            await _session.InitialiseAsync().ConfigureAwait(true);

            // AgentViewApiClient exposes BaseUrl as a property.
            // Retrieve it through the coordinator's backing field by
            // casting; the coordinator holds the same reference.
            var agentBaseUrl = GetAgentBaseUrl();
            var login = new LoginWindow(
                _session,
                title:      "Token Counter — Sign in to agentView",
                header:     "Sign in to agentView",
                subtitle:   "This window closes automatically once you are signed in.",
                loginUrl:   agentBaseUrl + "/login.html",
                isLoggedIn: () => _coordinator.RefreshAgentStateAsync()
                                              .ContinueWith(t => t.Result.LoggedIn,
                                                  System.Threading.Tasks.TaskScheduler.Current))
            {
                Owner = Window.GetWindow(this),
            };
            login.ShowDialog();
            await RefreshAgentStateAsync().ConfigureAwait(true);
        }
        catch (WebView2RuntimeMissingException ex) { ShowWebView2Missing(ex); }
        catch (Exception ex)
        {
            _log.Error("agentView login failed", ex);
            SetPill(AgentPillText, AgentBullet, "Sign-in failed", PillState.Danger);
            SetCaption(DisplayStatus, ex.Message, isError: true);
        }
        finally { AgentLoginButton.IsEnabled = true; }
    }

    private async Task RefreshAgentStateAsync()
    {
        var result = await _coordinator.RefreshAgentStateAsync().ConfigureAwait(true);

        AgentSignOutLine.Visibility =
            result.LoggedIn ? Visibility.Visible : Visibility.Collapsed;

        if (!result.LoggedIn)
        {
            SetPill(AgentPillText, AgentBullet, "Not signed in", PillState.Pending);
            DisplayPicker.IsEnabled   = false;
            DisplayPicker.ItemsSource = null;
            _detectedDisplays.Clear();
            UpdateStepPill(2, StepState.Pending);
            UpdateInstallEnabled();
            return;
        }

        ApplyEmailIfChanged(result.UserEmail);
        SetPill(AgentPillText, AgentBullet, "Signed in", PillState.Success);
        UpdateStepPill(2, StepState.Done);

        if (result.ErrorMessage is not null)
        {
            DisplayPicker.IsEnabled = false;
            SetCaption(DisplayStatus, result.ErrorMessage, isError: true);
            UpdateInstallEnabled();
            return;
        }

        SetCaption(DisplayStatus, "Loading your displays…");
        PopulateDisplayPicker(result.Displays);
        SetCaption(DisplayStatus, result.Displays.Count switch
        {
            0 => "No existing displays — a new one will be created.",
            1 => "Auto-selected. You can also create a new one.",
            _ => $"{result.Displays.Count} displays — pick one, or create a new one.",
        });
        UpdateInstallEnabled();
    }

    private void PopulateDisplayPicker(IReadOnlyList<AgentViewDisplay> displays)
    {
        _detectedDisplays = new List<AgentViewDisplay>(displays);
        var items = _detectedDisplays.Select(d => d.FriendlyLabel()).ToList();
        items.Add(CreateNewToken);
        DisplayPicker.ItemsSource   = items;
        DisplayPicker.IsEnabled     = true;
        DisplayPicker.SelectedIndex = 0;
    }

    private void ApplyEmailIfChanged(string? email)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            UserEmailUpdated?.Invoke(this, email);
        }
    }

    private void UpdateInstallEnabled()
    {
        var orgPicked     = OrgPicker.IsEnabled     && OrgPicker.SelectedIndex >= 0;
        var displayPicked = DisplayPicker.IsEnabled && DisplayPicker.SelectedIndex >= 0;

        InstallButton.IsEnabled = orgPicked && displayPicked;

        if (!orgPicked || !displayPicked)
        {
            if (!_installComplete)
            {
                SetPill(InstallPillText, InstallBullet, "Not published", PillState.Pending);
                InstallStatus.Text = "Sign in to Claude and agentView first";
            }
        }
        else if (!_installComplete)
        {
            SetPill(InstallPillText, InstallBullet, "Ready to publish", PillState.Pending);
            InstallStatus.Text = "Ready when you are.";
        }
        // _installComplete && both picked → don't touch
    }

    /// <summary>
    /// Receives the most recent ping result from the host. A successful
    /// sync proves the API key works and the bridge is delivering.
    /// </summary>
    public void UpdateFromSyncResult(PingResult result)
    {
        if (result.Status == PingStatus.Ok)
        {
            _installComplete = true;
            UpdateStepPill(3, StepState.Done);
            InstallButton.Content = "Re-publish";

            var displayLabel = !string.IsNullOrEmpty(_config.AgentViewDisplayName)
                ? _config.AgentViewDisplayName
                : !string.IsNullOrEmpty(_config.AgentViewDisplayId)
                    ? _config.AgentViewDisplayId
                    : "this display";
            SetPill(InstallPillText, InstallBullet, "Published", PillState.Success);
            ShowInstallDetails(
                slot:        _config.AgentViewSlotSlug,
                publishedAt: $"{result.At:HH:mm:ss}",
                apiKey:      _config.AgentViewApiKey);
            InstallStatus.Text = $"Live on {displayLabel}";
            UpdatePairEnabled();
        }
        else if (result.Status == PingStatus.Failed && _installComplete)
        {
            SetPill(InstallPillText, InstallBullet, "Sync failed", PillState.Danger);
            InstallStatus.Text = Truncate(result.Message, 80) + " — try Re-publish";
        }
        else if (result.Status == PingStatus.Unknown && !_installComplete)
        {
            if (!string.IsNullOrEmpty(_config.AgentViewDisplayId))
            {
                _installComplete = true;
                UpdateStepPill(3, StepState.Done);
                InstallButton.Content = "Re-publish";
                SetPill(InstallPillText, InstallBullet, "Published", PillState.Pending);
                ShowInstallDetails(
                    slot:        _config.AgentViewSlotSlug,
                    publishedAt: "waiting for first sync…",
                    apiKey:      _config.AgentViewApiKey);
                InstallStatus.Text = "Previously published · waiting for first sync to confirm…";
                UpdatePairEnabled();
            }
        }
    }

    private void ShowInstallDetails(string? slot, string publishedAt, string? apiKey)
    {
        InstallDetails.Visibility  = Visibility.Visible;
        InstallSlotValue.Text      = string.IsNullOrEmpty(slot) ? "—" : slot;
        InstallPublishedValue.Text = publishedAt;
        InstallApiKeyValue.Text    = MaskApiKey(apiKey);
    }

    private static string MaskApiKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "—";
        }
        var prefix = key.Length >= 4 ? key[..4] : key;
        var suffix = key.Length >= 4 ? key[^4..] : "";
        return $"{prefix}_•••••••• {suffix}";
    }

    private async void OnInstallClicked(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        SetPill(InstallPillText, InstallBullet, "Working…", PillState.Pending);
        InstallStatus.Text = "Provisioning…";

        try
        {
            if (OrgPicker.SelectedIndex < 0 || OrgPicker.SelectedIndex >= _detectedOrgs.Count)
            {
                throw new InvalidOperationException("No Claude organisation selected.");
            }
            if (DisplayPicker.SelectedIndex < 0)
            {
                throw new InvalidOperationException("No agentView display selected.");
            }

            var org = _detectedOrgs[OrgPicker.SelectedIndex];

            // null means "create new display"
            AgentViewDisplay? selectedDisplay = null;
            var pick = DisplayPicker.SelectedItem as string;
            if (pick != CreateNewToken && DisplayPicker.SelectedIndex < _detectedDisplays.Count)
            {
                selectedDisplay = _detectedDisplays[DisplayPicker.SelectedIndex];
            }

            var result = await _coordinator.PublishAsync(
                org, selectedDisplay,
                progress: msg => InstallStatus.Text = msg)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                SetPill(InstallPillText, InstallBullet,
                    result.IsAuthError ? "Auth expired" : "Failed",
                    result.IsAuthError ? PillState.Danger : PillState.Danger);
                InstallStatus.Text = result.ErrorMessage;
                if (result.IsAuthError)
                {
                    SetPill(AgentPillText, AgentBullet, "Session expired — sign in again", PillState.Danger);
                }
                return;
            }

            _installComplete = true;
            UpdateStepPill(3, StepState.Done);
            InstallButton.Content = "Re-publish";
            SetPill(InstallPillText, InstallBullet, "Published", PillState.Success);
            ShowInstallDetails(
                slot:        result.SlotSlug,
                publishedAt: $"{result.PublishedAt:HH:mm:ss}",
                apiKey:      result.ApiKey);
            InstallStatus.Text = $"Live on {result.DisplayName} · refreshes within 120 s";
            UpdatePairEnabled();
            InstallCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _log.Error("Unexpected error in install click", ex);
            SetPill(InstallPillText, InstallBullet, "Failed", PillState.Danger);
            InstallStatus.Text = "Setup failed: " + ex.Message;
        }
        finally
        {
            var orgPicked     = OrgPicker.IsEnabled     && OrgPicker.SelectedIndex >= 0;
            var displayPicked = DisplayPicker.IsEnabled && DisplayPicker.SelectedIndex >= 0;
            InstallButton.IsEnabled = orgPicked && displayPicked;
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────

    private static void SetCaption(TextBlock target, string text, bool isError = false)
    {
        target.Text = text;
        target.SetResourceReference(TextBlock.StyleProperty, isError ? "Text.Status.Danger" : "Text.Caption");
    }

    private enum PillState { Pending, Success, Danger }

    private static void SetPill(
        TextBlock pillText, System.Windows.Shapes.Ellipse bullet,
        string label, PillState state)
    {
        var (bulletStyle, textStyle) = state switch
        {
            PillState.Success => ("StatusBullet.Small.Success", "PillText.Success"),
            PillState.Danger  => ("StatusBullet.Small.Danger",  "PillText.Danger"),
            _                 => ("StatusBullet.Small",         "PillText.Pending"),
        };
        bullet.SetResourceReference(System.Windows.Shapes.Ellipse.StyleProperty, bulletStyle);
        pillText.SetResourceReference(TextBlock.StyleProperty, textStyle);
        pillText.Text = label;
    }

    private enum StepState { Pending, Done }

    private void UpdateStepPill(int stepNumber, StepState state)
    {
        var (pill, text) = stepNumber switch
        {
            1 => (Step1Pill, Step1Text),
            2 => (Step2Pill, Step2Text),
            3 => (Step3Pill, Step3Text),
            _ => throw new ArgumentOutOfRangeException(nameof(stepNumber)),
        };
        var doneStyle    = (Style)FindResource("StepDot.Done");
        var pendingStyle = (Style)FindResource("StepDot");
        var activeText   = (Style)FindResource("StepDotText.Active");
        var mutedText    = (Style)FindResource("StepDotText");
        if (state == StepState.Done)
        {
            pill.Style = doneStyle; text.Style = activeText; text.Text = "✓";
        }
        else
        {
            pill.Style = pendingStyle; text.Style = mutedText; text.Text = stepNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (_installComplete && stepNumber == 3)
        {
            Step3Pill.Style = doneStyle;
            Step3Text.Style = activeText;
            Step3Text.Text  = "✓";
        }
    }

    // ─── Pair physical display ──────────────────────────────────────

    private void UpdatePairEnabled()
    {
        var hasDisplay = !string.IsNullOrEmpty(_config.AgentViewDisplayId);
        PairButton.IsEnabled = GetPairCode().Length == 6;

        if (hasDisplay)
        {
            SetBulletStatus(PairBullet, PairStatus, "6-character code, e.g. ABC123", BulletState.Pending);
        }
        else if (_installComplete)
        {
            SetBulletStatus(PairBullet, PairStatus,
                "Click Re-publish to refresh display tracking, then pair.", BulletState.Pending);
        }
        else
        {
            SetBulletStatus(PairBullet, PairStatus,
                "You'll need to publish a display first (step 3 above).", BulletState.Pending);
        }
    }

    private void OnPairCharChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        // Pasted longer strings → spread across boxes.
        if (box.Text.Length > 1)
        {
            var idx    = Array.IndexOf(_pairBoxes, box);
            var stream = new string(box.Text
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
            box.Text = "";
            for (var i = 0; i < stream.Length && idx + i < _pairBoxes.Length; i++)
            {
                _pairBoxes[idx + i].Text = stream[i].ToString();
            }
            var lastFilled = Math.Min(idx + stream.Length, _pairBoxes.Length) - 1;
            if (lastFilled + 1 < _pairBoxes.Length)
            {
                _pairBoxes[lastFilled + 1].Focus();
            }
            else
            {
                _pairBoxes[lastFilled].Focus();
            }
        }
        else if (box.Text.Length == 1)
        {
            var idx = Array.IndexOf(_pairBoxes, box);
            if (idx >= 0 && idx + 1 < _pairBoxes.Length)
            {
                _pairBoxes[idx + 1].Focus();
                _pairBoxes[idx + 1].SelectAll();
            }
        }

        PairButton.IsEnabled = !string.IsNullOrEmpty(_config.AgentViewDisplayId)
                             && GetPairCode().Length == 6;
    }

    private void OnPairKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Back)
        {
            return;
        }
        if (sender is not TextBox box)
        {
            return;
        }
        if (box.Text.Length > 0)
        {
            return;  // backspace deletes the char first
        }
        var idx = Array.IndexOf(_pairBoxes, box);
        if (idx > 0)
        {
            _pairBoxes[idx - 1].Focus();
            _pairBoxes[idx - 1].SelectAll();
            e.Handled = true;
        }
    }

    private string GetPairCode() =>
        string.Concat(_pairBoxes.Select(b => (b.Text ?? "").Trim().ToUpperInvariant()));

    private async void OnPairClicked(object sender, RoutedEventArgs e)
    {
        var code = GetPairCode();
        if (code.Length != 6)
        {
            SetBulletStatus(PairBullet, PairStatus, "Enter all 6 characters first.", BulletState.Danger);
            _pairBoxes.FirstOrDefault(b => string.IsNullOrEmpty(b.Text))?.Focus();
            return;
        }

        PairButton.IsEnabled = false;
        SetBulletStatus(PairBullet, PairStatus, "Pairing…", BulletState.Pending);
        try
        {
            var result = await _coordinator.PairByCodeAsync(code).ConfigureAwait(true);
            if (result.Success)
            {
                foreach (var b in _pairBoxes)
                {
                    b.Text = "";
                }
                SetBulletStatus(PairBullet, PairStatus,
                    "Paired. The screen switches to the Token Counter within seconds.",
                    BulletState.Success);
            }
            else
            {
                SetBulletStatus(PairBullet, PairStatus,
                    ExtractServerMessage(result.ErrorMessage ?? "Pairing failed."),
                    BulletState.Danger);
            }
        }
        finally
        {
            PairButton.IsEnabled = !string.IsNullOrEmpty(_config.AgentViewDisplayId)
                                 && GetPairCode().Length == 6;
        }
    }

    private enum BulletState { Pending, Success, Danger }

    private static void SetBulletStatus(System.Windows.Shapes.Ellipse bullet, TextBlock text, string label, BulletState state)
    {
        var styleKey = state switch
        {
            BulletState.Success => "StatusBullet.Success",
            BulletState.Danger  => "StatusBullet.Danger",
            _                   => "StatusBullet.Pending",
        };
        bullet.SetResourceReference(System.Windows.Shapes.Ellipse.StyleProperty, styleKey);
        text.Text = label;
        text.SetResourceReference(TextBlock.ForegroundProperty,
            state switch
            {
                BulletState.Success => "Brush.Success",
                BulletState.Danger  => "Brush.Danger",
                _                   => "Brush.TextSecondary",
            });
    }

    private static string ExtractServerMessage(string raw)
    {
        try
        {
            var jsonStart = raw.IndexOf('{');
            if (jsonStart < 0)
            {
                return raw;
            }
            var jsonEnd = raw.LastIndexOf('}');
            if (jsonEnd <= jsonStart)
            {
                return raw;
            }
            var json = raw.Substring(jsonStart, jsonEnd - jsonStart + 1);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var msg) &&
                msg.ValueKind == JsonValueKind.String)
            {
                return msg.GetString() ?? raw;
            }
        }
        catch { /* fall through */ }
        return raw.Length > 200 ? raw[..200] + "…" : raw;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");

    private void ShowWebView2Missing(WebView2RuntimeMissingException ex)
    {
        SetCaption(OrgStatus, ex.Message, isError: true);
        var result = MessageBox.Show(Window.GetWindow(this)!,
            ex.Message + "\n\nOpen the download page?",
            "WebView2 Runtime missing", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result == MessageBoxResult.Yes)
        {
            Services.ExternalLink.OpenInBrowser(
                "https://developer.microsoft.com/microsoft-edge/webview2/", _log);
        }
    }

    /// <summary>
    /// Reads the agentView base URL from the underlying config. The
    /// coordinator doesn't expose this directly (it's not business
    /// logic), so we pull it from config which the coordinator has
    /// already persisted into via its backing <see cref="AppConfig"/>.
    /// </summary>
    private string GetAgentBaseUrl() =>
        string.IsNullOrWhiteSpace(_config.AgentViewBaseUrl)
            ? "https://agentview.de"
            : _config.AgentViewBaseUrl.TrimEnd('/');
}
