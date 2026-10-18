// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Estate;

/// <summary>Versioned named-input grammar: fixed literals or bounded explicit identities, paths and instants, never ambient defaults.</summary>
public static class DocumentInputs
{
    private static readonly ImmutableArray<string> Tokens = ["@ulid", "@timestamp", "@sha256", "@commit", "@path", "@text"];

    /// <summary>Whether a descriptor's grammar token is supported. Other nonempty values are literal alternatives.</summary>
    public static bool Supported(string value) => !string.IsNullOrWhiteSpace(value) && (!value.StartsWith('@') || Tokens.Contains(value));

    /// <summary>Whether a supplied value matches at least one declared literal or typed alternative.</summary>
    public static bool Accepts(ImmutableArray<string> grammar, string value) => grammar.Any(rule => rule switch
    {
        "@ulid" => Ulid.IsValid(value),
        "@timestamp" => RecordParser.IsTimestamp(value),
        "@sha256" => ContentHash.IsValid(value),
        "@commit" => Patterns.Commit.IsMatch(value),
        "@path" => DocumentPatterns.IsSafePath(value),
        "@text" => value.Length is > 0 and <= 500 && value.All(c => !char.IsControl(c)),
        _ => value == rule,
    });
}
