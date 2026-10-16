// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;

namespace Vericairn.Runtime.Service;

public sealed partial class EstateService
{
    /// <inheritdoc />
    public WriteOutcome ObserveEstate(EstateObserveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            EstateModel model = session.Load();
            if (model.Manifest?.RecordFormat < 2)
            {
                return Refuse("An estate observation needs record format 2; explicitly upgrade the estate before recording one.");
            }

            if (model.CurrentPrincipal(request.By) is not { } person || EstateModel.BodyString(person, "kind") != "human")
            {
                return Refuse("Only a person may record that they observed the estate; automation cannot advance this timestamp.");
            }

            if (model.CurrentEstate is not { } charter)
            {
                return Refuse("No estate charter is available to observe.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            JsonElement identities = JsonSerializer.SerializeToElement(model.Records.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new { r.Id, Hash = r.ComputedHash }), ProtocolJson.Options);
            using JsonDocument status = JsonDocument.Parse(EstateStatus.RenderJson(model, ChecksWithBatches(model)));
            var body = new JsonObject
            {
                ["kind"] = "estate",
                ["asOf"] = RecordParser.FormatTimestamp(now),
                ["summary"] = "A person explicitly observed the estate's status.",
                ["facts"] = new JsonObject
                {
                    ["records"] = model.Records.Count,
                    ["recordSetHash"] = ContentHash.Sha256(CanonicalJson.SerializeUtf8(identities)),
                    ["pinHash"] = ContentHash.Sha256(CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(model.Manifest, ProtocolJson.Options))),
                    ["statusHash"] = ContentHash.Sha256(CanonicalJson.SerializeUtf8(status.RootElement)),
                },
                ["limitations"] = new JsonArray("Recorded status as observed, not live external health, approval or acceptance."),
            };
            return WriteLocked(new RecordDraft("observation", request.By, JsonSerializer.SerializeToElement(body, ProtocolJson.Options), charter.Reference, At: now), model);
        }
    }

    /// <inheritdoc />
    public ResumeResponse ResumeHandoff(HandoffPackage package)
    {
        lock (gate)
        {
            IReadOnlyList<RecordFile> files = session.Files();
            EstateModel model = EstateLoader.Load(session.Root, files);
            foreach (var finding in InterruptedBatches(model))
            {
                model.Findings.Error(finding.Code, finding.Location, finding.Message);
            }

            return HandoffComparison.Compare(package, model, files, session.Manifest.Estate);
        }
    }

    /// <inheritdoc />
    public HandoffResponse Handoff(long? maxBytes = null)
    {
        lock (gate)
        {
            // Load and retain precisely one file set: a concurrent sync cannot mix two revisions of the copy.
            IReadOnlyList<RecordFile> files = session.Files();
            EstateModel model = EstateLoader.Load(session.Root, files);
            foreach (var finding in InterruptedBatches(model))
            {
                model.Findings.Error(finding.Code, finding.Location, finding.Message);
            }

            return HandoffAssembly.Build(model, files, maxBytes);
        }
    }
}
