// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

internal sealed class EstateViews(CommandContext context)
{
    public int Status(string root, bool json, bool observe = false, string? by = null, CommunicationSelection? presentation = null)
    {
        if (observe || by is not null)
        {
            if (!observe || string.IsNullOrWhiteSpace(by))
            {
                return context.Fail("Recording an observation requires both --observe and --by with a human principal id.");
            }

            return context.WithApi(root, api =>
            {
                WriteOutcome written = api.ObserveEstate(new EstateObserveRequest(by));
                if (!written.Ok)
                {
                    return context.ReportOutcome(written, json);
                }

                StatusResponse observed = api.Status();
                context.Output.Write(json ? CanonicalJson.SerializePretty(observed.Json) : CommandPresentation.Status(api, observed, presentation));
                return observed.HasErrors ? HasErrors : Clean;
            });
        }

        if (!IsEstate(root))
        {
            return context.WithFiles(root, model =>
            {
                FindingList checks = EstateChecks.Run(model, CommandProviders.SourceAdapter?.IgnoredEstateEntries(model.Root));
                context.Output.Write(json ? EstateStatus.RenderJson(model, checks) : EstateStatus.Render(model, checks));
                return model.Findings.HasErrors || checks.HasErrors ? HasErrors : Clean;
            });
        }

        return context.WithApi(root, api =>
        {
            StatusResponse status = api.Status();
            context.Output.Write(json ? CanonicalJson.SerializePretty(status.Json) : CommandPresentation.Status(api, status, presentation));
            return status.HasErrors ? HasErrors : Clean;
        });
    }

    /// <summary>Recorded progress with labelled local claims and worker observations, through the API; exit 1 when the check it shows found errors, as status does.</summary>
    public int Progress(string root, bool json, CommunicationSelection? presentation = null) => context.WithApi(root, api =>
    {
        ProgressResponse progress = api.Progress();
        context.Output.Write(json ? CanonicalJson.SerializePretty(progress.Json) : CommandPresentation.Protected(api, "status", "estate progress", progress.Text, presentation) + CommunicationPresentation.RenderAttention(CommunicationPresentation.Attention(api, presentation?.Recipient, () => progress)));
        return progress.HasErrors ? HasErrors : Clean;
    });

    /// <summary>What waits on a principal, or on every principal, through the API: at most three asks in one delivery to each, the recommendation first.</summary>
    public int Ask(string root, string? to, bool json) => context.WithApi(root, api =>
    {
        if (to is not null && !Records.Identity.Ulid.IsValid(to))
        {
            return context.Fail($"--to names a principal by its ULID, not '{to}'.");
        }

        AsksResponse asks = api.Asks(to);
        context.Output.Write(json ? CanonicalJson.SerializePretty(asks.Json) : asks.Text);
        return Clean;
    });

    public int Decisions(string root, string? id, bool json, CommunicationSelection? presentation = null) => context.WithApi(root, api =>
    {
        if (id is null)
        {
            DecisionsResponse decisions = api.Decisions();
            context.Output.Write(json ? CanonicalJson.SerializePretty(decisions.Json) : CommandPresentation.Protected(api, "decision", "decision index", decisions.Text, presentation));
            return Clean;
        }

        DecisionResponse? decision = api.Decision(id);
        if (decision is null)
        {
            return context.Fail($"No decision has id {id}.");
        }

        context.Output.Write(json ? CanonicalJson.SerializePretty(decision.Body) : CommandPresentation.Protected(api, "decision", id, decision.Text, presentation));
        return Clean;
    });
}
