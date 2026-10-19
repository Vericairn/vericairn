// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Workbench;

public sealed partial class ShellViewModel
{
    private void InvalidateCompanions()
    {
        foreach (CompanionDocument page in Documents.OfType<CompanionDocument>()) page.Unavailable();
        foreach (CompanionCatalogDocument page in Documents.OfType<CompanionCatalogDocument>()) page.Unavailable();
    }

    /// <summary>Open the runtime's registered collections without adopting any output.</summary>
    public Task OpenCompanionCatalogAsync(CancellationToken cancellationToken = default) => RunCommandAsync("Read companion coverage", async token =>
    {
        if (connection?.Api is not { } api) { InvalidateCompanions(); return; }
        var page = new CompanionCatalogDocument(await api.CompanionsAsync(token).ConfigureAwait(true));
        page.Selected = page.Rows.Count == 0 ? null : page.Rows[0];
        Open(page);
    }, cancellationToken);

    /// <summary>Open an exact record revision beside its source.</summary>
    public Task OpenCompanionAsync(string revision, CancellationToken cancellationToken = default) => RunCommandAsync("Read exact revision",
        token => OpenCompanionCoreAsync(new("vericairn.records/items", "item", revision), token), cancellationToken);

    /// <summary>Open a selected collection with the catalog's explicit snapshot.</summary>
    public Task OpenCompanionCollectionAsync(CompanionCatalogDocument? catalog, CancellationToken cancellationToken = default) => RunCommandAsync("Read collection", async token =>
    {
        if (catalog?.Selected is not { } selected) return;
        bool manifest = selected.Id == "vericairn.estate/manifest";
        await OpenCompanionCoreAsync(new(selected.Id, manifest ? "item" : "current", Snapshot: manifest ? catalog.Coverage.ManifestSnapshot : catalog.Coverage.Snapshot), token).ConfigureAwait(true);
    }, cancellationToken);

    /// <summary>Read a selected document's declared generator, or its exact authored declaration.</summary>
    public Task OpenDeclaredCompanionAsync(LivingDocumentsWorkspace? documents, CancellationToken cancellationToken = default) => RunCommandAsync("Read declared companion", async token =>
    {
        if (documents?.Selected is not { } row || connection?.Api is not { } api) return;
        CompanionsResponse coverage = await api.CompanionsAsync(token).ConfigureAwait(true);
        CompanionDestination? destination = coverage.Destinations.FirstOrDefault(d => d.Declaration == row.Row.Declaration);
        CompanionGeneratorRow? generator = coverage.Generators.FirstOrDefault(g => g.Id == destination?.Generator);
        CompanionRequest request = destination is null || generator is null ? new("vericairn.records/items", "item", row.Row.Declaration)
            : generator.Pattern is not null ? new(generator.Id, "all-revisions", Snapshot: coverage.Snapshot)
            : new(generator.Id, "item", Declaration: destination.Declaration, Commit: string.IsNullOrWhiteSpace(documents.Commit) ? null : documents.Commit.Trim());
        await OpenCompanionCoreAsync(request, token).ConfigureAwait(true);
    }, cancellationToken);

    /// <summary>Refresh a reader or move within its exact snapshot; a page change never silently selects a new collection.</summary>
    public Task RefreshCompanionAsync(CompanionDocument? page, int pageChange = 0, CancellationToken cancellationToken = default) => RunCommandAsync("Refresh readable companion", async token =>
    {
        if (page is null) return;
        if (connection?.Api is not { } api) { page.Unavailable(); return; }
        await page.RefreshAsync(api, renewSnapshot: pageChange == 0, page: Math.Max(1, page.Request.Page + pageChange), cancellationToken: token).ConfigureAwait(true);
    }, cancellationToken);

    private async Task OpenCompanionCoreAsync(CompanionRequest request, CancellationToken cancellationToken)
    {
        var page = new CompanionDocument(request);
        Open(page);
        if (connection?.Api is { } api) await page.RefreshAsync(api, renewSnapshot: false, cancellationToken: cancellationToken).ConfigureAwait(true);
    }
}
