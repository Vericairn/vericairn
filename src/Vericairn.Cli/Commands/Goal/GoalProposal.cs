// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Cli;

internal sealed record GoalProposal(string By, string Title, string Objective, string[] Done, string? Sponsor, string? Delegate, string[] Constraints, string[] Budget, string? Roster, string Kind, string[] WorkOrders, string[] Inputs, string? Deadline, string? Supersedes);
