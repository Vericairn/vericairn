// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using Vericairn.Presentation;
using Vericairn.Protocol;

namespace Vericairn.Workbench;

/// <summary>A permitted source row with explicit column labels for keyboard and assistive readers.</summary>
public sealed record CompanionSourceRow(CompanionSource Source)
{
    /// <summary>The immutable source identity.</summary>
    public string Identity => Source.RecordType is null ? Source.Identity : Source.RecordType + " " + Source.Identity;
    /// <summary>Every basis field remains readable without relying on a color or hover.</summary>
    public string Detail => "Hash: " + Source.Hash + "; path: " + (Source.Path ?? "not applicable") + "; repository: " + (Source.Repository ?? "estate")
        + "; recorded at: " + (Source.At ?? "not applicable") + "; lineage: " + (Source.Current is true ? "current at this snapshot" : Source.Current is false ? "historical" : "not applicable");
    /// <summary>The complete accessible row label.</summary>
    public string AccessibleName => Identity + ". " + Detail;
}

/// <summary>A read-only companion beside its exact source inspector. Refresh never follows an item's successor.</summary>
public sealed class CompanionDocument(CompanionRequest request) : Document("Readable " + (request.Revision ?? request.Declaration ?? request.Generator), "companion")
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private string text = "", basis = "Unavailable; refresh to read the selected basis.", sourceText = "", difference = "No earlier rendition in this view.", previousText = "";
    private string currentness = "unavailable";
    private string? contentHash;
    private CompanionSourceRow? selectedSource;

    /// <summary>The exact selection used for this page.</summary>
    public CompanionRequest Request { get; private set; } = request;
    /// <summary>Generated content, selectable but never editable.</summary>
    public string Text { get => text; private set => Set(ref text, value); }
    /// <summary>The runtime's exact freshness and source basis.</summary>
    public string Basis { get => basis; private set => Set(ref basis, value); }
    /// <summary>Current, historical or unavailable, as the runtime last confirmed.</summary>
    public string Currentness { get => currentness; private set => Set(ref currentness, value); }
    /// <summary>The unchanged exact source envelope, or source metadata for a file.</summary>
    public string SourceText { get => sourceText; private set => Set(ref sourceText, value); }
    /// <summary>A bounded comparison of the previous and current generated rendition.</summary>
    public string Difference { get => difference; private set => Set(ref difference, value); }
    /// <summary>The earlier observed rendition with its own basis; it is never called current.</summary>
    public string PreviousText { get => previousText; private set => Set(ref previousText, value); }
    /// <summary>Permitted source rows from this response, not independently joined metadata.</summary>
    public ObservableCollection<CompanionSourceRow> Sources { get; } = [];
    /// <summary>The exact source selected for navigation.</summary>
    public CompanionSourceRow? SelectedSource { get => selectedSource; set => Set(ref selectedSource, value); }
    /// <summary>Guidance is separate from authored review and protected actions.</summary>
    public string EditGuidance { get; } = "Generated view. Correct the authoritative source, then refresh. Reading or regenerating clears no authored review and grants no authority.";

    /// <summary>Invalidates a cached view after loss of connection or access. Old source bytes are not served again.</summary>
    public void Unavailable()
    {
        Currentness = "unavailable";
        Basis = "Unavailable. Current access and freshness have not been confirmed; refresh before relying on this view.";
        Text = SourceText = PreviousText = "";
        contentHash = null;
        Difference = "A comparison requires a successful authorized refresh.";
        Sources.Clear();
        SelectedSource = null;
    }

    /// <summary>Renews source access and optionally selects a new collection snapshot; an exact revision stays exact.</summary>
    public async Task RefreshAsync(IEstateApi api, bool renewSnapshot = true, int? page = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        string old = Text, oldBasis = Basis;
        string? oldHash = contentHash;
        Unavailable();
        CompanionRequest next = Request with { Page = page ?? Request.Page };
        if (renewSnapshot && next.Revision is null && next.Declaration is null)
        {
            CompanionsResponse coverage = await api.CompanionsAsync(cancellationToken).ConfigureAwait(true);
            if (coverage.Status != "success") { Basis = coverage.Reason ?? Basis; return; }
            next = next with { Snapshot = next.Generator == "vericairn.estate/manifest" ? coverage.ManifestSnapshot : coverage.Snapshot };
        }
        CompanionResponse response = await api.CompanionRenderAsync(next, cancellationToken).ConfigureAwait(true);
        if (response.Status != "success")
        {
            Basis = "Unavailable. " + response.Reason;
            if (next.Revision is { } exact)
            {
                // A missing specialist must not remove the source inspector. Resolve its access anew through the common reader.
                CompanionResponse sourceBasis = await api.CompanionSourcesAsync(new("vericairn.records/items", "item", exact, Audience: next.Audience), cancellationToken).ConfigureAwait(true);
                if (sourceBasis.Status == "success")
                {
                    RecordResponse? source = await api.RecordAsync(exact, cancellationToken).ConfigureAwait(true);
                    if (source is not null && sourceBasis.Sources.Any(s => s.Identity == source.Id && s.Hash == source.Hash))
                    {
                        Request = next;
                        SourceText = JsonSerializer.Serialize(source.Envelope, Pretty);
                        foreach (CompanionSource row in sourceBasis.Sources) Sources.Add(new(row));
                        SelectedSource = Sources.FirstOrDefault();
                        Basis += "\nThe exact source is available for inspection; its lineage is " + sourceBasis.Currentness
                            + " at snapshot " + sourceBasis.Snapshot + ". The readable rendition remains unavailable.";
                    }
                }
            }
            return;
        }
        string inspected = string.Join('\n', response.Sources.Select(s => new CompanionSourceRow(s).AccessibleName));
        if (next.Revision is { } revision)
        {
            RecordResponse? record = await api.RecordAsync(revision, cancellationToken).ConfigureAwait(true);
            if (record is null || !response.Sources.Any(s => s.Identity == record.Id && s.Hash == record.Hash))
            {
                Basis = "Unavailable. The source inspector did not return the exact generated basis.";
                return;
            }
            inspected = JsonSerializer.Serialize(record.Envelope, Pretty);
        }
        Request = next;
        Currentness = response.Currentness;
        Basis = response.Currentness + "; " + response.Completeness + "; page " + next.Page + "; audience " + response.Audience
            + "\nSnapshot: " + response.Snapshot + (response.Commit is null ? "" : "\nSource commit: " + response.Commit)
            + "\nInput digest: " + response.InputDigest + "\nRendition hash: " + response.ContentHash
            + (response.Reason is null ? "" : "\n" + response.Reason);
        Text = WebUtility.HtmlDecode(response.Text ?? "");
        contentHash = response.ContentHash;
        SourceText = inspected;
        foreach (CompanionSource source in response.Sources) Sources.Add(new(source));
        SelectedSource = Sources.FirstOrDefault(s => s.Source.Identity == next.Revision) ?? Sources.FirstOrDefault();
        if (old.Length == 0) return;
        PreviousText = "Earlier observation; refresh does not make this basis current.\n" + oldBasis + "\n\n" + old;
        Difference = oldHash == contentHash ? "Generated bytes are unchanged. The displayed basis was freshly checked."
            : old.Count(c => c == '\n') <= 400 && Text.Count(c => c == '\n') <= 400 ? Diff.Render(Diff.Lines(old, Text))
            : "The rendition changed. A line comparison exceeds this view's bound; both complete renditions and their exact bases are available in the adjacent tabs.";
    }
}

/// <summary>Installed record collections, without implying adoption of their possible repository destinations.</summary>
public sealed class CompanionCatalogDocument(CompanionsResponse coverage) : Document("Readable collections", "companion-catalog")
{
    private CompanionGeneratorRow? selected;

    /// <summary>The runtime's explicit current selection basis.</summary>
    public CompanionsResponse Coverage { get; private set; } = coverage;
    /// <summary>Collection readers admitted by the installed runtime; local inspectors stay in their owning local surface.</summary>
    public IReadOnlyList<CompanionGeneratorRow> Rows => Coverage.Generators.Where(g => !g.LocalOnly &&
        (g.Pattern is not null || g.Id is "vericairn.records/items" or "vericairn.estate/manifest")).ToArray();
    /// <summary>The collection selected for opening.</summary>
    public CompanionGeneratorRow? Selected { get => selected; set => Set(ref selected, value); }
    /// <summary>No catalog read adopts any declaration.</summary>
    public string Note => Coverage.Status == "success" ? "Select a collection and press Enter. Item links retain exact source revisions. Possible register paths are not declarations." : Coverage.Reason ?? "Coverage is unavailable.";

    /// <summary>Replaces the catalog with a fresh authorized observation while retaining only a still-admitted selection.</summary>
    public void Fill(CompanionsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        string? previous = Selected?.Id;
        Coverage = response;
        Raise(nameof(Coverage));
        Raise(nameof(Rows));
        Raise(nameof(Note));
        IReadOnlyList<CompanionGeneratorRow> rows = Rows;
        Selected = rows.FirstOrDefault(row => row.Id == previous) ?? (rows.Count == 0 ? null : rows[0]);
    }

    /// <summary>Discards cached destinations, digests and selections when current access cannot be confirmed.</summary>
    public void Unavailable() => Fill(new(1, "unavailable", null, [], [], [], "Coverage is unavailable; refresh to confirm current access and selection."));
}
