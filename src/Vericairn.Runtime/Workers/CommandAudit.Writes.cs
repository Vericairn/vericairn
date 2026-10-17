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
                if (!InPlaceEditor(executable, values)) return false;
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
                bool dryRun = false;
                bool directories = executable == "install" && values.Any(v => v is "-d" or "--directory");
                bool options = true;
                for (int i = 0; i < args.Length; i++)
                {
                    string value = values[i];
                    if (options && value == "--") { options = false; continue; }
                    if (options && executable == "rsync" && (value == "--dry-run" || value.StartsWith('-') && !value.StartsWith("--", StringComparison.Ordinal) && value.Contains('n', StringComparison.Ordinal))) dryRun = true;
                    if (options && executable != "rsync" && value is "-t" or "--target-directory") { if (++i < args.Length) destination = args[i]; continue; }
                    if (options && executable != "rsync" && value.StartsWith("--target-directory=", StringComparison.Ordinal)) { destination = SuffixWord(args[i], 19); continue; }
                    if (options && executable != "rsync" && value.StartsWith("-t", StringComparison.Ordinal) && value.Length > 2) { destination = SuffixWord(args[i], 2); continue; }
                    if (options && (executable == "install" && value is "-m" or "--mode" or "-o" or "--owner" or "-g" or "--group" or "-S" or "--suffix"
                        || executable == "rsync" && value is "-e" or "--rsh" or "--exclude" or "--include" or "--filter" or "-f" or "--exclude-from" or "--include-from" or "--files-from")) { i++; continue; }
                    if (options && value.StartsWith('-')) continue;
                    operands.Add(args[i]);
                }
                for (int i = 0; i < operands.Count; i++)
                {
                    string value = Value(operands[i]);
                    if (executable == "rsync" && RemotePath(value)) { codes.Add("network"); codes.Add("audit-unverified"); continue; }
                    AddShellPath(paths, operands[i], !dryRun && (directories || destination is null && i == operands.Count - 1) ? PathAccess.Write : PathAccess.Read, context);
                }
                if (destination is not null) AddShellPath(paths, destination, PathAccess.Write, context);
                return true;
            case "dd":
                foreach (string argument in args)
                {
                    if (Value(argument).StartsWith("of=", StringComparison.Ordinal)) AddShellPath(paths, SuffixWord(argument, 3), PathAccess.Write, context);
                    if (Value(argument).StartsWith("if=", StringComparison.Ordinal)) AddShellPath(paths, SuffixWord(argument, 3), PathAccess.Read, context);
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
                    if (value == "-d" && i + 1 < args.Length) unzip.Working = ResolveDirectory(args[++i], context, codes);
                    else if (value.StartsWith("-d", StringComparison.Ordinal) && value.Length > 2) unzip.Working = ResolveDirectory(SuffixWord(args[i], 2), context, codes);
                    else if (value == "-P") i++;
                    else if (!value.StartsWith('-') && !archive) { AddShellPath(paths, args[i], PathAccess.Read, context); archive = true; }
                }
                if (extracting) AddPath(paths, ".", PathAccess.Write, unzip);
                return true;
            case "patch":
                CommandContext patch = context.Copy();
                var patchOperands = new List<string>();
                var rejects = new List<string>();
                string? output = null;
                bool patchDryRun = false;
                for (int i = 0; i < args.Length; i++)
                {
                    string value = values[i];
                    if (value == "--dry-run") patchDryRun = true;
                    else if (value is "-d" or "--directory" && i + 1 < args.Length) patch.Working = ResolveDirectory(args[++i], patch, codes);
                    else if (value.StartsWith("--directory=", StringComparison.Ordinal)) patch.Working = ResolveDirectory(SuffixWord(args[i], 12), patch, codes);
                    else if (value.StartsWith("-d", StringComparison.Ordinal) && value.Length > 2) patch.Working = ResolveDirectory(SuffixWord(args[i], 2), patch, codes);
                    else if (value is "-i" or "--input" && i + 1 < args.Length) AddShellPath(paths, args[++i], PathAccess.Read, context);
                    else if (value.StartsWith("--input=", StringComparison.Ordinal)) AddShellPath(paths, SuffixWord(args[i], 8), PathAccess.Read, context);
                    else if (value.StartsWith("-i", StringComparison.Ordinal) && value.Length > 2) AddShellPath(paths, SuffixWord(args[i], 2), PathAccess.Read, context);
                    else if (value is "-o" or "--output" && i + 1 < args.Length) output = args[++i];
                    else if (value.StartsWith("--output=", StringComparison.Ordinal)) output = SuffixWord(args[i], 9);
                    else if (value.StartsWith("-o", StringComparison.Ordinal) && value.Length > 2) output = SuffixWord(args[i], 2);
                    else if (value is "-r" or "--reject-file" && i + 1 < args.Length) rejects.Add(args[++i]);
                    else if (value.StartsWith("--reject-file=", StringComparison.Ordinal)) rejects.Add(SuffixWord(args[i], 14));
                    else if (value.StartsWith("-r", StringComparison.Ordinal) && value.Length > 2) rejects.Add(SuffixWord(args[i], 2));
                    else if (value is "-p" or "--strip" or "-F" or "--fuzz" or "-V" or "--version-control") i++;
                    else if (!value.StartsWith('-')) patchOperands.Add(args[i]);
                }
                PathAccess patchAccess = patchDryRun ? PathAccess.Read : PathAccess.Write;
                AddShellPath(paths, output ?? patchOperands.FirstOrDefault() ?? ".", patchAccess, patch);
                if (output is not null && patchOperands.Count > 0) AddShellPath(paths, patchOperands[0], PathAccess.Read, patch);
                foreach (string reject in rejects.Where(r => r != "-")) AddShellPath(paths, reject, patchAccess, patch);
                foreach (string source in patchOperands.Skip(1)) AddShellPath(paths, source, PathAccess.Read, context);
                return true;
            default:
                return false;
        }
    }

    private static bool RemotePath(string path) => path.Contains(':', StringComparison.Ordinal) && !Path.IsPathFullyQualified(path);

    private static bool InPlaceEditor(string executable, string[] values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            string value = values[i];
            if (value == "--") break;
            if (executable == "sed" && (value == "--in-place" || value.StartsWith("--in-place=", StringComparison.Ordinal))) return true;
            if (value is "--expression" or "--file") { i++; continue; }
            if (!value.StartsWith('-') || value.StartsWith("--", StringComparison.Ordinal)) continue;
            for (int flag = 1; flag < value.Length; flag++)
            {
                char option = value[flag];
                if (option == 'i') return true;
                // Do not mistake the text of an attached expression, filename or module for flags.
                if (option is 'e' or 'f' || executable == "perl" && option is 'E' or 'I' or 'M' or 'm' or 'F')
                {
                    if (flag == value.Length - 1) i++;
                    break;
                }
                if (!(executable == "sed" ? "nEruzsbl" : "panlwUTSWXcd").Contains(option, StringComparison.Ordinal)) break;
            }
        }
        return false;
    }

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
            else if (value is "-C" or "--directory" && i + 1 < args.Length) target.Working = ResolveDirectory(args[++i], target, codes);
            else if (value.StartsWith("--directory=", StringComparison.Ordinal)) target.Working = ResolveDirectory(SuffixWord(args[i], 12), target, codes);
            else if (value is "-f" or "--file" && i + 1 < args.Length) archives.Add(args[++i]);
            else if (value.StartsWith("--file=", StringComparison.Ordinal)) archives.Add(SuffixWord(args[i], 7));
            else if (value.StartsWith("-C", StringComparison.Ordinal) && value.Length > 2) target.Working = ResolveDirectory(SuffixWord(args[i], 2), target, codes);
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
                else if (value.StartsWith("--work-tree=", StringComparison.Ordinal) || value.StartsWith("--output=", StringComparison.Ordinal)) target = SuffixWord(args[i], value.IndexOf('=', StringComparison.Ordinal) + 1);
                else if (value.StartsWith("-C", StringComparison.Ordinal) && value.Length > 2) { option = "-C"; target = SuffixWord(args[i], 2); }
                if (target is not null)
                {
                    AddShellPath(paths, target, reads && option != "--output" ? PathAccess.Read : PathAccess.Write, git);
                    if (option == "-C") git.Working = ResolveDirectory(target, git, codes);
                    continue;
                }
                if (i < commandIndex && value is "-c" or "--git-dir" or "--namespace" or "--config-env")
                {
                    if (++i < values.Length && values[i].Contains("credential", StringComparison.Ordinal)) codes.Add("credentials");
                    continue;
                }
                if (command is "init" or "clone" or "worktree" && value.StartsWith("--separate-git-dir=", StringComparison.Ordinal))
                {
                    AddShellPath(paths, SuffixWord(args[i], 19), PathAccess.Write, git);
                    continue;
                }
                if (command is "init" or "clone" or "worktree" && value is "-b" or "-B" or "--branch" or "--initial-branch" or "--template" or "--separate-git-dir" or "--depth" or "-o" or "--origin" or "--reference" or "--reference-if-able" or "--reason" or "-c" or "--config" or "-u" or "--upload-pack" or "--shallow-since" or "--shallow-exclude" or "--filter" or "--revision")
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
            if (operands.Count > 0 && !RemotePath(Value(operands[0]))) AddShellPath(paths, operands[0], PathAccess.Read, git);
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
