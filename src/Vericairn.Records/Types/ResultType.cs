// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Records.Types;

/// <summary>What an attempt submits: a claim to inspect, never an accepted state.</summary>
public sealed class ResultType : IRecordType
{
    /// <summary>Allowed outcomes.</summary>
    public static readonly string[] Outcomes = ["completed", "partial", "blocked", "abandoned"];

    /// <summary>Allowed check outcomes.</summary>
    public static readonly string[] CheckOutcomes = ["passed", "failed", "skipped", "not-performed"];

    /// <inheritdoc />
    public string Name => "result";

    /// <inheritdoc />
    public string Summary => "What an attempt submits: outcome, summary, outputs by hash, evidence, checks performed or not, limitations, unresolved external effects and the next permitted step. A claim to inspect; acceptance is a separate record pinned to this exact result.";

    /// <inheritdoc />
    public string? SubjectType => "attempt";

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("outcome", string.Join('|', Outcomes), true, "completed when the brief's outcome was produced; partial when usable work exists but the outcome was not reached; blocked or abandoned otherwise."),
        new("summary", "string", true, "What was done and what changed."),
        new("outputs", "object[]", true, "name, hash (sha256 of the bytes), optional bytes, mediaType and location (repository-relative; verified when the file is present); may be empty."),
        new("evidence", "object[]", true, "summary, optional hash and location; may be empty."),
        new("checks", "object[]", true, "name, outcome (passed|failed|skipped|not-performed), summary; may be empty."),
        new("limitations", "string[]", true, "What the result does not establish; may be empty."),
        new("unresolvedEffects", "string[]", true, "External effects whose outcome is not known and must be reconciled before any retry; may be empty."),
        new("nextStep", "string", true, "The next permitted step for whoever continues, or 'none'."),
        new("duties", "object[]", false, "Exact originating policy, rule, tagged occurrence, examined subject, scope, coverage and supporting proof bindings. This is a signed claim, not acceptance.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        DutyRecords.Results(context);
        string? outcome = body.Enum("outcome", true, Outcomes);
        body.Text("summary", true, 8000);
        bool failedCheck = false;
        foreach (BodyReader output in body.NestedArray("outputs", true, allowEmpty: true) ?? [])
        {
            output.Text("name", true, 300);
            string? hash = output.Hash("hash", true);
            long? bytes = output.Number("bytes", false, 0, 1L << 40);
            output.Text("mediaType", false, 100);
            string? location = output.Text("location", false, 500);
            output.NoOtherKeys();
            VerifyOutput(context, output, hash, bytes, location);
        }

        foreach (BodyReader evidence in body.NestedArray("evidence", true, allowEmpty: true) ?? [])
        {
            evidence.Text("summary", true, 2000);
            evidence.Hash("hash", false);
            evidence.Text("location", false, 500);
            evidence.NoOtherKeys();
        }

        foreach (BodyReader check in body.NestedArray("checks", true, allowEmpty: true) ?? [])
        {
            check.Text("name", true, 300);
            string? checkOutcome = check.Enum("outcome", true, CheckOutcomes);
            check.Text("summary", true, 2000);
            check.NoOtherKeys();
            failedCheck |= checkOutcome == "failed";
        }

        body.StringArray("limitations", true, allowEmpty: true, maxItemLength: 2000);
        body.StringArray("unresolvedEffects", true, allowEmpty: true, maxItemLength: 2000);
        body.Text("nextStep", true, 2000);
        body.NoOtherKeys();
        if (outcome == "completed" && failedCheck)
        {
            // The reviewer's acceptance answers the failed check, through its causal pin or its recorded order.
            RecordEnvelope? acceptance = AcceptanceAfter(context.Model, context.Record);
            if (acceptance is null)
            {
                context.Warning("result.checks", "The result claims completed while a check failed; the reviewer decides whether that is acceptable.");
            }
            else
            {
                string accepted = acceptance.Subject!.Id == context.Record.Id ? "this result" : $"its work order {EstateModel.BodyString(context.Model.ById[acceptance.Subject.Id], "key")}";
                bool self = acceptance.Body.TryGetProperty("self", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;
                context.Findings.Info("result.checks-accepted", context.Location, $"The result claims completed while a check failed, and acceptance {acceptance.Id} accepts {accepted}; it is {(self ? "a self-acceptance" : "not a self-acceptance")}.");
            }
        }

        if (context.Principal(context.Record.By) is null)
        {
            context.Error("result.author", $"Submitting principal {context.Record.By} has no principal record.");
            return;
        }

        RecordEnvelope? attempt = context.Record.Subject is null ? null : context.Find(context.Record.Subject.Id);
        if (attempt is null || attempt.Type != "attempt")
        {
            return;
        }

        if (attempt.By != context.Record.By)
        {
            context.Error("result.contributor", $"A result is submitted by the principal that made the attempt ({attempt.By}), not by {context.Record.By}.");
        }

        if (context.Model.IsCurrent(context.Record) && context.Model.Current(Name).Any(other => other.Id != context.Record.Id && other.Subject?.Id == attempt.Id))
        {
            context.Error("result.duplicate", "Another current result exists for the same attempt; a corrected result supersedes the earlier one instead of standing beside it.");
        }
    }

    /// <summary>
    /// An exact hash pin establishes that the acceptance saw the result, regardless of clocks or identifier order. An acceptance
    /// of its work order instead uses recorded time with the identifier as a deterministic tie-break. The latest qualifying
    /// current acceptance is selected by that same deterministic ordering; null when there is none.
    /// </summary>
    private static RecordEnvelope? AcceptanceAfter(EstateModel model, RecordEnvelope result)
    {
        RecordEnvelope? order = WorkView.OrderOfResult(model, result);
        IReadOnlyList<string> orders = order is null ? [] : model.Chain(model.Latest(order));
        return model.Current("acceptance")
            .Where(a => a.Subject is { } subject &&
                ((subject.Id == result.Id && subject.Hash == result.ComputedHash) ||
                 (subject.Type == "work-order" && orders.Contains(subject.Id, StringComparer.Ordinal) &&
                  (string.CompareOrdinal(a.At, result.At) > 0 || (a.At == result.At && string.CompareOrdinal(a.Id, result.Id) > 0)))))
            .OrderBy(a => a.At, StringComparer.Ordinal).ThenBy(a => a.Id, StringComparer.Ordinal)
            .LastOrDefault();
    }

    private static void VerifyOutput(ValidationContext context, BodyReader output, string? hash, long? bytes, string? location)
    {
        if (location is null)
        {
            return;
        }

        if (Path.IsPathRooted(location) || location.Contains("..", StringComparison.Ordinal) || location.Contains('\\', StringComparison.Ordinal))
        {
            output.Error("result.output-path", "location", "Output locations are repository-relative paths with forward slashes.");
            return;
        }

        string full = Path.Combine(context.Model.Root, location.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full))
        {
            return;
        }

        // A result names the exact bytes it produced. The path may legitimately change later (a manifest is re-pinned,
        // a document is revised), so drift is reported as information; the CLI refuses a mismatch at authoring time.
        if ((hash is not null && ContentHash.Sha256File(full) != hash) || (bytes is not null && new FileInfo(full).Length != bytes.Value))
        {
            context.Findings.Info("result.output-changed", output.Location, $"The file at {location} no longer has the hash or size this result recorded; the result names the bytes it produced, not the path's current content.");
        }
    }
}
