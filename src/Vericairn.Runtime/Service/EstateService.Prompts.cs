// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public PromptsResponse Prompts(PromptQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (gate) return PromptsLocked(session.Load(), query);
    }

    private PromptsResponse PromptsLocked(EstateModel model, PromptQuery query)
    {
        if (RequestDeviceRefusal(model) is { } denied) return new([], denied);
        if (query.Person is not null && model.CurrentPrincipal(query.Person) is null)
            return new([], new("not-found", "The prompt recipient is not a known principal.", []));
        if (query.Role is not null && !Roles.All.Contains(query.Role, StringComparer.Ordinal))
            return new([], new("refused", "Select a known principal role.", []));
        if (model.Findings.HasErrors)
            return new([], new("invalid-estate", "Reconcile invalid or divergent estate records before using the prompt library.", []));

        var rows = Providers.Prompts.All.ToList();
        rows.AddRange(model.Current("saved-prompt").Select(r => new PromptRow("estate:" + EstateModel.BodyString(r, "key"), "estate",
            EstateModel.BodyString(r, "key")!, r.Id, r.ComputedHash, r.Body.Clone(), Retired: EstateModel.BodyString(r, "state") == "retired")));
        RecordEnvelope[] preferences = model.Current("prompt-preference").ToArray();
        IEnumerable<PromptRow> selected = rows.Select(row =>
        {
            RecordEnvelope? personal = preferences.SingleOrDefault(p => EstateModel.BodyString(p, "prompt") == row.Identity && EstateModel.BodyString(p, "scope") == "person" && EstateModel.BodyString(p, "person") == query.Person);
            RecordEnvelope? estate = preferences.SingleOrDefault(p => EstateModel.BodyString(p, "prompt") == row.Identity && EstateModel.BodyString(p, "scope") == "estate");
            return row with { Favorite = Flag(personal, "favorite"), Hidden = Flag(personal, "hidden"), Disabled = Flag(estate, "disabled") };
        });
        selected = selected.Where(p => (query.IncludeUnavailable || !p.Hidden && !p.Disabled && !p.Retired)
            && (!query.Favorites || p.Favorite)
            && (query.Category is null || Category(p) == query.Category || Category(p).StartsWith(query.Category + "/", StringComparison.Ordinal))
            && Suits(model, p, query.Person, query.Role));
        return new(selected.OrderBy(Category, StringComparer.Ordinal).ThenByDescending(p => p.Favorite)
            .ThenBy(p => p.Definition.GetProperty("title").GetString(), StringComparer.Ordinal).ThenBy(p => p.Identity, StringComparer.Ordinal).ToArray());
    }

    private static bool Flag(RecordEnvelope? record, string field) => record is not null && record.Body.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    private static string Category(PromptRow row) => row.Definition.GetProperty("category").GetString()!;
    private static bool Suits(EstateModel model, PromptRow row, string? person, string? role)
    {
        string[] roles = row.Definition.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray();
        return (roles.Length == 0 || (role is null || roles.Contains(role, StringComparer.Ordinal))
            && (person is null || roles.Any(r => model.HasRole(person, r))));
    }

    /// <inheritdoc />
    public WriteOutcome PromptSave(PromptSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (session.Manifest.RecordFormat != 3) return Refuse("Saved prompts require record format 3; no format migration is performed.");
            if (request.Definition.ValueKind != JsonValueKind.Object || !request.Definition.TryGetProperty("key", out JsonElement key) || key.ValueKind != JsonValueKind.String)
                return Refuse("Supply a complete estate prompt definition with its stable key.");
            RecordEnvelope[] previous = model.Current("saved-prompt").Where(r => EstateModel.BodyString(r, "key") == key.GetString()).ToArray();
            if (previous.Length > 1 || (previous.Length == 0 ? request.Expected is not null : previous[0].ComputedHash != request.Expected))
                return WriteOutcome.Fail(new("conflict", "The shared prompt changed; read its current revision and submit its expected hash.", []));
            return WriteLocked(new RecordDraft("saved-prompt", request.By, request.Definition, Supersedes: previous.SingleOrDefault()?.Id), model);
        }
    }

    /// <inheritdoc />
    public WriteOutcome PromptCopy(PromptCopyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            PromptsResponse library = PromptsLocked(session.Load(), new(IncludeUnavailable: true));
            if (library.Problem is { } problem) return WriteOutcome.Fail(problem);
            PromptRow? source = library.Prompts.SingleOrDefault(p => p.Identity == request.Prompt);
            if (source is null || source.Source == "estate") return Refuse("Copy names an installed foundation or pack prompt.");
            if (source.Hash != request.Expected) return WriteOutcome.Fail(new("conflict", "The source definition changed; inspect its exact hash before copying.", []));
            if (source.Draft) return Refuse("This built-in is a draft awaiting operator review; it cannot be copied or used yet.");
            JsonObject body = JsonNode.Parse(source.Definition.GetRawText())!.AsObject();
            body["key"] = request.Key;
            body["state"] = "active";
            body["origin"] = new JsonObject { ["source"] = source.Source, ["key"] = source.Key, ["version"] = source.Version, ["hash"] = source.Hash };
            return PromptSave(new(request.By, Element(body)));
        }
    }

    /// <inheritdoc />
    public WriteOutcome PromptPreference(PromptPreferenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (session.Manifest.RecordFormat != 3) return Refuse("Prompt preferences require record format 3.");
            if (request.Disabled is not null && (request.Hidden is not null || request.Favorite is not null))
                return Refuse("Change the estate disable separately from a personal preference.");
            if (request.Disabled is null && request.Hidden is null && request.Favorite is null) return Refuse("Name a preference to change.");
            PromptsResponse library = PromptsLocked(model, new(IncludeUnavailable: true));
            if (library.Problem is { } problem) return WriteOutcome.Fail(problem);
            if (library.Prompts.All(p => p.Identity != request.Prompt)) return NotFound("No installed or estate prompt has that identity.");
            string scope = request.Disabled is not null ? "estate" : "person";
            RecordEnvelope[] previous = model.Current("prompt-preference").Where(r => EstateModel.BodyString(r, "prompt") == request.Prompt
                && EstateModel.BodyString(r, "scope") == scope && (scope == "estate" || EstateModel.BodyString(r, "person") == request.By)).ToArray();
            if (previous.Length > 1 || request.Expected is not null && previous.SingleOrDefault()?.ComputedHash != request.Expected)
                return WriteOutcome.Fail(new("conflict", "The preference changed; inspect its current revision.", []));
            var body = new JsonObject { ["prompt"] = request.Prompt, ["scope"] = scope };
            if (scope == "estate") body["disabled"] = request.Disabled;
            else
            {
                body["person"] = request.By;
                body["hidden"] = request.Hidden ?? Flag(previous.SingleOrDefault(), "hidden");
                body["favorite"] = request.Favorite ?? Flag(previous.SingleOrDefault(), "favorite");
            }
            return WriteLocked(new RecordDraft("prompt-preference", request.By, Element(body), Supersedes: previous.SingleOrDefault()?.Id), model);
        }
    }

    private (JsonObject? Use, Problem? Problem) PreparePrompt(EstateModel model, string by, PromptSelection selection, string? recipient = null)
    {
        if (session.Manifest.RecordFormat != 3) return (null, new("refused", "Prompt uses require record format 3.", []));
        PromptsResponse library = PromptsLocked(model, new(by, IncludeUnavailable: true));
        if (library.Problem is { } problem) return (null, problem);
        PromptRow? row = library.Prompts.SingleOrDefault(p => p.Identity == selection.Prompt);
        if (row is null) return (null, new("not-found", "The prompt is unavailable for this principal's roles.", []));
        if (row.Hash != selection.Expected) return (null, new("conflict", "The prompt changed; inspect its current revision or version before use.", []));
        if (row.Disabled || row.Retired || row.Draft) return (null, new("refused", "This prompt is disabled, retired or a draft awaiting operator review.", []));
        if (recipient is not null && !Suits(model, row, recipient, null)) return (null, new("refused", "The prompt does not suit the receiving principal's roles.", []));
        try
        {
            string rendered = SavedPrompts.Render(row.Definition, selection.Values);
            return (new JsonObject
            {
                ["source"] = row.Source, ["key"] = row.Key, ["version"] = row.Version, ["hash"] = row.Hash,
                ["definition"] = JsonNode.Parse(row.Definition.GetRawText()), ["values"] = JsonSerializer.SerializeToNode(selection.Values, ProtocolJson.Options),
                ["rendered"] = rendered, ["authority"] = "unchanged",
            }, null);
        }
        catch (ArgumentException exception) { return (null, new("refused", exception.Message, [])); }
    }

    /// <inheritdoc />
    public PromptUseResponse PromptUse(PromptUseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Favor)
        {
            PromptUseResponse used = PromptUse(request with { Favor = false });
            if (!used.Ok) return used;
            WriteOutcome favored = PromptPreference(new(request.By, request.Selection.Prompt, Favorite: true));
            return favored.Ok ? used : used with { Ok = false, Problem = new("favorite-incomplete",
                $"Use {used.Written?.Id} is recorded. Its favorite change did not complete: {favored.Problem?.Message} Retry only the favorite change, never the use.", favored.Problem?.Findings ?? []) };
        }
        if (request.Selection is null || (request.Assignment is null ? 0 : 1) + (request.Run is null ? 0 : 1) + (request.Ask is null ? 0 : 1) != 1)
            return new(false, Problem: new("refused", "Choose exactly one new assignment, new run or ask answer. Changes to outcome, target, budget, disclosure or permitted effects require an amendment.", []));
        if (request.Assignment is { } assignment)
        {
            if (assignment.By != request.By || assignment.Prompt is not null) return PromptAuthorRefusal();
            WriteOutcome outcome = Assign(assignment with { Prompt = request.Selection });
            return PromptOutcome(outcome);
        }
        if (request.Run is { } run)
        {
            if (run.By != request.By || run.Prompt is not null) return PromptAuthorRefusal();
            RunResponse outcome = RunStart(run with { Prompt = request.Selection });
            return new(outcome.Ok, outcome.Ok && outcome.Operation is { } written ? PromptText(written.Id) : null, outcome.Operation, outcome, outcome.Problem);
        }
        lock (gate)
        {
            EstateModel model = session.Load();
            if (!model.ById.TryGetValue(request.Ask!, out RecordEnvelope? ask) || ask.Type != "ask" || !model.IsCurrent(ask)
                || EstateModel.BodyString(ask, "state") == "withdrawn" || EstateModel.BodyString(ask, "to") != request.By
                || model.Current("decision").Any(d => d.Body.TryGetProperty("answers", out JsonElement answers) && answers.EnumerateArray().Any(a => a.GetString() == ask.Id)))
                return new(false, Problem: new("refused", "Answer an open current ask addressed to the acting principal; an existing answer cannot be replaced by a prompt use.", []));
            (JsonObject? use, Problem? problem) = PreparePrompt(model, request.By, request.Selection);
            if (problem is not null) return new(false, Problem: problem);
            string title = "Prompt answer: " + use!["definition"]!["title"]!.GetValue<string>();
            var body = new JsonObject
            {
                ["title"] = title[..Math.Min(title.Length, 200)], ["statement"] = use["rendered"]!.DeepClone(),
                ["summary"] = "Answer the named ask with the selected prompt, within existing authority.", ["synthesis"] = "The recorded prompt text answers this ask without changing the work's terms.",
                ["effects"] = new JsonArray("Answer the named ask; existing authority is unchanged."), ["affects"] = new JsonArray(),
                ["decidedBy"] = request.By, ["decidedAt"] = RecordParser.FormatTimestamp(DateTimeOffset.UtcNow), ["stake"] = "low",
                ["notAuthorized"] = new JsonArray("Changes to outcome, target, budget, data disclosure or permitted effects; request an amendment."),
                ["answers"] = new JsonArray(ask.Id), ["promptUse"] = use,
            };
            return PromptOutcome(WriteLocked(new RecordDraft("decision", request.By, Element(body)), model));
        }
    }

    private static PromptUseResponse PromptAuthorRefusal() => new(false, Problem: new("refused", "The target must name the same acting author and a single prompt selection.", []));
    private PromptUseResponse PromptOutcome(WriteOutcome outcome) => new(outcome.Ok, outcome.Ok ? PromptText(outcome.Written!.Id) : null, outcome.Written, Problem: outcome.Problem);
    private string? PromptText(string id)
    {
        lock (gate) return session.Load().ById.TryGetValue(id, out RecordEnvelope? record) && record.Body.TryGetProperty("promptUse", out JsonElement use) ? SavedPrompts.Addendum(use) : null;
    }

    private string? PromptWriteRefusal(RecordDraft draft, EstateModel model)
    {
        if (draft.Type == "saved-prompt" && !SavedPrompts.MayWrite(model, draft.By, DateTimeOffset.UtcNow))
            return "Writing shared prompts requires current operator authority or an explicit signed manage-prompts grant without unproven conditions.";
        if (!draft.Body.TryGetProperty("promptUse", out JsonElement use)) return null;
        if (draft.Type is "operation" or "decision" && draft.Supersedes is { } previous && model.ById.TryGetValue(previous, out RecordEnvelope? prior)
            && SavedPrompts.SameField(prior.Body, draft.Body, "promptUse")) return null;
        try
        {
            string identity = use.GetProperty("source").GetString() + ":" + use.GetProperty("key").GetString();
            var selection = new PromptSelection(identity, use.GetProperty("hash").GetString()!,
                use.GetProperty("values").Deserialize<Dictionary<string, string>>(ProtocolJson.Options)!);
            string? recipient = draft.Type == "assignment" ? draft.Body.GetProperty("contributor").GetString()
                : draft.Type == "operation" ? draft.Body.GetProperty("target").GetProperty("principal").GetString() : null;
            (JsonObject? prepared, Problem? problem) = PreparePrompt(model, draft.By, selection, recipient);
            if (problem is not null) return problem.Message;
            return JsonNode.DeepEquals(prepared, JsonNode.Parse(use.GetRawText())) ? null
                : "The use differs from the exact offered prompt and values. Prompts cannot change authority; request an amendment for changed terms.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or JsonException or ArgumentException)
        {
            return "Supply a complete exact prompt selection. Outcome, target, budget, disclosure and permitted-effect changes require an amendment.";
        }
    }
}
