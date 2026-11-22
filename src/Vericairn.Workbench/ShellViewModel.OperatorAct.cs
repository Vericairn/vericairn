// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Workbench;

public sealed partial class ShellViewModel
{
    /// <summary>From 0.9.4 (OPS-24): opens the reviewed list of every ready operator item, as vericairn operator act --ready lists it.</summary>
    public Task<OperatorActDocument?> OpenOperatorActAsync(CancellationToken cancellationToken = default) => RunCommandAsync("Open Operator act", async token =>
    {
        OperatorActDocument page = Documents.OfType<OperatorActDocument>().FirstOrDefault() ?? new OperatorActDocument(() => connection?.Api, () => ActingPrincipalId);
        await page.RefreshAsync(token).ConfigureAwait(true);
        if (HomeSelected) { SelectedRegion = Regions.First(r => r.Name == "Work"); }
        Inspector = "The operator items whose command is ready, each with what its signature records. Unselect any to leave it waiting or dismiss a request with your reason; the rest sign in one act on your own device, under one presence check where presence sessions apply.";
        Open(page);
        return page;
    }, cancellationToken);

    /// <summary>Reads the ready items again; reading signs nothing.</summary>
    public Task RefreshOperatorActAsync(OperatorActDocument? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Read ready operator items", token => page!.RefreshAsync(token), false, cancellationToken: cancellationToken);

    /// <summary>Signs the reviewed act, following the runtime's presence notices while it signs.</summary>
    public Task SignOperatorActAsync(OperatorActDocument? page, CancellationToken cancellationToken = default) =>
        DocumentCommandAsync(page, "Sign the operator act", token => page!.SignAsync(token), cancellationToken: cancellationToken, submission: true);
}
