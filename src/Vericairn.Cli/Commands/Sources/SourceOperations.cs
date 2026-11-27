// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Providers;
using Vericairn.Packs.Software;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class SourceOperations(CommandContext context)
{
    public int SourcesInspect(string path, string? boundary, bool json)
    {
        try
        {
            ISourceAdapter? adapter = CommandProviders.Git(context, out int failure);
            if (adapter is null)
            {
                return failure;
            }

            RepositoryObservation observation = adapter.Inspect(path, boundary);
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(observation, ProtocolJson.Options));
            }
            else
            {
                PrintObservation(observation);
                context.Output.WriteLine("Read-only: nothing in the repository changed.");
            }

            return Clean;
        }
        catch (ProviderRefusedException exception)
        {
            return context.Fail(exception.Message);
        }
    }

    public int SourcesAdopt(string root, string path, string by, string name, string role, string visibility, string? notes, string? boundary, bool json)
    {
        RepositoryObservation observation;
        try
        {
            observation = (CommandProviders.Git(context, out int failure) ?? throw new ProviderRefusedException("No git adapter is available in this distribution; repositories cannot be observed here.")).Inspect(path, boundary);
        }
        catch (ProviderRefusedException exception)
        {
            return context.Fail(exception.Message);
        }

        return context.WithApi(root, api =>
        {
            WriteOutcome resource = api.NewRecord(new NewRecordRequest("resource", by, CommandJson.Element(SourceAdoption.ResourceBody(name, role, visibility, observation, notes))));
            if (!resource.Ok)
            {
                return context.ReportOutcome(resource, json);
            }

            WriteOutcome observed = api.NewRecord(new NewRecordRequest("observation", by, CommandJson.Element(SourceAdoption.ObservationBody(observation)), $"resource:{resource.Written!.Id}:{resource.Written.Hash}"));
            if (!observed.Ok)
            {
                return context.ReportOutcome(observed, json);
            }

            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { resource = resource.Written, observation = observed.Written, commit = observation.Commit, clean = observation.Clean,
                    observation.IgnoredConfiguration, observation.ConfigurationHash, observation.WorktreeConfigurationHash, observation.GitRedirections }, ProtocolJson.Options));
            }
            else
            {
                context.Output.WriteLine($"resource     {resource.Written.Id}  {resource.Written.Path}");
                context.Output.WriteLine($"observation  {observed.Written!.Id}  {observed.Written.Path}");
                PrintObservation(observation);
                context.Output.WriteLine("Adopted by observation only: the repository is a resource the estate uses and does not own; uncommitted work is preserved and nothing in it changed.");
            }

            return Clean;
        });
    }

    /// <summary>Registers this machine's clone of an adopted repository, for the sources that read it, such as living documents of other repositories; nothing is recorded.</summary>
    public int SourcesClone(string root, string resourceId, string path, string? boundary, bool json)
    {
        RepositoryObservation observation;
        try
        {
            observation = (CommandProviders.Git(context, out int failure) ?? throw new ProviderRefusedException("No git adapter is available in this distribution; repositories cannot be observed here.")).Inspect(path, boundary);
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

            // The clone is the repository the resource names: by its root commit where recorded, else by its GitHub remote.
            JsonElement location = resource.Envelope.GetProperty("body").GetProperty("location");
            string? rootCommit = location.TryGetProperty("rootCommit", out JsonElement r) ? r.GetString() : null;
            string? github = location.TryGetProperty("github", out JsonElement g) ? g.GetString() : null;
            bool matches = rootCommit is not null ? rootCommit == observation.RootCommit : github is not null && string.Equals(SourceAdoption.GitHubRepository(observation.Remote), github, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                return context.Fail($"That working tree is not the repository of resource {resource.Id}: its {(rootCommit is not null ? "root commit" : "GitHub remote")} differs.");
            }

            RepositoryClones.Register(api.Health().Estate, resource.Id, path);
            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new { resource = resource.Id, commit = observation.Commit,
                    observation.IgnoredConfiguration, observation.ConfigurationHash, observation.WorktreeConfigurationHash, observation.GitRedirections }, ProtocolJson.Options));
            }
            else
            {
                context.Output.WriteLine($"Registered this machine's clone of resource {resource.Id} at {observation.Commit[..12]}; the path stays in this machine's configuration, never in a record.");
                PrintAcquisition(observation);
            }
            return Clean;
        });
    }

    public int SourcesObserve(string root, string path, string resourceId, string by, string? boundary, bool json)
    {
        RepositoryObservation observation;
        try
        {
            observation = (CommandProviders.Git(context, out int failure) ?? throw new ProviderRefusedException("No git adapter is available in this distribution; repositories cannot be observed here.")).Inspect(path, boundary);
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

            int result = context.ReportOutcome(api.NewRecord(new NewRecordRequest("observation", by, CommandJson.Element(SourceAdoption.ObservationBody(observation)), $"resource:{resource.Id}:{resource.Hash}")), json);
            if (result == Clean) PrintAcquisition(observation, json ? context.Error : context.Output);
            return result;
        });
    }

    private void PrintObservation(RepositoryObservation observation)
    {
        context.Output.WriteLine($"commit      {observation.Commit}{(observation.Branch is null ? "  (detached)" : "  on " + observation.Branch)}");
        context.Output.WriteLine($"remote      {(SourceAdoption.IsNetworkRemote(observation.Remote) ? observation.Remote : observation.Remote is null ? "none" : "a path on this machine (not recorded)")}");
        context.Output.WriteLine($"identity    root commit {observation.RootCommit}{(SourceAdoption.GitHubRepository(observation.Remote) is string github ? ", github " + github : string.Empty)}");
        context.Output.WriteLine($"working     {(observation.Clean ? "clean" : $"{observation.Modified} modified, {observation.Untracked} untracked")}");
        context.Output.WriteLine($"tags        {(observation.Tags.Count == 0 ? "none" : string.Join(", ", observation.Tags))}");
        PrintAcquisition(observation);
        foreach (RecentCommit commit in observation.Recent)
        {
            context.Output.WriteLine($"  {commit.Commit[..12]}  {commit.At}  {commit.Summary}");
        }
    }
    private void PrintAcquisition(RepositoryObservation observation, TextWriter? writer = null)
    {
        writer ??= context.Output;
        if (observation.ConfigurationHash is { } hash) writer.WriteLine($"git config  {hash}");
        if (observation.WorktreeConfigurationHash is { } worktree) writer.WriteLine($"worktree config  {worktree}");
        foreach (string key in observation.IgnoredConfiguration) writer.WriteLine($"ignored git configuration  {key}");
        foreach (string member in observation.GitRedirections) writer.WriteLine($"git redirection inside boundary  {member}");
    }
}
