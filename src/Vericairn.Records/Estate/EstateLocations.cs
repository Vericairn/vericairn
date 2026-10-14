// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Estate;

/// <summary>Where an estate's per-machine state lives: computed the same way by the runtime that owns it and by the clients that look for its daemon.</summary>
public static class EstateLocations
{
    /// <summary>The live store of an estate on this machine: per user, never inside the repository; a local-only estate has one, a repository-synced one has one per clone.</summary>
    public static string LiveStorePath(string root, string estateId, string profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(estateId);
        string key = profile == "local-only" ? "local" : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..16];
        return Path.Combine(VericairnHome.Path(), "estates", estateId, key, "live.db");
    }

    /// <summary>The file a daemon publishes its endpoint in, beside the estate's live store.</summary>
    public static string EndpointFilePath(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        var findings = new Model.FindingList();
        EstateManifest manifest = EstateManifest.Load(Path.GetFullPath(root), findings) ?? throw new InvalidOperationException(string.Join("; ", findings.Select(f => f.Message)));
        return Path.Combine(Path.GetDirectoryName(LiveStorePath(root, manifest.Estate, manifest.Profile))!, "daemon.json");
    }
}
