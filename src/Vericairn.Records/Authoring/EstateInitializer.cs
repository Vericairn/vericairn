// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jayson Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Records.Authoring;

/// <summary>What a new estate needs.</summary>
/// <param name="Name">Estate display name.</param>
/// <param name="Slug">Estate slug.</param>
/// <param name="Purpose">Why the estate exists.</param>
/// <param name="OperatorName">The root operator's display name.</param>
/// <param name="OrganizationName">Organization display name.</param>
/// <param name="GitHubOrganization">GitHub organization login, or null.</param>
/// <param name="Repository">GitHub owner/name of the Vericairn source to pin.</param>
/// <param name="Commit">Commit of that source to pin (source mode).</param>
/// <param name="At">Authoring time; now when omitted.</param>
/// <param name="Profile">Storage profile: repository-synced (default) or local-only.</param>
public sealed record EstateInitialization(string Name, string Slug, string Purpose, string OperatorName, string OrganizationName, string? GitHubOrganization, string Repository, string Commit, DateTimeOffset? At = null, string Profile = "repository-synced");

/// <summary>The records and manifest of a new estate before anything is written.</summary>
/// <param name="Operator">The root operator principal.</param>
/// <param name="Estate">The charter.</param>
/// <param name="ManifestBytes">Canonical manifest bytes.</param>
public sealed record PreparedEstate(AuthoredRecord Operator, AuthoredRecord Estate, byte[] ManifestBytes);

/// <summary>Creates the root of trust of a new estate: the operator principal, the charter and the manifest, together.</summary>
public static class EstateInitializer
{
    /// <summary>Initializes an estate in an empty or record-less directory. Returns the operator and estate record envelopes.</summary>
    public static (RecordEnvelope Operator, RecordEnvelope Estate) Initialize(string root, EstateInitialization initialization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(initialization);
        root = Path.GetFullPath(root);
        PreparedEstate prepared = Prepare(root, initialization);
        string manifestPath = Path.Combine(root, EstateLayout.ManifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllBytes(manifestPath, prepared.ManifestBytes);
        RecordAuthor.Write(root, prepared.Operator);
        RecordAuthor.Write(root, prepared.Estate);
        EstateModel model = EstateLoader.Load(root);
        if (model.Findings.HasErrors)
        {
            throw new InvalidOperationException("The initialized estate is not valid: " + string.Join("; ", model.Findings.Where(f => f.Severity == Severity.Error).Select(f => f.Message)));
        }

        return (prepared.Operator.Envelope, prepared.Estate.Envelope);
    }

    /// <summary>Prepares the root of trust without writing anything; the caller stores the records wherever the profile keeps them.</summary>
    public static PreparedEstate Prepare(string root, EstateInitialization initialization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(initialization);
        root = Path.GetFullPath(root);
        if (Directory.Exists(Path.Combine(root, EstateLayout.RecordsDirectory)) || File.Exists(Path.Combine(root, EstateLayout.ManifestPath)))
        {
            throw new InvalidOperationException("This directory already holds an estate; initialization never overwrites records or a manifest.");
        }

        if (Array.IndexOf(EstateManifest.Profiles, initialization.Profile) < 0)
        {
            throw new ArgumentException($"Unknown profile '{initialization.Profile}'. Known: {string.Join(", ", EstateManifest.Profiles)}.", nameof(initialization));
        }

        DateTimeOffset at = initialization.At ?? DateTimeOffset.UtcNow;
        string operatorId = Ulid.NewUlid(at).Value;
        var operatorBody = new JsonObject { ["kind"] = "human", ["name"] = initialization.OperatorName, ["roles"] = new JsonArray("operator") };
        AuthoredRecord operatorRecord = Author("principal", operatorId, operatorBody, at, operatorId);
        var estateBody = new JsonObject
        {
            ["name"] = initialization.Name,
            ["slug"] = initialization.Slug,
            ["purpose"] = initialization.Purpose,
            ["operator"] = operatorId,
            ["recordFormat"] = 1,
            ["policies"] = new JsonObject { ["signatures"] = "optional", ["releaseAcceptance"] = new JsonArray("operator") },
            ["organization"] = initialization.GitHubOrganization is null
                ? new JsonObject { ["name"] = initialization.OrganizationName }
                : new JsonObject { ["name"] = initialization.OrganizationName, ["github"] = initialization.GitHubOrganization },
        };
        AuthoredRecord estateRecord = Author("estate", operatorId, estateBody, at, null);
        var manifest = new JsonObject
        {
            ["format"] = EstateManifest.Format1,
            ["estate"] = estateRecord.Envelope.Id,
            ["recordFormat"] = 1,
            ["profile"] = initialization.Profile,
            ["vericairn"] = new JsonObject { ["mode"] = "source", ["repository"] = initialization.Repository, ["commit"] = initialization.Commit },
        };
        byte[] manifestBytes;
        using (JsonDocument document = JsonDocument.Parse(manifest.ToJsonString()))
        {
            manifestBytes = CanonicalJson.SerializePrettyUtf8(document.RootElement);
        }

        return new PreparedEstate(operatorRecord, estateRecord, manifestBytes);
    }

    private static AuthoredRecord Author(string type, string by, JsonObject body, DateTimeOffset at, string? id)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return RecordAuthor.Create(new RecordDraft(type, by, document.RootElement, null, null, at, id));
    }
}
