// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Vericairn.Daemon;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Progress;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Store;
using Vericairn.Sdk;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

/// <summary>
/// From 0.9.4 (WATCH-06): the progress-target commands. A target's configuration is this machine's, beside the estate's live store;
/// the estate is read from its records, read-only, and following needs the daemon, which keeps the event journal.
/// </summary>
internal sealed class ProgressTargetOperations(CommandContext context)
{
    private const string OutcomeFormat = "vericairn.progress-target-outcome/1";
    private const string TargetsFormat = "vericairn.progress-targets/1";

    public int Set(string root, string name, string adapter, string[] settings, string form, string? title, string[]? refresh, string[]? comment, bool json)
    {
        if (!IsEstate(root)) return context.Fail(NotAnEstate(root));
        var values = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (string setting in settings)
        {
            int equals = setting.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0) return context.Fail($"--setting is name=value, not '{setting}'. Nothing was changed.");
            if (!values.TryAdd(setting[..equals], setting[(equals + 1)..])) return context.Fail($"--setting names {setting[..equals]} once. Nothing was changed.");
        }

        ImmutableArray<ProgressTrigger> refreshing, commenting;
        try
        {
            refreshing = Triggers(refresh, ProgressTrigger.DefaultRefresh);
            commenting = Triggers(comment, ProgressTrigger.DefaultComment);
        }
        catch (FormatException exception)
        {
            return context.Fail(exception.Message + " Nothing was changed.");
        }

        var target = new ProgressTargetConfiguration(name, adapter, values.ToImmutable(), form, title, refreshing, commenting);
        if (target.Problem(ProviderRegistry.Current.ProgressTarget(adapter)) is string problem) return context.Fail(problem + " Nothing was changed.");
        try
        {
            ProgressTargetStore store = ProgressTargetStore.Of(root);
            store.Save(target);
            return Show(store, [target], json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int List(string root, bool json)
    {
        if (!IsEstate(root)) return context.Fail(NotAnEstate(root));
        try
        {
            ProgressTargetStore store = ProgressTargetStore.Of(root);
            IReadOnlyList<ProgressTargetConfiguration> targets = store.Load();
            if (!json && targets.Count == 0)
            {
                context.Output.WriteLine("No progress target is configured on this machine; set one with 'vericairn progress target set'.");
                return Clean;
            }

            return Show(store, targets, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int Remove(string root, string name, bool json)
    {
        if (!IsEstate(root)) return context.Fail(NotAnEstate(root));
        try
        {
            if (!ProgressTargetStore.Of(root).Remove(name)) return context.Fail($"No progress target is named '{name}' on this machine.");
            context.Output.WriteLine(json ? new JsonObject { ["format"] = TargetsFormat, ["removed"] = name }.ToJsonString()
                : $"Removed the progress target {name} from this machine; the place it kept stays where it is.");
            return Clean;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int Refresh(string root, string name, bool dryRun, bool json)
    {
        if (!IsEstate(root)) return context.Fail(NotAnEstate(root));
        try
        {
            ProgressOutcome outcome = Runner(root).Refresh(name, dryRun);
            Write(outcome, json, dryRun);
            return Code(outcome);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }
    }

    public int Follow(string root, string name, int? count, bool json)
    {
        if (count is <= 0) return context.Fail("--count is a positive number of outcomes.");
        if (!IsEstate(root)) return context.Fail(NotAnEstate(root));
        ProgressTargetRunner runner;
        try
        {
            runner = Runner(root);
            if (runner.Resolve(name).Problem is string problem) return context.Fail(problem);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return context.Fail(exception.Message);
        }

        DaemonEndpoint? endpoint = DaemonHost.Discover(root);
        using VericairnClient? client = endpoint is null ? null : new VericairnClient(endpoint);
        if (client is null || !context.IsAlive(endpoint!))
            return context.Fail("No daemon is serving this estate, and following needs one: the daemon keeps the event journal. Start it with 'vericairn runtime start', or refresh once with 'vericairn progress target refresh'.");

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        int told = 0;
        try
        {
            runner.FollowAsync(name, (after, filter) => client.WatchEventsAsync(after, filter, null, stop.Token), outcome =>
            {
                Write(outcome, json, dryRun: false);
                context.Output.Flush();
                if (count is int limit && ++told >= limit) stop.Cancel();
            }, stop.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            return context.Fail(exception.Message);
        }

        return Clean;
    }

    private static ImmutableArray<ProgressTrigger> Triggers(string[]? given, ImmutableArray<ProgressTrigger> defaults)
    {
        if (given is null || given.Length == 0) return defaults;
        if (given is ["none"]) return [];
        if (given.Contains("none", StringComparer.Ordinal)) throw new FormatException("none stands alone: no event of that kind.");
        return [.. given.Select(ProgressTrigger.Parse).Distinct()];
    }

    private static ProgressTargetRunner Runner(string root) => new(() =>
    {
        using EstateSession session = EstateSession.Open(root, readOnly: true);
        return session.Load();
    }, ProviderRegistry.Current, ProgressTargetStore.Of(root));

    // The comments a person should check: those whose outcome is uncertain, and those with no outcome when no follower holds the target
    // (one stopped before saving it). While a follower holds it, an entry with no outcome is one it is sending.
    private static IEnumerable<KeyValuePair<string, ProgressTargetComment>> Uncertain(ProgressTargetStore store, string name, ProgressTargetState state)
    {
        bool followed = state.Comments.Values.Any(entry => entry.Outcome is null) && store.Followed(name);
        return state.Comments.Where(entry => entry.Value.Outcome == "uncertain" || (entry.Value.Outcome is null && !followed));
    }

    private int Show(ProgressTargetStore store, IReadOnlyList<ProgressTargetConfiguration> targets, bool json)
    {
        if (json)
        {
            var list = new JsonArray();
            foreach (ProgressTargetConfiguration target in targets)
            {
                JsonObject item = target.ToJson();
                ProgressTargetState state = store.State(target.Name);
                item["place"] = state.PlaceUrl is null ? null : new JsonObject { ["id"] = state.PlaceId, ["url"] = state.PlaceUrl };
                var uncertain = new JsonArray();
                foreach ((string key, ProgressTargetComment entry) in Uncertain(store, target.Name, state))
                {
                    JsonObject comment = entry.ToJson();
                    comment["key"] = key;
                    comment["outcome"] = "uncertain";
                    uncertain.Add(comment);
                }
                item["uncertainComments"] = uncertain;
                list.Add(item);
            }

            context.Output.WriteLine(new JsonObject { ["format"] = TargetsFormat, ["targets"] = list }.ToJsonString());
            return Clean;
        }

        foreach (ProgressTargetConfiguration target in targets)
        {
            ProgressTargetState state = store.State(target.Name);
            context.Output.WriteLine($"{target.Name}: {target.Form} form through {target.Adapter}" + (target.Settings.Count == 0 ? string.Empty : " (" + string.Join(", ", target.Settings.Select(s => s.Key + "=" + s.Value)) + ")"));
            if (target.Title is not null) context.Output.WriteLine("  title: " + target.Title);
            context.Output.WriteLine("  refreshes on: " + (target.Refresh.IsEmpty ? "no event" : string.Join("; ", target.Refresh)));
            context.Output.WriteLine("  comments on: " + (target.Comment.IsEmpty ? "no event" : string.Join("; ", target.Comment)));
            context.Output.WriteLine("  place: " + (state.PlaceUrl ?? "none yet; the first refresh creates it"));
            foreach ((string key, ProgressTargetComment entry) in Uncertain(store, target.Name, state))
                context.Output.WriteLine($"  uncertain comment {key} ({entry.Form} form, {entry.PayloadHash}) at {entry.PlaceUrl} through {entry.Adapter}. Check the original destination.");
        }

        return Clean;
    }

    private void Write(ProgressOutcome outcome, bool json, bool dryRun)
    {
        if (json)
        {
            var node = new JsonObject
            {
                ["format"] = OutcomeFormat,
                ["target"] = outcome.Target,
                ["action"] = outcome.Action,
                ["url"] = outcome.Url,
                ["form"] = outcome.Form,
                ["detail"] = outcome.Detail,
                ["carried"] = new JsonArray([.. outcome.Carried.Select(r => (JsonNode)new JsonObject { ["type"] = r.Type, ["id"] = r.Id, ["hash"] = r.Hash })]),
                ["leftOut"] = new JsonArray([.. outcome.LeftOut.Select(k => (JsonNode)JsonValue.Create(k))]),
            };
            if (outcome.CommentKey is not null) node["commentKey"] = outcome.CommentKey;
            if (dryRun) node["text"] = outcome.Text;
            context.Output.WriteLine(node.ToJsonString());
            return;
        }

        context.Output.WriteLine($"{outcome.Target}: {outcome.Action}" + (outcome.Url is null ? string.Empty : " " + outcome.Url) + (outcome.Form is null ? string.Empty : $" ({outcome.Form} form)"));
        if (outcome.Detail is not null) context.Output.WriteLine("  " + outcome.Detail);
        if (outcome.Form is not null) context.Output.WriteLine($"  The text carries {outcome.Carried.Count} record{(outcome.Carried.Count == 1 ? string.Empty : "s")}; --json names each.");
        if (outcome.LeftOut.Count > 0) context.Output.WriteLine("  Left out as a disclosure rule holds them: " + string.Join(", ", outcome.LeftOut));
        if (dryRun && outcome.Text is not null) context.Output.Write("\n" + outcome.Text);
    }

    private static int Code(ProgressOutcome outcome) => outcome.Action switch
    {
        "refused" => Failed,
        "stopped" or "uncertain" => HasErrors,
        _ => Clean,
    };
}
