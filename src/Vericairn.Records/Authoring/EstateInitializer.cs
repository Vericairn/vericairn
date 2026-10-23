// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Updates;

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
public sealed record EstateInitialization(string Name, string Slug, string Purpose, string OperatorName, string OrganizationName, string? GitHubOrganization, string Repository, string Commit, DateTimeOffset? At = null, string Profile = "repository-synced")
{
    /// <summary>Optional verified published dependency; absent, initialization retains its offline source pin.</summary>
    public VerifiedRelease? Release { get; init; }
}

/// <summary>The records and manifest of a new estate before anything is written.</summary>
/// <param name="Operator">The root operator principal.</param>
/// <param name="Estate">The charter.</param>
/// <param name="ManifestBytes">Canonical manifest bytes.</param>
public sealed record PreparedEstate(AuthoredRecord Operator, AuthoredRecord Estate, byte[] ManifestBytes)
{
    /// <summary>The verified dependency observation, when release mode was requested.</summary>
    public AuthoredRecord? Release { get; init; }
}

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
        if (prepared.Release is not null) RecordAuthor.Write(root, prepared.Release);
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

        if (EstateManifest.Profiles.IndexOf(initialization.Profile) < 0)
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
            ["recordFormat"] = RecordFormats.Current,
            ["policies"] = new JsonObject { ["signatures"] = "optional", ["releaseAcceptance"] = new JsonArray("operator") },
            ["organization"] = initialization.GitHubOrganization is null
                ? new JsonObject { ["name"] = initialization.OrganizationName }
                : new JsonObject { ["name"] = initialization.OrganizationName, ["github"] = initialization.GitHubOrganization },
        };
        AuthoredRecord estateRecord = Author("estate", operatorId, estateBody, at, null);
        AuthoredRecord? observation = null;
        var manifest = new JsonObject
        {
            ["format"] = EstateManifest.Format1,
            ["estate"] = estateRecord.Envelope.Id,
            ["recordFormat"] = RecordFormats.Current,
            ["profile"] = initialization.Profile,
            ["vericairn"] = new JsonObject { ["mode"] = "source", ["repository"] = initialization.Repository, ["commit"] = initialization.Commit },
        };
        if (initialization.Release is { } release)
        {
            if (!string.Equals(initialization.Repository, VerifiedRelease.Repository, StringComparison.OrdinalIgnoreCase) || initialization.Commit != release.Build.Commit
                || release.Build.Writes != RecordFormats.Current || RecordFormats.Reads.Any(f => !release.Build.Reads.Contains(f)))
                throw new InvalidOperationException("The verified release does not match this initialization's source or record formats. Run the target release's command line and use its repository and commit. Nothing changed.");
            observation = RecordAuthor.Create(new RecordDraft("observation", operatorId, release.ObservationBody(), estateRecord.Envelope.Reference,
                At: release.ObservedAt > at ? release.ObservedAt : at, Format: RecordFormats.Current));
            var artifacts = new JsonObject();
            foreach (ReleaseArchive archive in release.Archives.Where(a => a.Name == ReleaseArchive.FileName(release.Build.Version, a.Platform))) artifacts[archive.Platform] = archive.Hash;
            manifest["vericairn"] = new JsonObject
            {
                ["mode"] = "release", ["repository"] = VerifiedRelease.Repository, ["version"] = release.Build.Version, ["artifacts"] = artifacts,
                ["release"] = new JsonObject { ["type"] = "observation", ["id"] = observation.Envelope.Id, ["hash"] = observation.Envelope.ComputedHash },
            };
        }
        byte[] manifestBytes;
        using (JsonDocument document = JsonDocument.Parse(manifest.ToJsonString()))
        {
            manifestBytes = CanonicalJson.SerializePrettyUtf8(document.RootElement);
        }

        var files = new List<RecordFile> { new(operatorRecord.RelativePath, operatorRecord.Bytes), new(estateRecord.RelativePath, estateRecord.Bytes) };
        if (observation is not null) files.Add(new(observation.RelativePath, observation.Bytes));
        EstateModel proposed = EstateLoader.LoadPrepared(root, files, manifestBytes);
        if (proposed.Findings.HasErrors)
            throw new InvalidOperationException("The proposed estate is invalid; correct the initialization options before retrying. Nothing changed. "
                + string.Join("; ", proposed.Findings.Where(f => f.Severity == Severity.Error).Select(f => f.Message)));
        return new PreparedEstate(operatorRecord, estateRecord, manifestBytes) { Release = observation };
    }

    private static AuthoredRecord Author(string type, string by, JsonObject body, DateTimeOffset at, string? id)
    {
        using JsonDocument document = JsonDocument.Parse(body.ToJsonString());
        return RecordAuthor.Create(new RecordDraft(type, by, document.RootElement, null, null, at, id, RecordFormats.Current));
    }
}
