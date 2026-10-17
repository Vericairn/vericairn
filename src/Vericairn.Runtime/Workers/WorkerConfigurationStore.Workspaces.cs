// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

public sealed partial class WorkerConfigurationStore
{
    private sealed record WorkspaceConfiguration(string Root);

    /// <summary>The machine-local root for every launcher's workspaces, or null for the stock location. Read and validated afresh.</summary>
    public string? ReadWorkspaceRoot()
    {
        try
        {
            string path = ConfigurationPath("workspaces.json");
            RefuseLink(path);
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 65536) throw new ProviderRefusedException("The workspace configuration exceeds its size limit.");
            WorkspaceConfiguration configuration = JsonSerializer.Deserialize<WorkspaceConfiguration>(File.ReadAllBytes(path), Json)
                ?? throw new JsonException();
            return ValidateWorkspaceRoot(configuration.Root);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ProviderRefusedException("The local workspace configuration could not be read; use vericairn worker configure --workspaces <dir> to replace it. No default fallback is permitted.", exception);
        }
    }

    /// <summary>Sets the root for every launcher on this machine, outside repositories and estates, never in a record.</summary>
    public void WriteWorkspaceRoot(string root)
    {
        string path = ConfigurationPath("workspaces.json");
        RefuseLink(path);
        string validated = ValidateWorkspaceRoot(root);
        if (!Directory.Exists(validated)) CreateOwnerDirectory(validated);
        WriteFile(path, new WorkspaceConfiguration(validated));
    }

    private static string ValidateWorkspaceRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new ProviderRefusedException("The workspace root must be an absolute directory outside repositories and estates.");
        RefuseLink(root);
        string full = CanonicalDirectory(root);
        var found = new List<string>();
        for (DirectoryInfo? parent = new(full); parent is not null; parent = parent.Parent)
        {
            if (File.Exists(parent.FullName)) throw new ProviderRefusedException("The workspace root must be a directory.");
            if (RepositoryOrEstate(parent.FullName))
                throw new ProviderRefusedException("The workspace root must be outside repositories and estates.");
            foreach (string name in new[] { ".claude", ".mcp.json", ".codex", ".agents" })
            {
                string configuration = Path.Combine(parent.FullName, name);
                if (File.Exists(configuration) || Directory.Exists(configuration) || new FileInfo(configuration).LinkTarget is not null)
                    found.Add(configuration);
            }
        }
        if (found.Count > 0)
            throw new ProviderRefusedException("The workspace root or its ancestors hold harness configuration: " + string.Join(", ", found)
                + ". Choose a root outside those directories with vericairn worker configure --workspaces <dir>.");
        return full;
    }
}
