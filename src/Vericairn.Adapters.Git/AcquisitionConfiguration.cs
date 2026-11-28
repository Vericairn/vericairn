// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Hashing;

namespace Vericairn.Adapters.Git;

/// <summary>Names repository configuration that an acquisition refuses to carry, without exposing its values.</summary>
internal sealed class AcquisitionConfiguration
{
    private static readonly HashSet<string> BooleanSettings = new(StringComparer.Ordinal)
    {
        "core.filemode", "core.ignorecase", "core.precomposeunicode", "core.symlinks", "core.trustctime",
        "core.sparsecheckout", "core.sparsecheckoutcone", "index.sparse",
    };
    private static readonly HashSet<string> BooleanValues = new(StringComparer.OrdinalIgnoreCase) { "true", "false", "yes", "no", "on", "off", "1", "0" };
    private static readonly HashSet<string> Metadata = new(StringComparer.Ordinal)
    {
        "user.name", "user.email",
    };
    private readonly SortedSet<string> findings = new(StringComparer.Ordinal);
    private readonly List<string> comparison = [];

    internal AcquisitionConfiguration(AcquisitionGitView view, Func<string, string> parse)
    {
        ConfigurationHash = ContentHash.Sha256(view.Config);
        WorktreeConfigurationHash = view.WorktreeConfig is { } bytes ? ContentHash.Sha256(bytes) : null;
        Read(view.Config, worktree: false);
        if (view.WorktreeConfig is { } worktree) Read(worktree, worktree: true);
        if (Remote is { } remote && !RepositoryAddress.IsNetwork(remote) && view.LocalOrigin(remote) is null)
        {
            findings.Add("remote.origin.url (unsupported transport or outside operator boundary)");
            Remote = null;
        }

        void Read(byte[] contents, bool worktree)
        {
            string copy = Path.Combine(view.Directory, worktree ? "observed-config.worktree" : "observed-config");
            File.WriteAllBytes(copy, contents);
            foreach (string entry in parse(copy).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int delimiter = entry.IndexOf('\n', StringComparison.Ordinal);
                string name = delimiter < 0 ? entry : entry[..delimiter];
                string value = delimiter < 0 ? "true" : entry[(delimiter + 1)..];
                if (name == "extensions.refstorage" && value != "files")
                    throw new Vericairn.Runtime.Providers.ProviderRefusedException("The repository uses an unsupported Git reference storage format.");
                if (worktree)
                {
                    findings.Add("config.worktree: " + FindingName(name));
                    continue;
                }
                if (BooleanSettings.Contains(name) && BooleanValues.Contains(value)
                    || name == "core.autocrlf" && (BooleanValues.Contains(value) || value == "input")
                    || name == "core.eol" && value is "lf" or "crlf" or "native"
                    || name == "core.checkstat" && value is "default" or "minimal")
                {
                    // Git receives only these plain comparison and layout settings, never an executable or a file path.
                    comparison.Add(name + "=" + value);
                }
                else if (name == "extensions.objectformat" && value is "sha1" or "sha256" && !worktree)
                {
                    ObjectFormat = value;
                }
                else if (name == "remote.origin.url" && !worktree)
                {
                    Remote ??= value;
                }
                else if (name == "remote.origin.fetch" && value == "+refs/heads/*:refs/remotes/origin/*")
                {
                    // The usual origin mapping describes a clone; it is never passed back to Git.
                }
                else if (!Metadata.Contains(name)
                    && !(name is "core.bare" or "core.logallrefupdates" && BooleanValues.Contains(value))
                    && !(name == "core.repositoryformatversion" && value is "0" or "1"))
                {
                    findings.Add(FindingName(name));
                }
            }
        }
    }

    private static string FindingName(string name)
    {
        int section = name.IndexOf('.', StringComparison.Ordinal);
        int member = name.LastIndexOf('.');
        return section >= 0 && member > section ? name[..section] + ".<subsection>" + name[member..] : name;
    }

    internal string ObjectFormat { get; private set; } = "sha1";

    internal string? Remote { get; private set; }

    internal string ConfigurationHash { get; }

    internal string? WorktreeConfigurationHash { get; }

    internal IReadOnlyList<string> Findings => [.. findings];

    internal IReadOnlyList<string> Comparison => comparison;
}
