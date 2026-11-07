// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using Vericairn.Records.Types;

namespace Vericairn.Runtime.Providers;

/// <summary>
/// One security advisory as a reader may report it: identity, state, severity and the hash of the provider's whole response for it.
/// Nothing else of the advisory is kept: never its summary, description, reporter or credits.
/// </summary>
/// <param name="Identifier">The GHSA identifier, validated.</param>
/// <param name="State">triage, draft, published, closed or withdrawn; unknown for any other value.</param>
/// <param name="Severity">critical, high, medium or low; unknown when none or any other value.</param>
/// <param name="ContentHash">sha256 over the advisory's canonical response, so any change to it is a new revision.</param>
public sealed record SecurityAdvisory(string Identifier, string State, string Severity, string ContentHash)
{
    /// <summary>The states a reader names; any other value is reported as unknown.</summary>
    public static readonly ImmutableArray<string> States = ["triage", "draft", "published", "closed", "withdrawn"];

    /// <summary>The severities a reader names; none or any other value is reported as unknown.</summary>
    public static readonly ImmutableArray<string> Severities = ["critical", "high", "medium", "low"];

    /// <summary>Throws unless every field is in its closed form; a reader never passes provider text through.</summary>
    public void Validate()
    {
        if (!Patterns.GitHubAdvisory.IsMatch(Identifier) || !(States.Contains(State, StringComparer.Ordinal) || State == "unknown")
            || !(Severities.Contains(Severity, StringComparer.Ordinal) || Severity == "unknown") || !Records.Hashing.ContentHash.IsValid(ContentHash))
            throw new ProviderRefusedException("A security advisory was reported outside its closed form.");
    }
}

/// <summary>Every advisory of one repository, in every state, as one complete listing.</summary>
public sealed record SecurityAdvisoryListing(string Repository, IReadOnlyList<SecurityAdvisory> Advisories);

/// <summary>
/// Security reports, contract version 1: lists a repository's security advisories, drafts and private reports included, read-only.
/// A listing is complete or the read is refused; a reader never reports a partial listing, never writes, never reads or stores a
/// credential, and never returns provider text. It keeps its own spending within a share of the allowance it reads under.
/// </summary>
public interface ISecurityAdvisoryReader
{
    /// <summary>The version implemented by every reader.</summary>
    const string Contract = "vericairn.provider.security-advisories/1";

    /// <summary>The stable provider id a product's declaration names.</summary>
    string Provider { get; }

    /// <summary>The capabilities and repository location keys this reader serves.</summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>Every advisory of the repository; throws <see cref="ProviderRefusedException"/> when the listing cannot be complete.</summary>
    SecurityAdvisoryListing Read(string repository);
}

/// <summary>An independent in-memory implementation for conformance tests; qualifies no external service.</summary>
public sealed class MemorySecurityAdvisoryReader(string provider = "memory-security-advisories") : ISecurityAdvisoryReader
{
    private readonly object gate = new();
    private readonly Dictionary<string, List<SecurityAdvisory>> repositories = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string Provider => provider;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new(provider, ISecurityAdvisoryReader.Contract, "implemented", ["read"],
        ["In-memory conformance implementation; reads no external service."], Locations: ["github"]);

    /// <summary>A refusal every read throws while set, as a missing login, a network failure or a rate-limit stop would.</summary>
    public string? Refusal { get; set; }

    /// <summary>How many reads were requested.</summary>
    public int Reads { get; private set; }

    /// <summary>Supplies or replaces one advisory of a repository.</summary>
    public void Set(string repository, SecurityAdvisory advisory)
    {
        ArgumentNullException.ThrowIfNull(advisory);
        advisory.Validate();
        lock (gate)
        {
            List<SecurityAdvisory> list = repositories.TryGetValue(repository, out List<SecurityAdvisory>? existing) ? existing : repositories[repository] = [];
            list.RemoveAll(a => a.Identifier == advisory.Identifier);
            list.Add(advisory);
        }
    }

    /// <inheritdoc />
    public SecurityAdvisoryListing Read(string repository)
    {
        lock (gate)
        {
            Reads++;
            if (Refusal is not null) throw new ProviderRefusedException(Refusal);
            if (!Patterns.GitHubRepository.IsMatch(repository)) throw new ProviderRefusedException("The repository must be a GitHub owner/name.");
            return new(repository, repositories.TryGetValue(repository, out List<SecurityAdvisory>? list) ? list.ToArray() : []);
        }
    }
}
