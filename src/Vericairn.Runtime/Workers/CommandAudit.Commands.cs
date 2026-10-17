// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Workers;

public static partial class CommandAudit
{
    private static bool Separator(string word) => word is ";" or "&&" or "||" or "|" or "&" or "\n" or "\r\n";
    private static string Value(string word) => word.Length >= 2 && word[0] is '\'' or '"' && word[^1] == word[0] ? word[1..^1] : word;
    private static string SuffixWord(string word, int prefix) => word.Length != Value(word).Length
        ? word[0] + Value(word)[prefix..] + word[^1] : word[prefix..];
    private static string Executable(string word)
    {
        string name = Value(word).Replace('\\', '/').Split('/')[^1];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private static bool InspectCommand(string command, List<InvocationPath> paths, HashSet<string> codes, Func<string, bool> isReadOnly, CommandContext context, int depth)
    {
        if (depth > 8)
        {
            codes.Add("audit-unverified");
            return false;
        }

        ShellToken[] tokens = ShellTokens(command, codes);
        bool reads = true;
        CommandContext? pipeline = null;
        for (int start = 0; start < tokens.Length;)
        {
            int end = start;
            while (end < tokens.Length && !Separator(tokens[end].Text)) end++;
            string? separator = end < tokens.Length ? tokens[end].Text : null;
            CommandContext before = context.Copy();
            if (separator == "|") pipeline ??= before;
            bool segmentReads = false;
            if (!Check(() => segmentReads = InspectSimple(tokens[start..end], paths, codes, isReadOnly, context, depth), codes)) context.Working = null;
            reads &= segmentReads;
            // A pipeline/background child cannot change the parent's directory or variables. The
            // failure branch of || cannot assume that a preceding cd/assignment succeeded.
            if (separator is "|" or "&" or "||") context = pipeline?.Copy() ?? before;
            else if (pipeline is not null)
            {
                context = pipeline;
                pipeline = null;
            }
            start = end + 1;
        }

        return reads;
    }

    private static bool InspectSimple(ShellToken[] input, List<InvocationPath> paths, HashSet<string> codes, Func<string, bool> isReadOnly, CommandContext context, int depth)
    {
        var arguments = new List<string>();
        var bodies = new List<ShellToken>();
        bool redirects = false;
        for (int i = 0; i < input.Length; i++)
        {
            ShellToken item = input[i];
            if (item.Input is not null) { bodies.Add(item); continue; }
            string word = item.Text;
            if (!word.StartsWith('\'') && SecretEnvironment().IsMatch(word)) codes.Add("credentials");
            if (!word.StartsWith('\'') && (word.Contains("$(", StringComparison.Ordinal) || word.Contains('`', StringComparison.Ordinal))) codes.Add("audit-unverified");
            if (word is ">" or ">>" or ">|" or "&>" or "&>>" or ">&" or "<" or "<&")
            {
                // Only an adjacent descriptor belongs to the redirect. A separated number can be
                // a real filename (rm 2 > sink); PowerShell also uses an adjacent * for all streams.
                if (i > 0 && input[i - 1].End == item.Start && arguments.Count > 0
                    && (arguments[^1].All(char.IsAsciiDigit) || OperatingSystem.IsWindows() && arguments[^1] == "*")) arguments.RemoveAt(arguments.Count - 1);
                if (++i >= input.Length) { codes.Add("audit-unverified"); break; }
                string target = input[i].Text;
                if (!(word.EndsWith('&') && (target == "-" || target.All(char.IsAsciiDigit))))
                    AddShellPath(paths, target, word.StartsWith('<') ? PathAccess.Read : PathAccess.Write, context);
                redirects |= !word.StartsWith('<');
                continue;
            }
            arguments.Add(word);
        }

        int start = 0;
        CommandContext local = context.Copy();
        local.ExpansionEnvironment = context.Environment;
        while (start < arguments.Count && Assignment(arguments[start], local)) start++;
        if (start == arguments.Count)
        {
            foreach ((string name, string value) in local.Environment) context.Environment[name] = value;
            return !redirects;
        }
        start = Unwrap(arguments, start, paths, codes, local);
        if (start >= arguments.Count) return false;
        string executable = Executable(arguments[start]);
        string[] args = arguments.Skip(start + 1).ToArray();
        string[] values = args.Select(Value).ToArray();
        if (executable is "export" or "readonly")
        {
            foreach (string argument in args)
                if (!Assignment(argument, context)) codes.Add("audit-unverified");
            return !redirects;
        }
        bool reads = false;
        Check(() => reads = isReadOnly(string.Join(' ', arguments.Skip(start))), codes);
        if (NetworkCommands.Contains(executable)) codes.Add("network");
        if (executable is "printenv" or "cmdkey" or "secret-tool" or "Get-StoredCredential" or "security" or "op" or "pass"
            || executable == "gpg" && values.Any(v => v.StartsWith("--export-secret", StringComparison.Ordinal))
            || executable == "gh" && GitHubTokenOutput(values)) codes.Add("credentials");

        if (executable is "cd" or "chdir" or "Set-Location")
        {
            string? target = args.FirstOrDefault(a => !a.StartsWith('-'));
            if (target is null) target = local.Environment.GetValueOrDefault(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME");
            context.Working = target is null || values.Contains("-", StringComparer.Ordinal) ? null : ResolveDirectory(target, local, codes);
            if (context.Working is null) codes.Add("audit-unverified");
            return !redirects;
        }

        if (executable == "xargs")
        {
            // stdin supplies operands we cannot see, including replacements in otherwise literal paths.
            codes.Add("audit-unverified");
            int child = 0;
            while (child < values.Length && values[child].StartsWith('-'))
            {
                string option = values[child++];
                if (option is "-I" or "-n" or "-L" or "-P" or "-s" or "-d" or "-E" or "-a") child++;
            }
            if (child < args.Length)
                InspectCommand(string.Join(' ', args[child..]), paths, codes, isReadOnly, new CommandContext(local.Working, local.Environment), depth + 1);
            return false;
        }

        if (Shell(executable))
        {
            int code = Array.FindIndex(values, a => a is "-c" or "-lc" or "-Command" or "/c");
            if (code >= 0 && code + 1 < args.Length)
                return InspectCommand(ShellCode(args[code + 1], local), paths, codes, isReadOnly, new CommandContext(local.Working, local.Environment), depth + 1) && !redirects;
        }

        bool classified = InspectWrites(executable, args, paths, codes, local);
        if (Interpreter(executable) && !classified)
        {
            codes.Add("audit-unverified");
            for (int i = 1; i < args.Length; i++)
                if (values[i - 1] is "-c" or "-e" or "-Command") InspectCode(ShellCode(args[i], local), paths, codes, local);
        }
        foreach (ShellToken body in bodies)
        {
            if (Interpreter(executable)) InspectCode(body.Input!, paths, codes, local);
            else if (body.ExpandInput && SecretEnvironment().IsMatch(body.Input!)) codes.Add("credentials");
        }

        if (executable.Equals("git", StringComparison.OrdinalIgnoreCase))
            InspectGit(args, paths, codes, local, reads);
        else if (!classified)
        {
            int pathCount = paths.Count;
            int operand = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string token = values[i];
                if (executable is "npm" or "pnpm" or "yarn" or "pip" or "pip3" && token is "install" or "ci" or "add"
                    || executable == "dotnet" && token == "restore") codes.Add("network");
                if (Interpreter(executable) && i > 0 && values[i - 1] is "-c" or "-e" or "-Command") continue;
                if (executable == "sed" && token is "-e" or "--expression") { i++; continue; }
                if (executable == "sed" && token is "-f" or "--file")
                {
                    if (++i < args.Length) AddShellPath(paths, args[i], PathAccess.Read, local);
                    continue;
                }
                if (executable.EndsWith("-Content", StringComparison.OrdinalIgnoreCase) && token.Equals("-Value", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
                if (executable is "rg" or "grep" && token is "-g" or "--glob" or "--iglob" or "-e" or "--regexp" or "-t" or "--type" or "-T" or "--type-not") { i++; continue; }
                if (token.StartsWith('-') || token.Contains("://", StringComparison.Ordinal) || executable is "echo" or "printf") continue;
                operand++;
                bool pattern = executable is "rg" or "grep" or "sed" or "awk" && operand == 1
                    && !values.Contains("--files", StringComparer.Ordinal)
                    && !values.Any(t => t is "-e" or "--regexp" or "--expression" or "-f" or "--file" || t.StartsWith("--regexp=", StringComparison.Ordinal)
                        || executable == "sed" && (t.StartsWith("-e", StringComparison.Ordinal) || t.StartsWith("-f", StringComparison.Ordinal) || t.StartsWith("--expression=", StringComparison.Ordinal) || t.StartsWith("--file=", StringComparison.Ordinal)));
                if (!pattern && (reads && executable is not ("pwd" or "Get-Location") || MutatingCommands.Contains(executable) || PathLike(token)))
                    AddShellPath(paths, args[i], reads ? PathAccess.Read : MutatingCommands.Contains(executable) ? PathAccess.Write : PathAccess.Unknown, local);
            }
            if (paths.Count == pathCount && executable is "ls" or "find" or "rg" or "Get-ChildItem") AddPath(paths, ".", reads ? PathAccess.Read : PathAccess.Unknown, local);
        }
        return reads && !redirects && !classified;
    }

    private static bool PathLike(string value) => value.IndexOfAny(['/', '\\', '.', '~', '$', '`', '%']) >= 0;
    private static bool Shell(string executable) => executable is "bash" or "sh" or "zsh" or "pwsh" or "powershell" or "cmd";
    private static bool Interpreter(string executable) => Shell(executable) || executable is "python" or "python3" or "node" or "ruby" or "perl" or "osascript";

    private static bool GitHubTokenOutput(string[] values)
    {
        if (values.Length < 2 || values[0] != "auth") return false;
        if (values[1] == "token") return true;
        if (values[1] != "status") return false;
        bool showToken = false;
        for (int i = 2; i < values.Length; i++)
        {
            string value = values[i];
            if (value == "--") break;
            if (value is "--hostname" or "--jq" or "--json" or "--template") { i++; continue; }
            if (value == "--show-token") { showToken = true; continue; }
            if (value.StartsWith("--show-token=", StringComparison.Ordinal))
            {
                showToken = Enabled(value[13..]);
                continue;
            }
            if (!value.StartsWith('-') || value.StartsWith("--", StringComparison.Ordinal)) continue;
            for (int flag = 1; flag < value.Length; flag++)
            {
                // -a and -t are booleans; -h consumes the rest of this word or the next word.
                // A hostname containing 't' is not a combined token-display flag.
                if (value[flag] == 'h') { if (flag == value.Length - 1) i++; break; }
                if (value[flag] is not ('a' or 't')) break;
                bool attached = flag + 1 < value.Length && value[flag + 1] == '=';
                if (value[flag] == 't') showToken = !attached || Enabled(value[(flag + 2)..]);
                if (attached) break;
            }
        }
        return showToken;

        static bool Enabled(string value) => value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("t", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddShellPath(List<InvocationPath> paths, string word, PathAccess access, CommandContext context) =>
        paths.Add(new InvocationPath(ShellPath(word, context), access, context.Working));

    private static string ShellPath(string word, CommandContext context) => word.StartsWith('\'') && word.EndsWith('\'')
        ? Value(word) : ExpandPath(word.Replace("\"", "", StringComparison.Ordinal), context.ExpansionEnvironment ?? context.Environment);

    private static string ShellCode(string word, CommandContext context)
    {
        if (word.StartsWith('\'') && word.EndsWith('\'')) return Value(word);
        string expanded = ExpandPath(Value(word), context.ExpansionEnvironment ?? context.Environment);
        return word.StartsWith('"') && word.EndsWith('"') ? DoubleQuotedEscape().Replace(expanded, "$1") : expanded;
    }

    private static string? ResolveDirectory(string word, CommandContext context, HashSet<string> codes)
    {
        string? resolved = null;
        Check(() => resolved = ResolvePath(ShellPath(word, context), context.Working), codes);
        if (resolved is null) codes.Add("audit-unverified");
        return resolved;
    }

    private static bool Assignment(string word, CommandContext context)
    {
        int equal = word.IndexOf('=', StringComparison.Ordinal);
        if (equal <= 0 || !word[..equal].All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return false;
        context.Environment[word[..equal]] = ShellPath(word[(equal + 1)..], context);
        return true;
    }

    private static int Unwrap(List<string> args, int start, List<InvocationPath> paths, HashSet<string> codes, CommandContext context)
    {
        while (start < args.Count)
        {
            string wrapper = Executable(args[start]);
            if (wrapper is not ("env" or "sudo" or "nohup" or "time" or "nice" or "timeout" or "command" or "exec" or "busybox")) break;
            start++;
            while (start < args.Count && args[start].StartsWith('-'))
            {
                string option = Value(args[start++]);
                if (option == "--") break;
                bool value = wrapper switch
                {
                    "env" => option is "-u" or "--unset" or "-C" or "--chdir" or "-S" or "--split-string",
                    "sudo" => option is "-u" or "--user" or "-g" or "--group" or "-h" or "--host" or "-p" or "--prompt" or "-C" or "--close-from" or "-D" or "--chdir",
                    "nice" => option is "-n" or "--adjustment",
                    "timeout" => option is "-s" or "--signal" or "-k" or "--kill-after",
                    "time" => option is "-f" or "--format" or "-o" or "--output",
                    _ => false,
                };
                if (wrapper == "env" && option is "-i" or "--ignore-environment") context.Environment.Clear();
                if (value && start >= args.Count) { codes.Add("audit-unverified"); return start; }
                string? argument = value ? args[start++] : option.Contains('=', StringComparison.Ordinal) ? option[(option.IndexOf('=', StringComparison.Ordinal) + 1)..] : null;
                string name = option.Split('=')[0];
                if (argument is not null)
                {
                    if (wrapper == "env" && name is "-u" or "--unset") context.Environment.Remove(Value(argument));
                    if (wrapper == "env" && name is "-C" or "--chdir" || wrapper == "sudo" && name is "-D" or "--chdir")
                    {
                        context.Working = ResolveDirectory(argument, context, codes);
                    }
                    if (wrapper == "time" && name is "-o" or "--output") AddShellPath(paths, argument, PathAccess.Write, context);
                    if (wrapper == "env" && name is "-S" or "--split-string") codes.Add("audit-unverified");
                }
            }
            if (wrapper == "timeout" && start < args.Count) start++;
            while (start < args.Count && Assignment(args[start], context)) start++;
            if (wrapper == "env" && start == args.Count) codes.Add("credentials");
        }
        return start;
    }
}
