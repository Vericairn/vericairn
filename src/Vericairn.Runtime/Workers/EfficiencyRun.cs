// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Representation;

namespace Vericairn.Runtime.Workers;

/// <summary>Owns the optional hook, reducer and forwarding service for exactly one run.</summary>
internal sealed class EfficiencyRun : IDisposable
{
    private readonly IAgentLauncher launcher;
    private readonly LaunchContext direct;
    private readonly LoopbackAdapter service;
    private bool disposed;

    internal EfficiencyRun(IAgentLauncher launcher, LaunchContext context, JsonElement brief, Action? beforeStart = null,
        Func<byte[], RequestReduction.Result>? reducer = null)
    {
        this.launcher = launcher;
        direct = context;
        // Hook support is a preflight requirement. Only service startup failure may fall back to direct.
        Uri upstream = launcher.EfficiencyUpstream(context);
        IReadOnlyDictionary<string, byte[]> originals = Originals(brief);
        service = new LoopbackAdapter(upstream, reducer ?? (body => RequestReduction.Reduce(body, pin => originals.GetValueOrDefault(pin))),
            measure: body => RequestReduction.Reduce(body, pin => originals.GetValueOrDefault(pin), enabled: false).BeforeBytes);
        try { Context = service.Start(beforeStart) ? launcher.ConfigureEfficiencyHook(context, service.Address) : context; }
        catch { service.Dispose(); throw; }
    }

    internal LaunchContext Context { get; }
    internal JsonObject Measurement() => service.Measurement();

    // Reuse the run's existing retained context and original-retrieval pins. Nothing is fetched, written or cached by the adapter.
    internal static IReadOnlyDictionary<string, byte[]> Originals(JsonElement brief)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (Records.Canonical.CanonicalJson.SerializePrettyUtf8(brief).Length > 64 * 1024 * 1024) return result;
        if (!brief.TryGetProperty("context", out JsonElement context) || !context.TryGetProperty("payload", out JsonElement payload)
            || !payload.TryGetProperty("originals", out JsonElement originals) || originals.ValueKind != JsonValueKind.Array) return result;
        foreach (JsonElement source in originals.EnumerateArray())
        {
            if (!source.TryGetProperty("mandatory", out JsonElement mandatory) || mandatory.ValueKind != JsonValueKind.False
                || !source.TryGetProperty("kind", out JsonElement kind) || kind.GetString() != "document"
                || !source.TryGetProperty("encoding", out JsonElement encoding) || encoding.GetString() != "base64"
                || !source.TryGetProperty("pin", out JsonElement pin) || pin.ValueKind != JsonValueKind.String
                || !source.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.String
                || !source.TryGetProperty("byteHash", out JsonElement hash) || hash.ValueKind != JsonValueKind.String) continue;
            byte[] bytes = content.GetBytesFromBase64();
            if (ContentHash.Sha256(bytes) == hash.GetString()) result.TryAdd(pin.GetString()!, bytes);
        }
        return result;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { service.Dispose(); }
        finally { if (Context.AdapterAddress is not null) _ = launcher.ConfigureEfficiencyHook(direct, null); }
    }
}
