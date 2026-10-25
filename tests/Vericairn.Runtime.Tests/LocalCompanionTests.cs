// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Reading;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

[CollectionDefinition("Local companion cultures", DisableParallelization = true)]
public sealed class LocalCompanionCultureScope;

[Collection("Local companion cultures")]
[Trait("Journey", "D19-H")]
public class LocalCompanionTests
{
    private const string At = "2026-10-17T12:00:00Z";
    private const string Id = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private static readonly string Hash = "sha256:" + new string('3', 64);

    [Fact]
    public void EveryOwnedLocalKindAndNestedListHasCoverageOrABoundedExclusion()
    {
        LocalCompanions.EnsureRegistered();
        Assert.Empty(DocumentCoverage.Check(LocalCompanions.Inventory, LocalCompanions.Coverage));
        string[] required = ["clones", "devices", "worker-settings", "launchers", "runtime", "certificates", "connections", "write-ledger", "run-state", "retained-context", "recovery"];
        Assert.Equal(required.Order(StringComparer.Ordinal), LocalCompanionSchemas.All.Keys.Order(StringComparer.Ordinal));
        foreach (DocumentManagedKind kind in LocalCompanions.Inventory)
        {
            Assert.Contains(DocumentCoverage.All, d => d.Kind == kind.Id);
            Assert.Single(DocumentCoverage.Check(LocalCompanions.Inventory, LocalCompanions.Coverage.Where(d => d.Kind != kind.Id)));
        }
        foreach (string kind in LocalCompanionSchemas.NoReader.Keys)
        {
            DocumentCoverageDescriptor entry = Assert.Single(LocalCompanions.Coverage, d => d.Kind == "local/" + kind);
            Assert.NotNull(entry.NoReaderReason);
            Assert.Null(entry.Item);
            Assert.Null(entry.List);
            Failed(LocalCompanions.Inspect(kind, JsonSerializer.SerializeToElement(new { }), At), DocumentGenerationStatus.Unavailable);
        }
        string[] excluded = ["supervisor", "processId", "escalationFailure", "problem"];
        Assert.Equal(typeof(RunObservedState).GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order(StringComparer.Ordinal),
            LocalCompanionSchemas.All["run-state"].Select(f => f.Name).Concat(excluded).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SensitivePayloadsAndMachineBindingsNeverReachBytesOrDigest()
    {
        string hidden = "fixture-" + Guid.NewGuid().ToString("N");
        string binding = Path.Combine(Path.GetTempPath(), hidden);
        foreach ((string kind, var schema) in LocalCompanionSchemas.All)
        {
            JsonObject metadata = Example(schema);
            DocumentRendition clean = LocalCompanions.Inspect(kind, Element(metadata), At);
            string text = Text(clean);
            foreach (string field in new[] { "secret", "privateKey", "publicKey", "recoverySecret", "pairingCode", "bearer", "replayCache", "environment", "log", "content", "command", "mcpConfiguration", "path", "root", "workspace", "address" })
                metadata[field] = field is "path" or "root" or "workspace" or "address" ? binding : hidden;
            DocumentRendition withPrivate = LocalCompanions.Inspect(kind, Element(metadata), At);
            Assert.Equal(clean.ContentHash, withPrivate.ContentHash);
            Assert.Equal(clean.InputDigest, withPrivate.InputDigest);
            Assert.Equal(clean.Bytes, withPrivate.Bytes);
            Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
            Assert.DoesNotContain(binding, JsonSerializer.Serialize(withPrivate), StringComparison.Ordinal);
            Assert.Contains("unknown", text, StringComparison.Ordinal);
            foreach (string collection in LocalCompanionSchemas.Collections(schema))
            {
                Assert.Equal(DocumentGenerationStatus.Success, LocalCompanions.Inspect(kind, Element(metadata), At, field: collection).Status);
                Assert.Equal(DocumentGenerationStatus.Success, LocalCompanions.Inspect(kind, Element(metadata), At, field: collection, item: 1).Status);
                Failed(LocalCompanions.Inspect(kind, Element(metadata), At, field: collection, item: 2), DocumentGenerationStatus.Unavailable);
            }
        }
    }

    [Fact]
    public void PathsInMetadataPositionsBecomeUnknownAndEmptyIsNotUnavailable()
    {
        string binding = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string text = Text(LocalCompanions.Inspect("worker-settings", Element(new JsonObject
        {
            ["launcher"] = binding, ["model"] = binding, ["origin"] = binding, ["allowedModels"] = new JsonArray(binding),
        }), At));
        Assert.DoesNotContain(binding, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFileName(binding), text, StringComparison.Ordinal);
        Assert.Contains("None (complete selected metadata list)", Text(LocalCompanions.Inspect("devices", Element(new JsonArray()), At)), StringComparison.Ordinal);
        Assert.Contains("unknown or withheld", Text(LocalCompanions.Inspect("devices", Element(new JsonObject()), At, field: "profiles")), StringComparison.Ordinal);
        Failed(LocalCompanions.Inspect("devices", Element(new JsonArray()), At, complete: false), DocumentGenerationStatus.Unavailable);
        Failed(LocalCompanions.Inspect("devices", Element(new JsonArray()), At, item: 1), DocumentGenerationStatus.Unavailable);
        Failed(LocalCompanions.Inspect("devices", Element(new JsonObject()), At, field: "privateKey"), DocumentGenerationStatus.Unavailable);
        Failed(LocalCompanions.Inspect("devices", Element(new JsonObject()), "unknown"), DocumentGenerationStatus.Error);
        Failed(LocalCompanions.Inspect("devices", Element(new JsonArray("bad item")), At), DocumentGenerationStatus.Error);
    }

    [Fact]
    public void RawOrMalformedMetadataCannotBypassTheProjectionBoundary()
    {
        LocalCompanions.EnsureRegistered();
        IDocumentGenerator generator = DocumentGenerators.Find("vericairn.local/inspectors")!;
        var inputs = new Dictionary<string, string> { ["format"] = "1", ["audience"] = "Internal", ["selection"] = "list", ["field"] = "body", ["index"] = "all", ["page"] = "1" }.ToImmutableDictionary(StringComparer.Ordinal);
        foreach (JsonNode value in new JsonNode[] { new JsonObject(), new JsonObject { ["kind"] = "devices", ["asOf"] = At, ["items"] = new JsonArray(new JsonObject { ["privateKey"] = Guid.NewGuid().ToString("N") }) } })
        {
            var source = DocumentSourceSnapshot.LocalMetadata("devices", CanonicalJson.SerializeUtf8(Element(value)));
            Failed(DocumentRendering.Render(generator, new(1, generator.Descriptor!, inputs, "list", null, [source])), DocumentGenerationStatus.Error);
        }
        Assert.False(generator.Descriptor!.AllowsRepositoryOutput);
        var valid = new JsonObject { ["kind"] = "devices", ["asOf"] = At, ["items"] = new JsonArray(Example(LocalCompanionSchemas.All["devices"])) };
        var context = new DocumentGenerationContext(1, generator.Descriptor, inputs, "list", null, [DocumentSourceSnapshot.LocalMetadata("devices", CanonicalJson.SerializeUtf8(Element(valid)))]);
        Assert.Equal(DocumentGenerationStatus.Success, DocumentRendering.Render(generator, context).Status);
        Failed(DocumentRendering.Render(generator, context with { Parameters = inputs.SetItem("selection", "item") }), DocumentGenerationStatus.Error);
    }

    [Fact]
    public void CloneIdentityObservationsDoNotExposeBindingsOrExceptionMessages()
    {
        using var estate = new TestEstate();
        RecordEnvelope resource = estate.Add("resource", estate.OperatorId, """{"kind":"repository","name":"Example","role":"Fixture","location":{"github":"Example/example"},"visibility":"private"}""");
        string binding = Path.Combine(estate.Root, "owned-clone");
        int inspected = 0;
        DocumentRendition result = LocalCompanions.Clones(estate.Load(), new Dictionary<string, string> { [resource.Id] = binding, [estate.OperatorId] = binding }, (record, path) =>
        {
            Assert.Equal(resource.Reference, record.Reference);
            Assert.Equal(binding, path);
            inspected++;
            return new("mismatch", "available");
        }, At);
        string text = Text(result);
        Assert.Equal(1, inspected);
        Assert.Contains("mismatch", text, StringComparison.Ordinal);
        Assert.Contains("unknown", text, StringComparison.Ordinal);
        Assert.DoesNotContain(binding, text, StringComparison.Ordinal);
        text = Text(LocalCompanions.Clones(estate.Load(), new Dictionary<string, string> { [resource.Id] = binding }, (_, _) => throw new IOException(binding), At));
        Assert.Contains("unreadable", text, StringComparison.Ordinal);
        Assert.DoesNotContain(binding, text, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerAndLauncherChoicesHaveOriginsAndNeverRunAProbe()
    {
        string text = Text(LocalCompanions.Worker(new("choice-a", "standard", ["choice-a", "choice-b"], ["standard"]), "fixture", "operator-configuration", false, null, At));
        Assert.Contains("choice-a", text, StringComparison.Ordinal);
        Assert.Contains("operator-configuration", text, StringComparison.Ordinal);
        Assert.Contains("Compatible: No", text, StringComparison.Ordinal);
        Assert.Contains("Workspace Available: Unknown (no recorded value)", text, StringComparison.Ordinal);
        text = Text(LocalCompanions.Launchers([new MetadataLauncher()], At));
        Assert.Contains("Generic: Yes", text, StringComparison.Ordinal);
        Assert.Contains("Available: Unknown (no recorded value)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RunAndRetainedContextKeepFactsAccountingAndUnknownsWithoutPayloads()
    {
        string hidden = Guid.NewGuid().ToString("N");
        DateTimeOffset at = DateTimeOffset.Parse(At, CultureInfo.InvariantCulture);
        var state = new RunObservedState
        {
            Run = Id, HeartbeatSeconds = 0.5, Heartbeat = At, Heartbeats = 2, Problem = hidden, EscalationFailure = hidden,
            Stop = new(At, "worker", hidden, AcknowledgedAt: At), Signals = [new("execution", "warning", At, hidden)],
            Execution = Element(new JsonObject { ["reports"] = new JsonArray(new JsonObject { ["effort"] = "standard", ["environment"] = hidden }), ["unknown"] = new JsonArray("model", "complete-report") }),
            MinuteTicks = TimeSpan.TicksPerMinute, SettlementPrepared = true,
            Outcome = new(0, at, at, 2, 10, 3, "USD", new(At, "worker", hidden), EscalationFailure: hidden,
                Signals: [new("execution", "warning", At, hidden)]),
            Scope = new(1, 1, [hidden], hidden),
        };
        string text = Text(LocalCompanions.Run(state, At));
        Assert.Contains("Heartbeat Seconds: 0.5", text, StringComparison.Ordinal);
        Assert.Contains("Acknowledged At", text, StringComparison.Ordinal);
        Assert.Contains("complete-report", text, StringComparison.Ordinal);
        Assert.Contains("Settlement Prepared: Yes", text, StringComparison.Ordinal);
        Assert.Contains("Minute Ticks: 600000000", text, StringComparison.Ordinal);
        Assert.Contains("Price: 3", text, StringComparison.Ordinal);
        Assert.Contains("Price Unit: USD", text, StringComparison.Ordinal);
        Assert.Contains("Outside Count: 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
        var payload = Example(LocalCompanionSchemas.All["retained-context"]);
        payload["originals"]![0]!["content"] = hidden;
        payload["originals"]![0]!["path"] = Path.Combine(Path.GetTempPath(), hidden);
        payload["withheld"]![0]!["pin"] = hidden;
        payload["presentation"] = new JsonObject { ["text"] = hidden };
        text = Text(LocalCompanions.Inspect("retained-context", Element(payload), At));
        Assert.Contains(Hash, text, StringComparison.Ordinal);
        Assert.Contains("Byte counts are bytes", text, StringComparison.Ordinal);
        Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
        payload["originals"]![0]!["hash"] = null;
        Assert.Contains("hash not recorded; no current revision substituted", Text(LocalCompanions.Inspect("retained-context", Element(payload), At)), StringComparison.Ordinal);
    }

    [Fact]
    public void CertificateDatesUseExplicitInstantsAndNeverEmitTheCertificateOrKey()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var request = new CertificateRequest("CN=fixture", key, HashAlgorithmName.SHA384);
        DateTimeOffset begins = DateTimeOffset.Parse(At, CultureInfo.InvariantCulture), ends = begins.AddDays(1);
        using X509Certificate2 certificate = request.Create(new X500DistinguishedName("CN=fixture"), X509SignatureGenerator.CreateForECDsa(key), begins, ends, [1, 2, 3]);
        string before = Text(LocalCompanions.Certificate(certificate, RecordParser.FormatTimestamp(ends.AddSeconds(-1))));
        string at = Text(LocalCompanions.Certificate(certificate, RecordParser.FormatTimestamp(ends)));
        Assert.Contains("within-dates", before, StringComparison.Ordinal);
        Assert.Contains("expired", at, StringComparison.Ordinal);
        Assert.Contains("Algorithm: ecdsa", at, StringComparison.Ordinal);
        Assert.DoesNotContain("p256", at, StringComparison.Ordinal);
        Assert.Contains("sha256:" + Convert.ToHexStringLower(certificate.GetCertHash(HashAlgorithmName.SHA256)), at, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(certificate.RawData), at, StringComparison.Ordinal);
    }

    [Fact]
    public void LedgerOperationTemplatesKeepTheOutcomeWithoutRequestArguments()
    {
        var source = Example(LocalCompanionSchemas.All["write-ledger"]);
        source["operation"] = "POST /v1/runs/" + Id + "/stop";
        source["state"] = "interrupted";
        string text = Text(LocalCompanions.Inspect("write-ledger", Element(source), At));
        Assert.Contains("POST /v1/runs/{id}/stop", text, StringComparison.Ordinal);
        Assert.Contains("interrupted", text, StringComparison.Ordinal);
        source["operation"] = "POST " + Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Contains("Operation: Unknown (no recorded value)", Text(LocalCompanions.Inspect("write-ledger", Element(source), At)), StringComparison.Ordinal);
    }

    [Fact]
    public void LocalSnapshotOrderingAndPaginationHaveStableCanonicalBytes()
    {
        JsonObject first = Example(LocalCompanionSchemas.All["devices"]), second = Example(LocalCompanionSchemas.All["devices"]);
        second["id"] = "01ARZ3NDEKTSV4RRFFQ69G5FAW";
        DocumentRendition ordered = LocalCompanions.Inspect("devices", Element(new JsonArray(first.DeepClone(), second.DeepClone())), At);
        DocumentRendition reversed = LocalCompanions.Inspect("devices", Element(new JsonArray(second.DeepClone(), first.DeepClone())), At);
        Assert.Equal(ordered.Bytes, reversed.Bytes);
        CultureInfo oldCulture = CultureInfo.CurrentCulture, oldUi = CultureInfo.CurrentUICulture;
        string? oldZone = Environment.GetEnvironmentVariable("TZ");
        try
        {
            foreach (string locale in new[] { "en-US", "tr-TR", "ar-SA", "ja-JP" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(locale);
                CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
                foreach (string zone in new[] { "UTC", "Pacific/Kiritimati", "America/Los_Angeles" })
                {
                    Environment.SetEnvironmentVariable("TZ", zone);
                    TimeZoneInfo.ClearCachedData();
                    Assert.Equal(ordered.Bytes, LocalCompanions.Inspect("devices", Element(new JsonArray(second.DeepClone(), first.DeepClone())), At).Bytes);
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUi;
            Environment.SetEnvironmentVariable("TZ", oldZone);
            TimeZoneInfo.ClearCachedData();
        }
        var many = new JsonArray(Enumerable.Range(0, 2200).Select(_ => (JsonNode)first.DeepClone()).ToArray());
        string page = Text(LocalCompanions.Inspect("devices", Element(many), At));
        Assert.Contains("Partial page", page, StringComparison.Ordinal);
        Assert.InRange(LocalCompanions.Inspect("devices", Element(many), At, page: 2).Bytes.Length, 1, 1048576);
        Failed(LocalCompanions.Inspect("devices", Element(many), At, page: 100), DocumentGenerationStatus.Unavailable);
    }

    private static JsonObject Example(ImmutableArray<LocalCompanionSchemas.Field> fields)
    {
        var result = new JsonObject();
        foreach (var field in fields)
        {
            if (field.Kind == "object") result[field.Name] = Example(field.Children);
            else if (field.Kind == "list") result[field.Name] = new JsonArray(Example(field.Children));
            else if (field.Kind.EndsWith("[]", StringComparison.Ordinal)) result[field.Name] = new JsonArray(Scalar(field.Kind[..^2]));
            else result[field.Name] = Scalar(field.Kind);
        }
        return result;
    }

    private static JsonValue Scalar(string kind) => kind switch
    {
        "bool" => JsonValue.Create(true), "number" or "signed" => JsonValue.Create(12), "duration" => JsonValue.Create("0.5"),
        "id" or "actor" or "identity" => JsonValue.Create(Id), "hash" => JsonValue.Create(Hash), "time" => JsonValue.Create(At),
        "selector" => JsonValue.Create("fixture"), "repository" => JsonValue.Create("Example/example"), "record-type" => JsonValue.Create("result"),
        "operation" => JsonValue.Create("POST /v1/records"), _ => JsonValue.Create(kind.Split('|')[0]),
    };

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static string Text(DocumentRendition result)
    {
        Assert.Equal(DocumentGenerationStatus.Success, result.Status);
        Assert.NotNull(result.InputDigest);
        Assert.Equal(result.ContentHash, ContentHash.Sha256(result.Bytes.AsSpan()));
        return System.Net.WebUtility.HtmlDecode(Encoding.UTF8.GetString(result.Bytes.AsSpan()));
    }

    private static void Failed(DocumentRendition result, DocumentGenerationStatus status)
    {
        Assert.Equal(status, result.Status);
        Assert.Empty(result.Bytes);
        Assert.Null(result.ContentHash);
        Assert.Null(result.InputDigest);
        Assert.NotNull(result.Reason);
    }

    private sealed class MetadataLauncher : IAgentLauncher
    {
        public string Launcher => "fixture";
        public ProviderDescriptor Descriptor => throw new InvalidOperationException("Not needed for metadata.");
        public bool Generic => true;
        public RunCapabilities ExecutionCapabilities => new(["run"], ["standard"], ["example"], ["local"], ["private"]);
        public IReadOnlyList<string> Command(LaunchContext context) => throw new InvalidOperationException("A reader cannot launch.");
        public string? Verify(IReadOnlyDictionary<string, string> settings, IReadOnlyDictionary<string, string> environment) => throw new InvalidOperationException("A reader cannot probe.");
    }
}
