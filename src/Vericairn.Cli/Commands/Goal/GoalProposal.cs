// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Cli;

internal sealed record GoalProposal(string By, string Title, string Objective, string[] Done, string? Sponsor, string? Delegate, string[] Constraints, string[] Budget, string? Roster, string Kind, string[] WorkOrders, string[] Inputs, string? Deadline, string? Supersedes, string? Flow = null, string[]? Subject = null, string[]? FlowOptions = null, string? Until = null)
{
    /// <summary>From 0.9.4, the standing flow a standing agent runs once per occurrence, as id@version.</summary>
    public string? Standing { get; init; }

    /// <summary>From 0.9.4, a standing agent's triggers, each as JSON.</summary>
    public string[]? Triggers { get; init; }

    /// <summary>From 0.9.4, a standing agent's period caps, each as unit=value.</summary>
    public string[]? Period { get; init; }

    /// <summary>From 0.9.4, the runtime that dispatches a standing agent unattended, by its identity's public-key hash.</summary>
    public string? RunsOn { get; init; }

    /// <summary>From 0.9.4, the results awaiting review that defer a standing agent's dispatch; three when omitted.</summary>
    public long? AwaitingReview { get; init; }
}
