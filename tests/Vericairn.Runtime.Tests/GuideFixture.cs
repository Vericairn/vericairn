// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

// All prose, names and identities here are synthetic. Only the owning table column contracts
// reproduce the catalog format. Inputs are created at runtime, independently of checkout newlines.
public sealed class GuideFixture
{
    public GuideSourceIndex Index { get; set; } = new();
    public Dictionary<string, byte[]> Sources { get; } = new(StringComparer.Ordinal);
    public string[] PlannedKeys => Index.Entries.Where(e => e.State is "planned" or "remainder").Select(e => e.Key).Order(StringComparer.Ordinal).ToArray();

    public static GuideFixture Create(int familyCount = 2, string newline = "\n")
    {
        var fixture = new GuideFixture();
        var pins = new List<GuideSourcePin>();
        var blocks = new List<GuideBlockPin>();
        var roles = new List<GuideRoleBinding>();
        var families = new List<GuideFamilyBinding>();
        var tables = new List<GuideTableBinding>();
        var entries = new List<GuideEntryBinding>();
        string[] kinds = ["domain", "foundry", "adapter", "provider", "shared-capability"];
        var contract = new Source("contracts", "contracts", "roles.md");
        contract.Append("# Synthetic role contracts\n");
        foreach (string kind in kinds)
        {
            contract.Append("## " + kind + "\n");
            contract.Block("role." + kind, "Synthetic " + kind + " contract: present a proposed result and retain the reviewer's decision.\n");
            roles.Add(new(kind, ["role." + kind]));
        }
        Complete(contract);

        var shared = new Source("catalog", "catalog", "catalog.md");
        shared.Append("# Fixture catalog\n");
        shared.Block("shared.status", "The fixture's new entries are planned, with no availability claim.\n");
        shared.Block("shared.remainder", "Planned remainder: alternate views. Implemented subset: plain views, observed in a synthetic check.\n");
        shared.Block("shared.available", "The small retired conversion scope is fully available in the synthetic fixture documentation.\n");
        shared.Append("\n## Industry families\n\n| Family | Source |\n|---|---|\n");
        for (int i = 0; i < familyCount; i++)
        {
            string suffix = i.ToString("00", CultureInfo.InvariantCulture);
            shared.Append("| [Workshop " + suffix + "](catalog/workshop" + suffix + ".md) | Synthetic selection |\n");
        }
        shared.Append("\n## Platform capability areas (1)\n");
        shared.Block("shared.platform", "Synthetic shared boundary: a diagram cannot accept its own change.\n");
        shared.Append("\n| # | Area | Purpose | Owns | Boundary |\n|---|---|---|---|---|\n| 1 | **Sketch** | Prepare diagrams. | Proposed diagram revisions. | \"Keep approval attributable.\" |\n");
        AddShared("Platform capability areas (1)", "shared-capability", "shared.platform", "shared-capability.shared.sketch", "Sketch");

        shared.Append("\n## Product/IP capabilities (1)\n");
        shared.Block("shared.product", "Synthetic graph boundary: a relationship does not convey ownership.\n");
        shared.Append("\n| # | Capability | Role | Works with | Caveat |\n|---|---|---|---|---|\n| 1 | **sequence** | Relate drawings. | Sketch. | \"Preserve each drawing's identity.\" |\n");
        AddShared("Product/IP capabilities (1)", "shared-capability", "shared.product", "shared-capability.shared.sequence", "sequence");

        shared.Append("\n## Adapters (2)\n");
        shared.Block("shared.adapter", "Synthetic conversion boundary: conversion cannot deliver to a device.\n");
        shared.Append("\n| # | Identifier | Role | Consumes and produces | Works with | Caveat |\n|---|---|---|---|---|---|\n" +
            "| 1 | `fixture.shared.adapter` | Transform a sketch. | Consumes lines; produces points. | Sketch. | \"Declare unsupported curves; preserve `a|b` and escaped \\| symbols.\" |\n" +
            "| 2 | `fixture.shared.available` | Copy a point. | Consumes and produces one point. | Sketch. | \"Retain attribution.\" |\n");
        AddShared("Adapters (2)", "adapter", "shared.adapter", "fixture.shared.adapter", "fixture.shared.adapter");
        entries.Add(entries[^1] with { Key = "fixture.shared.available", Name = "fixture.shared.available", State = "available", Status = ["shared.available"] });
        if (familyCount > 0) entries[^2] = entries[^2] with { Boundaries = ["family00.common", "family00.profile"] };

        shared.Append("\n## Planned additions\n");
        shared.Block("shared.additions", "Synthetic addition boundary: a preview has no actuator authority.\n");
        shared.Append("\n| Entry | Area | Status | Where |\n|---|---|---|---|\n" +
            "| Alternate views | Sketch | planned remainder; plain views implemented | roles.md |\n" +
            "| Shared sketch conversion | Sketch | planned reference | existing shared adapter |\n");
        AddShared("Planned additions", "shared-capability", "shared.additions", "shared-capability.shared.views", "Alternate views");
        entries[^1] = entries[^1] with { State = "remainder", Status = ["shared.remainder"] };
        entries.Add(entries[^1] with { Key = "shared-capability.shared.reference", Name = "Shared sketch conversion", State = "reference", Status = ["shared.status"], Owner = "fixture.shared.adapter" });
        Complete(shared);

        for (int i = 0; i < familyCount; i++)
        {
            string suffix = i.ToString("00", CultureInfo.InvariantCulture);
            string id = "family" + suffix;
            string title = "Workshop " + suffix;
            var family = new Source(id, "catalog", "catalog/workshop" + suffix + ".md");
            family.Append("# " + title + "\n");
            family.Block(id + ".status", "All entries here are planned; candidate routes need their own qualification.\n");
            family.Append("\n## Domain Packs\n");
            family.Block(id + ".common", "Reserved acts: the designated person approves every delivery.\nHard lines: never energize an actuator from a draft; no sample contains a real access value.\n");
            family.Append("\n| # | Identifier | Who needs it and why | Works with | Caveat | Journeys |\n|---|---|---|---|---|---|\n" +
                "| 1 | `fixture.workshop" + suffix + ".domain` | Builders prepare measured sketches; added by the builder's draft. | Shared sketch conversion. | \"A measured sketch is not delivery approval.\" | none yet (planned) |\n");
            AddFamily("Domain Packs", "domain", id + ".common", "fixture.workshop" + suffix + ".domain", "fixture.workshop" + suffix + ".domain");
            family.Append("\n## Shared profiles\n");
            family.Block(id + ".profile", "Shared sketch profile: do not add a live route while converting; retain the original units.\n");

            family.Append("\n## Foundries\n");
            family.Block(id + ".foundry", "Synthetic composition boundary: keep existing sketches during setup.\n");
            family.Append("\n| # | Foundry | Who needs it and why | Initializes or adopts | Caveat |\n|---|---|---|---|---|\n" +
                "| 1 | **Sketch desk** | Builders need a review desk. | A desk and its existing sketches. | \"Setup cannot approve delivery.\" |\n");
            AddFamily("Foundries", "foundry", id + ".foundry", "foundry.workshop" + suffix + ".desk", "Sketch desk");

            family.Append("\n## Adapters\n");
            family.Block(id + ".adapter", "Synthetic representation boundary: conversions have declared loss.\n");
            family.Append("\n| # | Identifier | Role | Consumes and produces | Works with | Caveat |\n|---|---|---|---|---|---|\n" +
                "| 1 | `fixture.workshop" + suffix + ".adapter` | Read a measure. | Consumes lines; produces ticks. | Sketch desk. | \"Do not guess missing units.\" |\n");
            AddFamily("Adapters", "adapter", id + ".adapter", "fixture.workshop" + suffix + ".adapter", "fixture.workshop" + suffix + ".adapter");

            family.Append("\n## Provider entries\n\n### Candidate group\n");
            family.Block(id + ".provider", "Candidate-only grouped routes: choose and qualify one concrete operation; observation is read-only.\n");
            family.Append("\n| # | Provider | Role | Works with | Caveat |\n|---|---|---|---|---|\n" +
                "| 1 | **Route Amber / Route Indigo** | Observe a prepared sketch; candidate only. | Sketch desk and shared adapter. | \"Hard lines above apply; observation cannot dispatch.\" |\n");
            AddFamily("Provider entries / Candidate group", "provider", id + ".provider", "provider.workshop" + suffix + ".group", "Route Amber / Route Indigo");
            entries[^1] = entries[^1] with { References = ["prior.shared.adapter"], Boundaries = [id + ".profile"] };
            families.Add(new(title, [id + ".common"]));
            Complete(family);

            void AddFamily(string heading, string kind, string preamble, string key, string name)
            {
                tables.Add(new() { Source = id, Heading = title + " / " + heading, Kind = kind, Family = title, Boundaries = [preamble] });
                entries.Add(new() { Source = id, Heading = title + " / " + heading, Key = key, Name = name, State = "planned", Status = [id + ".status"] });
            }
        }
        int sharedAdapter = entries.FindIndex(e => e.Key == "fixture.shared.adapter");
        entries[sharedAdapter] = entries[sharedAdapter] with { Aliases = ["prior.shared.adapter"] };
        fixture.Index = new() { Sources = [.. pins], Blocks = [.. blocks], Roles = [.. roles], Families = [.. families], Tables = [.. tables], Entries = [.. entries] };
        return fixture;

        void AddShared(string heading, string kind, string preamble, string key, string name)
        {
            tables.Add(new() { Source = "catalog", Heading = "Fixture catalog / " + heading, Kind = kind, Boundaries = [preamble] });
            entries.Add(new() { Source = "catalog", Heading = "Fixture catalog / " + heading, Key = key, Name = name, State = "planned", Status = ["shared.status"] });
        }
        void Complete(Source source)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(source.Text.ToString().Replace("\n", newline, StringComparison.Ordinal));
            fixture.Sources.Add(source.Id, bytes);
            pins.Add(new(source.Id, source.Area, source.Path, GuideGenerator.Hash(bytes)));
            foreach ((string id, int start, int end, string text) in source.Blocks)
                blocks.Add(new(id, source.Id, start, end, GuideGenerator.Hash(Encoding.UTF8.GetBytes(text.Replace("\n", newline, StringComparison.Ordinal)))));
        }
    }

    public void Replace(string source, string oldText, string newText, bool repin = true)
    {
        Sources[source] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Sources[source]).Replace(oldText, newText, StringComparison.Ordinal));
        if (!repin) return;
        string[] lines = Encoding.UTF8.GetString(Sources[source]).Split('\n');
        Index = Index with
        {
            Sources = Index.Sources.Select(s => s.Id == source ? s with { Sha256 = GuideGenerator.Hash(Sources[source]) } : s).ToArray(),
            Blocks = Index.Blocks.Select(b => b.Source == source ? b with
            {
                Sha256 = GuideGenerator.Hash(Encoding.UTF8.GetBytes(string.Concat(lines[(b.StartLine - 1)..b.EndLine].Select(l => l + "\n"))))
            } : b).ToArray(),
        };
    }

    public void WriteInputs(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "home"));
        foreach (GuideSourcePin source in Index.Sources)
        {
            string path = Path.Combine(root, source.Area, source.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Sources[source.Id]);
        }
        File.WriteAllText(Path.Combine(root, "index.json"), JsonSerializer.Serialize(Index, GuideJson.Options), new UTF8Encoding(false));
    }

    private sealed class Source(string id, string area, string path)
    {
        public string Id { get; } = id;
        public string Area { get; } = area;
        public string Path { get; } = path;
        public StringBuilder Text { get; } = new();
        public List<(string Id, int Start, int End, string Text)> Blocks { get; } = [];
        public void Append(string text) => Text.Append(text);
        public void Block(string id, string text)
        {
            int start = Text.ToString().Count(c => c == '\n') + 1;
            Text.Append(text);
            Blocks.Add((id, start, start + text.Count(c => c == '\n') - 1, text));
        }
    }
}
