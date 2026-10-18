// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Runtime.Service;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class HandoffFiles(CommandContext context)
{
    public int Resume(string root, string input, bool json) => context.WithBody(input, body => context.WithApi(root, api =>
    {
        HandoffPackage? package = body.Deserialize<HandoffPackage>(ProtocolJson.Options);
        if (package is null)
        {
            return context.Fail("A retained hand-over package is required.");
        }

        ResumeResponse response = api.ResumeHandoff(package);
        context.Output.Write(json ? response.Json is JsonElement view ? CanonicalJson.SerializePretty(view) : JsonSerializer.Serialize(response.Problem, ProtocolJson.Options) : response.Text);
        return response.Problem is not null ? Failed : response.HasErrors ? HasErrors : Clean;
    }));

    public int Handoff(string root, string? output, long? maxBytes, bool json, CommunicationSelection? presentation = null) => context.WithApi(root, api =>
    {
        HandoffResponse response = api.Handoff(maxBytes);
        if (response.Package is null)
        {
            context.Output.Write(json ? JsonSerializer.Serialize(response.Problem, ProtocolJson.Options) : response.Text);
            return Failed;
        }

        string package = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(response.Package, ProtocolJson.Options));
        if (output is not null)
        {
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(Encoding.UTF8.GetBytes(package));
        }

        context.Output.Write(json ? package : ContextTextMeasurement.Presentation(CommandPresentation.Protected(api, "handoff", response.Package.Hash, response.Text, presentation)).Text);
        return Clean;
    });
}
