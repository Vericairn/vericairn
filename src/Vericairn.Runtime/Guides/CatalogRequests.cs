// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;

namespace Vericairn.Runtime.Guides;

/// <summary>The deliberate request and optional fields shared by the issue form and structured comments.</summary>
public sealed record CatalogRequest(string EntryKey, bool Request, string? IntendedUse = null,
    bool OfferToTest = false, bool OfferToContribute = false, bool NoticeOptIn = false);

/// <summary>A request's admission and a useful public pointer, never an inferred submission.</summary>
public sealed record CatalogRequestValidation(bool Accepted, string Code, string Reference);

/// <summary>A parsed provider form or comment. Ordinary discussion does not produce a request.</summary>
public sealed record CatalogRequestParse(CatalogRequest? Value, string? Error);

/// <summary>Shared request fields and pure admission against an exact public inventory.</summary>
public static class CatalogRequests
{
    /// <summary>The explanation displayed before a deliberate request.</summary>
    public const string Explanation = "A request informs priority and sets no timeline. Only what you enter in the request is collected, with the hosting account attribution needed to count it. Do not include personal or confidential data. Public requests are visible on the hosting platform.";
    private const string RequestLabel = "I request implementation of this entry.";
    private const string TestLabel = "I offer to test.";
    private const string ContributeLabel = "I offer to contribute.";
    private const string NoticeLabel = "I would like status notices.";

    /// <summary>Resolves stable keys only. Optional offers, use text and notice intent are never eligibility requirements.</summary>
    public static CatalogRequestValidation Validate(CatalogInventory inventory, CatalogRequest request)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(request);
        if (!ValidInventory(inventory)) return new(false, "invalid-inventory", "indexes/entries.md");
        CatalogEntry? entry = inventory.Entries.SingleOrDefault(e => e.Key == request.EntryKey);
        return ValidateEntry(entry, request);
    }

    internal static CatalogRequestValidation ValidateEntry(CatalogEntry? entry, CatalogRequest request)
    {
        if (entry is null) return new(false, "unknown-entry", "indexes/entries.md");
        if (!entry.Requestable) return new(false, entry.State == "retired" ? "retired-entry" : "not-requestable", entry.Guide);
        if (!request.Request) return new(false, "request-not-affirmed", entry.Guide);
        if (request.IntendedUse is { Length: > 500 } || request.IntendedUse?.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')) == true)
            return new(false, "invalid-intended-use", entry.Guide);
        return new(true, "accepted", entry.Guide);
    }

    /// <summary>One reusable native form; inventory admission happens in Validate, since a native form cannot enforce it atomically.</summary>
    public static string Form(CatalogInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (!ValidInventory(inventory)) throw new ArgumentException("A valid public inventory is required.", nameof(inventory));
        string placeholder = inventory.Entries.Where(e => e.Requestable).OrderBy(e => e.Key, StringComparer.Ordinal).FirstOrDefault()?.Key ?? "No requestable entries";
        var form = new StringBuilder("name: Request implementation\ndescription: Request planned scope; one vote per hosting account and entry.\ntitle: 'Request implementation'\nbody:\n");
        form.Append("  - type: markdown\n    attributes:\n      value: |\n        ").Append(Explanation).Append('\n')
            .Append("        Check indexes/entries.json and indexes/tracking-issues.json first. For an existing issue, use Add my request in CONTRIBUTING.md. Unknown, retired and nonrequestable keys are refused by the validator.\n")
            .Append("        This form creates an issue, not an atomic vote or an append to another issue. Optional fields are optional. No subscription is enabled by this form.\n")
            .Append("  - type: input\n    id: entry-key\n    attributes:\n      label: Entry key\n      description: Stable requestable key from the entry inventory.\n      placeholder: '").Append(placeholder).Append("'\n    validations:\n      required: true\n");
        Checkbox("request", "Request", RequestLabel, required: true);
        form.Append("  - type: textarea\n    id: intended-use\n    attributes:\n      label: Intended use\n      description: Optional, at most 500 characters. Do not include personal or confidential data.\n    validations:\n      required: false\n");
        Checkbox("offer-to-test", "Offer to test", TestLabel, required: false);
        Checkbox("offer-to-contribute", "Offer to contribute", ContributeLabel, required: false);
        Checkbox("notice-opt-in", "Status notices", NoticeLabel, required: false);
        return form.ToString();

        void Checkbox(string id, string title, string label, bool required) => form.Append("  - type: checkboxes\n    id: ").Append(id)
            .Append("\n    attributes:\n      label: ").Append(title).Append("\n      options:\n        - label: ").Append(label)
            .Append("\n          required: ").Append(required ? "true" : "false").Append('\n');
    }

    /// <summary>Parses exactly the form's headings and checkbox labels. Optional headings may be absent; unknown fields refuse.</summary>
    public static CatalogRequestParse Parse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.Length > 12000) return new(null, "invalid-form");
        string[] titles = ["Entry key", "Request", "Intended use", "Offer to test", "Offer to contribute", "Status notices"];
        var fields = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        StringBuilder? current = null;
        foreach (string line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                string title = line[4..].Trim();
                if (!titles.Contains(title, StringComparer.Ordinal) || !fields.TryAdd(title, new())) return new(null, "invalid-form");
                current = fields[title];
            }
            else if (current is not null) current.Append(line).Append('\n');
            else if (!string.IsNullOrWhiteSpace(line)) return new(null, "invalid-form");
        }
        string Field(string title)
        {
            string value = fields.GetValueOrDefault(title)?.ToString().Trim() ?? "";
            return value == "_No response_" ? "" : value;
        }
        bool? Checked(string title, string label)
        {
            string value = Field(title);
            if (value == "- [x] " + label || value == "- [X] " + label) return true;
            return value == "" || value == "- [ ] " + label ? false : null;
        }
        string key = Field("Entry key");
        bool? request = Checked("Request", RequestLabel), test = Checked("Offer to test", TestLabel),
            contribute = Checked("Offer to contribute", ContributeLabel), notice = Checked("Status notices", NoticeLabel);
        if (key.Length == 0 || !GuideGenerator.SafeKey(key) || !fields.ContainsKey("Request") ||
            request is null || test is null || contribute is null || notice is null) return new(null, "invalid-form");
        return new(new(key, request.Value, Field("Intended use"), test.Value, contribute.Value, notice.Value), null);
    }

    internal static bool ValidInventory(CatalogInventory inventory) => inventory.Format == "vericairn.catalog-inventory/1" &&
        inventory.Entries is { Length: <= 20000 } && inventory.Entries.All(e => e is not null && e.Key is not null && GuideGenerator.SafeKey(e.Key) &&
            e.Name is not null && GuideTemplate.IsRole(e.Kind) && e.State is "planned" or "remainder" or "available" or "retired" &&
            e.Requestable == (e.State is "planned" or "remainder") && e.Guide == "guides/" + e.Kind + "/" + e.Key + ".md") &&
        inventory.Entries.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count() == inventory.Entries.Length;
}
