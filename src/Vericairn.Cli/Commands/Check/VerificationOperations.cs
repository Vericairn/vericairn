// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Json = System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Runtime.Providers;
using Vericairn.Packs.Software;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class VerificationOperations(CommandContext context)
{
    public int CheckRun(string root, string resourceId, string by, string path, string role, string? summary, int timeoutSeconds, string[] command, bool json)
    {
        RepositoryObservation observation;
        CheckRun run;
        try
        {
            observation = (CommandProviders.Git(context, out int failure) ?? throw new ProviderRefusedException("No git adapter is available in this distribution; repositories cannot be observed here.")).Inspect(path);
            run = Checks.Run(path, command, TimeSpan.FromSeconds(timeoutSeconds));
        }
        catch (ProviderRefusedException exception)
        {
            return context.Fail(exception.Message);
        }

        return context.WithApi(root, api =>
        {
            RecordResponse? resource = api.Record(resourceId);
            if (resource is null || resource.Type != "resource" || !resource.Current)
            {
                return context.Fail($"No current resource has id {resourceId}.");
            }

            var evidence = new List<Checks.Evidence>();
            if (run.Output.Length > 0)
            {
                (ObjectStored? stored, _) = api.ObjectPut(run.Output);
                evidence.Add(stored is null ? new Checks.Evidence($"Command output ({run.Output.Length:N0} bytes, above the object limit, not stored): {run.OutputTail()}") : new Checks.Evidence("Command output", stored.Hash));
            }

            var limitations = new List<string> { "A check that passed here establishes the result for this commit on this platform only." };
            if (!observation.Clean)
            {
                limitations.Add($"The working tree had {observation.Modified} modified and {observation.Untracked} untracked files; the result may not reproduce from the commit alone.");
            }

            string text = summary ?? run.Summary;
            Json.JsonObject body = Checks.VerificationBody("command", observation.Commit, run.Command, Checks.LocalEnvironment(), role, run.Outcome, text, run.ExitCode, run.DurationMs, run.StartedAt, run.FinishedAt, evidence, limitations);
            if (api.Health().RecordFormat >= 2)
            {
                // The command ran here and was seen to finish: an observation by a tool.
                body["evidenceClass"] = "observation";
                body["observer"] = "automation";
            }

            WriteOutcome outcome = api.NewRecord(new NewRecordRequest("verification", by, CommandJson.Element(body), $"resource:{resource.Id}:{resource.Hash}"));
            if (!outcome.Ok)
            {
                return context.ReportOutcome(outcome, json);
            }

            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { verification = outcome.Written, run.Outcome, run.ExitCode, run.DurationMs, commit = observation.Commit }, ProtocolJson.Options));
            }
            else
            {
                context.Output.WriteLine($"{outcome.Written!.Path}  {outcome.Written.Id}  {outcome.Written.Hash}");
                context.Output.WriteLine($"{run.Outcome}: {text} (commit {observation.Commit[..12]})");
            }

            return Clean;
        });
    }

    public int CheckObserve(string root, string resourceId, string? repository, string? providerId, string runId, string workflow, string by, bool json)
    {
        return context.WithApi(root, api =>
        {
            RecordResponse? resource = api.Record(resourceId);
            if (resource is null || resource.Type != "resource" || !resource.Current)
            {
                return context.Fail($"No current resource has id {resourceId}.");
            }

            // The provider is the one named, else the one serving the resource's recorded location; the destination likewise.
            Composition.Register(ProviderRegistry.Current);
            JsonElement location = resource.Envelope.GetProperty("body").TryGetProperty("location", out JsonElement l) && l.ValueKind == JsonValueKind.Object ? l : default;
            IReleaseProvider? provider = null;
            string? destination = repository;
            if (!string.IsNullOrWhiteSpace(providerId))
            {
                provider = ProviderRegistry.Current.Release(providerId);
                if (provider is null)
                {
                    return context.Fail($"The provider '{providerId}' has no implementation here; nothing selected another.");
                }
            }

            if (location.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty key in location.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
                {
                    IReleaseProvider? serving = ProviderRegistry.Current.ReleaseForLocation(key.Name);
                    if (serving is not null && (provider is null || ReferenceEquals(serving, provider)))
                    {
                        provider ??= serving;
                        destination ??= key.Value.GetString();
                        break;
                    }
                }
            }

            if (provider is null || string.IsNullOrWhiteSpace(destination))
            {
                return context.Fail($"No provider here serves resource {resourceId}'s recorded location; name the provider and the repository.");
            }

            RunObservation? run;
            try
            {
                run = provider.ObserveRun(destination, runId);
            }
            catch (Exception exception) when (exception is ProviderRefusedException or ProviderUncertainException)
            {
                return context.Fail(exception.Message);
            }

            if (run is null)
            {
                return context.Fail($"No run {runId} is observable in {destination}.");
            }

            Json.JsonObject observed = Checks.VerificationOfRun(run, workflow);
            if (api.Health().RecordFormat >= 2)
            {
                // A workflow run's conclusion is the host's report of it, not an observation made here.
                observed["evidenceClass"] = "report";
                observed["observer"] = "provider";
            }

            WriteOutcome outcome = api.NewRecord(new NewRecordRequest("verification", by, CommandJson.Element(observed), $"resource:{resource.Id}:{resource.Hash}"));
            if (!outcome.Ok)
            {
                return context.ReportOutcome(outcome, json);
            }

            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { verification = outcome.Written, run.Status, run.Conclusion, commit = run.HeadSha }, ProtocolJson.Options));
            }
            else
            {
                context.Output.WriteLine($"{outcome.Written!.Path}  {outcome.Written.Id}  {outcome.Written.Hash}");
                context.Output.WriteLine($"run {run.RunId}: {run.Status}{(run.Conclusion is null ? string.Empty : ", " + run.Conclusion)} at {run.HeadSha[..12]}");
            }

            return Clean;
        });
    }
}
