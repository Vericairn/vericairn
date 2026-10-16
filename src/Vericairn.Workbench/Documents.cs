// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Presentation;
using Vericairn.Protocol;

namespace Vericairn.Workbench;

/// <summary>A document open in the center workspace; the kind picks the view.</summary>
public abstract class Document(string title, string kind) : Observable
{
    /// <summary>Tab title.</summary>
    public string Title { get; } = title;

    /// <summary>The kind of document, for tests and the shell.</summary>
    public string Kind { get; } = kind;
}

/// <summary>Plain text: observations, plans, records.</summary>
public sealed class TextDocument(string title, string text, string kind) : Document(title, kind)
{
    /// <summary>The text.</summary>
    public string Text { get; } = text;
}

/// <summary>The runtime's recorded progress, contributor claims and worker observations; refresh renews it in place.</summary>
public sealed class ProgressDocument() : Document("Progress", "progress")
{
    /// <summary>What the page says while the runtime gives no report.</summary>
    public const string Unavailable = "The runtime did not give the progress report; the estate is unavailable to this client.";

    private string text = Unavailable;

    /// <summary>The report's text, each line naming the records it rests on.</summary>
    public string Text { get => text; set => Set(ref text, value); }
}

/// <summary>
/// The Goals workspace (the estate's design/goals.md § 8): the estate's goals, and for the one selected its objective, its conditions with
/// their current verdicts, its plan and steps with the principal each reaches from the runtime's machine, spend against its budget from
/// the agent-run ledger, its asks and its evaluations. It reads the runtime's goal view and routed next steps and shows them; it
/// activates, accepts, funds and answers nothing, and takes no step: those are the sponsor's or an operator's, through the command line.
/// </summary>
public sealed class GoalsWorkspace : Document
{
    /// <summary>What the page says while the runtime gives no goal.</summary>
    public const string Unavailable = "The runtime gave no goal; the estate is unavailable to this client.";

    private readonly Func<IEstateApi?> api;
    private GoalListRow? selected;
    private string heading = string.Empty, objective = string.Empty, acceptance = string.Empty, next = string.Empty, note = Unavailable;

    /// <summary>The workspace over the API the shell holds now.</summary>
    public GoalsWorkspace(Func<IEstateApi?> api) : base("Goals", "goals")
    {
        this.api = api;
    }

    /// <summary>The estate's goals.</summary>
    public ObservableCollection<GoalListRow> Goals { get; } = [];

    /// <summary>The goal shown; choosing one shows it.</summary>
    public GoalListRow? Selected
    {
        get => selected;
        set
        {
            if (Set(ref selected, value))
            {
                Show();
            }
        }
    }

    /// <summary>The selected goal's title and status.</summary>
    public string Heading { get => heading; private set => Set(ref heading, value); }

    /// <summary>Its objective, in the sponsor's words.</summary>
    public string Objective { get => objective; private set => Set(ref objective, value); }

    /// <summary>Who sponsors and accepts it, and whether it is ready for its acceptance.</summary>
    public string Acceptance { get => acceptance; private set => Set(ref acceptance, value); }

    /// <summary>What comes next.</summary>
    public string Next { get => next; private set => Set(ref next, value); }

    /// <summary>What the page is, or why it shows nothing.</summary>
    public string Note { get => note; private set => Set(ref note, value); }

    /// <summary>The conditions with their current verdicts.</summary>
    public ObservableCollection<ConditionRow> Conditions { get; } = [];

    /// <summary>The plan's steps with the principal each reaches.</summary>
    public ObservableCollection<StepRow> Steps { get; } = [];

    /// <summary>Spend against the budget.</summary>
    public ObservableCollection<SpendRow> Spend { get; } = [];

    /// <summary>The goal's asks, the recommendation first.</summary>
    public ObservableCollection<AskRow> Asks { get; } = [];

    /// <summary>The goal's evaluations.</summary>
    public ObservableCollection<EvaluationRow> Evaluations { get; } = [];

    /// <summary>Reads the goals again and shows the one named, else the one shown, else the first open one.</summary>
    public void Fill(string? goal = null)
    {
        IEstateApi? estate = api();
        IReadOnlyList<GoalRow> rows;
        try
        {
            rows = estate?.Goals() ?? [];
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException)
        {
            Note = "The runtime did not answer: " + exception.Message;
            return;
        }

        string? keep = goal ?? selected?.Id;
        Goals.Clear();
        foreach (GoalRow row in rows)
        {
            Goals.Add(new GoalListRow(row.Id, row.Title, row.Status, row.SponsorName ?? row.Sponsor));
        }

        Note = estate is null ? Unavailable : Goals.Count == 0
            ? "No goal is proposed; vericairn goal propose proposes one."
            : "Each goal from the runtime's records; the steps are routed from the runtime's machine. Activation, acceptance, funding, answers and steps are the sponsor's or an operator's, through vericairn goal and vericairn ask; this page shows them.";
        GoalListRow? chosen = Goals.FirstOrDefault(g => g.Id == keep) ?? Goals.FirstOrDefault(g => g.Status is not ("achieved" or "impossible" or "abandoned")) ?? Goals.FirstOrDefault();
        if (ReferenceEquals(chosen, selected) || chosen?.Id == selected?.Id)
        {
            selected = chosen;
            Raise(nameof(Selected));
            Show();
        }
        else
        {
            Selected = chosen;
        }
    }

    private void Show()
    {
        Conditions.Clear();
        Steps.Clear();
        Spend.Clear();
        Asks.Clear();
        Evaluations.Clear();
        IEstateApi? estate = api();
        if (selected is null || estate is null)
        {
            Heading = Objective = Acceptance = Next = string.Empty;
            return;
        }

        GoalPresentation view;
        try
        {
            GoalResponse? goal = estate.Goal(selected.Id);
            NextResponse? routed = estate.GoalNext(selected.Id);
            view = GoalPresentation.From(goal?.Json ?? default, routed?.Json ?? default);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException)
        {
            Heading = selected.Title;
            Objective = "The runtime did not answer: " + exception.Message;
            return;
        }

        Heading = view.Heading;
        Objective = view.Objective;
        Acceptance = view.Acceptance;
        Next = view.Next;
        foreach (ConditionRow row in view.Conditions)
        {
            Conditions.Add(row);
        }

        foreach (StepRow row in view.Steps)
        {
            Steps.Add(row);
        }

        foreach (SpendRow row in view.Spend)
        {
            Spend.Add(row);
        }

        foreach (AskRow row in view.Asks)
        {
            Asks.Add(row);
        }

        foreach (EvaluationRow row in view.Evaluations)
        {
            Evaluations.Add(row);
        }
    }
}

/// <summary>A product as its record states it.</summary>
public sealed record ProductRow(string Id, string Name, string Slug, string Summary, string Editions, string Surfaces, string? ReleaseProvider = null, IReadOnlyDictionary<string, string>? ReleaseSettings = null);

/// <summary>The products of the estate from their records; projects are not a record type of format 1 and the page says so.</summary>
public sealed class ProductsDocument(IReadOnlyList<ProductRow> products) : Document("Products", "products")
{
    /// <summary>The products.</summary>
    public IReadOnlyList<ProductRow> Products { get; } = products;

    /// <summary>What the page cannot show yet.</summary>
    public string Note => Products.Count == 0 ? "No product record yet." : "Projects are added to the record format by the milestone that needs them; until then a product's work is its work orders.";
}

/// <summary>An assignment as the work order page lists it.</summary>
public sealed record AssignmentView(string Id, string Hash, string Contributor, string Attempts, string? LatestResultId, string? LatestResultState, string Summary);

/// <summary>A work order page: the brief, the assignments with their attempts and results, and the actions that start the loop.</summary>
public sealed class WorkOrderDocument : Document
{
    /// <summary>Builds the page from the work order's brief document.</summary>
    public WorkOrderDocument(string key, JsonElement brief, string observation, string communication = "") : base(key, "work-order")
    {
        Key = key;
        Observation = observation;
        Communication = communication;
        JsonElement order = brief.TryGetProperty("workOrder", out JsonElement o) ? o : default;
        Id = Str(order, "id") ?? string.Empty;
        Hash = Str(order, "hash") ?? string.Empty;
        JsonElement body = order.ValueKind == JsonValueKind.Object && order.TryGetProperty("body", out JsonElement b) ? b : default;
        Heading = $"{key}: {Str(body, "title")}";
        Kind2 = Str(body, "kind") ?? string.Empty;
        Intent = Str(body, "intent") ?? string.Empty;
        ExitQuestion = Str(body, "exitQuestion") ?? string.Empty;
        var assignments = new List<AssignmentView>();
        if (brief.TryGetProperty("assignments", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement a in list.EnumerateArray())
            {
                string? latestId = null, latestState = null;
                int attempts = 0;
                if (a.TryGetProperty("attempts", out JsonElement at) && at.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement attempt in at.EnumerateArray())
                    {
                        attempts++;
                        if (attempt.TryGetProperty("result", out JsonElement r) && r.ValueKind == JsonValueKind.Object)
                        {
                            latestId = Str(r, "id");
                            latestState = Str(r, "state");
                        }
                    }
                }

                string outcome = Str(a.TryGetProperty("brief", out JsonElement bb) ? bb : default, "outcome") ?? string.Empty;
                assignments.Add(new AssignmentView(Str(a, "id") ?? string.Empty, Str(a, "hash") ?? string.Empty, Str(a, "contributor") ?? "?", attempts.ToString(CultureInfo.InvariantCulture), latestId, latestState is null ? null : StateLanguage.Label("assignment", latestState), outcome));
            }
        }

        Assignments = assignments;
    }

    /// <summary>Work-order key.</summary>
    public string Key { get; }

    /// <summary>Current revision id.</summary>
    public string Id { get; }

    /// <summary>Current revision hash.</summary>
    public string Hash { get; }

    /// <summary>Key and title.</summary>
    public string Heading { get; }

    /// <summary>ordinary or milestone.</summary>
    public string Kind2 { get; }

    /// <summary>Intent.</summary>
    public string Intent { get; }

    /// <summary>Exit question.</summary>
    public string ExitQuestion { get; }

    /// <summary>The addressed recipient profile and its provenance.</summary>
    public string Communication { get; }

    /// <summary>The observation text.</summary>
    public string Observation { get; }

    /// <summary>Assignments.</summary>
    public IReadOnlyList<AssignmentView> Assignments { get; }

    /// <summary>The selected assignment.</summary>
    public AssignmentView? SelectedAssignment { get; set; }

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>An attempt of an assignment with its result, if any.</summary>
public sealed record AttemptView(string Id, string By, string StartedAt, string Workspace, string Note, string? ResultId, string? ResultState, string? ResultOutcome, string? ResultSummary)
{
    /// <summary>First line.</summary>
    public string Heading => $"{StartedAt}  {Workspace}";

    /// <summary>Second line.</summary>
    public string Detail => ResultId is null ? $"{Note} (no result yet)" : $"result {ResultId[..Math.Min(12, ResultId.Length)]}: {ResultOutcome}; {ResultState}. {ResultSummary}";
}

/// <summary>An assignment page: the brief as delegated, the attempts and their results.</summary>
public sealed class AssignmentDocument : Document
{
    /// <summary>Builds the page from the assignment node of the work order's brief document.</summary>
    public AssignmentDocument(string key, JsonElement assignment, string contributorName, CommunicationProfile? communication = null, RecordResponse? original = null) : base($"Assignment {Str(assignment, "id")?[..12]}", "assignment")
    {
        Key = key;
        Id = Str(assignment, "id") ?? string.Empty;
        Hash = Str(assignment, "hash") ?? string.Empty;
        Contributor = contributorName;
        Heading = $"{key}: assignment {Id[..Math.Min(12, Id.Length)]} to {contributorName}";
        JsonElement brief = assignment.TryGetProperty("brief", out JsonElement b) ? b : default;
        var lines = new List<string> { "Outcome: " + (Str(brief, "outcome") ?? string.Empty) };
        foreach ((string name, string title) in new[] { ("permittedChanges", "Permitted changes"), ("prohibitedEffects", "Prohibited effects"), ("workspace", "Workspace"), ("limits", "Limits"), ("expectedOutputs", "Expected outputs"), ("reviewCriteria", "Review criteria") })
        {
            if (brief.ValueKind == JsonValueKind.Object && brief.TryGetProperty(name, out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                lines.Add(title + ":");
                lines.AddRange(list.EnumerateArray().Select(e => "- " + (e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText())));
            }
        }

        lines.Add("Recovery: " + (Str(brief, "recovery") ?? string.Empty));
        BriefText = string.Join("\n", lines);
        Note = Str(assignment, "note") ?? string.Empty;
        if (communication is not null)
        {
            string complete = original is null ? "\n\nAssignment view (original record unavailable):\n" + assignment.GetRawText()
                : "\n\nComplete original assignment:\n" + original.Envelope.GetRawText();
            BriefText = CommunicationPresentation.Protected(communication, "handoff", Id + " at " + Hash,
                "The delegated outcome, limits and original source follow.", BriefText + complete);
        }

        var attempts = new List<AttemptView>();
        if (assignment.TryGetProperty("attempts", out JsonElement at) && at.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement attempt in at.EnumerateArray())
            {
                JsonElement result = attempt.TryGetProperty("result", out JsonElement r) && r.ValueKind == JsonValueKind.Object ? r : default;
                JsonElement body = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("body", out JsonElement rb) ? rb : default;
                attempts.Add(new AttemptView(Str(attempt, "id") ?? string.Empty, Str(attempt, "by") ?? string.Empty, Str(attempt, "startedAt") ?? string.Empty, Str(attempt, "workspace") ?? string.Empty, Str(attempt, "note") ?? string.Empty, Str(result, "id"), result.ValueKind == JsonValueKind.Object ? StateLanguage.Label("assignment", Str(result, "state")) : null, Str(body, "outcome"), Str(body, "summary")));
            }
        }

        Attempts = attempts;
        LatestResultId = attempts.LastOrDefault(a => a.ResultId is not null)?.ResultId;
    }

    /// <summary>Work-order key.</summary>
    public string Key { get; }

    /// <summary>Assignment id.</summary>
    public string Id { get; }

    /// <summary>Assignment hash.</summary>
    public string Hash { get; }

    /// <summary>The contributor's name.</summary>
    public string Contributor { get; }

    /// <summary>Heading.</summary>
    public string Heading { get; }

    /// <summary>The brief as delegated.</summary>
    public string BriefText { get; }

    /// <summary>The assigning note.</summary>
    public string Note { get; }

    /// <summary>Attempts, oldest first.</summary>
    public IReadOnlyList<AttemptView> Attempts { get; }

    /// <summary>The selected attempt.</summary>
    public AttemptView? SelectedAttempt { get; set; }

    /// <summary>The latest result, if any.</summary>
    public string? LatestResultId { get; }

    private static string? Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>A principal a work order can be delegated to.</summary>
public sealed record ContributorRow(string Id, string Name, string Kind)
{
    /// <summary>Display.</summary>
    public string Display => $"{Name} ({Kind})";
}

/// <summary>Composes an assignment: the brief's fields, one per line where a list is expected; nothing is sent until Submit.</summary>
public sealed class AssignmentComposer(string workOrderKey, IReadOnlyList<ContributorRow> contributors) : Document($"Assign {workOrderKey}", "composer")
{
    private string outcome = string.Empty, permittedChanges = string.Empty, prohibitedEffects = string.Empty, workspace = string.Empty, limits = string.Empty, expectedOutputs = string.Empty, reviewCriteria = string.Empty, recovery = string.Empty, note = string.Empty, status = "Not sent.";
    private ContributorRow? contributor = contributors.Count > 0 ? contributors[0] : null;

    /// <summary>The work order.</summary>
    public string WorkOrderKey { get; } = workOrderKey;

    /// <summary>Principals that can be assigned.</summary>
    public IReadOnlyList<ContributorRow> Contributors { get; } = contributors;

    /// <summary>The chosen contributor.</summary>
    public ContributorRow? Contributor { get => contributor; set => Set(ref contributor, value); }

    /// <summary>Outcome.</summary>
    public string Outcome { get => outcome; set => Set(ref outcome, value); }

    /// <summary>Permitted changes, one per line.</summary>
    public string PermittedChanges { get => permittedChanges; set => Set(ref permittedChanges, value); }

    /// <summary>Prohibited effects, one per line.</summary>
    public string ProhibitedEffects { get => prohibitedEffects; set => Set(ref prohibitedEffects, value); }

    /// <summary>Workspace, one entry per line, never a machine path.</summary>
    public string Workspace { get => workspace; set => Set(ref workspace, value); }

    /// <summary>Limits, one per line.</summary>
    public string Limits { get => limits; set => Set(ref limits, value); }

    /// <summary>Expected outputs, one per line.</summary>
    public string ExpectedOutputs { get => expectedOutputs; set => Set(ref expectedOutputs, value); }

    /// <summary>Review criteria, one per line.</summary>
    public string ReviewCriteria { get => reviewCriteria; set => Set(ref reviewCriteria, value); }

    /// <summary>Recovery.</summary>
    public string Recovery { get => recovery; set => Set(ref recovery, value); }

    /// <summary>Note.</summary>
    public string Note { get => note; set => Set(ref note, value); }

    /// <summary>What the runtime answered.</summary>
    public string Status { get => status; private set => Set(ref status, value); }

    /// <summary>The assignment id once written.</summary>
    public string? WrittenId { get; private set; }

    /// <summary>The brief as the runtime wants it.</summary>
    public JsonElement Brief()
    {
        var brief = new JsonObject
        {
            ["outcome"] = Outcome.Trim(),
            ["sources"] = new JsonArray(),
            ["permittedChanges"] = Lines(PermittedChanges),
            ["prohibitedEffects"] = Lines(ProhibitedEffects),
            ["workspace"] = Lines(Workspace),
            ["limits"] = Lines(Limits),
            ["expectedOutputs"] = Lines(ExpectedOutputs),
            ["reviewCriteria"] = Lines(ReviewCriteria),
            ["recovery"] = Recovery.Trim(),
        };
        using JsonDocument document = JsonDocument.Parse(brief.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <summary>Sends the assignment; the runtime validates and signs.</summary>
    public void Submit(IEstateApi api, string? by)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (string.IsNullOrWhiteSpace(by))
        {
            Status = "No acting principal: this machine holds no admitted device, so nothing can be authored here.";
            return;
        }

        if (Contributor is null)
        {
            Status = "Choose a contributor.";
            return;
        }

        WriteOutcome outcome = api.Assign(new AssignRequest(WorkOrderKey, Contributor.Id, by, Brief(), string.IsNullOrWhiteSpace(Note) ? null : Note.Trim()));
        if (outcome.Ok)
        {
            WrittenId = outcome.Written!.Id;
            Status = $"Written: assignment {outcome.Written.Id} ({outcome.Written.Hash[..19]}...).";
        }
        else
        {
            Status = Refusal(outcome.Problem);
        }
    }

    internal static string Refusal(Problem? problem)
    {
        if (problem is null)
        {
            return "Refused.";
        }

        var lines = new List<string> { $"{problem.Code}: {problem.Message}" };
        lines.AddRange(problem.Findings.Select(f => $"{f.Severity.ToUpperInvariant()} {f.Code} {f.Location}: {f.Message}"));
        return string.Join("\n", lines);
    }

    private static JsonArray Lines(string text) => new(text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => (JsonNode)l).ToArray());
}

/// <summary>A result awaiting a person.</summary>
public sealed record InboxRow(string Key, string AssignmentId, string ResultId, string Contributor, string Outcome, string State)
{
    /// <summary>First line.</summary>
    public string Heading => $"{Key}: result {ResultId[..Math.Min(12, ResultId.Length)]} from {Contributor}";

    /// <summary>Second line.</summary>
    public string Detail => $"{Outcome}; {State}";
}

/// <summary>Results across the estate that await review, from the status document.</summary>
public sealed class ResultInboxDocument(IReadOnlyList<InboxRow> rows) : Document("Result inbox", "inbox")
{
    /// <summary>The rows.</summary>
    public IReadOnlyList<InboxRow> Rows { get; } = rows;

    /// <summary>The selected row.</summary>
    public InboxRow? Selected { get; set; }

    /// <summary>What the inbox holds.</summary>
    public string Note => Rows.Count == 0 ? "No result awaits review." : $"{Rows.Count} result(s) await review; each is a claim to inspect. Enter opens the review workspace.";
}

/// <summary>An existing review of the subject, selectable as the basis of an acceptance.</summary>
public sealed class ReviewRow(string id, string by, string independence, string outcome, string summary, JsonElement findings = default) : Observable
{
    private bool selected;

    /// <summary>Record id.</summary>
    public string Id { get; } = id;

    /// <summary>Reviewer.</summary>
    public string By { get; } = by;

    /// <summary>Display.</summary>
    public string Display => $"{Id[..Math.Min(12, Id.Length)]} {independence} {outcome}: {summary}";

    /// <summary>Every recorded finding, with its severity and location, independent of presentation depth.</summary>
    public string Findings { get; } = DescribeFindings(findings);

    /// <summary>Whether the acceptance cites it.</summary>
    public bool Selected { get => selected; set => Set(ref selected, value); }

    private static string DescribeFindings(JsonElement findings)
    {
        if (findings.ValueKind != JsonValueKind.Array)
        {
            return "Review findings unavailable; inspect the original record.";
        }

        if (findings.GetArrayLength() == 0)
        {
            return "No findings recorded.";
        }

        static string Field(JsonElement finding, string name) => finding.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;
        return string.Join('\n', findings.EnumerateArray().Select(f =>
            Field(f, "severity") + ": " + Field(f, "summary") + (Field(f, "location") is { Length: > 0 } location ? " (" + location + ")" : string.Empty)));
    }
}

/// <summary>
/// The review and decision workspace for one exact result: what was claimed, the evidence and limitations, the reviews on
/// record, and the drafts of a review and an acceptance pinned to the result's hash. The client shows what it will send and
/// what the runtime answered; it decides neither independence nor authority.
/// </summary>
public sealed class ReviewWorkspace : Document
{
    private readonly string subjectType;
    private PreparedAction prepared;
    private string reviewOutcome = "pass", reviewSummary = string.Empty, reviewFindings = string.Empty, acceptanceStatement = string.Empty, status = "Nothing sent.", preparedState = string.Empty;

    /// <summary>A workspace over a record (a result or a work order), pinned to its hash and to the facts about it now.</summary>
    public ReviewWorkspace(RecordResponse subject, string facts, string heading, string summaryText, IReadOnlyList<ReviewRow> reviews, string actingPrincipal, string actingRole) : base(heading, "review")
    {
        subjectType = subject.Type;
        SubjectId = subject.Id;
        prepared = new PreparedAction("review-and-acceptance", subject.Type, subject.Id, subject.Hash, facts);
        Heading = heading;
        SummaryText = summaryText;
        Reviews = new ObservableCollection<ReviewRow>(reviews);
        string author = subject.Envelope.TryGetProperty("by", out JsonElement by) ? by.GetString() ?? string.Empty : string.Empty;
        Independence = author == actingPrincipal ? "self" : actingRole == "operator" ? "operator" : "independent-agent";
        SelfAcceptance = author == actingPrincipal;
        ActingPrincipal = actingPrincipal;
    }

    /// <summary>The outcomes a review can state.</summary>
    public static IReadOnlyList<string> Outcomes { get; } = ["pass", "findings", "fail"];

    /// <summary>The record under review.</summary>
    public string SubjectId { get; }

    /// <summary>What the client will declare to the runtime; the runtime verifies both against the records.</summary>
    public string Declaration => ActingPrincipal.Length == 0
        ? "No acting principal: this machine holds no admitted device, so nothing can be sent from here."
        : $"Acting as {ActingPrincipal}. The review will state independence '{Independence}' and the acceptance self = {(SelfAcceptance ? "true" : "false")}, both derived from who authored the subject; the runtime checks them.";

    /// <summary>The pinned subject and facts; re-pinned after a write of this workspace's own, which the person has just seen.</summary>
    public PreparedAction Prepared { get => prepared; private set => Set(ref prepared, value); }

    /// <summary>Heading.</summary>
    public string Heading { get; }

    /// <summary>The claim and its evidence, rendered.</summary>
    public string SummaryText { get; }

    /// <summary>Reviews of the subject on record.</summary>
    public ObservableCollection<ReviewRow> Reviews { get; }

    /// <summary>The independence the client will declare, derived from who acts and who authored; the runtime verifies it.</summary>
    public string Independence { get; }

    /// <summary>Whether the acceptance would be a self-acceptance, derived the same way.</summary>
    public bool SelfAcceptance { get; }

    /// <summary>The acting principal.</summary>
    public string ActingPrincipal { get; }

    /// <summary>pass, findings or fail.</summary>
    public string ReviewOutcome { get => reviewOutcome; set => Set(ref reviewOutcome, value); }

    /// <summary>What was reviewed and how.</summary>
    public string ReviewSummary { get => reviewSummary; set => Set(ref reviewSummary, value); }

    /// <summary>Findings, one per line, optionally prefixed with error:, warning: or info:.</summary>
    public string ReviewFindings { get => reviewFindings; set => Set(ref reviewFindings, value); }

    /// <summary>The acceptance statement.</summary>
    public string AcceptanceStatement { get => acceptanceStatement; set => Set(ref acceptanceStatement, value); }

    /// <summary>What the runtime answered last.</summary>
    public string Status { get => status; private set => Set(ref status, value); }

    /// <summary>The prepared action's state on the last check.</summary>
    public string PreparedState { get => preparedState; private set => Set(ref preparedState, value); }

    /// <summary>Last written record id.</summary>
    public string? WrittenId { get; private set; }

    /// <summary>Checks the pinned subject against the runtime; false when the draft must not be sent.</summary>
    public bool CheckPrepared(IEstateApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        PreparedState state = PreparedAction.Check(Prepared, api.Record(SubjectId), SubjectFacts.About(api, SubjectId));
        PreparedState = PreparedAction.Label(state);
        return state == Presentation.PreparedState.Reusable;
    }

    /// <summary>Sends the review.</summary>
    public void SubmitReview(IEstateApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (!CheckPrepared(api))
        {
            Status = PreparedState;
            return;
        }

        var findings = new JsonArray();
        foreach (string line in ReviewFindings.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            string severity = "info";
            string text = line;
            foreach (string s in new[] { "error", "warning", "info" })
            {
                if (line.StartsWith(s + ":", StringComparison.OrdinalIgnoreCase))
                {
                    severity = s;
                    text = line[(s.Length + 1)..].Trim();
                }
            }

            findings.Add(new JsonObject { ["severity"] = severity, ["summary"] = text });
        }

        var body = new JsonObject { ["independence"] = Independence, ["outcome"] = ReviewOutcome, ["summary"] = ReviewSummary.Trim(), ["findings"] = findings };
        Send(api, "review", body);
    }

    /// <summary>Sends the acceptance with the selected reviews as its basis.</summary>
    public void SubmitAcceptance(IEstateApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (!CheckPrepared(api))
        {
            Status = PreparedState;
            return;
        }

        var body = new JsonObject
        {
            ["statement"] = AcceptanceStatement.Trim(),
            ["basis"] = new JsonArray(Reviews.Where(r => r.Selected).Select(r => (JsonNode)r.Id).ToArray()),
            ["self"] = SelfAcceptance,
        };
        Send(api, "acceptance", body);
    }

    private void Send(IEstateApi api, string type, JsonObject body)
    {
        if (ActingPrincipal.Length == 0)
        {
            Status = "No acting principal: this machine holds no admitted device, so nothing can be authored here.";
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
            WriteOutcome outcome = api.NewRecord(new NewRecordRequest(type, ActingPrincipal, document.RootElement.Clone(), Prepared.Subject));
            if (outcome.Ok)
            {
                WrittenId = outcome.Written!.Id;
                Status = $"Written: {type} {outcome.Written.Id}, signed by the runtime with the acting principal's device.";
                Prepared = Prepared with { Facts = SubjectFacts.About(api, SubjectId) };
                PreparedState = PreparedAction.Label(Presentation.PreparedState.Reusable);
                if (type == "review")
                {
                    Reviews.Add(new ReviewRow(outcome.Written.Id, ActingPrincipal, Independence, ReviewOutcome, ReviewSummary.Trim(), document.RootElement.GetProperty("findings")));
                }
            }
            else
            {
                Status = AssignmentComposer.Refusal(outcome.Problem);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException)
        {
            Status = "The runtime did not answer: " + exception.Message;
        }
    }
}

/// <summary>Two records side by side with their difference; the raw sides are the alternative inspection path.</summary>
public sealed class ComparisonDocument(string title, string leftTitle, string leftText, string rightTitle, string rightText) : Document(title, "comparison")
{
    /// <summary>Left heading.</summary>
    public string LeftTitle { get; } = leftTitle;

    /// <summary>Left raw text.</summary>
    public string LeftText { get; } = leftText;

    /// <summary>Right heading.</summary>
    public string RightTitle { get; } = rightTitle;

    /// <summary>Right raw text.</summary>
    public string RightText { get; } = rightText;

    /// <summary>The difference, one mark per line.</summary>
    public string DiffText { get; } = Diff.Render(Diff.Lines(leftText, rightText));

    /// <summary>Counts.</summary>
    public string Summary { get; } = Count(leftText, rightText);

    private static string Count(string left, string right)
    {
        IReadOnlyList<DiffLine> lines = Diff.Lines(left, right);
        return $"{lines.Count(l => l.Mark == '-')} removed, {lines.Count(l => l.Mark == '+')} added, {lines.Count(l => l.Mark == ' ')} unchanged";
    }
}

/// <summary>The runtime workspace: the daemon's state and health, the install plan, and the consequential actions that follow the plan.</summary>
public sealed class RuntimeDocument : Document
{
    private string label = string.Empty, where = string.Empty, health = "No daemon answers.", plan = string.Empty, output = string.Empty, acting = string.Empty;
    private bool planShown;

    /// <summary>The runtime workspace.</summary>
    public RuntimeDocument() : base("Runtime", "runtime")
    {
    }

    /// <summary>The runtime state's label.</summary>
    public string Label { get => label; set => Set(ref label, value); }

    /// <summary>Where the daemon is, in words.</summary>
    public string Where { get => where; set => Set(ref where, value); }

    /// <summary>What the daemon reports about itself.</summary>
    public string Health { get => health; set => Set(ref health, value); }

    /// <summary>The per-user service unit and commands, as the command line printed them.</summary>
    public string Plan { get => plan; set => Set(ref plan, value); }

    /// <summary>Whether the plan has been shown in this workspace; install and uninstall wait for it.</summary>
    public bool PlanShown { get => planShown; set => Set(ref planShown, value); }

    /// <summary>What the last command printed.</summary>
    public string Output { get => output; set => Set(ref output, value); }

    /// <summary>The acting principal, repeated here because the workspace acts.</summary>
    public string Acting { get => acting; set => Set(ref acting, value); }
}

/// <summary>One destination of a release as the records show it, with the four stages the design keeps apart.</summary>
public sealed record DestinationRow(string Identity, string Version, string Repository, string OperationId, string OperationStatus, string? Run, string? ReleaseId, string? ReleaseStatus, string Preparation, string Submission, string ExternalReview, string Availability, string Provider = "", string? Product = null)
{
    /// <summary>First line.</summary>
    public string Heading => $"{Version} at {Repository} through {Provider}: {StateLanguage.Label("operation", OperationStatus)}" + (ReleaseStatus is null ? string.Empty : $"; release {StateLanguage.Label("release", ReleaseStatus)}");

    /// <summary>Second line: the four stages separately.</summary>
    public string Detail => $"preparation: {Preparation}. submission: {Submission}. external review: {ExternalReview}. observed availability: {Availability}.";
}

/// <summary>One setting the selected provider declares, as the release form shows it: the product's declared value, or empty for the provider's default.</summary>
public sealed class SettingField(ProviderSettingRow setting, string? declared) : Observable
{
    private string value = declared ?? string.Empty;

    /// <summary>The setting's name, which is also the field's accessible name.</summary>
    public string Name { get; } = setting.Name;

    /// <summary>What the setting is.</summary>
    public string Summary { get; } = setting.Summary;

    /// <summary>Whether the provider refuses without it.</summary>
    public bool Required { get; } = setting.Required;

    /// <summary>The label: name, and where the value comes from when the field is left empty.</summary>
    public string Label => Name + (declared is not null ? " (declared on the product)" : setting.Default is not null ? $" (default {setting.Default} when empty)" : setting.Required ? " (required)" : " (optional)");

    /// <summary>The value the request carries; empty means the product's or the provider's.</summary>
    public string Value { get => value; set => Set(ref this.value, value); }
}

/// <summary>
/// The release workspace over a provider's intent-then-effect flow: the provider and its declared settings (the product's
/// declared destination preselects them; nothing is selected for a product that declares none, or one the runtime cannot reach,
/// even after another product's provider was selected), submit (the intent is recorded before the dispatch), observe (the run,
/// the draft, the publication), accept the exact candidate, and the operator's manual publication shown as a command the client
/// never runs.
/// </summary>
public sealed class ReleaseWorkspace : Document
{
    private string version = string.Empty, reconciliation = string.Empty, qualified = string.Empty, limitations = string.Empty, signing = "unsigned developer build", cancelReason = string.Empty, status = "Nothing sent.", publish = string.Empty;
    private ProductRow? product;
    private ProviderRow? provider;
    private DestinationRow? selected;

    /// <summary>The release workspace over the products and the release providers the runtime can reach.</summary>
    public ReleaseWorkspace(IReadOnlyList<ProductRow> products, IReadOnlyList<ProviderRow> providers) : base("Releases", "release")
    {
        Products = products;
        Providers = providers;
        Product = products.Count > 0 ? products[0] : null;
    }

    /// <summary>Destinations: one per release operation on record.</summary>
    public ObservableCollection<DestinationRow> Destinations { get; } = [];

    /// <summary>Release records.</summary>
    public ObservableCollection<ReleaseRowView> Releases { get; } = [];

    /// <summary>The selected destination; observe and accept act on it.</summary>
    public DestinationRow? Selected { get => selected; set => Set(ref selected, value); }

    /// <summary>Products a release can belong to.</summary>
    public IReadOnlyList<ProductRow> Products { get; }

    /// <summary>The release providers the runtime can reach on its machine.</summary>
    public IReadOnlyList<ProviderRow> Providers { get; }

    /// <summary>The settings the selected provider declares, filled from the product's declaration.</summary>
    public ObservableCollection<SettingField> Settings { get; } = [];

    /// <summary>
    /// The product; its declared destination selects its provider. A product that declares none, or one the runtime cannot reach,
    /// clears the selection and its setting fields, so that no provider chosen for another product carries over to this one.
    /// </summary>
    public ProductRow? Product
    {
        get => product;
        set
        {
            if (Set(ref product, value))
            {
                SelectedProvider = value?.ReleaseProvider is string declared ? Providers.FirstOrDefault(p => p.Provider == declared) : null;
                RebuildSettings();
            }
        }
    }

    /// <summary>The provider the release goes through; chosen by the person or by the product's declaration, never by default.</summary>
    public ProviderRow? SelectedProvider
    {
        get => provider;
        set
        {
            if (Set(ref provider, value))
            {
                RebuildSettings();
            }
        }
    }

    /// <summary>Version to release.</summary>
    public string Version { get => version; set => Set(ref version, value); }

    /// <summary>How an earlier failed, canceled or uncertain attempt was settled; required for a retry.</summary>
    public string Reconciliation { get => reconciliation; set => Set(ref reconciliation, value); }

    /// <summary>Qualified platforms for the candidate, one per line.</summary>
    public string Qualified { get => qualified; set => Set(ref qualified, value); }

    /// <summary>Known limitations for the candidate, one per line.</summary>
    public string Limitations { get => limitations; set => Set(ref limitations, value); }

    /// <summary>Signing state recorded on the candidate.</summary>
    public string Signing { get => signing; set => Set(ref signing, value); }

    /// <summary>A reason that cancels an intent never dispatched or an outcome reconciled as having had no effect.</summary>
    public string CancelReason { get => cancelReason; set => Set(ref cancelReason, value); }

    /// <summary>What the runtime answered last.</summary>
    public string Status { get => status; private set => Set(ref status, value); }

    /// <summary>The operator's manual publication command, as the runtime named it; never run here.</summary>
    public string Publish { get => publish; private set => Set(ref publish, value); }

    /// <summary>The last response.</summary>
    public ReleaseResponse? LastResponse { get; private set; }

    /// <summary>Submits the release: the runtime records the intent, then dispatches; nothing is published.</summary>
    public void Submit(IEstateApi api, string? by)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (string.IsNullOrWhiteSpace(by))
        {
            Status = "No acting principal: this machine holds no admitted device, so nothing can be authored here.";
            return;
        }

        if (Product is null || string.IsNullOrWhiteSpace(Version))
        {
            Status = "A product and a version are needed before anything is sent.";
            return;
        }

        if (SelectedProvider is null)
        {
            Status = Product.ReleaseProvider is null
                ? "Choose the provider the release goes through; the product declares none, and nothing is selected for you."
                : $"Choose the provider the release goes through; the product declares {Product.ReleaseProvider}, which the runtime cannot reach, and nothing is selected for you.";
            return;
        }

        List<string> missing = Settings.Where(s => s.Required && s.Value.Trim().Length == 0 && Product.ReleaseSettings?.ContainsKey(s.Name) != true).Select(s => s.Name).ToList();
        if (missing.Count > 0)
        {
            Status = $"The provider {SelectedProvider.Provider} needs {string.Join(", ", missing)} before anything is sent.";
            return;
        }

        var values = Settings.Where(s => s.Value.Trim().Length > 0).ToDictionary(s => s.Name, s => s.Value.Trim(), StringComparer.Ordinal);
        Answer(() => api.ReleaseSubmit(new ReleaseSubmitRequest(Product.Id, Version.Trim(), null, by, null, null, string.IsNullOrWhiteSpace(Reconciliation) ? null : Reconciliation.Trim(), SelectedProvider.Provider, values)));
    }

    /// <summary>Observes the selected destination (or the form's product and version): the run, the draft, the publication; or cancels with the reason given.</summary>
    public void Observe(IEstateApi api, string? by)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (string.IsNullOrWhiteSpace(by))
        {
            Status = "No acting principal: this machine holds no admitted device, so nothing can be authored here.";
            return;
        }

        string targetVersion = Selected?.Version ?? Version.Trim();
        if (targetVersion.Length == 0 || (Selected is null && Product is null))
        {
            Status = "Select a destination, or give a product and a version, before observing.";
            return;
        }

        string? cancel = string.IsNullOrWhiteSpace(CancelReason) ? null : CancelReason.Trim();
        string signingState = string.IsNullOrWhiteSpace(Signing) ? "unsigned developer build" : Signing.Trim();
        ReleaseObserveRequest request = Selected is not null
            ? new ReleaseObserveRequest(targetVersion, Selected.Repository, by, Lines(Qualified), Lines(Limitations), signingState, cancel, Selected.Product, Selected.Provider)
            : new ReleaseObserveRequest(targetVersion, null, by, Lines(Qualified), Lines(Limitations), signingState, cancel, Product!.Id);
        Answer(() => api.ReleaseObserve(request));
    }

    private void RebuildSettings()
    {
        Settings.Clear();
        if (SelectedProvider is null)
        {
            return;
        }

        IReadOnlyDictionary<string, string>? declared = Product?.ReleaseProvider == SelectedProvider.Provider ? Product.ReleaseSettings : null;
        foreach (ProviderSettingRow setting in SelectedProvider.Settings)
        {
            Settings.Add(new SettingField(setting, declared is not null && declared.TryGetValue(setting.Name, out string? value) ? value : null));
        }
    }

    private void Answer(Func<ReleaseResponse> call)
    {
        try
        {
            ReleaseResponse response = call();
            LastResponse = response;
            Status = response.Problem is null ? response.Message : AssignmentComposer.Refusal(response.Problem);
            if (response.Publish is not null)
            {
                Publish = response.Publish;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException)
        {
            Status = "The runtime did not answer: " + exception.Message;
        }
    }

    private static List<string> Lines(string text) => text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
}

/// <summary>A living document as the Documents page lists it: its state, what made it due, and its latest clearance.</summary>
public sealed class LivingDocumentRow(DocumentRow row)
{
    /// <summary>The runtime's row.</summary>
    public DocumentRow Row { get; } = row;

    /// <summary>The document and its state.</summary>
    public string Heading => $"{Row.Path}  {Row.State}{(Row.Level is null ? string.Empty : " at " + Row.Level)}{(Row.Title is null ? string.Empty : "  " + Row.Title)}";

    /// <summary>Every reason, missing contribution and note, or the latest clearance when it is current.</summary>
    public string Detail => string.Join("\n", Row.Reasons.Select(r => $"{r.On} ({r.Level}): {r.Summary}")
        .Concat(Row.Unavailable.Select(u => $"unavailable: {u} is provided by no installed pack"))
        .Concat(Row.Notes.Select(n => "note: " + n))
        .DefaultIfEmpty(Row.Clearance is null ? "Never cleared." : $"Cleared by {Row.Clearance}."));
}

/// <summary>
/// The Documents page: each declared living document with its state, what made it due and its latest clearance, and a review of
/// the selected one. The runtime reads the content, records the triggers the review answers and runs the declared checks; the
/// page decides neither independence nor clearance, and a refusal comes back with every reason.
/// </summary>
public sealed class LivingDocumentsWorkspace : Document
{
    private static readonly string[] Severities = ["error", "warning", "info"];
    private LivingDocumentRow? selected;
    private string outcome = "current", summary = string.Empty, commit = string.Empty, findings = string.Empty, status = "Nothing sent.";

    /// <summary>The page; fill it with the runtime's rows.</summary>
    public LivingDocumentsWorkspace() : base("Living documents", "documents")
    {
    }

    /// <summary>The declared documents.</summary>
    public ObservableCollection<LivingDocumentRow> Rows { get; } = [];

    /// <summary>The document a review is for.</summary>
    public LivingDocumentRow? Selected
    {
        get => selected;
        set
        {
            if (Set(ref selected, value))
            {
                Raise(nameof(CommitHint));
            }
        }
    }

    /// <summary>What a review can find.</summary>
    public IReadOnlyList<string> Outcomes { get; } = ["current", "revised", "needs-change"];

    /// <summary>current (it holds), revised (it was changed for the trigger) or needs-change (it is stale and stays due).</summary>
    public string Outcome { get => outcome; set => Set(ref outcome, value); }

    /// <summary>What was checked.</summary>
    public string Summary { get => summary; set => Set(ref summary, value); }

    /// <summary>The commit a document of another repository is reviewed at.</summary>
    public string Commit { get => commit; set => Set(ref commit, value); }

    /// <summary>Findings, one per line as severity: text.</summary>
    public string Findings { get => findings; set => Set(ref findings, value); }

    /// <summary>What the runtime answered.</summary>
    public string Status { get => status; set => Set(ref status, value); }

    /// <summary>What needs attention.</summary>
    public string Note => Rows.Count == 0
        ? "No living document is declared; declare one with vericairn docs declare."
        : $"{Rows.Count(r => r.Row.State != "current")} of {Rows.Count} need attention. A document due at refuse holds its product's next release.";

    /// <summary>Whether the selected document needs a commit.</summary>
    public string CommitHint => Selected?.Row.Repository is null ? "Commit (only for a document of another repository)" : "Commit (required: the document is in another repository)";

    /// <summary>Replaces the rows with the runtime's, keeping the selection.</summary>
    public void Fill(IReadOnlyList<DocumentRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        string? declaration = Selected?.Row.Declaration;
        Rows.Clear();
        foreach (DocumentRow row in rows)
        {
            Rows.Add(new LivingDocumentRow(row));
        }

        Selected = Rows.FirstOrDefault(r => r.Row.Declaration == declaration) ?? Rows.FirstOrDefault(r => r.Row.State != "current") ?? Rows.FirstOrDefault();
        Raise(nameof(Note));
    }

    /// <summary>Sends the review of the selected document as the acting principal.</summary>
    public void Submit(IEstateApi api, string? by)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (string.IsNullOrWhiteSpace(by))
        {
            Status = "No acting principal: this machine holds no admitted device, so nothing can be authored here.";
            return;
        }

        if (Selected is null)
        {
            Status = "Choose a document.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Summary))
        {
            Status = "Say what was checked.";
            return;
        }

        var parsed = new List<DocumentFinding>();
        foreach (string line in Findings.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            string severity = Severities.FirstOrDefault(s => line.StartsWith(s + ":", StringComparison.OrdinalIgnoreCase)) ?? "info";
            parsed.Add(new DocumentFinding(severity, line.StartsWith(severity + ":", StringComparison.OrdinalIgnoreCase) ? line[(severity.Length + 1)..].Trim() : line));
        }

        string? reviewed = string.IsNullOrWhiteSpace(Commit) ? null : Commit.Trim();
        WriteOutcome sent = api.DocumentReview(Selected.Row.Declaration, new DocumentReviewRequest(by, Outcome, Summary.Trim(), parsed, reviewed));
        Status = sent.Ok
            ? $"Written: document review {sent.Written!.Id}. The runtime read {Selected.Row.Path}, recorded the triggers the review answers and ran the declared checks."
            : AssignmentComposer.Refusal(sent.Problem);
    }
}
