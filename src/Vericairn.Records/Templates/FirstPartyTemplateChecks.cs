// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;

namespace Vericairn.Records.Templates;

/// <summary>Installed checks retain their versioned names with their template contracts.</summary>
internal sealed class FirstPartyTemplateChecks : ITemplateCheck
{
    internal static readonly IReadOnlyList<ITemplateCheck> All = [new FirstPartyTemplateChecks()];
    public string Id => "vericairn.estate/roadmap-placements-v1";

    public TemplateCheckResult Check(string content, IReadOnlyList<TemplateSource> sources)
    {
        TemplateSource? source = sources.SingleOrDefault(s => s.Id == "work-orders");
        if (source?.Content is null || source.Status != "available")
            return new(Id, "not-performed", "The complete current work-order selection is unavailable.");
        var work = new List<JsonElement>();
        try
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(source.Content), new JsonReaderOptions { AllowMultipleValues = true });
            while (reader.Read())
            {
                using JsonDocument json = JsonDocument.ParseValue(ref reader);
                work.Add(json.RootElement.Clone());
            }
            if (work.Count != source.Records.Count) return new(Id, "not-performed", "The work-order source is incomplete.");
            return new(Id, content == FirstPartyTemplateGenerators.RoadmapRows(work) ? "passed" : "failed",
                "Every release placement must occur exactly once under its recorded product, version and carrier; backlog and unplaced work cannot appear in release rows.");
        }
        catch (JsonException) { return new(Id, "not-performed", "The complete work-order source could not be read."); }
    }
}
