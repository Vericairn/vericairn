// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Workers;

public static partial class CommandAudit
{
    private static bool InspectWrites(string executable, string[] args, List<InvocationPath> paths, HashSet<string> codes, CommandContext context)
    {
        string[] values = args.Select(Value).ToArray();
        switch (executable)
        {
            case "sed":
            case "perl":
                if (!values.Any(v => v.StartsWith("--in-place", StringComparison.Ordinal) || v.StartsWith('-') && !v.StartsWith("--", StringComparison.Ordinal)
                    && (executable == "sed" ? v.StartsWith("-i", StringComparison.Ordinal) : v.Contains('i', StringComparison.Ordinal)))) return false;
                bool script = values.Any(v => v is "-e" or "--expression" or "-f" or "--file" || v.StartsWith("--expression=", StringComparison.Ordinal)
                    || v.StartsWith("--file=", StringComparison.Ordinal) || v.StartsWith("-e", StringComparison.Ordinal) && v.Length > 2);
                bool files = false;
                for (int i = 0; i < args.Length; i++)
                {
                    string value = values[i];
                    if (!files && value == "--") { files = true; continue; }
                    if (!files && value is "-e" or "--expression") { i++; continue; }
                    if (!files && value is "-f" or "--file") { if (++i < args.Length) AddShellPath(paths, args[i], PathAccess.Read, context); continue; }
                    if (!files && value.StartsWith('-'))
                    {
                        if (value == "-i" && i + 1 < args.Length && values[i + 1].Length == 0) i++; // BSD sed's empty backup suffix.
                        continue;
                    }
                    if (!script) { script = true; continue; }
                    AddShellPath(paths, args[i], PathAccess.Write, context);
                }
                return true;
            case "rsync":
            case "install":
            case "cp":
                var operands = new List<string>();
                string? destination = null;
                bool directories = executable == "install" && values.Any(v => v is "-d" or "--directory");
                bool options = true;
                for (int i = 0; i < args.Length; i++)
                {
                    string value = values[i];
                    if (options && value == "--") { options = false; continue; }
                    if (options && value is "-t" or "--target-directory") { if (++i < args.Length) destination = args[i]; continue; }
                    if (options && value.StartsWith("--target-directory=", StringComparison.Ordinal)) { destination = value[19..]; continue; }
                    if (options && (executable == "install" && value is "-m" or "--mode" or "-o" or "--owner" or "-g" or "--group" or "-S" or "--suffix"
                        || executable == "rsync" && value is "-e" or "--rsh" or "--exclude" or "--include" or "--filter" or "-f" or "--exclude-from" or "--include-from" or "--files-from")) { i++; continue; }
                    if (options && value.StartsWith('-')) continue;
                    operands.Add(args[i]);
                }
                for (int i = 0; i < operands.Count; i++)
                {
                    string value = Value(operands[i]);
                    if (executable == "rsync" && RemotePath(value)) { codes.Add("network"); codes.Add("audit-unverified"); continue; }
                    AddShellPath(paths, operands[i], directories || destination is null && i == operands.Count - 1 ? PathAccess.Write : PathAccess.Read, context);
                }
                if (destination is not null) AddShellPath(paths, destination, PathAccess.Write, context);
                return true;
            case "dd":
                foreach (string argument in args)
                {
                    if (argument.StartsWith("of=", StringComparison.Ordinal)) AddShellPath(paths, argument[3..], PathAccess.Write, context);
                    if (argument.StartsWith("if=", StringComparison.Ordinal)) AddShellPath(paths, argument[3..], PathAccess.Read, context);
                }
                return true;
            case "tar":
                InspectTar(args, paths, codes, context);
                return true;
            case "unzip":
                bool extracting = !values.Any(v => v.StartsWith('-') && !v.StartsWith("-d", StringComparison.Ordinal) && v.IndexOfAny(['l', 't', 'p', 'Z']) >= 0);
                CommandContext unzip = context.Copy();
                bool archive = false;
                for (int i = 0; i < args.Length; i++)
                {
                    string value = values[i];
                    if (value == "-d" && i + 1 < args.Length) unzip.Working = ResolvePath(ShellPath(args[++i], context), context.Working);
                    else if (value.StartsWith("-d", StringComparison.Ordinal) && value.Length > 2) unzip.Working = ResolvePath(ShellPath(args[i][2..], context), context.Working);
                    else if (value == "-P") i++;
                    else if (!value.StartsWith('-') && !archive) { AddShellPath(paths, args[i], PathAccess.Read, context); archive = true; }
                }
                if (extracting) AddPath(paths, ".", PathAccess.Write, unzip);
                return true;
            case "patch":
                CommandContext patch = context.Copy();
                var patchOperands = new List<string>();
                string? output = null;
                for (int i = 0; i < args.Length; i++)
                {
                    string value = values[i];
                    if (value is "-d" or "--directory" && i + 1 < args.Length) patch.Working = ResolvePath(ShellPath(args[++i], patch), patch.Working);
                    else if (value.StartsWith("--directory=", StringComparison.Ordinal)) patch.Working = ResolvePath(ShellPath(value[12..], patch), patch.Working);
                    else if (value is "-i" or "--input" && i + 1 < args.Length) AddShellPath(paths, args[++i], PathAccess.Read, context);
                    else if (value is "-o" or "--output" && i + 1 < args.Length) output = args[++i];
                    else if (value.StartsWith("--output=", StringComparison.Ordinal)) output = value[9..];
                    else if (value is "-p" or "--strip" or "-F" or "--fuzz" or "-V" or "--version-control") i++;
                    else if (!value.StartsWith('-')) patchOperands.Add(args[i]);
                }
                AddShellPath(paths, output ?? patchOperands.FirstOrDefault() ?? ".", PathAccess.Write, patch);
                foreach (string source in patchOperands.Skip(1)) AddShellPath(paths, source, PathAccess.Read, context);
                return true;
            default:
                return false;
        }
    }

    private static bool RemotePath(string path) => path.Contains(':', StringComparison.Ordinal) && !Path.IsPathFullyQualified(path);

    private static void InspectTar(string[] args, List<InvocationPath> paths, HashSet<string> codes, CommandContext context)
    {
        bool extract = false;
        bool create = false;
        var sources = new List<string>();
        var archives = new List<string>();
        CommandContext target = context.Copy();
        for (int i = 0; i < args.Length; i++)
        {
            string value = Value(args[i]);
            if (value is "--extract" or "--get") extract = true;
            else if (value is "--create" or "--append" or "--update") create = true;
            else if (value is "-C" or "--directory" && i + 1 < args.Length) target.Working = ResolvePath(ShellPath(args[++i], target), target.Working);
            else if (value.StartsWith("--directory=", StringComparison.Ordinal)) target.Working = ResolvePath(ShellPath(value[12..], target), target.Working);
            else if (value is "-f" or "--file" && i + 1 < args.Length) archives.Add(args[++i]);
            else if (value.StartsWith("--file=", StringComparison.Ordinal)) archives.Add(value[7..]);
            else if (value.StartsWith("-C", StringComparison.Ordinal) && value.Length > 2) target.Working = ResolvePath(ShellPath(value[2..], target), target.Working);
            else if (value.StartsWith('-') && !value.StartsWith("--", StringComparison.Ordinal) || i == 0 && value.All(char.IsAsciiLetter))
            {
                extract |= value.Contains('x', StringComparison.Ordinal);
                create |= value.IndexOfAny(['c', 'r', 'u']) >= 0;
                int file = value.IndexOf('f', StringComparison.Ordinal);
                if (file >= 0)
                {
                    if (file + 1 < value.Length) archives.Add(value[(file + 1)..]);
                    else if (i + 1 < args.Length) archives.Add(args[++i]);
                }
            }
            else if (!value.StartsWith('-')) sources.Add(args[i]);
        }
        foreach (string archive in archives.Where(a => a != "-")) AddShellPath(paths, archive, create ? PathAccess.Write : PathAccess.Read, context);
        if (extract) AddPath(paths, ".", PathAccess.Write, target);
        foreach (string source in sources) AddShellPath(paths, source, extract ? PathAccess.Write : PathAccess.Read, target);
        if (!extract && !create && archives.Count == 0) codes.Add("audit-unverified");
    }

    private static void InspectGit(string[] args, List<InvocationPath> paths, HashSet<string> codes, CommandContext context, bool reads)
    {
        string[] values = args.Select(Value).ToArray();
        int commandIndex = GitSubcommand(values, 0, values.Length);
        string? command = commandIndex >= 0 ? values[commandIndex] : null;
        if (command is "fetch" or "pull" or "push" or "clone" or "ls-remote") codes.Add("network");
        bool pathsOnly = false;
        var operands = new List<string>();
        CommandContext git = context.Copy();
        for (int i = 0; i < args.Length; i++)
        {
            string value = values[i];
            if (i > commandIndex && value == "--") { pathsOnly = true; continue; }
            if (!pathsOnly && i > commandIndex && command is not null && GitMessageCommands.Contains(command))
            {
                if (value is "-m" or "--message") { i++; continue; }
                if (value.StartsWith("--message=", StringComparison.Ordinal)) continue;
            }
            if (value.Contains("credential", StringComparison.Ordinal)) codes.Add("credentials");
            if (!pathsOnly)
            {
                string? target = null;
                string option = value.Split('=')[0];
                if (value is "-C" or "--work-tree" or "--output" && i + 1 < args.Length) target = args[++i];
                else if (value.StartsWith("--work-tree=", StringComparison.Ordinal) || value.StartsWith("--output=", StringComparison.Ordinal)) target = value[(value.IndexOf('=', StringComparison.Ordinal) + 1)..];
                else if (value.StartsWith("-C", StringComparison.Ordinal) && value.Length > 2) { option = "-C"; target = value[2..]; }
                if (target is not null)
                {
                    AddShellPath(paths, target, PathAccess.Write, git);
                    if (option == "-C") git.Working = ResolvePath(ShellPath(target, git), git.Working);
                    continue;
                }
                if (i < commandIndex && value is "-c" or "--git-dir" or "--namespace" or "--config-env")
                {
                    if (++i < values.Length && values[i].Contains("credential", StringComparison.Ordinal)) codes.Add("credentials");
                    continue;
                }
                if (command is "init" or "clone" or "worktree" && value is "-b" or "-B" or "--branch" or "--initial-branch" or "--template" or "--separate-git-dir" or "--depth" or "-o" or "--origin" or "--reference" or "--reference-if-able" or "--reason")
                {
                    if (++i < args.Length && value is "--separate-git-dir") AddShellPath(paths, args[i], PathAccess.Write, git);
                    continue;
                }
                if (value.StartsWith('-')) continue;
                if (i > commandIndex && command is not null && GitReadCommands.Contains(command) && GitObjectPath(value))
                {
                    if (SecretPath().IsMatch(value[(value.IndexOf(':', StringComparison.Ordinal) + 1)..].Replace('\\', '/'))) codes.Add("credentials");
                    continue;
                }
            }
            if (i <= commandIndex) continue;
            operands.Add(args[i]);
        }
        if (command == "init") AddShellPath(paths, operands.FirstOrDefault() ?? ".", PathAccess.Write, git);
        else if (command == "clone")
        {
            if (operands.Count > 1) AddShellPath(paths, operands[1], PathAccess.Write, git);
            else AddPath(paths, ".", PathAccess.Write, git);
        }
        else if (command == "worktree" && operands.Count > 0 && Value(operands[0]) == "add")
        {
            if (operands.Count > 1) AddShellPath(paths, operands[1], PathAccess.Write, git);
            else codes.Add("audit-unverified");
        }
        else foreach (string operand in operands)
        {
            if (!operand.Contains("://", StringComparison.Ordinal) && (reads || PathLike(operand)))
                AddShellPath(paths, operand, reads ? PathAccess.Read : PathAccess.Unknown, git);
        }
    }
}
