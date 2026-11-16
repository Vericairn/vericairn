// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Workers;

/// <summary>
/// The custody precondition as a guard on launch (the estate's design/goals.md § 10). Whatever key a store of this machine holds for
/// the operating-system user the worker runs as, the agent it launches can use too, since the agent runs as that user. So the worker
/// launches nothing while a device of an operator, or of the sponsor of the goal the run is charged to, has its key held here in
/// custody that signs without its owner's presence: its file, keychain, secret-service or CNG key signs for any process of the user,
/// and an agent could sign as the operator or the sponsor. A key whose custody enforces its owner's presence (the Secure Enclave with
/// presence required) signs nothing unattended, and a device whose key this machine does not hold signs nothing here. Every device
/// of those principals counts, admitted, revoked or not yet admitted, since a revoked key still signs records dated before its
/// revocation. The agent the run launches is never one of them: its own key is what its MCP server signs with. The guard holds for
/// every run, whether a person starts it with the worker or the one-step conductor dispatches it, unless the goal's sponsor recorded
/// an exception in force for the goal (<see cref="CustodyExceptions"/>), which the run then names.
/// </summary>
public static class CustodyGuard
{
    /// <summary>
    /// Why the worker may not launch, one reason per device: each device of the principals named (an operator, or the goal's sponsor)
    /// whose key a store here holds and can sign without its owner's presence. Empty when none can.
    /// </summary>
    public static IReadOnlyList<string> Refusals(EstateModel model, IEnumerable<(string Principal, string Role)> principals, IReadOnlyList<IDeviceKeyStore> stores, string user) =>
        Exposed(model, principals, stores, user).Select(e => e.Reason).ToList();

    /// <summary>
    /// The devices the guard finds exposed here, each by its identity with the reason in words: every device of the principals named
    /// whose key a store here holds and can sign without its owner's presence.
    /// </summary>
    public static IReadOnlyList<(string Device, string Reason)> Exposed(EstateModel model, IEnumerable<(string Principal, string Role)> principals, IReadOnlyList<IDeviceKeyStore> stores, string user)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(stores);
        var refusals = new List<(string Device, string Reason)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string principal, string role) in principals)
        {
            if (!seen.Add(principal))
            {
                continue;
            }

            string name = model.CurrentPrincipal(principal) is RecordEnvelope record ? EstateModel.BodyString(record, "name") ?? principal : principal;
            foreach (RecordEnvelope device in model.Current("device").Where(d => EstateModel.BodyString(d, "principal") == principal).OrderBy(d => model.Chain(d)[0], StringComparer.Ordinal))
            {
                string identity = model.Chain(device)[0];
                IDeviceKeyStore? store = stores.FirstOrDefault(s => s.Has(identity));
                if (store is null)
                {
                    continue;
                }

                bool presence = EstateModel.BodyString(device, "presence") == "required" && DeviceKeyStores.PresenceKinds.IndexOf(store.Kind) >= 0;
                if (presence)
                {
                    continue;
                }

                RecordEnvelope? admission = SignatureChecks.AdmissionOf(model, device);
                string admitted = admission is null ? "not admitted" : EstateModel.BodyString(admission, "status") ?? "admitted";
                string custody = EstateModel.BodyString(device, "presence") == "required"
                    ? $"its record requires its owner's presence, but this machine holds its key in {store.Kind} custody, which cannot enforce it"
                    : $"this machine holds its key in {store.Kind} custody without its owner's presence";
                refusals.Add((identity, $"Device {identity} ({EstateModel.BodyString(device, "name") ?? "unnamed"}, {admitted}) of {name} ({principal}), {role}: {custody}, so any process of the operating-system user {user}, the user the worker runs as, can sign with it, and the agent the worker would launch could sign as {name}. Rotate the key into custody that requires its owner's presence (vericairn devices rotate {identity} --custody secure-enclave --presence), or run the worker as an operating-system user that cannot read the key."));
            }
        }

        return refusals;
    }

    /// <summary>
    /// From 0.9.4 (ID-16): whether an agent run this machine's worker started is unsettled under this operating-system user, the agent
    /// condition of design/presence-sessions.md § 10 (decision 4): while one is, every exact entry of a presence session's list gets its own
    /// check and only kind entries share one, so an approval the person did not mean gives that run no batch of judgments. The worker runs
    /// an agent as the user it runs as, and keeps each run's observed state under that user's Vericairn home, so the runs found there are
    /// this user's: one counts until its settlement is recorded while its harness, or its worker before the harness starts, may still run.
    /// A state that cannot be read counts as under way. An agent the runtime did not start is not seen.
    /// </summary>
    public static bool UnsettledAgentRuns(string? home = null)
    {
        string estates = Path.Combine(home ?? VericairnHome.Path(), "estates");
        if (!Directory.Exists(estates)) return false;
        try
        {
            foreach (string runs in Directory.EnumerateDirectories(estates).Select(e => Path.Combine(e, "runs")).Where(Directory.Exists))
            {
                foreach (string state in Directory.EnumerateDirectories(runs).Select(r => Path.Combine(r, "state.json")).Where(File.Exists))
                {
                    if (UnderWay(state)) return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }

        return false;
    }

    private static bool UnderWay(string path)
    {
        RunObservedState? state;
        try
        {
            state = System.Text.Json.JsonSerializer.Deserialize<RunObservedState>(File.ReadAllBytes(path), Protocol.ProtocolJson.Options);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return true;
        }

        if (state is null) return true;
        if (state.Settled is not null) return false;
        if (state.ProcessId is int harness && DateTimeOffset.TryParse(state.LaunchedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTimeOffset launched))
            return !RunProcesses.Gone(harness, launched) || Alive(state.Supervisor);
        return Alive(state.Supervisor);
    }

    private static bool Alive(int processId)
    {
        if (processId <= 0) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// The principals whose devices the guard checks for a run of a goal: every operator, and the goal's sponsor; never the agent the
    /// run launches, whose own key its MCP server signs with.
    /// </summary>
    public static IReadOnlyList<(string Principal, string Role)> Guarded(EstateModel model, RecordEnvelope goal, string agent)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(goal);
        var guarded = new List<(string Principal, string Role)>();
        foreach (RecordEnvelope principal in model.Current("principal").Where(p => model.HasRole(model.Chain(p)[0], Roles.Operator)).OrderBy(p => model.Chain(p)[0], StringComparer.Ordinal))
        {
            guarded.Add((model.Chain(principal)[0], "an operator"));
        }

        if (EstateModel.BodyString(goal, "sponsor") is string sponsor)
        {
            guarded.Add((sponsor, "the sponsor of the goal the run is charged to"));
        }

        return guarded.Where(g => g.Principal != agent).ToList();
    }
}
