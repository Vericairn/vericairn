// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Structured record prose shared by revision files and paginated readers; no live joins or implicit evaluation.</summary>
internal static class RecordCompanionText
{
    internal static string Notice(string? type) => type switch
    {
        "assignment" or "attempt" or "amendment" or "work-progress" or "result" => "Contributor claim or assigned terms. This is not an observation, independent review or acceptance. Later proofs and completion must be read separately.",
        "review" or "document-review" => "Attributed review of its exact subject revision. A passing review is not acceptance of another revision.",
        "acceptance" => "Recorded acceptance of its exact subject. It does not accept a later revision.",
        "observation" or "verification" or "qualification" or "harness-qualification" => "Recorded evidence within its stated method, scope and limitations. Unknowns and non-claims remain unknown.",
        "operation" => "Recorded intent and observations. A dispatch or transport receipt does not prove the intended effect succeeded.",
        "operator-request" => "An exact requested action and draft. A publication request records intent before effect; it grants no publication authority and dispatches nothing. Read its outcome separately.",
        "operator-request-close" => "A signed outcome for the exact request. An agent's citation preserves the operator's words while retaining the agent as author. This closes no acceptance or decision question.",
        "estate-policy" => "Recorded policy terms, including duty schedules and debt dispositions. Current findings require the policy evaluator and an explicit instant.",
        "policy-exception" => "Recorded grant or revocation terms. Expiry is exclusive; effectiveness requires the policy evaluator at an explicit instant. An exception does not complete work or discharge debt.",
        "writing-exception" => "Persistent exact-token terms and any recorded removal. There is no expiry; current match counts belong to the writing checker.",
        "authority" or "roster" or "principal" => "Recorded identity, routing or grant terms. A role or preference alone grants no permission.",
        "device" or "admission" => "Recorded device and admission facts. Admission is distinct from possession of a key on this machine.",
        "work-order" => "Recorded work definition and typed placement. Derived work status and acceptance are separate reader views.",
        "living-document" or "document-revision" => "Recorded declaration or content pin. Generation equality does not establish authored review or clearance.",
        "possibility" => "Recorded possibility and disposition. Adoption is distinct from permission to execute it.",
        _ => "Recorded facts at this exact revision. Later revisions and current state must be read separately.",
    };

    internal static string Header(DocumentSourceSnapshot source, DocumentGenerationContext context, string prefix = "")
    {
        string title = source.Title == "item" ? Label(source.RecordType ?? "Record") : source.Title!;
        var text = new StringBuilder("# ").Append(DocumentMarkdown.Text(title)).Append("\n\n");
        text.Append("Recorded revision; Internal. Generator: ").Append(DocumentMarkdown.Text(context.Generator.Id))
            .Append("; contract ").Append(context.Generator.ContractVersion.ToString(CultureInfo.InvariantCulture)).Append(".\n\n")
            .Append("Source: [").Append(DocumentMarkdown.Text(source.RecordType + " " + source.Identity)).Append("](").Append(prefix).Append(source.Path)
            .Append(") at ").Append(source.Hash).Append(".\n\n")
            .Append("Recorded at: ").Append(DocumentMarkdown.Timestamp(DateTimeOffset.ParseExact(source.At!, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)))
            .Append(". Author: ").Append(source.Author).Append(". Record format: ").Append(source.RecordFormat!.Value.ToString(CultureInfo.InvariantCulture)).Append(".\n\n")
            .Append("Input digest: ").Append(context.InputDigest).Append(".\n\n").Append(Notice(source.RecordType)).Append("\n\n")
            .Append("Inspect this exact revision with `vericairn records show ").Append(source.Identity).Append("`; use the owning reader for current state.\n\n");
        if (source.Subject is { } subject) text.Append("Subject: ").Append(DocumentMarkdown.Text(subject.Type + " " + subject.Id))
            .Append(" at ").Append(subject.Hash ?? "hash not recorded").Append(".\n\n");
        if (source.Supersedes is { } previous) text.Append("Predecessor revision: ").Append(previous)
            .Append(". Inspect with `vericairn records show ").Append(previous).Append("`; no current label is inferred.\n\n");
        return text.ToString();
    }

    internal static IEnumerable<string> Body(DocumentSourceSnapshot source)
    {
        IRecordType type = RecordTypeRegistry.Find(source.RecordType!)!;
        JsonElement body = source.Body!.Value;
        foreach (FieldDescription field in type.Fields.Where(f => f.SinceFormat <= source.RecordFormat))
        {
            // Preserve earlier rendition bytes when the additive prompt-use field was not recorded.
            if (field.Name == "promptUse" && !body.TryGetProperty(field.Name, out _)) continue;
            yield return "## " + Label(field.Name) + "\n\n" + DocumentMarkdown.Text(field.Description) + "\n\n";
            if (body.TryGetProperty(field.Name, out JsonElement value))
                foreach (string block in Value(value, field.Name)) yield return block;
            else yield return "Not recorded in this revision. Absence supplies no inferred value or permission.\n\n";
        }
        if (source.RecordType == "work-order" && !body.TryGetProperty("placement", out _)) yield return "No typed placement recorded.\n\n";
    }

    internal static bool KnownFields(DocumentSourceSnapshot source) => source.Body is { } body
        && RecordTypeRegistry.Find(source.RecordType!) is { } type
        && body.EnumerateObject().All(p => type.Fields.Any(f => f.Name == p.Name && f.SinceFormat <= source.RecordFormat));

    internal static IEnumerable<string> Value(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (!value.EnumerateObject().Any()) { yield return "- " + DocumentMarkdown.Text(Label(path)) + ": No fields recorded.\n\n"; yield break; }
            if (value.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String
                && value.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String)
            {
                yield return "- " + DocumentMarkdown.Text(Label(path)) + ": Exact record reference " + DocumentMarkdown.Text(type.GetString() + " " + id.GetString())
                    + " at " + (value.TryGetProperty("hash", out JsonElement hash) && hash.ValueKind == JsonValueKind.String ? DocumentMarkdown.Text(hash.GetString()!) : "hash not recorded; no current revision substituted") + ".\n\n";
                // Keep any additional fields: a reference-shaped object is not permission to drop data.
                foreach (JsonProperty extra in value.EnumerateObject().Where(p => p.Name is not ("type" or "id" or "hash")).OrderBy(p => p.Name, StringComparer.Ordinal))
                    foreach (string block in Value(extra.Value, path + "." + extra.Name)) yield return block;
                yield break;
            }
            if (path is "budget" or "cap") yield return "Caps are grants by unit. An absent unit is not unlimited. Currency amounts are minor units of the named currency.\n\n";
            if (path == "usage") yield return "Reported usage retains its units. Unreported units are unknown and charged at the cap, never counted as zero.\n\n";
            foreach (JsonProperty property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                foreach (string block in Value(property.Value, path + "." + property.Name)) yield return block;
            yield break;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() == 0) yield return "- " + DocumentMarkdown.Text(Label(path)) + ": None recorded (complete selected list).\n\n";
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
                foreach (string block in Value(item, path + "[" + (++index).ToString(CultureInfo.InvariantCulture) + "]")) yield return block;
            yield break;
        }
        string raw = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Null or JsonValueKind.Undefined => "Unknown (no recorded value)",
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            _ => value.GetRawText(),
        };
        string meaning = Meaning(path, raw);
        bool continued = false;
        foreach (string chunk in Chunks(raw))
        {
            yield return "- " + DocumentMarkdown.Text(Label(path)) + (continued ? " (continued)" : "") + ": "
                + DocumentMarkdown.Text(chunk) + meaning + "\n\n";
            continued = true;
        }
    }

    private static string Meaning(string path, string value)
    {
        if (value == "unknown") return " (unknown; not inferred from a request or treated as zero)";
        string field = path.Split('.').Last();
        return field switch
        {
            "bytes" or "maxBytes" => " (UTF-8 bytes where the source describes text; not tokens)",
            "wallMinutes" => " (minutes)",
            "tokens" or "inputTokens" or "outputTokens" => " (tokens reported or capped by the named source)",
            "amount" => " (minor units of the recorded currency)",
            "stallSeconds" => " (seconds)",
            "graceDays" or "leadDays" or "maxExceptionDays" => " (days)",
            "expiresAt" => " (exclusive UTC expiry)",
            _ => "",
        };
    }

    // Chunk before escaping so pagination cannot split a Unicode scalar or a Markdown entity.
    private static IEnumerable<string> Chunks(string text)
    {
        var chunk = new StringBuilder();
        foreach (Rune rune in text.Replace("\r\n", "\n", StringComparison.Ordinal).EnumerateRunes())
        {
            if (rune.Value == '\n' || chunk.Length >= 4096)
            {
                yield return chunk.ToString();
                chunk.Clear();
                if (rune.Value == '\n') continue;
            }
            chunk.Append(rune.ToString());
        }
        yield return chunk.ToString();
    }

    internal static string Label(string name)
    {
        var text = new StringBuilder();
        bool start = true;
        foreach (char c in name)
        {
            if (c == '.') { text.Append(" / "); start = true; }
            else if (c == '-') text.Append(' ');
            else { if (char.IsAsciiLetterUpper(c) && text.Length > 0 && !start) text.Append(' '); text.Append(start ? char.ToUpperInvariant(c) : c); start = false; }
        }
        if (text.Length > 0) text[0] = char.ToUpperInvariant(text[0]);
        return text.ToString();
    }
}
