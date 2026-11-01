// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

/// <summary>A disposable fork with published runtime archives and independent release lines.</summary>
public sealed class EstateLineFixture : IDisposable
{
    public ReleaseLineFixture Releases { get; } = new();
    public TestEstate Estate => Releases.Estate;
    public string ManifestPath => Path.Combine(Estate.Root, EstateLayout.ManifestPath);

    public EstateLineFixture() => Follow();

    public void Follow(string line = "current", string channel = "stable", string? version = null, string repository = "Example/example")
    {
        JsonObject pin = version is null
            ? new() { ["mode"] = "source", ["commit"] = new string('a', 40) }
            : new() { ["mode"] = "release", ["version"] = version, ["artifacts"] = new JsonObject { ["linux-x64"] = "sha256:" + new string('1', 64) } };
        pin["repository"] = repository;
        pin["line"] = line;
        pin["channel"] = channel;
        var manifest = new JsonObject
        {
            ["estate"] = Estate.Estate.Id, ["format"] = EstateManifest.Format1, ["recordFormat"] = 3, ["vericairn"] = pin,
        };
        File.WriteAllBytes(ManifestPath, CanonicalJson.SerializePrettyUtf8(JsonSerializer.SerializeToElement(manifest)));
    }

    public RecordEnvelope Publish(string version, string? line = null, string? channel = null, string? endOfSupport = null)
    {
        JsonObject body = ReleaseLineFixture.Body(version, line, channel);
        body["artifacts"] = new JsonArray(new JsonObject
        {
            ["name"] = $"vericairn-{version}-linux-x64.tar.gz", ["hash"] = "sha256:" + new string('1', 64),
            ["bytes"] = 10, ["platform"] = "linux-x64",
        });
        if (endOfSupport is not null) body["endOfSupport"] = endOfSupport;
        return Releases.Publish(body);
    }

    public void Dispose() => Releases.Dispose();
}
