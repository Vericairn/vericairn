// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Workers;

public static partial class CommandAudit
{
    private static bool ShortFlag(string value, string flags, string arguments = "")
    {
        if (!value.StartsWith('-') || value.StartsWith("--", StringComparison.Ordinal)) return false;
        foreach (char option in value.Skip(1))
        {
            if (flags.Contains(option, StringComparison.Ordinal)) return true;
            if (arguments.Contains(option, StringComparison.Ordinal)) break;
        }
        return false;
    }

    private static void InspectZip(string[] args, List<InvocationPath> paths, HashSet<string> codes, CommandContext context)
    {
        bool recursive = false;
        bool options = true;
        var operands = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string value = Value(args[i]);
            if (options && value == "--") { options = false; continue; }
            if (options && (value == "--recurse-paths" || ShortFlag(value, "rR", "bOPs"))) recursive = true;
            if (options && value is "-x" or "--exclude" or "-i" or "--include")
            {
                // Selection patterns are not sources. Their effect on an archive is not evaluated.
                codes.Add("audit-unverified");
                while (i + 1 < args.Length && Value(args[i + 1]) != "@") i++;
                i++;
                continue;
            }
            if (options && value is "-b" or "--temp-path" or "-O" or "--out")
            {
                if (++i < args.Length) AddShellPath(paths, args[i], PathAccess.Write, context);
                continue;
            }
            if (options && value == "-P") { i++; continue; }
            if (options && value.StartsWith('-')) { if (value == "-@") codes.Add("audit-unverified"); continue; }
            operands.Add(args[i]);
        }
        if (operands.Count == 0) { codes.Add("audit-unverified"); return; }
        if (Value(operands[0]) != "-") AddShellPath(paths, operands[0], PathAccess.Write, context);
        foreach (string source in operands.Skip(1)) AddShellPath(paths, source, PathAccess.Read, context, recursive);
    }

    private static bool InspectMake(string[] args, List<InvocationPath> paths, HashSet<string> codes, CommandContext context)
    {
        CommandContext make = context.Copy();
        string? prefix = make.Environment.GetValueOrDefault("PREFIX");
        string? staging = make.Environment.GetValueOrDefault("DESTDIR");
        bool install = false;
        bool dryRun = false;
        bool options = true;
        for (int i = 0; i < args.Length; i++)
        {
            string value = Value(args[i]);
            if (options && value == "--") { options = false; continue; }
            if (options && value is "-C" or "--directory" && i + 1 < args.Length) make.Working = ResolveDirectory(args[++i], make, codes);
            else if (options && value.StartsWith("--directory=", StringComparison.Ordinal)) make.Working = ResolveDirectory(SuffixWord(args[i], 12), make, codes);
            else if (options && value.StartsWith("-C", StringComparison.Ordinal) && value.Length > 2) make.Working = ResolveDirectory(SuffixWord(args[i], 2), make, codes);
            else if (options && value is "-f" or "--file" or "--makefile" or "-I" or "--include-dir" or "--eval" or "-o" or "--old-file" or "-W" or "--what-if") i++;
            else if (options && (value is "--dry-run" or "--just-print" or "--recon" or "--question" || ShortFlag(value, "nq", "fICoWjl"))) dryRun = true;
            else if (value.StartsWith("PREFIX=", StringComparison.Ordinal)) prefix = ShellPath(SuffixWord(args[i], 7), context);
            else if (value.StartsWith("DESTDIR=", StringComparison.Ordinal)) staging = ShellPath(SuffixWord(args[i], 8), context);
            else if (value == "install") install = true;
        }
        if (!install) return false;
        // DESTDIR stages an absolute installation prefix under another root. Makefiles remain
        // arbitrary programs; only these visible installation targets support a path verdict.
        string? target = string.IsNullOrEmpty(staging) ? prefix : staging.TrimEnd('/', '\\') + "/" + (prefix ?? "").TrimStart('/', '\\');
        if (string.IsNullOrEmpty(target)) codes.Add("audit-unverified");
        else AddPath(paths, target, dryRun ? PathAccess.Read : PathAccess.Write, make);
        return true;
    }

    private static bool InspectDefaults(string[] args, List<InvocationPath> paths, HashSet<string> codes, CommandContext context)
    {
        int action = 0;
        while (action < args.Length && Value(args[action]).StartsWith('-'))
        {
            string option = Value(args[action++]);
            if (option == "-host") action++;
            else if (option != "-currentHost") return false;
        }
        if (action >= args.Length || Value(args[action]) != "write") return false;
        if (++action >= args.Length) { codes.Add("audit-unverified"); return true; }
        string domain = ShellPath(args[action], context);
        if (Unresolved(domain)) codes.Add("audit-unverified");
        else if (domain.IndexOfAny(['/', '\\']) >= 0 || domain is "." or "..") AddPath(paths, domain, PathAccess.Write, context);
        else codes.Add("scope"); // Named domains and global/application preferences live outside the workspace.
        return true;
    }
}
