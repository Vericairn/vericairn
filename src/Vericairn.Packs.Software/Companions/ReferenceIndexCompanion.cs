// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Policies;
using static Vericairn.Packs.Software.CompanionInput;

namespace Vericairn.Packs.Software;

internal sealed class ReferenceIndexCompanion() : SoftwareCompanion("reference-index")
{
    protected override string Render(CompanionInput input)
    {
        var repository = new PolicyRepository(input.File(input.SourcePath).Repository!, input.Commit, PolicyBasis.RepositoryContract, true,
            [.. input.Context.Sources.Select(s => new PolicyFile(s.Path!, s.Hash, s.Bytes))]);
        RetainedReferenceInventory inventory;
        try { inventory = ReferenceInventory.Read(repository, input.SourcePath, input.Commit); }
        catch (PolicyInputException) { throw new CompanionUnavailableException(); }
        var text = new StringBuilder("# Generated reference index\n\nThe categories and exact names below come from this build's retained inventory and its hash-bound references. Mapping status is separate from reference membership.\n\n| Category | Exact name | Reference | Register mapping |\n|---|---|---|---|\n");
        foreach (ReferenceItem item in inventory.Items)
        {
            string reference = input.Relative(ReferenceInventory.ReferencePaths[item.Category]);
            text.Append("| ").Append(DocumentMarkdown.Text(item.Category)).Append(" | ").Append(DocumentMarkdown.Text(item.Name))
                .Append(" | ").Append(DocumentMarkdown.Link(ReferenceInventory.ReferencePaths[item.Category], reference))
                .Append(" | Not selected |\n");
        }
        return text.ToString();
    }
}
