// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Workbench;

/// <summary>A region of the shell's primary navigation.</summary>
public sealed record Region(string Name, string Question);

/// <summary>
/// The shell's state: the target context, navigation, documents, the inspector, the activity log, the layout and the views
/// of home, work and runtime. Estate state comes from the runtime's responses; release notices are labelled machine-local knowledge.
/// </summary>
public sealed partial class ShellViewModel : Observable
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private readonly Func<string, IEstateConnection> connect;
    private readonly IRuntimeManager runtime;
    private readonly Preferences preferences;
    private readonly ReleaseUpdates updates;
    private string updateDisclosure = string.Empty;
    private string updateNotice = string.Empty;
    private IEstateConnection? connection;
    private EstateView estate = EstateView.From(default(JsonElement), string.Empty);
    private Document? selectedDocument;
    private Region selectedRegion;
    private string inspector = "Select something to inspect it.";
    private string status = "No estate open.";
    private string actingPrincipal = "read-only: no admitted device on this machine";
    private DeviceRow? selectedActing;
    private bool choosingActing;
    private string actingRole = string.Empty;
    private string freshness = "no view yet";
    private string lastPersonObservation = "Last observed by a person: unknown.";
    private ShellLayout layout;
    private string estatePath = string.Empty;

    /// <summary>A shell over a way to connect and a runtime manager.</summary>
    public ShellViewModel(Func<string, IEstateConnection> connect, IRuntimeManager runtime, Preferences preferences)
        : this(connect, runtime, preferences, new ReleaseUpdates(Vericairn.Providers.GitHub.Releases.GitHubReleaseSource.Default)) { }

    internal ShellViewModel(Func<string, IEstateConnection> connect, IRuntimeManager runtime, Preferences preferences, ReleaseUpdates updates)
    {
        RefreshCommand = new AsyncCommand(() => RefreshAsync(), () => !IsBusy);
        CancelCommand = new AsyncCommand(() => { CancelCurrentRequest(); return Task.CompletedTask; }, () => IsBusy);
        this.connect = connect;
        this.runtime = runtime;
        this.preferences = preferences;
        this.updates = updates;
        updateDisclosure = ReleaseUpdates.Disclosure(updates.Enabled);
        layout = preferences.LoadLayout();
        Regions = new ObservableCollection<Region>(
        [
            new("Home", "What needs me? What changed?"),
            new("Work", "Who is doing what, with what permission?"),
            new("Goals", "What are we carrying out, and what waits on whom?"),
            new("Products", "What are we building and why?"),
            new("Releases", "What is ready, delivered, running or still owed?"),
            new("Runtime", "Is the daemon serving this estate, and how do I start or stop it?"),
            new("Records", "What exactly is on record?"),
        ]);
        selectedRegion = Regions[0];
        Recent = new ObservableCollection<string>(preferences.Recent());
    }

    /// <summary>Primary navigation.</summary>
    public ObservableCollection<Region> Regions { get; }

    /// <summary>Open documents.</summary>
    public ObservableCollection<Document> Documents { get; } = [];

    /// <summary>The activity log: what the client did and what the runtime answered.</summary>
    public ObservableCollection<string> Activity { get; } = [];

    /// <summary>Attention items of home.</summary>
    public ObservableCollection<AttentionItem> Attention { get; } = [];

    /// <summary>The progress report's summary on home, one row per section; opening a row opens the whole report.</summary>
    public ObservableCollection<ProgressRow> Progress { get; } = [];

    /// <summary>Work orders.</summary>
    public ObservableCollection<WorkOrderRow> WorkOrders { get; } = [];

    /// <summary>Assignments.</summary>
    public ObservableCollection<AssignmentRow> Assignments { get; } = [];

    /// <summary>Releases.</summary>
    public ObservableCollection<ReleaseRowView> Releases { get; } = [];

    /// <summary>Recently opened estates.</summary>
    public ObservableCollection<string> Recent { get; }

    /// <summary>Admitted devices held on this machine; the acting principal is chosen among their principals.</summary>
    public ObservableCollection<DeviceRow> ActingCandidates { get; } = [];

    /// <summary>
    /// An explicit acting principal id (the --as argument). The selector then shows the admitted device of that principal held
    /// here, the one the runtime signs with; choosing another device in the selector replaces it.
    /// </summary>
    public string? ActingOverride { get; set; }

    /// <summary>The chosen device, and so the acting principal.</summary>
    public DeviceRow? SelectedActing
    {
        get => selectedActing;
        set
        {
            if (Set(ref selectedActing, value))
            {
                // A person choosing a device chooses its principal, which replaces a principal named with --as; the shell's own
                // selection while it refreshes the candidates never does.
                if (!choosingActing && value is not null && ActingOverride is not null && value.Principal != ActingOverride)
                {
                    ActingOverride = null;
                }

                _ = RefreshActingAsync();
            }
        }
    }

    /// <summary>The principal id writes are authored by, or null.</summary>
    public string? ActingPrincipalId => ActingOverride ?? selectedActing?.Principal;

    /// <summary>The selected region.</summary>
    public Region SelectedRegion
    {
        get => selectedRegion;
        set
        {
            if (Set(ref selectedRegion, value) && value is not null)
            {
                Raise(nameof(HomeSelected));
                _ = SelectRegionAsync(value);
            }
        }
    }

    /// <summary>Whether home is the selected region.</summary>
    public bool HomeSelected => selectedRegion?.Name == "Home";

    /// <summary>The selected document.</summary>
    public Document? SelectedDocument { get => selectedDocument; set => Set(ref selectedDocument, value); }

    /// <summary>The inspector text.</summary>
    public string Inspector { get => inspector; private set => Set(ref inspector, value); }

    /// <summary>The status line.</summary>
    public string Status { get => status; internal set => Set(ref status, value); }

    /// <summary>The machine-local release check's disclosure and off switch, visible even without an estate.</summary>
    public string UpdateDisclosure { get => updateDisclosure; private set => Set(ref updateDisclosure, value); }

    /// <summary>A newer published release and the explicit upgrade routes, outside canonical estate status.</summary>
    public string UpdateNotice
    {
        get => updateNotice;
        private set { if (Set(ref updateNotice, value)) Raise(nameof(HasUpdateNotice)); }
    }

    /// <summary>Whether there is a newer-release notice to display.</summary>
    public bool HasUpdateNotice => updateNotice.Length > 0;

    internal async Task RefreshUpdatesAsync()
    {
        UpdateDisclosure = ReleaseUpdates.Disclosure(updates.Enabled);
        var release = await updates.CheckAsync().ConfigureAwait(true);
        string installed = typeof(ShellViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        UpdateNotice = ReleaseUpdates.Notice(release, installed);
    }

    /// <summary>The path typed on the first screen.</summary>
    public string EstatePath { get => estatePath; set => Set(ref estatePath, value); }

    /// <summary>Whether an estate is open (a connection exists, available or not).</summary>
    public bool HasEstate => connection is not null;

    /// <summary>Estate display name.</summary>
    public string EstateName => connection is null ? "No estate" : estate.Name;

    /// <summary>The pinned Vericairn.</summary>
    public string Pin => estate.Pin;

    /// <summary>The runtime vocabulary word.</summary>
    public string RuntimeMode => connection?.Mode ?? "unavailable";

    /// <summary>The runtime mode's label.</summary>
    public string RuntimeLabel => StateLanguage.Label("runtime", RuntimeMode);

    /// <summary>Where the daemon serves.</summary>
    public string RuntimeWhere => connection?.Where ?? string.Empty;

    /// <summary>The acting principal, as the devices held here say.</summary>
    public string ActingPrincipal { get => actingPrincipal; private set => Set(ref actingPrincipal, value); }

    /// <summary>When the view was taken.</summary>
    public string Freshness { get => freshness; private set => Set(ref freshness, value); }

    /// <summary>The last explicit human observation from status, distinct from this client's refresh time.</summary>
    public string LastPersonObservation { get => lastPersonObservation; private set => Set(ref lastPersonObservation, value); }

    /// <summary>The layout.</summary>
    public ShellLayout Layout
    {
        get => layout;
        private set
        {
            if (Set(ref layout, value))
            {
                preferences.SaveLayout(value);
                Raise(nameof(LeftVisible));
                Raise(nameof(RightVisible));
                Raise(nameof(BottomVisible));
            }
        }
    }

    /// <summary>Left panel visibility.</summary>
    public bool LeftVisible => layout.LeftVisible;

    /// <summary>Right panel visibility.</summary>
    public bool RightVisible => layout.RightVisible;

    /// <summary>Bottom panel visibility.</summary>
    public bool BottomVisible => layout.BottomVisible;

    /// <summary>Opens an estate: connects, starts the daemon when none answers, and refreshes.</summary>
    private async Task OpenEstateCoreAsync(string root, CancellationToken cancellationToken = default)
    {
        root = root.Trim();
        if (string.IsNullOrEmpty(root))
        {
            Status = "Name the estate's root directory.";
            return;
        }

        if (!File.Exists(Path.Combine(root, "estate", "manifest.json")))
        {
            Status = $"{root} is not an estate root (no estate/manifest.json). Reconnect, locate it, inspect an export or recover; nothing is created here.";
            return;
        }

        connection?.Dispose();
        connection = connect(root);
        await connection.RefreshAsync(cancellationToken).ConfigureAwait(true);
        Log($"opened {root}: {connection.Mode}");
        if (connection.Api is null)
        {
            Status = "No daemon answers for this estate; starting the runtime.";
            await StartRuntimeCoreAsync(cancellationToken).ConfigureAwait(true);
        }

        preferences.Remember(root);
        if (!Recent.Contains(root))
        {
            Recent.Insert(0, root);
        }

        await RefreshCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
        Raise(nameof(HasEstate));
    }

    /// <summary>Refreshes every view from the runtime.</summary>
    private async Task RefreshCoreAsync(CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            return;
        }

        await connection.RefreshAsync(cancellationToken).ConfigureAwait(true);
        Raise(nameof(RuntimeMode));
        Raise(nameof(RuntimeLabel));
        Raise(nameof(RuntimeWhere));
        if (connection.Api is null)
        {
            Status = $"{connection.Root}: {StateLanguage.Label("runtime", "unavailable")}; the last view is kept as cached.";
            RequestState = "unavailable";
            Freshness = "cached";
            foreach (HandoffDocument page in Documents.OfType<HandoffDocument>())
            {
                page.Unavailable();
            }
            return;
        }

        try
        {
            StatusResponse response = (await connection.Api.StatusAsync(cancellationToken: cancellationToken).ConfigureAwait(true));
            LastPersonObservation = response.Json.TryGetProperty("lastObservedByPerson", out JsonElement observed) && observed.ValueKind == JsonValueKind.Object
                ? $"Last observed by a person: {Str(observed, "at")} · {Str(observed, "name") ?? Str(observed, "by")} · observation {Str(observed, "record")}."
                : "Last observed by a person: unknown (no estate observation recorded).";
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            estate = EstateView.From(response, now);
            Freshness = $"current as of {now}";

            Replace(Attention, estate.Attention);
            foreach (HandoffDocument page in Documents.OfType<HandoffDocument>())
            {
                if (page.HasPackage)
                {
                    await page.CompareAsync(cancellationToken).ConfigureAwait(true);
                }
            }
            Replace(WorkOrders, estate.WorkOrders);
            Replace(Assignments, estate.Assignments);
            Replace(Releases, estate.Releases);
            foreach (LivingDocumentsWorkspace page in Documents.OfType<LivingDocumentsWorkspace>().ToList())
            {
                page.Fill((await TryAsync(async () => (await connection.Api.DocumentsAsync(cancellationToken: cancellationToken).ConfigureAwait(true))).ConfigureAwait(true)) ?? []);
            }

            foreach (ReleaseWorkspace page in Documents.OfType<ReleaseWorkspace>().ToList())
            {
                await FillReleaseAsync(page, cancellationToken: cancellationToken).ConfigureAwait(true);
            }

            foreach (RuntimeDocument page in Documents.OfType<RuntimeDocument>().ToList())
            {
                await FillRuntimeAsync(page, cancellationToken: cancellationToken).ConfigureAwait(true);
            }

            foreach (GoalsWorkspace page in Documents.OfType<GoalsWorkspace>().ToList())
            {
                await page.FillAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
            }
            IReadOnlyList<DeviceRow> devices = (await connection.Api.DevicesAsync(cancellationToken: cancellationToken).ConfigureAwait(true));
            List<DeviceRow> held = devices.Where(d => d.HeldHere && d.Admission.StartsWith("admitted", StringComparison.OrdinalIgnoreCase)).ToList();
            string? previous = selectedActing?.Id;
            choosingActing = true;
            try
            {
                Replace(ActingCandidates, held);

                // A principal named with --as gets the device the runtime signs its records with: its first admitted device held
                // here, in the runtime's own order. Otherwise the chosen device stays chosen while it is held, or the first is.
                selectedActing = ActingOverride is not null
                    ? held.FirstOrDefault(d => d.Principal == ActingOverride)
                    : held.FirstOrDefault(d => d.Id == previous) ?? held.FirstOrDefault();
                Raise(nameof(SelectedActing));
            }
            finally
            {
                choosingActing = false;
            }

            RecordResponse? principal = (await DescribeActingAsync(cancellationToken: cancellationToken).ConfigureAwait(true));
            string? recipient = HumanRecipient(principal);
            await RefreshCommunicationAsync(response, recipient, cancellationToken: cancellationToken).ConfigureAwait(true);
            // Home and required attention use the same observation, including its failure. A second fetch used to repeat
            // the full estate load just to present the same report under the now-known communication settings.
            ProgressResponse? progress = await TryAsync(() => connection.Api.ProgressAsync(cancellationToken)).ConfigureAwait(true);
            FillProgress(progress);
            await AddCommunicationAttentionAsync(recipient, progress, cancellationToken: cancellationToken).ConfigureAwait(true);
            Status = $"{estate.Name}: {estate.Records} records, {Attention.Count} attention items, {StateLanguage.Label("runtime", connection.Mode)}.";
            Raise(nameof(EstateName));
            Raise(nameof(Pin));
            await ShowRegionAsync(SelectedRegion, cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when ((exception is IOException or InvalidOperationException or HttpRequestException or JsonException) && ClientFailure.FromException(exception) is null)
        {
            Status = "The runtime did not answer: " + exception.Message;
            RequestState = "unavailable";
            Freshness = "cached";
            foreach (HandoffDocument page in Documents.OfType<HandoffDocument>())
            {
                page.Unavailable();
            }
        }
    }

    private async Task<RecordResponse?> DescribeActingAsync(CancellationToken cancellationToken = default)
    {
        string? id = ActingPrincipalId;
        if (id is null)
        {
            ActingPrincipal = "read-only: no admitted device on this machine";
            actingRole = string.Empty;
            return null;
        }

        string name = ActingCandidates.FirstOrDefault(d => d.Principal == id)?.PrincipalName ?? id;
        actingRole = string.Empty;
        RecordResponse? principal = null;
        try
        {
            // A principal is identified by its first record; its name and roles are those of its current revision.
            principal = connection?.Api is IEstateApi api ? (await CurrentPrincipalAsync(api, id, cancellationToken: cancellationToken).ConfigureAwait(true)) : null;
            if (principal is not null && principal.Envelope.TryGetProperty("body", out JsonElement body))
            {
                name = body.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? name : name;
                if (body.TryGetProperty("roles", out JsonElement roles) && roles.ValueKind == JsonValueKind.Array)
                {
                    actingRole = roles.EnumerateArray().Any(r => r.GetString() == "operator") ? "operator" : roles.EnumerateArray().Select(r => r.GetString()).FirstOrDefault() ?? string.Empty;
                }
            }
        }
        catch (Exception exception) when ((exception is IOException or InvalidOperationException or HttpRequestException or JsonException) && ClientFailure.FromException(exception) is null)
        {
            Log("principal lookup failed: " + exception.Message);
        }

        ActingPrincipal = selectedActing is not null && selectedActing.Principal == id
            ? $"{name} (device {selectedActing.Name ?? selectedActing.Id})"
            : (await SignaturesRequiredAsync(cancellationToken: cancellationToken).ConfigureAwait(true))
                ? $"{name} (named with --as; no admitted device of this principal is held here, so writes will be refused)"
                : $"{name} (named with --as; no device of this principal is held here, so its records will be unsigned)";
        Raise(nameof(ActingPrincipalId));
        return principal;
    }

    /// <summary>Whether the estate's current policy requires every record to be signed, read as the runtime reads it.</summary>
    private async Task<bool> SignaturesRequiredAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<RecordResponse> charters = connection?.Api is IEstateApi current ? (await current.RecordsAsync("estate", cancellationToken).ConfigureAwait(true)) : [];
            RecordResponse? charter = charters.Count > 0 ? charters[0] : null;
            return charter is not null
                && charter.Envelope.TryGetProperty("body", out JsonElement body)
                && body.TryGetProperty("policies", out JsonElement policies)
                && policies.TryGetProperty("signatures", out JsonElement policy)
                && policy.ValueKind == JsonValueKind.String
                && policy.GetString() == "required";
        }
        catch (Exception exception) when ((exception is IOException or InvalidOperationException or HttpRequestException or JsonException) && ClientFailure.FromException(exception) is null)
        {
            Log("estate policy lookup failed: " + exception.Message);
            return true;
        }
    }

    /// <summary>The current revision of the principal whose identity, the id of its first record, is given; or null.</summary>
    internal static async Task<RecordResponse?> CurrentPrincipalAsync(IEstateApi api, string identity, CancellationToken cancellationToken = default)
    {
        foreach (RecordResponse candidate in (await api.RecordsAsync("principal", cancellationToken: cancellationToken).ConfigureAwait(true)))
        {
            string first = candidate.Id;
            string? earlier = Supersedes(candidate.Envelope);
            var seen = new HashSet<string>(StringComparer.Ordinal) { first };
            while (earlier is not null && seen.Add(earlier))
            {
                first = earlier;
                earlier = (await api.RecordAsync(earlier, cancellationToken: cancellationToken).ConfigureAwait(true)) is RecordResponse previous ? Supersedes(previous.Envelope) : null;
            }

            if (first == identity)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? Supersedes(JsonElement envelope) =>
        envelope.TryGetProperty("supersedes", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Starts the daemon through the runtime manager and reconnects.</summary>
    private async Task StartRuntimeCommandCoreAsync(CancellationToken cancellationToken = default)
    {
        await StartRuntimeCoreAsync(cancellationToken).ConfigureAwait(true);
        await RefreshCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    private async Task StartRuntimeCoreAsync(CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            return;
        }

        RuntimeOutcome outcome = await runtime.StartAsync(connection.Root, cancellationToken).ConfigureAwait(true);
        Log($"runtime start: {(outcome.Ok ? "answered" : "did not answer")}: {outcome.Output}");
    }

    /// <summary>Asks the daemon to stop and refreshes.</summary>
    private async Task StopRuntimeCoreAsync(CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            return;
        }

        RuntimeOutcome outcome = await runtime.StopAsync(connection.Root, cancellationToken).ConfigureAwait(true);
        Log($"runtime stop: {outcome.Output}");
        await RefreshCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Shows the install plan without installing; the runtime workspace, when open, records that the plan was shown.</summary>
    private async Task ShowInstallPlanCoreAsync(CancellationToken cancellationToken = default)
    {
        if (connection is null)
        {
            return;
        }

        RuntimeOutcome outcome = await runtime.InstallPlanAsync(connection.Root, cancellationToken).ConfigureAwait(true);
        if (Documents.OfType<RuntimeDocument>().FirstOrDefault() is RuntimeDocument page)
        {
            page.Plan = outcome.Output;
            page.PlanShown = outcome.Ok;
            page.Output = outcome.Ok ? "The plan is shown; install and uninstall follow it." : outcome.Output;
            SelectedDocument = page;
        }
        else
        {
            Open(new TextDocument("Runtime install plan", outcome.Output, "plan"));
        }
    }

    /// <summary>Installs the per-user service, after the plan was shown in the runtime workspace.</summary>
    private async Task InstallRuntimeCoreAsync(CancellationToken cancellationToken = default)
    {
        RuntimeDocument page = (await OpenRuntimeCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true));
        if (connection is null)
        {
            return;
        }

        if (!page.PlanShown)
        {
            page.Output = "Show the install plan first; installing changes this machine's login items.";
            Status = page.Output;
            return;
        }

        RuntimeOutcome outcome = await runtime.InstallAsync(connection.Root, cancellationToken).ConfigureAwait(true);
        page.Output = outcome.Output;
        Log($"runtime install: {(outcome.Ok ? "done" : "failed")}: {outcome.Output}");
        await RefreshCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Removes the per-user service, after the plan was shown in the runtime workspace.</summary>
    private async Task UninstallRuntimeCoreAsync(CancellationToken cancellationToken = default)
    {
        RuntimeDocument page = (await OpenRuntimeCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true));
        if (connection is null)
        {
            return;
        }

        if (!page.PlanShown)
        {
            page.Output = "Show the install plan first; it names the service that uninstall removes.";
            Status = page.Output;
            return;
        }

        RuntimeOutcome outcome = await runtime.UninstallAsync(connection.Root, cancellationToken).ConfigureAwait(true);
        page.Output = outcome.Output;
        Log($"runtime uninstall: {(outcome.Ok ? "done" : "failed")}: {outcome.Output}");
        await RefreshCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens or refreshes the runtime workspace.</summary>
    private async Task<RuntimeDocument> OpenRuntimeCoreAsync(CancellationToken cancellationToken = default)
    {
        RuntimeDocument page = Documents.OfType<RuntimeDocument>().FirstOrDefault() ?? new RuntimeDocument();
        await FillRuntimeAsync(page, cancellationToken: cancellationToken).ConfigureAwait(true);
        Open(page);
        return page;
    }

    private async Task FillRuntimeAsync(RuntimeDocument page, CancellationToken cancellationToken = default)
    {
        page.Label = RuntimeLabel;
        page.Where = RuntimeWhere;
        page.Acting = ActingPrincipal;
        HealthResponse? health = connection?.Api is null ? null : (await TryAsync(async () => (await connection.Api.HealthAsync(cancellationToken: cancellationToken).ConfigureAwait(true))).ConfigureAwait(true));
        page.Health = health is null ? "No daemon answers." : $"API {health.Api}; Vericairn {health.Vericairn}; estate {health.Estate}; profile {health.Profile}; record format {health.RecordFormat}; started {health.StartedAt}; process {health.ProcessId}.";
    }

    /// <summary>Opens or refreshes the whole progress report as the runtime renders it (GET /v1/progress), each line naming the records it rests on.</summary>
    private async Task<ProgressDocument> OpenProgressCoreAsync(CancellationToken cancellationToken = default)
    {
        ProgressDocument page = Documents.OfType<ProgressDocument>().FirstOrDefault() ?? new ProgressDocument();
        Open(page);
        FillProgress(connection?.Api is IEstateApi api ? (await TryAsync(() => api.ProgressAsync(cancellationToken)).ConfigureAwait(true)) : null);
        Inspector = page.Text == ProgressDocument.Unavailable ? page.Text : "The estate's recorded progress and latest local agent claims, labelled separately from worker observations. F5 refreshes it.";
        return page;
    }

    /// <summary>Polls progress claims, worker observations and attention without replacing the user's open editing documents.</summary>
    private async Task RefreshProgressCoreAsync(CancellationToken cancellationToken = default)
    {
        if (connection?.Api is IEstateApi api)
        {
            ProgressResponse? progress = await TryAsync(() => api.ProgressAsync(cancellationToken)).ConfigureAwait(true);
            await RefreshCommunicationAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
            FillProgress(progress, onDemand: false);
            Replace(Attention, estate.Attention);

            await AddCommunicationAttentionAsync((await CommunicationRecipientAsync(cancellationToken: cancellationToken).ConfigureAwait(true)), progress, cancellationToken: cancellationToken).ConfigureAwait(true);
            if (progress is null)
            {
                Attention.Add(new AttentionItem("unknown-check", "Progress unavailable", "The runtime did not answer the progress read.", "unknown",
                    Freshness, "Reconnect and refresh before relying on the cached view."));
            }
        }
    }

    /// <summary>Open the hand-over and resume page, preparing its first package only; reopening never replaces its retained basis.</summary>
    private async Task<HandoffDocument> OpenHandoffCoreAsync(CancellationToken cancellationToken = default)
    {
        HandoffDocument? page = Documents.OfType<HandoffDocument>().FirstOrDefault();
        if (page is null)
        {
            page = new HandoffDocument(() => connection?.Api, () => communication);
            await page.PrepareAsync(cancellationToken).ConfigureAwait(true);
        }

        // The work page uses the full document area; Home's summary remains available through navigation.
        if (HomeSelected)
        {
            SelectedRegion = Regions.First(r => r.Name == "Work");
        }

        Open(page);
        Inspector = "A hand-over keeps its exact originals. Save it for another surface, or open a saved package to see every change since it. Refresh compares with the retained basis. Remote is not built.";
        return page;
    }

    /// <summary>Explicitly record an observation by the selected person, through the runtime; a refresh alone never does this.</summary>
    private async Task ObserveEstateCoreAsync(CancellationToken cancellationToken = default)
    {
        if (connection?.Api is not { } api || ActingPrincipalId is not { } by)
        {
            Status = "Connect to the estate and select the observing person's admitted device first.";
            return;
        }

        WriteOutcome? result = (await TryAsync(async () => (await api.ObserveEstateAsync(new EstateObserveRequest(by), cancellationToken: cancellationToken).ConfigureAwait(true))).ConfigureAwait(true));
        if (result?.Written is { } written)
        {
            Log("person observed estate: " + written.Id);
            await RefreshCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
            Status = "Observation recorded: " + written.Id
                + (result.Problem is { } problem ? "\n" + problem.Message : string.Empty);
        }
        else
        {
            Status = result?.Problem?.Message ?? "The runtime did not record the observation.";
        }
    }

    /// <summary>The summary rows of home and an open report from one response of the runtime; without one, both say it is unavailable.</summary>
    private void FillProgress(ProgressResponse? progress, bool onDemand = true)
    {
        Replace(Progress, ProgressView.Summary(progress?.Json ?? default, communication.Fields.TryGetValue("progress", out CommunicationValue? frequency) ? frequency.Value : null, onDemand));
        foreach (ProgressDocument page in Documents.OfType<ProgressDocument>())
        {
            page.Text = progress is null ? ProgressDocument.Unavailable : CommunicationPresentation.Protected(communication, "status", "estate progress", "Complete recorded progress, with claims distinguished from observations.", progress.Text);
        }
    }

    /// <summary>
    /// Opens or refreshes the Goals workspace, showing the goal named or the one shown before: its objective, its conditions with their
    /// verdicts, its plan and steps with the principal each reaches, spend against its budget, its asks and its evaluations. It shows;
    /// it activates, accepts, funds and answers nothing.
    /// </summary>
    private async Task<GoalsWorkspace> OpenGoalsCoreAsync(string? goal = null, CancellationToken cancellationToken = default)
    {
        GoalsWorkspace page = Documents.OfType<GoalsWorkspace>().FirstOrDefault() ?? new GoalsWorkspace(() => connection?.Api, action => RunCommandAsync("Read goal", action));
        await page.FillAsync(goal, cancellationToken).ConfigureAwait(true);
        Open(page);
        Inspector = "The Goals workspace: each goal's objective, conditions, steps and the principal each reaches from the runtime's machine, spend, asks and evaluations. Activation, acceptance, funding and answers are the sponsor's or an operator's, through the command line. F5 refreshes it.";
        return page;
    }

    /// <summary>Opens or refreshes the Documents page: the declared living documents with their state and a review of one.</summary>
    private async Task<LivingDocumentsWorkspace> OpenLivingDocumentsCoreAsync(CancellationToken cancellationToken = default)
    {
        LivingDocumentsWorkspace page = Documents.OfType<LivingDocumentsWorkspace>().FirstOrDefault() ?? new LivingDocumentsWorkspace();
        page.Fill((await TryAsync(async () => connection?.Api is IEstateApi current ? (await current.DocumentsAsync(cancellationToken).ConfigureAwait(true)) : []).ConfigureAwait(true)) ?? []);
        Open(page);
        return page;
    }

    /// <summary>Opens or refreshes the release workspace.</summary>
    private async Task<ReleaseWorkspace> OpenReleaseCoreAsync(CancellationToken cancellationToken = default)
    {
        ReleaseWorkspace page = Documents.OfType<ReleaseWorkspace>().FirstOrDefault() ?? new ReleaseWorkspace((await ProductRowsAsync(cancellationToken: cancellationToken).ConfigureAwait(true)), ((await TryAsync(async () => connection?.Api is IEstateApi current ? (await current.ProvidersAsync(cancellationToken).ConfigureAwait(true)) : []).ConfigureAwait(true)) ?? []).Where(p => p.Kind == "release").ToList());
        await FillReleaseAsync(page, cancellationToken: cancellationToken).ConfigureAwait(true);
        Open(page);
        return page;
    }

    private async Task FillReleaseAsync(ReleaseWorkspace page, CancellationToken cancellationToken = default)
    {
        var rows = new List<DestinationRow>();
        foreach (RecordResponse operation in (await TryAsync(async () => connection?.Api is IEstateApi current ? (await current.RecordsAsync("operation", cancellationToken).ConfigureAwait(true)) : []).ConfigureAwait(true)) ?? [])
        {
            JsonElement body = operation.Envelope.TryGetProperty("body", out JsonElement b) ? b : default;
            if (Str(body, "action") != "dispatch-release-workflow")
            {
                continue;
            }

            JsonElement target = body.TryGetProperty("target", out JsonElement tg) ? tg : default;
            string status = Str(body, "status") ?? "?";
            string version = Str(target, "version") ?? "?";
            string? run = Str(target, "run");
            ReleaseRowView? release = Releases.FirstOrDefault(r => r.Version == version);
            string submission = status switch
            {
                "not-started" => "not dispatched",
                "canceled" => "canceled",
                "uncertain" => "uncertain: no run identified",
                _ => run is null ? "dispatched" : $"run {run} dispatched",
            };
            string review = status switch
            {
                "observed-success" => "run succeeded and the draft was observed",
                "known-failure" => "known failure",
                "dispatched" => "run outcome not yet observed",
                "uncertain" => "unknown until reconciled",
                _ => "none",
            };
            string availability = release?.Status switch
            {
                null => "no candidate recorded",
                "candidate" => "draft observed; candidate recorded, not published",
                "published" => "published, observed",
                _ => StateLanguage.Label("release", release.Status),
            };
            string identity = Str(body, "identity") ?? operation.Id;
            string provider = Str(body, "provider") ?? "?";
            int first = identity.IndexOf(':', StringComparison.Ordinal);
            int last = identity.LastIndexOf(":v", StringComparison.Ordinal);
            string destination = first >= 0 && last > first ? identity[(first + 1)..last] : Str(target, "repository") ?? "?";
            rows.Add(new DestinationRow(identity, version, destination, operation.Id, status, run, release?.Id, release?.Status, "intent recorded", submission, review, availability, provider, Str(target, "product")));
        }

        string? selectedIdentity = page.Selected?.Identity;
        Replace(page.Destinations, rows);
        Replace(page.Releases, Releases.ToList());
        page.Selected = rows.FirstOrDefault(r => r.Identity == selectedIdentity) ?? rows.LastOrDefault();
    }

    private async Task<List<ProductRow>> ProductRowsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<ProductRow>();
        foreach (RecordResponse product in (await TryAsync(async () => connection?.Api is IEstateApi current ? (await current.RecordsAsync("product", cancellationToken).ConfigureAwait(true)) : []).ConfigureAwait(true)) ?? [])
        {
            JsonElement body = product.Envelope.TryGetProperty("body", out JsonElement b) ? b : default;
            string? releaseProvider = null;
            Dictionary<string, string>? releaseSettings = null;
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("release", out JsonElement release) && release.ValueKind == JsonValueKind.Object
                && release.TryGetProperty("destination", out JsonElement destination) && destination.ValueKind == JsonValueKind.Object)
            {
                releaseProvider = Str(destination, "provider");
                releaseSettings = destination.TryGetProperty("settings", out JsonElement settings) && settings.ValueKind == JsonValueKind.Object
                    ? settings.EnumerateObject().Where(s => s.Value.ValueKind == JsonValueKind.String).ToDictionary(s => s.Name, s => s.Value.GetString()!, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal);
            }

            rows.Add(new ProductRow(product.Id, Str(body, "name") ?? "?", Str(body, "slug") ?? string.Empty, Str(body, "summary") ?? string.Empty, Names(body, "editions"), Names(body, "surfaces"), releaseProvider, releaseSettings, product.Hash));
        }

        return rows;
    }

    /// <summary>Shows an attention item in the inspector without opening anything.</summary>
    public void Inspect(AttentionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Inspector = $"{item.KindLabel}\n\n{item.Subject}\n\n{item.Why}\n\nState: {item.State}\nAs of: {item.AsOf}\nNext: {item.NextAction}";
    }

    /// <summary>Opens the subject of an attention item as a document.</summary>
    private async Task OpenAttentionCoreAsync(AttentionItem item, CancellationToken cancellationToken = default)
    {
        Inspect(item);
        if (item.SubjectType == "work-order" && item.SubjectId is not null)
        {
            await OpenWorkOrderCoreAsync(item.SubjectId, cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        else if (item.SubjectType == "ask" && item.SubjectId is not null)
        {
            // An ask opens in the Goals workspace of its goal, where its recommendation sits beside the steps it holds; one with no goal opens as its record.
            IReadOnlyList<RecordResponse> asks = (await TryAsync(async () => (connection?.Api is IEstateApi current ? (await current.RecordAsync(item.SubjectId, cancellationToken).ConfigureAwait(true)) : null) is RecordResponse record ? [record] : (IReadOnlyList<RecordResponse>)[]).ConfigureAwait(true)) ?? [];
            string? goal = asks.Count == 1 && asks[0].Envelope.TryGetProperty("body", out JsonElement body) ? Str(body, "goal") : null;
            if (goal is not null)
            {
                await OpenGoalsCoreAsync(goal, cancellationToken: cancellationToken).ConfigureAwait(true);
                Inspect(item);
            }
            else
            {
                await OpenRecordCoreAsync(item.SubjectId, cancellationToken: cancellationToken).ConfigureAwait(true);
            }
        }
        else if (item.SubjectType == "result" && item.SubjectId is not null)
        {
            await OpenReviewCoreAsync(item.SubjectId, cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        else if (item.SubjectId is not null)
        {
            await OpenRecordCoreAsync(item.SubjectId, cancellationToken: cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>Opens a work order's page: brief, assignments, attempts and results.</summary>
    private async Task OpenWorkOrderCoreAsync(string keyOrId, CancellationToken cancellationToken = default)
    {
        Log($"open work order {keyOrId}");
        if (connection?.Api is null)
        {
            return;
        }

        try
        {
            ObserveResponse? observed = (await connection.Api.ObserveAsync(keyOrId, cancellationToken: cancellationToken).ConfigureAwait(true));
            BriefResponse? brief = (await connection.Api.BriefForAsync(keyOrId, (await CommunicationRecipientAsync(cancellationToken: cancellationToken).ConfigureAwait(true)), cancellationToken: cancellationToken).ConfigureAwait(true));
            if (brief?.Problem is not null)
            {
                Open(new TextDocument(keyOrId, brief.Markdown + "\n" + AssignmentComposer.Refusal(brief.Problem), "refused"));
                return;
            }

            if (observed is null || brief is null)
            {
                Open(new TextDocument(keyOrId, $"No work order '{keyOrId}'.", "missing"));
                return;
            }

            string key = brief.Json.TryGetProperty("workOrder", out JsonElement o) && o.TryGetProperty("body", out JsonElement b) && b.TryGetProperty("key", out JsonElement k) ? k.GetString() ?? keyOrId : keyOrId;
            Open(new WorkOrderDocument(key, brief.Json, CommunicationPresentation.Protected(communication, "result", key, "Complete work and result observations.", observed.Text), CommunicationPresentation.Describe(communication)));
        }
        catch (Exception exception) when ((exception is IOException or InvalidOperationException or HttpRequestException or JsonException) && ClientFailure.FromException(exception) is null)
        {
            Log("observe failed: " + exception.Message);
        }
    }

    /// <summary>Opens an assignment's page: the brief as delegated, the attempts and their results.</summary>
    private async Task<AssignmentDocument?> OpenAssignmentCoreAsync(string assignmentId, CancellationToken cancellationToken = default)
    {
        if (connection?.Api is null)
        {
            return null;
        }

        AssignmentRow? row = Assignments.FirstOrDefault(a => a.Id == assignmentId);
        BriefResponse? brief = row is null ? null : (await TryAsync(async () => (await connection.Api.BriefAsync(row.Key, cancellationToken: cancellationToken).ConfigureAwait(true)) ?? new BriefResponse(string.Empty, default)).ConfigureAwait(true));
        if (brief?.Problem is not null)
        {
            Open(new TextDocument(assignmentId, brief.Markdown + "\n" + AssignmentComposer.Refusal(brief.Problem), "refused"));
            return null;
        }

        JsonElement node = default;
        if (brief is not null && brief.Json.TryGetProperty("assignments", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            node = list.EnumerateArray().FirstOrDefault(a => Str(a, "id") == assignmentId);
        }

        if (row is null || node.ValueKind != JsonValueKind.Object)
        {
            Open(new TextDocument(assignmentId, $"No current assignment '{assignmentId}'.", "missing"));
            return null;
        }

        RecordResponse? original = (await TryAsync(async () => (await connection.Api.RecordAsync(assignmentId, cancellationToken: cancellationToken).ConfigureAwait(true))!).ConfigureAwait(true));
        CommunicationProfile profile = (await AssignmentCommunicationAsync(assignmentId, cancellationToken: cancellationToken).ConfigureAwait(true));
        if (original is null) { profile = profile with { Unavailable = "The exact assignment source is unavailable; the view cannot confirm completeness." }; }
        var page = new AssignmentDocument(row.Key, node, row.ContributorName ?? row.Contributor, profile, original);
        Open(page);
        return page;
    }

    /// <summary>Opens the assignment composer for a work order.</summary>
    private async Task<AssignmentComposer?> OpenComposerCoreAsync(string workOrderKey, CancellationToken cancellationToken = default)
    {
        if (connection?.Api is null)
        {
            return null;
        }

        var contributors = new List<ContributorRow>();
        foreach (RecordResponse principal in (await TryAsync(async () => (await connection.Api.RecordsAsync("principal", cancellationToken: cancellationToken).ConfigureAwait(true))).ConfigureAwait(true)) ?? [])
        {
            JsonElement body = principal.Envelope.TryGetProperty("body", out JsonElement b) ? b : default;
            contributors.Add(new ContributorRow(principal.Id, Str(body, "name") ?? principal.Id, Str(body, "kind") ?? "?"));
        }

        var composer = new AssignmentComposer(workOrderKey, contributors);
        Open(composer);
        return composer;
    }

    /// <summary>Opens the result inbox: results awaiting review across the estate.</summary>
    public ResultInboxDocument OpenInbox()
    {
        var inbox = new ResultInboxDocument(Assignments.Where(a => a.State == "awaiting review" && a.LatestResult is not null).Select(a => new InboxRow(a.Key, a.Id, a.LatestResult!, a.ContributorName ?? a.Contributor, a.Outcome, a.StateLabel)).ToList());
        Open(inbox);
        return inbox;
    }

    /// <summary>Opens the review and decision workspace for a result or a work order revision.</summary>
    private async Task<ReviewWorkspace?> OpenReviewCoreAsync(string recordId, CancellationToken cancellationToken = default)
    {
        if (connection?.Api is null)
        {
            return null;
        }

        try
        {
            RecordResponse? record = (await connection.Api.RecordAsync(recordId, cancellationToken: cancellationToken).ConfigureAwait(true));
            if (record is null)
            {
                Open(new TextDocument(recordId, $"No record '{recordId}'.", "missing"));
                return null;
            }

            JsonElement body = record.Envelope.TryGetProperty("body", out JsonElement b) ? b : default;
            string heading = record.Type == "result" ? $"Review result {record.Id[..12]}" : $"Review {record.Type} {record.Id[..12]}";
            string original = record.Type == "result" ? RenderResult(record, body) + "\n\nComplete original record:\n" + JsonSerializer.Serialize(record.Envelope, Pretty) : JsonSerializer.Serialize(record.Envelope, Pretty);
            string summary = CommunicationPresentation.Protected((await ResultCommunicationAsync(record, cancellationToken: cancellationToken).ConfigureAwait(true)), record.Type == "result" ? "result" : "decision", record.Id + " at " + record.Hash, Str(body, "summary") ?? heading, original);
            var reviews = new List<ReviewRow>();
            foreach (RecordResponse review in (await TryAsync(async () => (await connection.Api.RecordsAsync("review", cancellationToken: cancellationToken).ConfigureAwait(true))).ConfigureAwait(true)) ?? [])
            {
                JsonElement rb = review.Envelope.TryGetProperty("body", out JsonElement x) ? x : default;
                string? subjectId = review.Envelope.TryGetProperty("subject", out JsonElement s) && s.ValueKind == JsonValueKind.Object ? Str(s, "id") : null;
                if (subjectId == record.Id)
                {
                    reviews.Add(new ReviewRow(review.Id, Str(review.Envelope, "by") ?? "?", Str(rb, "independence") ?? "?", Str(rb, "outcome") ?? "?", Str(rb, "summary") ?? string.Empty,
                        rb.TryGetProperty("findings", out JsonElement findings) ? findings : default));
                }
            }

            var workspace = new ReviewWorkspace(record, (await SubjectFacts.AboutAsync(connection.Api, record.Id, cancellationToken: cancellationToken).ConfigureAwait(true)), heading, summary, reviews, ActingPrincipalId ?? string.Empty, actingRole);
            Open(workspace);
            return workspace;
        }
        catch (Exception exception) when ((exception is IOException or InvalidOperationException or HttpRequestException or JsonException) && ClientFailure.FromException(exception) is null)
        {
            Log("review failed: " + exception.Message);
            return null;
        }
    }

    /// <summary>Opens the products page from the product records.</summary>
    private async Task OpenProductsCoreAsync(CancellationToken cancellationToken = default)
    {
        if (connection?.Api is null)
        {
            return;
        }

        Open(new ProductsDocument((await ProductRowsAsync(cancellationToken: cancellationToken).ConfigureAwait(true))));
    }

    /// <summary>Opens a comparison of a record with the record it supersedes, or of two records.</summary>
    private async Task OpenComparisonCoreAsync(string rightId, string? leftId = null, CancellationToken cancellationToken = default)
    {
        if (connection?.Api is null)
        {
            return;
        }

        try
        {
            RecordResponse? right = (await connection.Api.RecordAsync(rightId, cancellationToken: cancellationToken).ConfigureAwait(true));
            if (right is null)
            {
                Open(new TextDocument(rightId, $"No record '{rightId}'.", "missing"));
                return;
            }

            leftId ??= right.Envelope.TryGetProperty("supersedes", out JsonElement s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            RecordResponse? left = leftId is null ? null : (await connection.Api.RecordAsync(leftId, cancellationToken: cancellationToken).ConfigureAwait(true));
            string leftText = left is null ? "(no earlier revision)" : JsonSerializer.Serialize(left.Envelope, Pretty);
            Open(new ComparisonDocument($"Compare {right.Type} {right.Id[..12]}", left?.Id ?? "none", leftText, right.Id, JsonSerializer.Serialize(right.Envelope, Pretty)));
        }
        catch (Exception exception) when ((exception is IOException or InvalidOperationException or HttpRequestException or JsonException) && ClientFailure.FromException(exception) is null)
        {
            Log("comparison failed: " + exception.Message);
        }
    }

    /// <summary>The API for documents that send requests, or null while unavailable.</summary>
    public IEstateApi? Api => connection?.Api;

    private static string RenderResult(RecordResponse record, JsonElement body)
    {
        var lines = new List<string>
        {
            $"Result {record.Id} ({record.Hash})",
            $"By {Str(record.Envelope, "by")} at {Str(record.Envelope, "at")}",
            $"Outcome: {StateLanguage.Label("result-outcome", Str(body, "outcome"))}",
            string.Empty,
            Str(body, "summary") ?? string.Empty,
            string.Empty,
        };
        foreach ((string name, string title) in new[] { ("outputs", "Outputs"), ("evidence", "Evidence"), ("checks", "Checks"), ("limitations", "Limitations"), ("unresolvedEffects", "Unresolved effects") })
        {
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                lines.Add(title + ":");
                foreach (JsonElement item in list.EnumerateArray())
                {
                    lines.Add("- " + (item.ValueKind == JsonValueKind.String ? item.GetString() : Str(item, "name") is string n ? $"{n}: {Str(item, "outcome") ?? Str(item, "hash") ?? string.Empty} {Str(item, "summary") ?? string.Empty}".Trim() : Str(item, "summary") ?? item.GetRawText()));
                }

                lines.Add(string.Empty);
            }
        }

        lines.Add("Next step: " + (Str(body, "nextStep") ?? string.Empty));
        return string.Join("\n", lines);
    }

    private async Task<T?> TryAsync<T>(Func<Task<T>> read) where T : class
    {
        try
        {
            return await read().ConfigureAwait(true);
        }
        catch (Exception exception) when ((exception is IOException or InvalidOperationException or HttpRequestException or JsonException) && ClientFailure.FromException(exception) is null)
        {
            Log("read failed: " + exception.Message);
            return null;
        }
    }

    private static string Names(JsonElement body, string name) =>
        body.ValueKind == JsonValueKind.Object && body.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? string.Join(", ", list.EnumerateArray().Select(e => Str(e, "name") ?? string.Empty).Where(s => s.Length > 0)) : string.Empty;

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Opens a record's envelope as a document; a work order (by key or id) opens as its page.</summary>
    private async Task OpenRecordCoreAsync(string id, CancellationToken cancellationToken = default)
    {
        if (connection?.Api is null)
        {
            return;
        }

        try
        {
            if (WorkOrders.Any(w => w.Key == id || w.Id == id))
            {
                await OpenWorkOrderCoreAsync(id, cancellationToken: cancellationToken).ConfigureAwait(true);
                return;
            }

            RecordResponse? record = (await connection.Api.RecordAsync(id, cancellationToken: cancellationToken).ConfigureAwait(true));
            Open(record is null ? new TextDocument(id, $"No record '{id}'.", "missing") : new TextDocument($"{record.Type} {id[..Math.Min(12, id.Length)]}", JsonSerializer.Serialize(record.Envelope, Pretty), "record"));
        }
        catch (Exception exception) when ((exception is IOException or InvalidOperationException or HttpRequestException or JsonException) && ClientFailure.FromException(exception) is null)
        {
            Log("record failed: " + exception.Message);
        }
    }

    /// <summary>Toggles the left panel.</summary>
    public void ToggleLeft() => Layout = Layout with { LeftVisible = !Layout.LeftVisible };

    /// <summary>Toggles the right panel.</summary>
    public void ToggleRight() => Layout = Layout with { RightVisible = !Layout.RightVisible };

    /// <summary>Toggles the bottom panel.</summary>
    public void ToggleBottom() => Layout = Layout with { BottomVisible = !Layout.BottomVisible };

    /// <summary>Recovers the default layout.</summary>
    public void ResetLayout() => Layout = ShellLayout.Default;

    /// <summary>Records resized panel sizes.</summary>
    public void RememberSizes(double left, double right, double bottom) => Layout = Layout with { LeftWidth = left, RightWidth = right, BottomHeight = bottom };

    /// <summary>Closes a document; drafts would be preserved here, and none exist in this slice.</summary>
    public void Close(Document document)
    {
        Documents.Remove(document);
        if (SelectedDocument == document)
        {
            SelectedDocument = Documents.LastOrDefault();
        }
    }

    private async Task ShowRegionAsync(Region region, CancellationToken cancellationToken = default)
    {
        switch (region.Name)
        {
            case "Home":
                Inspector = Attention.Count == 0 ? "Nothing needs a person right now." : $"{Attention.Count} items need attention. Select one to see why and what to do next.";
                break;
            case "Work":
                Open(new TextDocument("Work", WorkOrders.Count == 0 ? "No work orders." : string.Join("\n", WorkOrders.Select(w => $"{w.Key,-14} {w.StatusLabel,-48} {w.Kind,-10} {w.Title}")) + "\n\nAssignments:\n" + string.Join("\n", Assignments.Select(a => $"{a.Key,-14} {a.ContributorName ?? a.Contributor,-20} attempts {a.Attempts}  {a.Outcome}, {a.StateLabel}")) + "\n\nOpen a work order by its key with Ctrl+G; the result inbox is Ctrl+I.", "work"));
                break;
            case "Goals":
                await OpenGoalsCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case "Products":
                await OpenProductsCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case "Releases":
                await OpenReleaseCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case "Runtime":
                await OpenRuntimeCoreAsync(cancellationToken: cancellationToken).ConfigureAwait(true);
                break;
            case "Records":
                Open(new TextDocument("Records", "Open a record or a work order by id or key with Ctrl+G (Cmd+G on macOS), or select an attention item.", "records"));
                break;
            default:
                break;
        }
    }

    /// <summary>Opens a document, replacing one with the same title.</summary>
    public void Open(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document? existing = Documents.FirstOrDefault(d => d.Title == document.Title);
        if (existing is not null)
        {
            Documents[Documents.IndexOf(existing)] = document;
        }
        else
        {
            Documents.Add(document);
        }

        SelectedDocument = document;
    }

    private void Log(string line) => Activity.Insert(0, $"{DateTime.UtcNow:HH:mm:ss}  {line}");

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        target.Clear();
        foreach (T item in items)
        {
            target.Add(item);
        }
    }
}
