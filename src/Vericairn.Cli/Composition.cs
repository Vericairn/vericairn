// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Adapters.Git;
using Vericairn.Packs.Software;
using Vericairn.Providers.GitHub;
using Vericairn.Runtime.Providers;

namespace Vericairn.Cli;

/// <summary>
/// What this distribution ships beside the foundation: the GitHub provider, the git adapter, and the software pack's git-file source
/// for living documents of other repositories, its coding-agent launchers (Claude Code, Codex and the generic one) and its Git repository
/// workspaces, registered as available. Being
/// available selects nothing: a release goes through the provider its product declares or its request names, and a provider with
/// no implementation here is refused. Registration keeps an implementation already registered (tests register in-memory ones).
/// </summary>
internal static class Composition
{
    /// <summary>Registers the shipped implementations unless the registry already holds one for the same provider or adapter.</summary>
    public static void Register(ProviderRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.RegisterIfAbsent(new GitHubReleaseProvider());
        registry.RegisterIfAbsent(new GitHubPullRequestProvider());
        registry.PushAdapter ??= new GitPushAdapter();
        registry.SecretScanner ??= new GitleaksScanner();
        registry.RecordCommitAdapter ??= new GitRecordCommitAdapter();
        registry.RegisterIfAbsent(new GitCommandAdapter());
        registry.RegisterIfAbsent(new ClaudeCodeLauncher());
        registry.RegisterIfAbsent(new CodexLauncher());
        registry.RegisterIfAbsent(new GenericLauncher());
        registry.RegisterIfAbsent(new GitWorktreeWorkspaces());
        Records.Estate.DocumentSources.Register(new GitFileSource());
    }
}
