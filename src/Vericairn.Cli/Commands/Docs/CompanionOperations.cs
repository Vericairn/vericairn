// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class CompanionOperations(CommandContext context)
{
    internal int Coverage(string root, bool json) => context.WithApi(root, api =>
    {
        CompanionsResponse response = api.Companions();
        context.Output.WriteLine(json ? JsonSerializer.Serialize(response, ProtocolJson.Options) : CompanionPresentation.Coverage(response));
        return response.Status == "success" ? Clean : Failed;
    });

    internal int Render(string root, CompanionRequest request, bool sources, bool json) => context.WithApi(root, api =>
    {
        CompanionResponse response = sources ? api.CompanionSources(request) : api.CompanionRender(request);
        context.Output.WriteLine(json ? JsonSerializer.Serialize(response, ProtocolJson.Options) : CompanionPresentation.Render(response));
        return response.Status == "success" ? Clean : Failed;
    });

    internal int Compare(string root, string selection, string? commit, bool json) => context.WithApi(root, api =>
    {
        CompanionComparison response = api.CompanionCompare(new(selection, commit));
        context.Output.WriteLine(json ? JsonSerializer.Serialize(response, ProtocolJson.Options) : CompanionPresentation.Compare(response));
        return response.Status != "success" ? Failed : response.Currentness == "current" ? Clean : HasErrors;
    });
}
