// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Policies;
using static Vericairn.Packs.Software.CompanionInput;

namespace Vericairn.Packs.Software;

internal sealed class ReferenceIndexCompanion() : SoftwareCompanion("reference-index", new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal) { ["register"] = ["none", "@path"] })
{
    protected override string Render(CompanionInput input)
    {
        var repository = new PolicyRepository(input.File(input.SourcePath).Repository!, input.Commit, PolicyBasis.RepositoryContract, true,
            [.. input.Context.Sources.Select(s => new PolicyFile(s.Path!, s.Hash, s.Bytes))]);
        RetainedReferenceInventory inventory;
        try { inventory = ReferenceInventory.Read(repository, input.SourcePath, input.Commit); }
        catch (PolicyInputException) { throw new CompanionUnavailableException(); }
        string register = input.Context.Parameters["register"];
        ReferenceCoverageTables? mappings = null;
        if (register != "none")
        {
            try { mappings = ReferenceCoverageTables.Read(input.Text(register)); }
            catch (PolicyInputException) { throw new CompanionUnavailableException(); }
        }
        var text = new StringBuilder("# Generated reference index\n\nThe categories and exact names below come from this build's retained inventory and its hash-bound references. Mapping status is separate from reference membership.\n\n| Category | Exact name | Reference | Register mapping |\n|---|---|---|---|\n");
        foreach (ReferenceItem item in inventory.Items)
        {
            string reference = input.Relative(ReferenceInventory.ReferencePaths[item.Category]);
            text.Append("| ").Append(DocumentMarkdown.Text(item.Category)).Append(" | ").Append(DocumentMarkdown.Text(item.Name))
                .Append(" | ").Append(DocumentMarkdown.Link(ReferenceInventory.ReferencePaths[item.Category], reference))
                .Append(" | ");
            ReferenceMapping[] matches = mappings?.Mappings.Where(m => m.Item == item).OrderBy(m => m.Term, StringComparer.Ordinal).ToArray() ?? [];
            if (mappings is null) text.Append("Not selected");
            else if (matches.Length == 0) text.Append("Not recorded");
            else text.AppendJoin(", ", matches.Select(m => "[" + DocumentMarkdown.Text(m.Term) + "](" + register + "#generated-reference-coverage)"));
            text.Append(" |\n");
        }
        if (mappings is not null)
        {
            text.Append("\n## Recorded register mappings\n\nAll selected mappings are listed, including references outside this inventory. A mapping is not a coverage-policy verdict.\n\n| Category | Reference | Term |\n|---|---|---|\n");
            foreach (ReferenceMapping mapping in mappings.Mappings.OrderBy(m => m.Item.Category, StringComparer.Ordinal).ThenBy(m => m.Item.Name, StringComparer.Ordinal).ThenBy(m => m.Term, StringComparer.Ordinal))
                Row(text, mapping.Item.Category, mapping.Item.Name, mapping.Term);
        }
        return text.ToString();
    }
}
