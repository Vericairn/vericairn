// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Sdk;

namespace Vericairn.Remote;

/// <summary>An estate this phone is paired with, and the last observation of it shown here.</summary>
public sealed class RemoteEstate(RuntimeConnection connection)
{
    /// <summary>The kept connection, updated with each check's observation.</summary>
    public RuntimeConnection Connection { get; internal set; } = connection;

    /// <summary>The estate's name, or its id when the runtime gave none.</summary>
    public string Name => Connection.EstateName ?? Connection.Estate;

    /// <summary>Who this phone acts as, and where the runtime is.</summary>
    public string Detail => $"{Connection.PrincipalName ?? Connection.Principal} at {Connection.Address}";

    internal IEstateApi? Client { get; set; }

    internal RemoteObservation? Observation { get; set; }

    internal bool Current { get; set; }

    internal string? DetailKey { get; set; }

    internal string? CacheProblem { get; set; }

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>An assignment as the phone lists it: the work, who holds it, and its state's words.</summary>
public sealed record AssignmentItem(string Key, string Contributor, string State)
{
    /// <summary>The exact assignment to open.</summary>
    public string Id { get; init; } = string.Empty;
}

/// <summary>A product of the estate, as its record names it.</summary>
public sealed record ProductItem(string Name, string Id);

/// <summary>The runtime this phone reaches, as its connection and last check describe it.</summary>
public sealed record NodeView(string Runtime, string Address, string Estate, string ActingAs, string Device, string Profiles, string PairedAt, string State, string Message, string LastCurrent);

/// <summary>
/// Remote's shell: first use explains the companion role and pairs with an authorized estate; the home then shows the selected
/// estate's Attention, Work, Products, Nodes and Handoffs with its connection state and freshness. Every state shown is the runtime's
/// or the SDK's, in the shared state language. Eligible decisions use the phone's own key after an exact review.
/// </summary>
public sealed partial class RemoteViewModel : Observable
{
    private readonly IRemoteConnections connections;
    private readonly TimeProvider clock;
    private RemoteEstate? selected;
    private bool pairing;
    private bool busy;
    private string address = string.Empty;
    private string code = string.Empty;
    private string deviceName = "Remote";
    private string? pairingMessage;
    private string connectionState = string.Empty;
    private string connectionMessage = string.Empty;
    private string freshness = "not observed yet";
    private IReadOnlyList<AttentionItem> attention = [];
    private IReadOnlyList<WorkOrderRow> work = [];
    private IReadOnlyList<AssignmentItem> assignments = [];
    private IReadOnlyList<ProductItem> products = [];
    private NodeView? node;

    /// <summary>Creates the shell over the phone's connections.</summary>
    public RemoteViewModel(IRemoteConnections connections, TimeProvider? clock = null)
    {
        this.connections = connections ?? throw new ArgumentNullException(nameof(connections));
        this.clock = clock ?? TimeProvider.System;
        PairCommand = new AsyncCommand(PairAsync, () => !Busy && CanPair);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !Busy && Selected is not null);
        AddEstateCommand = new AsyncCommand(() => { Pairing = true; return Task.CompletedTask; }, () => !Busy);
        CancelPairingCommand = new AsyncCommand(() => { Pairing = false; return Task.CompletedTask; }, () => !Busy && Estates.Count > 0);
        InitializeDecisionCommands();
        InitializeInspectionCommands();
        InitializePolicyCommands();
        foreach (RuntimeConnection connection in connections.List())
        {
            Estates.Add(Restore(connection));
        }

        Estates.CollectionChanged += (_, _) => Raise(nameof(HasEstates));

        pairing = Estates.Count == 0;
        selected = Estates.FirstOrDefault();
        RestoreDecisions();
        Show(selected);
    }

    /// <summary>Says how this copy of Remote was installed; there is no store distribution yet.</summary>
    public static string BuildLabel => "Developer build: installed for testing, not from an app store.";

    /// <summary>What Remote is for, shown at first use.</summary>
    public static string Companion => "Remote is the phone companion of an estate you already run: it shows what needs you, what is happening and where, and lets you decide when the complete material can be reviewed here. Your estate stays on its own runtime; nothing is copied to a cloud.";

    /// <summary>How to pair, shown beside the form.</summary>
    public static string PairingHelp => "On a machine running the estate's runtime with its local-network profile, an administrator runs vericairn devices pair for you; enter the address and the one-time code it prints. This phone keeps its own key and connection.";

    /// <summary>Estates this phone is paired with.</summary>
    public ObservableCollection<RemoteEstate> Estates { get; } = [];

    /// <summary>The estate the home shows.</summary>
    public RemoteEstate? Selected
    {
        get => selected;
        set
        {
            if (!Set(ref selected, value)) return;
            SetDecision(null);
            SetPolicyDetail(null);
            RestoreDecisions();
            DecisionMessage = string.Empty;
            detailCurrent = false;
            SelectedAttention = null;
            SelectedWork = null;
            SelectedAssignment = null;
            DetailMessage = string.Empty;
            Show(value);
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Whether the pairing form is shown: at first use, or when adding an estate.</summary>
    public bool Pairing
    {
        get => pairing;
        set { if (Set(ref pairing, value)) { Raise(nameof(ShowHome)); Raise(nameof(ShowAreas)); Raise(nameof(HasDecisionReview)); Raise(nameof(HasPolicyDetail)); Raise(nameof(ObservationUnavailable)); CancelPairingCommand.NotifyCanExecuteChanged(); } }
    }

    /// <summary>Whether the home is shown.</summary>
    public bool ShowHome => !Pairing;

    /// <summary>Whether there is a home to return to from the pairing form.</summary>
    public bool HasEstates => Estates.Count > 0;

    /// <summary>Whether a check or a pairing is in flight.</summary>
    public bool Busy
    {
        get => busy;
        private set
        {
            if (!Set(ref busy, value)) return;
            PairCommand.NotifyCanExecuteChanged();
            RefreshCommand.NotifyCanExecuteChanged();
            AddEstateCommand.NotifyCanExecuteChanged();
            CancelPairingCommand.NotifyCanExecuteChanged();
            NotifyInspectionCommands();
            NotifyDecisionCommands();
        }
    }

    /// <summary>The runtime's local-network address, host:port.</summary>
    public string Address { get => address; set { if (Set(ref address, value)) PairCommand.NotifyCanExecuteChanged(); } }

    /// <summary>The one-time pairing code.</summary>
    public string Code { get => code; set { if (Set(ref code, value)) PairCommand.NotifyCanExecuteChanged(); } }

    /// <summary>The name this phone's device record carries.</summary>
    public string DeviceName { get => deviceName; set { if (Set(ref deviceName, value)) PairCommand.NotifyCanExecuteChanged(); } }

    /// <summary>What the last pairing attempt found.</summary>
    public string? PairingMessage { get => pairingMessage; private set => Set(ref pairingMessage, value); }

    /// <summary>The selected connection's state: connecting, current, cached, partial, denied or unavailable.</summary>
    public string ConnectionState
    {
        get => connectionState;
        private set { if (Set(ref connectionState, value)) { Raise(nameof(ConnectionLabel)); Raise(nameof(StatusLine)); } }
    }

    /// <summary>The state's words in the shared state language.</summary>
    public string ConnectionLabel => ConnectionState.Length == 0 ? "not checked yet" : StateLanguage.Label("connection", ConnectionState);

    /// <summary>What the last check said.</summary>
    public string ConnectionMessage { get => connectionMessage; private set => Set(ref connectionMessage, value); }

    /// <summary>Whether the current observation could be retained across app restarts.</summary>
    public string CacheMessage => Selected?.CacheProblem ?? string.Empty;

    /// <summary>How fresh the home's content is.</summary>
    public string Freshness { get => freshness; private set { if (Set(ref freshness, value)) Raise(nameof(StatusLine)); } }

    /// <summary>The connection's state and the content's freshness, in one line.</summary>
    public string StatusLine => $"Connection {ConnectionLabel} · {Freshness}";

    /// <summary>Attention, most urgent first, as the runtime's status derives it.</summary>
    public IReadOnlyList<AttentionItem> Attention { get => attention; private set { if (Set(ref attention, value)) Raise(nameof(AttentionEmpty)); } }

    /// <summary>Whether nothing needs attention in the last observation.</summary>
    public bool AttentionEmpty => Selected?.Observation is not null && Attention.Count == 0;

    /// <summary>No successful observation has been kept for this connection.</summary>
    public bool ObservationUnavailable => ShowHome && Selected is not null && Selected.Observation is null;

    /// <summary>Work orders that are not accepted or retired.</summary>
    public IReadOnlyList<WorkOrderRow> Work { get => work; private set { if (Set(ref work, value)) Raise(nameof(WorkEmpty)); } }

    /// <summary>Whether no open work order was observed.</summary>
    public bool WorkEmpty => Selected?.Observation is not null && Work.Count == 0 && Assignments.Count == 0;

    /// <summary>Assignments that are not accepted.</summary>
    public IReadOnlyList<AssignmentItem> Assignments { get => assignments; private set { if (Set(ref assignments, value)) Raise(nameof(WorkEmpty)); } }

    /// <summary>The estate's products.</summary>
    public IReadOnlyList<ProductItem> Products { get => products; private set => Set(ref products, value); }

    /// <summary>The runtime this phone reaches.</summary>
    public NodeView? Node { get => node; private set => Set(ref node, value); }

    /// <summary>What Attention cannot do yet.</summary>
    public static string AttentionLater => "Acknowledging marks only your attention state. The item stays visible and its work remains unchanged. Offline acknowledgments are never queued.";

    /// <summary>What Work cannot do yet.</summary>
    public static string WorkLater => "Communication settings are read-only here. Clarifications to running attempts arrive with PROMPT-02 in 0.9.1; use Workbench or the command line for actions available there.";

    /// <summary>What Nodes means, and does not.</summary>
    public static string NodesNote => "This is the runtime this phone reaches. A healthy connection here says nothing about workers running on other machines.";

    /// <summary>Where to prepare the supported policy context and other desktop tasks.</summary>
    public static string HandoffsLater => "Desktop task delivery and opening are not in Remote yet. Open a policy or duty subject in Attention to prepare its exact desktop context, then continue in Workbench. A context package grants no authority and opening it completes no work.";

    /// <summary>Pairs with a runtime.</summary>
    public AsyncCommand PairCommand { get; }

    /// <summary>Checks the selected connection and reads the estate again.</summary>
    public AsyncCommand RefreshCommand { get; }

    /// <summary>Shows the pairing form to add another estate.</summary>
    public AsyncCommand AddEstateCommand { get; }

    /// <summary>Returns from the pairing form to the home.</summary>
    public AsyncCommand CancelPairingCommand { get; }

    private bool CanPair => RuntimeConnection.TryParseAddress(Address.Trim(), out _, out _) && Code.Trim().Length > 0 && DeviceName.Trim().Length > 0;

    private string Now => clock.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private async Task PairAsync()
    {
        Busy = true;
        PairingMessage = "Pairing…";
        try
        {
            RuntimeConnection connection = await connections.PairAsync(Address.Trim(), Code.Trim(), DeviceName.Trim(), CancellationToken.None).ConfigureAwait(true);
            // The SDK replaces an earlier connection to the same estate; show what it keeps.
            foreach (RemoteEstate earlier in Estates) (earlier.Client as IDisposable)?.Dispose();
            Estates.Clear();
            foreach (RuntimeConnection kept in connections.List()) Estates.Add(Restore(kept));
            Code = string.Empty;
            PairingMessage = $"Paired with {connection.EstateName ?? connection.Estate} as {connection.PrincipalName ?? connection.Principal}.";
            Pairing = false;
            Selected = Estates.FirstOrDefault(e => e.Connection.Id == connection.Id) ?? Estates.LastOrDefault();
        }
        catch (PairingException exception)
        {
            PairingMessage = $"Not paired ({exception.Code}): {exception.Message}";
        }
        catch (Exception exception) when (ClientFailure.FromException(exception) is ClientFailure failure)
        {
            PairingMessage = $"Not paired ({failure.Code}): {failure.Message}";
        }
        finally
        {
            Busy = false;
        }

        if (!Pairing && Selected is not null) await RefreshAsync().ConfigureAwait(true);
    }

    private async Task RefreshAsync()
    {
        if (Selected is not RemoteEstate estate) return;
        Busy = true;
        estate.Current = false;
        detailCurrent = false;
        policyCurrent = false;
        NotifyPolicy();
        decisionCurrent = false;
        MaterialReviewed = false;
        NotifyDecision();
        ConnectionState = ConnectionStates.Connecting;
        ConnectionMessage = "Checking the runtime's identity and this phone's access.";
        try
        {
            ConnectionStatus status = await connections.ProbeAsync(estate.Connection, CancellationToken.None).ConfigureAwait(true);
            estate.Connection = status.Connection;
            if (status.State is ConnectionStates.Current or ConnectionStates.Partial)
            {
                estate.Client ??= connections.Connect(estate.Connection);
                string observedAt = Now;
                StatusResponse response = await estate.Client.StatusAsync(CancellationToken.None).ConfigureAwait(true);
                IReadOnlyList<ProductItem> observedProducts = await ProductsAsync(estate.Client).ConfigureAwait(true);
                AttentionResponse observedAttention = await estate.Client.AttentionGetAsync(estate.Connection.Principal, CancellationToken.None).ConfigureAwait(true);
                CommunicationProfile profile = await estate.Client.CommunicationAsync(estate.Connection.Principal, cancellationToken: CancellationToken.None).ConfigureAwait(true);
                estate.Observation = new(response, observedAt, observedProducts, observedAttention, profile, estate.Observation?.Details ?? new Dictionary<string, RemoteWorkDetail>())
                { PolicyDetails = estate.Observation?.PolicyDetails ?? new Dictionary<string, RemotePolicyDetail>() };
                estate.Current = true;
                Keep(estate);
            }
            if (Selected == estate)
            {
                Show(estate);
                ShowNode(estate, status.State, status.Message);
            }
        }
        catch (Exception exception) when (ClientFailure.FromException(exception) is not null || exception is NotSupportedException or JsonException or IOException or UnauthorizedAccessException)
        {
            estate.Current = false;
            if (Selected == estate)
            {
                Show(estate);
                ShowNode(estate, ClientFailure.FromException(exception)?.Code == "refused" ? ConnectionStates.Denied : estate.Observation is null ? ConnectionStates.Unavailable : ConnectionStates.Cached,
                    ClientFailure.FromException(exception)?.Message ?? "The runtime did not provide a complete readable observation. Update the runtime if needed and refresh.");
            }
        }
        finally { Busy = false; }
        if (Selected == estate && estate.Current && decision is { Attempted: false } draft)
            await OpenDecisionAsync(draft.Review.Subject.Id).ConfigureAwait(true);
    }

    private RemoteEstate Restore(RuntimeConnection connection) => new(connection) { Observation = connections.LoadObservation(connection) };

    private void Keep(RemoteEstate estate)
    {
        if (estate.Observation is null) return;
        try { connections.SaveObservation(estate.Connection, estate.Observation); estate.CacheProblem = null; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { estate.CacheProblem = "This observation could not be kept in app storage. It remains visible until the app closes."; }
        if (Selected == estate) Raise(nameof(CacheMessage));
    }

    private void Show(RemoteEstate? estate)
    {
        RemoteObservation? observation = estate?.Observation;
        Raise(nameof(ObservationUnavailable));
        Raise(nameof(CacheMessage));
        EstateView? view = observation is null ? null : EstateView.From(observation.Status, observation.AsOf);
        string? attentionKey = SelectedAttention?.Notice?.Key;
        string? orderId = SelectedWork?.Id;
        string? assignmentId = SelectedAssignment?.Id;
        Attention = observation?.Attention.Items.Select(item => AttentionItem.From(item) with
        {
            AsOf = $"{(estate!.Current ? "observed" : "cached")} {observation.AsOf}; source {item.AsOf}",
        }).ToList() ?? [];
        Work = view?.WorkOrders.Where(w => w.Status is not ("accepted" or "retired")).ToList() ?? [];
        Assignments = view?.Assignments.Where(a => a.State != "accepted").Select(a => new AssignmentItem(a.Key, a.ContributorName ?? a.Contributor, a.StateLabel) { Id = a.Id }).ToList() ?? [];
        SelectedAttention = Attention.FirstOrDefault(a => a.Notice?.Key == attentionKey);
        SelectedWork = Work.FirstOrDefault(w => w.Id == orderId);
        SelectedAssignment = Assignments.FirstOrDefault(a => a.Id == assignmentId);
        Products = observation?.Products ?? [];
        Freshness = view is null ? "not observed yet" : estate!.Current ? $"current as of {view.AsOf}" : $"cached from {view.AsOf}; refresh before relying on it";
        ObservationAge = observation is null ? "age unknown" : RemoteFreshness.Age(observation.AsOf, clock);
        Communication = observation is null ? "Communication settings have not been observed." :
            $"{(estate!.Current ? "Observed" : "Cached")} {observation.AsOf}; {ObservationAge}\n" + CommunicationPresentation.Describe(observation.Communication) +
            "\nPreview\n" + CommunicationPresentation.Preview(observation.Communication);
        WorkDetail = estate?.DetailKey is string key && observation?.Details.TryGetValue(key, out RemoteWorkDetail? detail) == true ? detail : null;
        RaiseDetailFreshness();
        if (estate is null)
        {
            Node = null;
            ConnectionState = string.Empty;
            ConnectionMessage = string.Empty;
        }
        else
        {
            string state = estate.Current ? estate.Connection.Observed?.State ?? ConnectionStates.Current : observation is not null ? ConnectionStates.Cached :
                estate.Connection.Observed?.State == ConnectionStates.Denied ? ConnectionStates.Denied : ConnectionStates.Unavailable;
            ShowNode(estate, state, estate.Connection.Observed?.Message ?? string.Empty);
        }
        NotifyInspectionCommands();
    }

    private void ShowNode(RemoteEstate estate, string state, string message)
    {
        RuntimeConnection c = estate.Connection;
        ConnectionState = state;
        ConnectionMessage = message;
        Node = new NodeView(
            RuntimeIdentityScheme.Fingerprint(c.Identity), c.Address, c.EstateName ?? c.Estate, c.PrincipalName ?? c.Principal, c.Device, string.Join(", ", c.Profiles), c.PairedAt,
            ConnectionLabel, message, c.LastCurrent?.At ?? "never");
    }

    private static async Task<IReadOnlyList<ProductItem>> ProductsAsync(IEstateApi api)
    {
        var items = new List<ProductItem>();
        foreach (RecordResponse product in await api.RecordsAsync("product", CancellationToken.None).ConfigureAwait(true))
        {
            JsonElement body = product.Envelope.TryGetProperty("body", out JsonElement b) ? b : default;
            string? name = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            string id = product.Envelope.TryGetProperty("id", out JsonElement i) && i.ValueKind == JsonValueKind.String ? i.GetString() ?? "?" : "?";
            items.Add(new ProductItem(name ?? id, id));
        }

        return items;
    }
}
