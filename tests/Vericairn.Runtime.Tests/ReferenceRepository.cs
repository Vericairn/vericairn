// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text;
using Vericairn.Packs.Software;
using Vericairn.Runtime.Policies;
using Vericairn.Runtime.Workers;

namespace Vericairn.Runtime.Tests;

// A real local repository: generated sets enter a later commit than their build sources.
internal sealed class ReferenceRepository : IDisposable
{
    private static readonly string[] GitSettings = ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false"];
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "vericairn-reference-source-" + Guid.NewGuid().ToString("N"));
    internal string SourceCommit { get; }

    internal ReferenceRepository()
    {
        Directory.CreateDirectory(Root);
        Git("init", "--quiet");
        Git("config", "core.autocrlf", "false");
        Write("src/program.cs", "// Build source\n");
        Write("tools/generator.cs", "// Generator source\n");
        SourceCommit = Commit();
    }

    internal void Write(string path, string text)
    {
        string full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text, new UTF8Encoding(false));
    }

    internal void Retain(string version, string? sourceCommit = null, string? extraCommand = null)
    {
        string commit = sourceCommit ?? SourceCommit;
        PolicyRepository generated = ReferenceInventoryTests.Repository(commit: commit);
        var references = generated.Files.Skip(1).ToDictionary(f => f.Path, f => f.Bytes.ToArray(), StringComparer.Ordinal);
        IEnumerable<ReferenceItem> items = ReferenceInventoryTests.Items;
        if (extraCommand is not null)
        {
            references["cli-reference.md"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(references["cli-reference.md"]) + "\n## `" + extraCommand + "`\n");
            items = items.Append(new("command", extraCommand));
        }
        string prefix = "docs/releases/" + version + "/";
        foreach (var (path, bytes) in references) Write(prefix + path, Encoding.UTF8.GetString(bytes));
        Write(prefix + ReferenceInventory.FileName, ReferenceInventory.Generate(commit, items, references));
    }

    internal string Commit()
    {
        Git("add", ".");
        Git("commit", "--quiet", "--allow-empty", "-m", "Retain fixture sources");
        string commit = Git("rev-parse", "HEAD");
        Git("update-ref", "refs/remotes/origin/main", commit);
        Git("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");
        return commit;
    }

    internal string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in GitSettings.Concat(arguments))
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30000));
        Assert.True(process.ExitCode == 0, error);
        return output.Trim();
    }

    public void Dispose() => WorkspaceCleanup.Delete(Root);
}
