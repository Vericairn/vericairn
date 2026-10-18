// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Reading;

/// <summary>An adapter's clone identity and availability observations, never an exception message or machine binding.</summary>
public sealed record LocalCloneObservation(string Identity, string Availability);

/// <summary>Safe local read models. Adapters supply observed snapshots; this class never opens a home, probes a launcher or reads a clock.</summary>
public static class LocalCompanions
{
    private static readonly LocalCompanionGenerator Generator = new();

    static LocalCompanions()
    {
        DocumentGenerators.Register(Generator);
        DocumentCoverage.Register(Inventory, Coverage);
    }

    /// <summary>Installs local coverage when a runtime host initializes, without reading machine state.</summary>
    public static void EnsureRegistered() { }

    /// <summary>Local metadata and nested lists, with separate bounded exclusions for sensitive payloads.</summary>
    public static ImmutableArray<DocumentManagedKind> Inventory => [.. LocalCompanionSchemas.All.SelectMany(pair =>
        new[] { Kind(pair.Key) }.Concat(LocalCompanionSchemas.Collections(pair.Value).Select(path => Kind(pair.Key + "#" + path)))),
        .. LocalCompanionSchemas.NoReader.Keys.Select(name => Kind(name))];

    /// <summary>Coverage is shipped metadata; it creates no declaration or persisted status ledger.</summary>
    public static ImmutableArray<DocumentCoverageDescriptor> Coverage => [.. Inventory.Select(kind => LocalCompanionSchemas.NoReader.TryGetValue(kind.Id[6..], out string? reason)
        ? new DocumentCoverageDescriptor(1, kind.Id, "", null, null, kind.InputSchema, "Internal", "", reason)
        : new DocumentCoverageDescriptor(1, kind.Id, "Inspect observed local metadata, provenance, availability and unknown values without secret payloads or machine paths.",
            new(Generator.Id, 1), new(Generator.Id, 1), kind.InputSchema, "Internal", "local"))];

    /// <summary>Projects only allowlisted metadata, then renders item or list detail at an explicit observation instant.</summary>
    public static DocumentRendition Inspect(string kind, JsonElement source, string asOf, bool complete = true, int? item = null, int page = 1, string field = "body")
    {
        if (LocalCompanionSchemas.NoReader.ContainsKey(kind)) return Failed(DocumentGenerationStatus.Unavailable, "This payload has an explicit no-reader reason; inspect its metadata instead.");
        if (!LocalCompanionSchemas.All.TryGetValue(kind, out ImmutableArray<LocalCompanionSchemas.Field> schema)) return Failed(DocumentGenerationStatus.Unavailable, "The local inspector is unavailable.");
        if (field != "body" && !LocalCompanionSchemas.Collections(schema).Contains(field, StringComparer.Ordinal)) return Failed(DocumentGenerationStatus.Unavailable, "The selected metadata collection is unavailable.");
        if (!complete) return Failed(DocumentGenerationStatus.Unavailable, "The complete local metadata snapshot is unavailable.");
        if (!RecordParser.IsTimestamp(asOf) || page < 1 || item is < 1 || source.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            return Failed(DocumentGenerationStatus.Error, "The local metadata request is invalid.");
        JsonElement[] items = source.ValueKind == JsonValueKind.Array ? source.EnumerateArray().ToArray() : [source];
        if (items.Any(v => v.ValueKind != JsonValueKind.Object)) return Failed(DocumentGenerationStatus.Error, "The local metadata list is invalid.");
        JsonObject[] projected = items.Select(v => LocalCompanionSchemas.Project(v, schema)).OrderBy(v => Canonical(v), StringComparer.Ordinal).ToArray();
        var payload = new JsonObject { ["kind"] = kind, ["asOf"] = asOf, ["items"] = new JsonArray(projected.Select(v => (JsonNode)v).ToArray()) };
        byte[] bytes = CanonicalJson.SerializeUtf8(JsonSerializer.SerializeToElement(payload));
        var parameters = new Dictionary<string, string>
        {
            ["format"] = "1", ["audience"] = "Internal", ["selection"] = item is null ? "list" : "item",
            ["index"] = item?.ToString(CultureInfo.InvariantCulture) ?? "all", ["page"] = page.ToString(CultureInfo.InvariantCulture), ["field"] = field,
        }.ToImmutableDictionary(StringComparer.Ordinal);
        return DocumentRendering.Render(Generator, new(1, Generator.Descriptor, parameters, item is null ? "list" : "item", null, [DocumentSourceSnapshot.LocalMetadata(kind, bytes)]));
    }

    /// <summary>Validates bindings against the supplied repository identities through an explicit adapter; paths never enter the result.</summary>
    public static DocumentRendition Clones(EstateModel model, IReadOnlyDictionary<string, string> bindings, Func<RecordEnvelope, string, LocalCloneObservation> inspect, string asOf)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(inspect);
        if (model.Findings.HasErrors) return Failed(DocumentGenerationStatus.Unavailable, "The repository identity basis is unavailable.");
        var rows = new JsonArray();
        foreach ((string id, string path) in bindings.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            LocalCloneObservation observation = new("unknown", "unknown");
            if (model.ById.TryGetValue(id, out RecordEnvelope? resource) && resource.Type == "resource" && EstateModel.BodyString(resource, "kind") == "repository")
            {
                try { observation = inspect(resource, path); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { observation = new("unverified", "unreadable"); }
            }
            rows.Add(new JsonObject { ["resource"] = id, ["identity"] = observation.Identity, ["availability"] = observation.Availability });
        }
        return Inspect("clones", JsonSerializer.SerializeToElement(rows), asOf);
    }

    /// <summary>Displays supplied effective worker choices with their origin and observed compatibility; no ambient fallback.</summary>
    public static DocumentRendition Worker(WorkerConfiguration? configuration, string launcher, string origin, bool? compatible, bool? workspaceAvailable, string asOf) =>
        Inspect("worker-settings", JsonSerializer.SerializeToElement(new
        {
            launcher, origin, compatible, workspaceAvailable, model = configuration?.Model, effort = configuration?.Effort,
            allowedModels = configuration?.AllowedModels ?? (configuration is null ? null : [configuration.Model]), allowedEfforts = configuration?.AllowedEfforts,
        }), asOf);

    /// <summary>Declared launcher metadata only. Availability requires a separate explicit observation; no probe is executed here.</summary>
    public static DocumentRendition Launchers(IEnumerable<IAgentLauncher> launchers, string asOf)
    {
        ArgumentNullException.ThrowIfNull(launchers);
        return Inspect("launchers", JsonSerializer.SerializeToElement(launchers.Select(launcher => new
        {
            launcher = launcher.Launcher, contract = IAgentLauncher.Contract, generic = launcher.Generic,
            scopes = launcher.ExecutionCapabilities.Scopes, efforts = launcher.ExecutionCapabilities.Efforts,
            providers = launcher.ExecutionCapabilities.Providers, destinations = launcher.ExecutionCapabilities.Destinations,
            dataBoundaries = launcher.ExecutionCapabilities.DataBoundaries, providerDefault = launcher.ExecutionCapabilities.ProviderDefault,
            closedEnvironment = launcher.ExecutionCapabilities.ClosedEnvironment,
        })), asOf);
    }

    /// <summary>Structured worker observations only; process output, diagnostics and configuration payloads are excluded.</summary>
    public static DocumentRendition Run(RunObservedState state, string asOf)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Inspect("run-state", JsonSerializer.SerializeToElement(state, ProtocolJson.Options), asOf);
    }

    /// <summary>Certificate identity and dates only, evaluated at the supplied instant without accessing private bytes.</summary>
    public static DocumentRendition Certificate(X509Certificate2 certificate, string asOf)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!RecordParser.IsTimestamp(asOf)) return Failed(DocumentGenerationStatus.Error, "The observation instant is invalid.");
        DateTimeOffset at = DateTimeOffset.ParseExact(asOf, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        DateTimeOffset begins = certificate.NotBefore.ToUniversalTime(), ends = certificate.NotAfter.ToUniversalTime();
        return Inspect("certificates", JsonSerializer.SerializeToElement(new
        {
            fingerprint = "sha256:" + Convert.ToHexStringLower(certificate.GetCertHash(HashAlgorithmName.SHA256)),
            notBefore = RecordParser.FormatTimestamp(begins), notAfter = RecordParser.FormatTimestamp(ends),
            validity = at < begins ? "not-yet-valid" : at >= ends ? "expired" : "within-dates", privateMaterialPresent = certificate.HasPrivateKey,
            algorithm = certificate.PublicKey.Oid.Value == "1.2.840.10045.2.1" ? "ecdsa" : certificate.PublicKey.Oid.Value == "1.2.840.113549.1.1.1" ? "rsa" : "other",
        }), asOf);
    }

    internal static string Canonical(JsonNode value) => CanonicalJson.Serialize(JsonSerializer.SerializeToElement(value));
    private static DocumentManagedKind Kind(string name) => new("local/" + name, "vericairn.local-inspection/1#" + name);
    private static DocumentRendition Failed(DocumentGenerationStatus status, string reason) => new(DocumentRendering.Version, status, [], null, null, reason);
}
