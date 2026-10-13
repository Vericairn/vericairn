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
public sealed record EstateInitialization(string Name, string Slug, string Purpose, string OperatorName, string OrganizationName, string? GitHubOrganization, string Repository, string Commit, DateTimeOffset? At = null);

/// <summary>Creates the root of trust of a new estate: the operator principal, the charter and the manifest, together.</summary>
public static class EstateInitializer
{
    /// <summary>Initializes an estate in an empty or record-less directory. Returns the operator and estate record envelopes.</summary>
    public static (RecordEnvelope Operator, RecordEnvelope Estate) Initialize(string root, EstateInitialization initialization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(initialization);
        root = Path.GetFullPath(root);
        if (Directory.Exists(Path.Combine(root, EstateLayout.RecordsDirectory)) || File.Exists(Path.Combine(root, EstateLayout.ManifestPath)))
        {
            throw new InvalidOperationException("This directory already holds an estate; initialization never overwrites records or a manifest.");
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
        AuthoredRecord estateRecord = Author("estate", operatorId, estateBody, at.AddSeconds(1), null);
        var manifest = new JsonObject
        {
            ["format"] = EstateManifest.Format1,
            ["estate"] = estateRecord.Envelope.Id,
            ["recordFormat"] = 1,
            ["vericairn"] = new JsonObject { ["mode"] = "source", ["repository"] = initialization.Repository, ["commit"] = initialization.Commit },
        };
        byte[] manifestBytes;
        using (JsonDocument document = JsonDocument.Parse(manifest.ToJsonString()))
        {
            manifestBytes = CanonicalJson.SerializePrettyUtf8(document.RootElement);
        }

        string manifestPath = Path.Combine(root, EstateLayout.ManifestPath);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllBytes(manifestPath, manifestBytes);
        RecordAuthor.Write(root, operatorRecord);
        RecordAuthor.Write(root, estateRecord);
        EstateModel model = EstateLoader.Load(root);
        if (model.Findings.HasErrors)
        {
            throw new InvalidOperationException("The initialized estate is not valid: " + string.Join("; ", model.Findings.Where(f => f.Severity == Severity.Error).Select(f => f.Message)));
        }

        return (operatorRecord.Envelope, estateRecord.Envelope);
    }

    private static AuthoredRecord Author(string type, string by, JsonObject body, DateTimeOffset at, string? id)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return RecordAuthor.Create(new RecordDraft(type, by, document.RootElement, null, null, at, id));
    }
}
