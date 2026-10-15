// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Routing by roster from this machine (the estate's design/goals.md §§ 6 and 7): the next-eligible view of a goal with the principal
/// each eligible step reaches, read from the records and from what this machine offers, its coding-agent launchers, the device keys
/// its stores hold, and the custody guard over those keys.
/// </summary>
public sealed partial class EstateService
{
    /// <inheritdoc />
    public NextResponse? GoalNext(string goal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goal);
        lock (gate)
        {
            EstateModel model = session.Load();
            RecordEnvelope? current = GoalView.Find(model, goal);
            if (current is null)
            {
                return null;
            }

            NextEligible.GoalRoutes routes = NextEligible.Routes(model, current, Machine(model));
            using JsonDocument json = JsonDocument.Parse(NextEligible.RenderJson(model, routes));
            return new NextResponse(NextEligible.RenderText(model, routes), json.RootElement.Clone());
        }
    }

    /// <summary>
    /// What this machine offers routing now: the launchers its registry holds with the harness each starts, the device keys its stores
    /// hold, and the custody guard for an unattended run of a goal by an agent as the operating-system user this runtime runs as.
    /// </summary>
    private Routing.Machine Machine(EstateModel model)
    {
        IReadOnlyList<IDeviceKeyStore> stores = DeviceKeyStores.All();
        var held = new Dictionary<string, bool>(StringComparer.Ordinal);
        return new Routing.Machine(
            Providers.Launchers.Select(l => (l.Launcher, l.Harness)).ToList(),
            identity => held.TryGetValue(identity, out bool known) ? known : held[identity] = stores.Any(s => s.Has(identity)),
            (goal, agent) => CustodyGuard.Refusals(model, CustodyGuard.Guarded(model, goal, agent), stores, Environment.UserName),
            DateTimeOffset.UtcNow);
    }
}
