// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;
using static Vericairn.Packs.Software.CompanionInput;

namespace Vericairn.Packs.Software;

internal sealed class WorkflowCompanion() : SoftwareCompanion("workflow-actions")
{
    protected override string Render(CompanionInput input)
    {
        JsonElement root = input.Json();
        input.Build(root, "vericairn.workflow-inputs/1");
        // The source adapter runs FIX-13's actual parser; every original it inspected must be retained with its hash.
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { input.SourcePath };
        foreach (JsonElement source in Array(root, "sources"))
        {
            string path = String(source, "path");
            string retained = input.Relative(path);
            if (!expected.Add(retained) || input.File(retained).Hash != String(source, "hash")) throw new CompanionUnavailableException();
        }
        if (!expected.Contains(input.Relative(".github/workflows/actions.allow")) || !expected.Contains(input.Relative(".github/scripts/check-workflow-actions.rb"))
            || expected.Count != input.Context.Sources.Length) throw new CompanionUnavailableException();
        var text = new StringBuilder("# Workflow actions\n\nThis is the selected allow-list and the uses observed by the retained workflow parser. FIX-13's separate check enforces its policy; this rendition grants no permission.\n\n## Allowed identities\n\n| Action at exact commit |\n|---|\n");
        foreach (JsonElement action in Array(root, "allowed").OrderBy(a => a.GetString(), StringComparer.Ordinal)) Row(text, action.GetString()!);
        text.Append("\n## Workflow uses\n\n| Workflow | Line | Action at exact commit |\n|---|---|---|\n");
        foreach (JsonElement use in Array(root, "uses").OrderBy(u => String(u, "path"), StringComparer.Ordinal).ThenBy(u => u.GetProperty("line").GetInt32()))
        {
            string path = String(use, "path");
            if (!expected.Contains(input.Relative(path))) throw new CompanionUnavailableException();
            Row(text, path, Recorded(use, "line"), String(use, "action"));
        }
        return text.ToString();
    }
}
