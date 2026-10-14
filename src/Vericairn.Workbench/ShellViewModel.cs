// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;

namespace Vericairn.Workbench;

/// <summary>A document open in the center workspace.</summary>
public sealed record Document(string Title, string Text, string Kind);

/// <summary>A region of the shell's primary navigation.</summary>
public sealed record Region(string Name, string Question);

/// <summary>
/// The shell's state: the target context, navigation, documents, the inspector, the activity log, the layout and the views
/// of home, work and runtime. Everything shown comes from the runtime's responses; the client computes no state of its own.
/// </summary>
public sealed class ShellViewModel : Observable
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private readonly Func<string, IEstateConnection> connect;
    private readonly IRuntimeManager runtime;
    private readonly Preferences preferences;
    private IEstateConnection? connection;
    private EstateView estate = EstateView.From(default(JsonElement), string.Empty);
    private Document? selectedDocument;
    private Region selectedRegion;
    private string inspector = "Select something to inspect it.";
    private string status = "No estate open.";
    private string actingPrincipal = "read-only: no admitted device on this machine";
    private string freshness = "no view yet";
    private ShellLayout layout;
    private string estatePath = string.Empty;

    /// <summary>A shell over a way to connect and a runtime manager.</summary>
    public ShellViewModel(Func<string, IEstateConnection> connect, IRuntimeManager runtime, Preferences preferences)
    {
        this.connect = connect;
        this.runtime = runtime;
        this.preferences = preferences;
        layout = preferences.LoadLayout();
        Regions = new ObservableCollection<Region>(
        [
            new("Home", "What needs me? What changed?"),
            new("Work", "Who is doing what, with what permission?"),
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

    /// <summary>Work orders.</summary>
    public ObservableCollection<WorkOrderRow> WorkOrders { get; } = [];

    /// <summary>Assignments.</summary>
    public ObservableCollection<AssignmentRow> Assignments { get; } = [];

    /// <summary>Releases.</summary>
    public ObservableCollection<ReleaseRowView> Releases { get; } = [];

    /// <summary>Recently opened estates.</summary>
    public ObservableCollection<string> Recent { get; }

    /// <summary>The selected region.</summary>
    public Region SelectedRegion
    {
        get => selectedRegion;
        set
        {
            if (Set(ref selectedRegion, value) && value is not null)
            {
                Raise(nameof(HomeSelected));
                ShowRegion(value);
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
    public string Status { get => status; private set => Set(ref status, value); }

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
    public async Task OpenEstateAsync(string root)
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
        Log($"opened {root}: {connection.Mode}");
        if (connection.Api is null)
        {
            Status = "No daemon answers for this estate; starting the runtime.";
            await StartRuntimeAsync().ConfigureAwait(true);
        }

        preferences.Remember(root);
        if (!Recent.Contains(root))
        {
            Recent.Insert(0, root);
        }

        Refresh();
        Raise(nameof(HasEstate));
    }

    /// <summary>Refreshes every view from the runtime.</summary>
    public void Refresh()
    {
        if (connection is null)
        {
            return;
        }

        connection.Refresh();
        Raise(nameof(RuntimeMode));
        Raise(nameof(RuntimeLabel));
        Raise(nameof(RuntimeWhere));
        if (connection.Api is null)
        {
            Status = $"{connection.Root}: {StateLanguage.Label("runtime", "unavailable")}; the last view is kept as cached.";
            Freshness = "cached";
            return;
        }

        try
        {
            StatusResponse response = connection.Api.Status();
            string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            estate = EstateView.From(response, now);
            Freshness = $"current as of {now}";
            Replace(Attention, estate.Attention);
            Replace(WorkOrders, estate.WorkOrders);
            Replace(Assignments, estate.Assignments);
            Replace(Releases, estate.Releases);
            IReadOnlyList<DeviceRow> devices = connection.Api.Devices();
            DeviceRow? held = devices.FirstOrDefault(d => d.HeldHere && d.Admission.StartsWith("admitted", StringComparison.OrdinalIgnoreCase));
            ActingPrincipal = held is null ? "read-only: no admitted device on this machine" : $"{held.PrincipalName ?? held.Principal} (device {held.Name ?? held.Id})";
            Status = $"{estate.Name}: {estate.Records} records, {estate.Attention.Count} attention items, {StateLanguage.Label("runtime", connection.Mode)}.";
            Raise(nameof(EstateName));
            Raise(nameof(Pin));
            ShowRegion(SelectedRegion);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException)
        {
            Status = "The runtime did not answer: " + exception.Message;
            Freshness = "cached";
        }
    }

    /// <summary>Starts the daemon through the runtime manager and reconnects.</summary>
    public async Task StartRuntimeAsync()
    {
        if (connection is null)
        {
            return;
        }

        RuntimeOutcome outcome = await runtime.StartAsync(connection.Root).ConfigureAwait(true);
        Log($"runtime start: {(outcome.Ok ? "answered" : "did not answer")}: {outcome.Output}");
        Refresh();
    }

    /// <summary>Asks the daemon to stop and refreshes.</summary>
    public async Task StopRuntimeAsync()
    {
        if (connection is null)
        {
            return;
        }

        RuntimeOutcome outcome = await runtime.StopAsync(connection.Root).ConfigureAwait(true);
        Log($"runtime stop: {outcome.Output}");
        Refresh();
    }

    /// <summary>Shows the install plan without installing.</summary>
    public async Task ShowInstallPlanAsync()
    {
        if (connection is null)
        {
            return;
        }

        RuntimeOutcome outcome = await runtime.InstallPlanAsync(connection.Root).ConfigureAwait(true);
        Open(new Document("Runtime install plan", outcome.Output, "plan"));
    }

    /// <summary>Shows an attention item in the inspector without opening anything.</summary>
    public void Inspect(AttentionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Inspector = $"{item.KindLabel}\n\n{item.Subject}\n\n{item.Why}\n\nState: {item.State}\nAs of: {item.AsOf}\nNext: {item.NextAction}";
    }

    /// <summary>Opens the subject of an attention item as a document.</summary>
    public void OpenAttention(AttentionItem item)
    {
        Inspect(item);
        if (item.SubjectType == "work-order" && item.SubjectId is not null)
        {
            OpenWorkOrder(item.SubjectId);
        }
        else if (item.SubjectId is not null)
        {
            OpenRecord(item.SubjectId);
        }
    }

    /// <summary>Opens a work order's observation as a document.</summary>
    public void OpenWorkOrder(string keyOrId)
    {
        Log($"open work order {keyOrId}");
        if (connection?.Api is null)
        {
            return;
        }

        try
        {
            ObserveResponse? observed = connection.Api.Observe(keyOrId);
            BriefResponse? brief = connection.Api.Brief(keyOrId);
            Open(observed is null ? new Document(keyOrId, $"No work order '{keyOrId}'.", "missing") : new Document(keyOrId, observed.Text + (brief is null ? string.Empty : "\n\n" + brief.Markdown), "work-order"));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException)
        {
            Log("observe failed: " + exception.Message);
        }
    }

    /// <summary>Opens a record's envelope as a document.</summary>
    public void OpenRecord(string id)
    {
        if (connection?.Api is null)
        {
            return;
        }

        try
        {
            RecordResponse? record = connection.Api.Record(id);
            Open(record is null ? new Document(id, $"No record '{id}'.", "missing") : new Document($"{record.Type} {id[..Math.Min(12, id.Length)]}", JsonSerializer.Serialize(record.Envelope, Pretty), "record"));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException)
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

    private void ShowRegion(Region region)
    {
        switch (region.Name)
        {
            case "Home":
                Inspector = Attention.Count == 0 ? "Nothing needs a person right now." : $"{Attention.Count} items need attention. Select one to see why and what to do next.";
                break;
            case "Work":
                Open(new Document("Work", WorkOrders.Count == 0 ? "No work orders." : string.Join("\n", WorkOrders.Select(w => $"{w.Key,-14} {w.StatusLabel,-48} {w.Kind,-10} {w.Title}")) + "\n\nAssignments:\n" + string.Join("\n", Assignments.Select(a => $"{a.Key,-14} {a.ContributorName ?? a.Contributor,-20} attempts {a.Attempts}  {a.Outcome}, {a.StateLabel}")), "work"));
                break;
            case "Products":
                Open(new Document("Products", "Product and project pages arrive in WB-02; the product record is on record (open it from Records).", "planned"));
                break;
            case "Releases":
                Open(new Document("Releases", Releases.Count == 0 ? "No releases." : string.Join("\n", Releases.Select(r => $"{r.Version,-8} {r.StatusLabel,-44} {r.Commit?[..Math.Min(12, r.Commit.Length)]}  {r.Id}")), "releases"));
                break;
            case "Runtime":
                Open(new Document("Runtime", $"{RuntimeLabel}\n{RuntimeWhere}\n\nActing principal: {ActingPrincipal}\n\nStart, stop and the install plan are in the Runtime menu (keyboard: Alt+R).", "runtime"));
                break;
            case "Records":
                Open(new Document("Records", "Open a record by id with Ctrl+G (Cmd+G on macOS), or select an attention item.", "records"));
                break;
            default:
                break;
        }
    }

    private void Open(Document document)
    {
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
