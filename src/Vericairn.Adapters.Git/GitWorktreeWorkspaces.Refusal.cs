// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;

namespace Vericairn.Adapters.Git;

public sealed partial class GitWorktreeWorkspaces
{
    /// <inheritdoc />
    public string? DiscardUnused(PreparedWorkspace workspace, WorkspaceSnapshot? exported, string brief)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(brief);
        if (workspace.Reused) return "It belongs to an earlier preparation or run.";
        if (!reviews.TryGetValue(workspace, out Review? review) || review.Unused is not { Problem: null, Identity: not null } before)
            return "Its preparation inventory is incomplete or its branch already held work beyond the requested base.";

        bool removed = false;
        try
        {
            WorkspaceSnapshot after = WorkspaceScopeProbe.Capture(workspace.Path, WorkspaceScopeAudit.Deadline, auditCommand, includeGit: true);
            if (after.Problem is not null || before.Identity != after.Identity)
                return "Its complete contents and original directory identity could not be verified.";
            if (before.Files.Any(file => after.Files.GetValueOrDefault(file.Key) != file.Value)
                || after.Files.Any(file => !before.Files.ContainsKey(file.Key) && !Exported(file.Key, file.Value)))
                return "Its files or Git state changed after preparation.";

            // The catalogue is worker-owned, outside the workspace. Do not erase a later preparation's registration.
            if (File.Exists(review.Registration))
            {
                Location? registered = JsonSerializer.Deserialize<Location>(WorkspaceGitScope.Read(review.Registration, 65536));
                if (registered is null || string.IsNullOrWhiteSpace(registered.Path)
                    || !Same(registered.Path, workspace.Path) || registered.CloneHead != review.Expected || registered.Preparation != review.Preparation)
                    return "Its local workspace registration changed after preparation.";
            }
            else if (review.Registered) return "Its local workspace registration is unavailable.";

            WorkspaceCleanup.Delete(workspace.Path);
            removed = true;
            File.Delete(review.Registration);
            reviews.Remove(workspace);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ProviderRefusedException or JsonException)
        {
            // A stale catalogue entry for an absent directory does not block a retry, including under another root.
            return removed ? null : "The unused workspace could not be safely removed; inspect it before retrying.";
        }

        bool Exported(string name, string fingerprint) => name switch
        {
            ".vericairn" => fingerprint == "directory",
            ".vericairn/.gitignore" => ExportMatches(name, fingerprint, "*\n"),
            ".vericairn/brief.md" => ExportMatches(name, fingerprint, brief),
            _ => false,
        };

        bool ExportMatches(string name, string fingerprint, string content) => exported is { Problem: null }
            && exported.RunFiles?.GetValueOrDefault(name) == fingerprint && !fingerprint.StartsWith("link:", StringComparison.Ordinal)
            && fingerprint.EndsWith(":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))), StringComparison.Ordinal);
    }
}
