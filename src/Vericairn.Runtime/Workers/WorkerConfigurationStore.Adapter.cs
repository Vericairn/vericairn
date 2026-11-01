// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Representation;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>Machine admission only, never a run default or an endpoint setting.</summary>
public sealed record EfficiencyAdapterConfiguration
{
    /// <summary>The explicitly admitted adapter, or disabled.</summary>
    public string Adapter { get; init; } = "disabled";

    /// <summary>The native launchers admitted by the operator, copied on initialization.</summary>
    public IReadOnlyList<string> Launchers { get; init => field = OptionCollections.List(value); } = [];

    internal bool Allows(string adapter, string launcher) => Adapter == adapter && adapter == RepresentationContract.LocalLossless
        && Launchers.Contains(launcher, StringComparer.Ordinal);
}

public sealed partial class WorkerConfigurationStore
{
    /// <summary>Reads the independent efficiency-adapter permission. Absence means refused.</summary>
    public EfficiencyAdapterConfiguration? ReadAdapter()
    {
        try
        {
            string path = ConfigurationPath("efficiency-adapter.json");
            RefuseLink(path);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 4096) throw new JsonException();
            EfficiencyAdapterConfiguration permission = JsonSerializer.Deserialize<EfficiencyAdapterConfiguration>(File.ReadAllBytes(path), Json) ?? throw new JsonException();
            ValidateAdapter(permission);
            return permission;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { throw new ProviderRefusedException("The efficiency-adapter worker setting could not be read; configure it explicitly before selecting an adapter.", exception); }
    }

    /// <summary>Atomically replaces the machine permission; this never enables a particular run.</summary>
    public void WriteAdapter(EfficiencyAdapterConfiguration permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        ValidateAdapter(permission);
        WriteFile(ConfigurationPath("efficiency-adapter.json"), permission);
    }

    private static void ValidateAdapter(EfficiencyAdapterConfiguration permission)
    {
        if (permission.Adapter is not ("disabled" or RepresentationContract.LocalLossless)
            || permission.Launchers.Count > 2 || permission.Launchers.Distinct(StringComparer.Ordinal).Count() != permission.Launchers.Count
            || permission.Launchers.Any(l => l is not ("codex" or "claude-code"))
            || (permission.Adapter == "disabled" ? permission.Launchers.Count != 0 : permission.Launchers.Count == 0))
            throw new ProviderRefusedException("The efficiency-adapter worker setting names local-lossless and its allowed launchers, or disabled with no launchers.");
    }
}
