// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Policies;

internal sealed class PolicyInputException(string message) : Exception(message);
internal sealed class PolicyParameterException(string message) : Exception(message);

internal static class PolicyInputs
{
    internal static JsonElement Get(JsonElement value, string name) => PolicyRecords.Property(value, name);
    internal static string? Text(JsonElement value, string name) => PolicyRecords.Text(value, name);
    internal static IEnumerable<JsonElement> Items(JsonElement value) => PolicyRecords.Items(value);
    internal static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    internal static string Time(DateTimeOffset value) => RecordParser.FormatTimestamp(value);
    internal static string RecordPath(RecordEnvelope record) => "records/" + record.Type + "/" + (record.At.Length >= 7 ? record.At[..7] : "invalid-time") + "/" + record.Id + ".json";

    internal static void Shape(JsonElement value, string[] required, params string[] optional)
    {
        if (value.ValueKind != JsonValueKind.Object || required.Any(k => Get(value, k).ValueKind == JsonValueKind.Undefined) ||
            value.EnumerateObject().Any(p => !required.Contains(p.Name, StringComparer.Ordinal) && !optional.Contains(p.Name, StringComparer.Ordinal)) ||
            value.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() != 1))
            throw new PolicyParameterException("Parameters must use exactly the declared versioned fields: " + string.Join(", ", required.Concat(optional)) + ".");
    }

    internal static string String(JsonElement value, string name)
    {
        string? text = Text(value, name);
        return !string.IsNullOrWhiteSpace(text) ? text : throw new PolicyParameterException("A nonempty string is required for " + name + ".");
    }

    internal static string Choice(JsonElement value, string name, params string[] choices)
    {
        string text = String(value, name);
        return choices.Contains(text, StringComparer.Ordinal) ? text : throw new PolicyParameterException("Unsupported " + name + ".");
    }

    internal static long Integer(JsonElement value, string name, long minimum = 0, long maximum = int.MaxValue)
    {
        if (!Get(value, name).TryGetInt64Safe(out long number) || number < minimum || number > maximum) throw new PolicyParameterException("Invalid integer " + name + ".");
        return number;
    }

    internal static DateTimeOffset Timestamp(JsonElement value, string name) => DutyRecords.Time(Text(value, name)) ?? throw new PolicyParameterException("Invalid UTC timestamp " + name + ".");

    internal static JsonElement[] Array(JsonElement value, string name, bool empty = false)
    {
        JsonElement list = Get(value, name);
        if (list.ValueKind != JsonValueKind.Array || (!empty && list.GetArrayLength() == 0)) throw new PolicyParameterException("A finite array is required for " + name + ".");
        return list.EnumerateArray().ToArray();
    }

    internal static string[] Strings(JsonElement value, string name, bool empty = false)
    {
        JsonElement[] list = Array(value, name, empty);
        if (list.Any(s => s.ValueKind != JsonValueKind.String)) throw new PolicyParameterException("Expected literal strings in " + name + ".");
        string[] strings = list.Select(s => s.GetString()!).ToArray();
        if (strings.Distinct(StringComparer.Ordinal).Count() != strings.Length) throw new PolicyParameterException("Duplicate values in " + name + ".");
        return strings;
    }

    internal static RecordEnvelope Exact(EstateModel model, JsonElement reference)
    {
        string? id = Text(reference, "id");
        if (id is null || !model.ById.TryGetValue(id, out RecordEnvelope? found) || found.Type != Text(reference, "type") || found.ComputedHash != Text(reference, "hash"))
            throw new PolicyInputException("An exact referenced record is missing or its hash differs.");
        return found;
    }

    internal static PolicyRepository Repository(PolicyBasis basis, string identity)
    {
        PolicyRepository[] found = basis.Repositories.Where(r => r.Identity == identity).ToArray();
        if (found.Length != 1) throw new PolicyInputException("The named repository inventory is missing or conflicting: " + identity + ".");
        PolicyRepository repository = found[0];
        if (!repository.Complete || repository.Contract != PolicyBasis.RepositoryContract || string.IsNullOrWhiteSpace(repository.Revision))
            throw new PolicyInputException("The repository inventory is incomplete or its source contract is unsupported: " + identity + ".");
        if (repository.Files.Any(f => !Path(f.Path) || f.IsLink) || repository.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new PolicyInputException("The repository inventory contains unsafe, duplicate or case-colliding paths.");
        return repository;
    }

    internal static string Read(PolicyFile file)
    {
        if (!file.Available || ContentHash.Sha256(file.Bytes.AsSpan()) != file.Hash) throw new PolicyInputException("Source bytes are unavailable or differ from their hash: " + file.Path + ".");
        try { return new UTF8Encoding(false, true).GetString(file.Bytes.AsSpan()); }
        catch (DecoderFallbackException) { throw new PolicyInputException("Source text is not valid UTF-8: " + file.Path + "."); }
    }

    internal static bool Path(string path) => path.Length > 0 && !path.Contains('\\', StringComparison.Ordinal) && !path.Contains(':', StringComparison.Ordinal) &&
        !path.Contains('\0', StringComparison.Ordinal) && path.Split('/').All(p => p is not ("" or "." or ".."));

    internal static void Selectors(IEnumerable<string> selectors)
    {
        foreach (string selector in selectors)
            if (!Path(selector) || selector.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '_' or '-' or '*' or ' ')) || selector.Split('/').Any(p => p != "**" && p.Contains("**", StringComparison.Ordinal)))
                throw new PolicyParameterException("Invalid repository selector.");
    }

    internal static bool Matches(string selector, string path)
    {
        string[] pattern = selector.Split('/');
        string[] parts = path.Split('/');
        var memo = new Dictionary<(int, int), bool>();
        return Match(0, 0);
        bool Match(int p, int s)
        {
            if (memo.TryGetValue((p, s), out bool cached)) return cached;
            bool result = p == pattern.Length ? s == parts.Length : pattern[p] == "**"
                ? Match(p + 1, s) || s < parts.Length && Match(p, s + 1)
                : s < parts.Length && Component(pattern[p], parts[s]) && Match(p + 1, s + 1);
            memo[(p, s)] = result;
            return result;
        }
    }

    private static bool Component(string pattern, string value)
    {
        int p = 0, v = 0, star = -1, start = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && pattern[p] == '*') { star = p++; start = v; }
            else if (p < pattern.Length && pattern[p] == value[v]) { p++; v++; }
            else if (star >= 0) { p = star + 1; v = ++start; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}
