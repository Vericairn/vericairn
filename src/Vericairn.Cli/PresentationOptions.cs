// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;

namespace Vericairn.Cli;

internal sealed class PresentationOptions
{
    public Option<string?> Recipient { get; } = new("--recipient") { Description = "The person addressed by human presentation (ULID); omitted means estate defaults, never an inferred person.", Recursive = true };
    public Option<string?> Assignment { get; } = new("--for-assignment") { Description = "Exact assignment for the recipient's communication override.", Recursive = true };
    public Option<string?> Depth { get; } = new("--depth") { Description = "One-view depth: short, standard or comprehensive. Canonical --json is independent of depth.", Recursive = true };
    public CommunicationSelection Read(ParseResult result) => new(result.GetValue(Recipient), result.GetValue(Assignment), result.GetValue(Depth));
}
