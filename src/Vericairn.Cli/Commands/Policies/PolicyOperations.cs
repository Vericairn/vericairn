// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Policies;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class PolicyOperations(CommandContext context)
{
    internal int Hash(string path, bool json) => context.WithBody(path, body =>
    {
        PolicyHashResponse response = PolicyPreparation.Hash(new(body));
        if (response.Problem is { } problem) return context.Fail(problem.Message);
        context.Output.WriteLine(json ? JsonSerializer.Serialize(response, ProtocolJson.Options) : response.DefinitionHash);
        return Clean;
    });

    internal int List(string root, bool json) => context.WithApi(root, api => Report(api.Policies(), json));
    internal int Show(string root, string key, bool json) => context.WithApi(root, api =>
        api.Policy(key) is { } policy ? Report(policy, json) : context.Fail("No policy has that key or id."));
    internal int Check(string root, string? asOf, string? release, bool json) => context.WithApi(root, api => Report(api.PolicyEvaluate(asOf, release), json));
    internal int Kinds(string root, bool json) => context.WithApi(root, api =>
    {
        IReadOnlyList<PolicyKindDto> kinds = api.PolicyKinds();
        context.Output.WriteLine(json ? JsonSerializer.Serialize(kinds, ProtocolJson.Options) : string.Join('\n', kinds.Select(k =>
            $"{k.Kind}/{k.Version} {k.ImplementationDigest}; selections {string.Join(", ", k.Selections)}; sources {string.Join(", ", k.SourceContracts)}; bounds {k.MaximumRecords} records, {k.MaximumFindings} findings")));
        return Clean;
    });
    internal int Writing(string root, string? id, bool json) => context.WithApi(root, api =>
    {
        WritingExceptionsResponse? response = id is null ? api.WritingExceptions() : api.WritingException(id);
        if (response is null) return context.Fail("No writing exception has that id.");
        context.Output.WriteLine(json ? JsonSerializer.Serialize(response, ProtocolJson.Options) : response.Text);
        return response.Diagnostics.Any(d => d.Severity == "error") ? HasErrors : Clean;
    });
    private int Report(PolicyResponse response, bool json)
    {
        context.Output.WriteLine(json ? JsonSerializer.Serialize(response, ProtocolJson.Options) : response.Text);
        return response.Problem is not null ? Failed : response.Diagnostics.Any(d => d.Severity == "error") ? HasErrors : Clean;
    }
}
