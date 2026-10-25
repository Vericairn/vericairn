// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Updates;

namespace Vericairn.Records.Types;

/// <summary>Something reported or measured about an exact subject, with an as-of time; not health unless it says so.</summary>
public sealed class ObservationType : IRecordType
{
    /// <summary>What kinds of subject are observed so far.</summary>
    public static readonly ImmutableArray<string> Kinds = ["repository", "release", "workflow-run", "estate", "package", "vericairn-release", "operator-request"];

    /// <inheritdoc />
    public string Name => "observation";

    /// <inheritdoc />
    public string Summary => "Something reported or measured about an exact subject with an as-of time: a repository, release or workflow run; from record format 2, a person's explicit observation of an estate; from format 3, package registry state with response evidence. Facts, never permission or health.";

    /// <inheritdoc />
    public string? SubjectType => null;

    /// <inheritdoc />
    public bool SubjectHashRequired => true;

    /// <inheritdoc />
    public IReadOnlyList<FieldDescription> Fields { get; } =
    [
        new("kind", string.Join('|', Kinds), true, "What was observed; estate requires record format 2, a human author and an exact estate subject."),
        new("asOf", "timestamp", true, "When the observation was made."),
        new("summary", "string", true, "The observation in a sentence."),
        new("facts", "object", true, "Flat facts: strings, numbers, booleans or arrays of strings; never machine paths. An estate observation requires records (count), recordSetHash, pinHash and statusHash. An operator-request observation requires outcome (done), destination, location and evidenceHash, and its signed exact request subject. Format-3 package facts pin provider, product, productHash, package and version, with either release and releaseHash or reserved (the same version declared reserved in that exact product, with no release pins). The subject is the exact release, reserved product declaration or original account-holder intent."),
        new("limitations", "string[]", true, "What the observation does not establish; may be empty."),
        new("evidence", "object[]", false, "For package observations: exact public registry url, response-body hash, HTTP status, byte count and content encoding for each response; hashes cover bytes before gzip decoding.", 3),
    ];

    /// <inheritdoc />
    public void Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BodyReader body = context.Body;
        string? kind = body.Enum("kind", true, context.Format >= 3 ? Kinds : context.Format >= 2 ? ["repository", "release", "workflow-run", "estate", "vericairn-release"] : ["repository", "release", "workflow-run", "vericairn-release"]);
        string? asOf = body.Timestamp("asOf", true);
        body.Text("summary", true, 4000);
        BodyReader? facts = body.Nested("facts", true);
        if (kind == "operator-request") OperatorRequestRules.Observation(context, facts, asOf);
        if (facts is not null)
        {
            if (kind == "vericairn-release") VericairnReleaseObservation.Validate(context, facts);
            if (kind == "estate")
            {
                facts.Number("records", true, min: 0);
                facts.Hash("recordSetHash", true);
                facts.Hash("pinHash", true);
                facts.Hash("statusHash", true);
            }

            foreach (JsonProperty property in facts.Element.EnumerateObject())
            {
                facts.Allow(property.Name);
                bool ok = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString()!.Length <= 2000,
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => true,
                    JsonValueKind.Array => property.Value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String && v.GetString()!.Length <= 2000),
                    _ => false,
                };
                if (!ok)
                {
                    facts.Error("body.type", property.Name, "Facts are strings, numbers, booleans or arrays of strings.");
                }
            }

            facts.NoOtherKeys();
        }

        body.StringArray("limitations", true, allowEmpty: true, maxItemLength: 2000);
        PackageRecordRules.Observation(context, kind, facts);
        body.NoOtherKeys();
        if (context.Principal(context.Record.By) is null)
        {
            context.Error("observation.author", $"Observing principal {context.Record.By} has no principal record.");
        }

        if (kind == "estate")
        {
            if (context.Principal(context.Record.By) is not { } person || EstateModel.BodyString(person, "kind") != "human")
            {
                context.Error("observation.person", "An estate observation is authored by a person; automation cannot advance a person's last observation.");
            }

            if (context.Record.Subject?.Type != "estate")
            {
                context.Error("observation.estate", "An estate observation pins an estate charter as its subject.");
            }

            if (asOf is not null && string.CompareOrdinal(asOf, context.Record.At) > 0)
            {
                context.Error("observation.future", "An estate observation cannot report seeing a future state.");
            }
        }
    }
}
