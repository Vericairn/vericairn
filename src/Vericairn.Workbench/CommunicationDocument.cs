// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net.Http;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Workbench;

/// <summary>An editor of one attributable profile. The expected revision is held from refresh through submission.</summary>
public sealed class CommunicationDocument(Func<IEstateApi?> api, Func<string?> author, Action? saved = null) : Document("Communication settings", "communication")
{
    private string scope = "person", recipient = string.Empty, assignment = string.Empty, authority = string.Empty;
    private string progress = "inherit", status = "inherit", decision = "inherit", result = "inherit", handoff = "inherit";
    private string effective = "Refresh to read the effective values.", note = "No change submitted.";
    private string? expected;
    private CommunicationProfile profile = CommunicationResolution.Resolve(null, null, []);

    /// <summary>Supported scopes; only an administrator can write estate defaults.</summary>
    public IReadOnlyList<string> Scopes { get; } = ["person", "assignment", "estate"];
    /// <summary>The same depth choices as the CLI; inherit is distinct from any explicit value.</summary>
    public IReadOnlyList<string> Depths { get; } = ["inherit", "short", "standard", "comprehensive"];
    /// <summary>Optional progress frequency choices.</summary>
    public IReadOnlyList<string> Frequencies { get; } = ["inherit", "none", "completion-and-need", "milestones", "verbose"];
    /// <summary>The scope edited; refresh after changing it.</summary>
    public string Scope { get => scope; set { if (Set(ref scope, value)) { Changed(); } } }
    /// <summary>The person addressed. This is separate from the actual author.</summary>
    public string Recipient { get => recipient; set { if (Set(ref recipient, value)) { Changed(); } } }
    /// <summary>An exact assignment for an assignment override.</summary>
    public string Assignment { get => assignment; set { if (Set(ref assignment, value)) { Changed(); } } }
    /// <summary>Explicit authority for a delegated change, when needed.</summary>
    public string Authority { get => authority; set => Set(ref authority, value); }
    /// <summary>Optional progress frequency.</summary>
    public string ProgressFrequency { get => progress; set { if (Set(ref progress, value)) { Changed(); } } }
    /// <summary>Status depth.</summary>
    public string StatusDepth { get => status; set { if (Set(ref status, value)) { Changed(); } } }
    /// <summary>Decision lead depth; the complete decision is always retained.</summary>
    public string DecisionDepth { get => decision; set { if (Set(ref decision, value)) { Changed(); } } }
    /// <summary>Result explanation depth; the complete result is always retained.</summary>
    public string ResultDepth { get => result; set { if (Set(ref result, value)) { Changed(); } } }
    /// <summary>Handoff introduction depth; the retained package is unchanged.</summary>
    public string HandoffDepth { get => handoff; set { if (Set(ref handoff, value)) { Changed(); } } }
    /// <summary>Recorded effective values and their sources, including conflicts and freshness.</summary>
    public string Effective { get => effective; private set => Set(ref effective, value); }
    /// <summary>The complete outcome or refusal of the last action.</summary>
    public string Note { get => note; private set => Set(ref note, value); }
    /// <summary>A preview from the same examples and renderer as the CLI; a draft authors nothing.</summary>
    public string Preview => CommunicationPresentation.Preview(CommunicationResolution.Resolve(Scope == "estate" ? null : Recipient,
        Scope == "assignment" ? Assignment : null,
        [.. profile.Revisions.Where(r => r.Scope != Scope || !r.Current), new CommunicationLayer(Scope, Values(), By: author())]));

    /// <summary>Read the scope explicitly, loading its expected revision. This does not run on background refresh.</summary>
    public void Refresh()
    {
        Read(estate =>
        {
            if (Recipient.Length == 0) { Recipient = author() ?? string.Empty; }
            profile = estate.Communication(Scope == "estate" ? null : Empty(Recipient), Scope == "assignment" ? Empty(Assignment) : null);
            Effective = CommunicationPresentation.Describe(profile);
            CommunicationLayer? current = profile.Revisions.Where(r => r.Current && r.Scope == Scope).Take(2).ToList() is { Count: 1 } revisions ? revisions[0] : null;
            expected = current?.Hash;
            CommunicationValues values = current?.Values ?? new();
            ProgressFrequency = values.Progress ?? "inherit";
            StatusDepth = values.Status ?? "inherit";
            DecisionDepth = values.Decision ?? "inherit";
            ResultDepth = values.Result ?? "inherit";
            HandoffDepth = values.Handoff ?? "inherit";
            Raise(nameof(Preview));
        });
    }

    /// <summary>Submit this draft with the exact prior revision read, retaining a conflict for review if it changed.</summary>
    public void Save() => Submit(Values());

    /// <summary>Reset the whole selected scope to inheritance by authoring a new revision.</summary>
    public void Reset() => Submit(new CommunicationValues());

    private void Submit(CommunicationValues values)
    {
        if (author() is not string by)
        {
            Note = "Select the actual author's principal before changing a setting.";
            return;
        }

        Read(estate =>
        {
            WriteOutcome outcome = estate.CommunicationSet(new CommunicationSetRequest(by, Scope, values, Scope == "estate" ? null : Empty(Recipient),
                Scope == "assignment" ? Empty(Assignment) : null, expected, Empty(Authority)));
            Note = outcome.Written is { } written ? "Recorded " + written.Id + " at " + written.Hash
                : outcome.Problem?.Code + ": " + outcome.Problem?.Message + "\n" + string.Join('\n', outcome.Problem?.Findings.Select(f => f.Message) ?? []);
            if (outcome.Ok)
            {
                Refresh();
                saved?.Invoke();
            }
        });
    }

    private CommunicationValues Values() => new(Inherit(ProgressFrequency), Inherit(StatusDepth), Inherit(DecisionDepth), Inherit(ResultDepth), Inherit(HandoffDepth));
    private static string? Inherit(string value) => value == "inherit" ? null : value;
    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void Changed() => Raise(nameof(Preview));

    private void Read(Action<IEstateApi> action)
    {
        if (api() is not { } estate)
        {
            Note = "Communication settings unavailable: the runtime did not answer. Draft values are not recorded.";
            return;
        }

        try { action(estate); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or HttpRequestException or JsonException or ArgumentException)
        {
            Note = "Communication settings unavailable: " + exception.Message;
        }
    }
}
