// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Runtime.Workers;

public static partial class CommandAudit
{
    private static void InspectCode(string code, List<InvocationPath> paths, HashSet<string> codes, CommandContext context)
    {
        codes.Add("audit-unverified");
        Check(() => { if (IsMatch(NetworkCode(), code)) codes.Add("network"); }, codes);
        Check(() => { if (IsMatch(SecretEnvironment(), code)) codes.Add("credentials"); }, codes, credentials: true);
        Check(() =>
        {
            foreach (Match word in Matches(ShellWords(), code))
            {
                string text = Value(word.Value);
                if (NetworkCommands.Contains(text)) codes.Add("network");
                if (IsMatch(SecretPath(), text.Replace('\\', '/'))) codes.Add("credentials");
            }
        }, codes, credentials: true);
        Check(() =>
        {
            bool[] syntax = CodeSyntax(code);
            foreach (Match match in Matches(CodePaths(), code))
            {
                if (!syntax[match.Index]) continue;
                string function = match.Groups["function"].Value;
                string mode = match.Groups["mode"].Value;
                PathAccess access = function is "open" ? mode.IndexOfAny(['w', 'a', 'x', '+']) >= 0 ? PathAccess.Write : PathAccess.Read
                    : function.StartsWith("read", StringComparison.Ordinal) ? PathAccess.Read : PathAccess.Write;
                paths.Add(new InvocationPath(match.Groups["path"].Value, access, context.Working));
            }
            foreach (Match match in Matches(PathlibWrites(), code))
                if (syntax[match.Index]) paths.Add(new InvocationPath(match.Groups["path"].Value, PathAccess.Write, context.Working));
        }, codes, credentials: true);
    }

    // A call printed as sample text or placed in a comment is not a literal write invocation.
    // Mask common string/comment forms without evaluating code. Template expressions and language
    // constructs this small scanner cannot interpret retain only the opaque-code warning.
    private static bool[] CodeSyntax(string code)
    {
        var syntax = new bool[code.Length];
        for (int i = 0; i < code.Length; i++)
        {
            char token = code[i];
            if (token == '#' || token == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n') i++;
            }
            else if (token == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/')) i++;
                i++;
            }
            else if (token is '\'' or '"' or '`')
            {
                bool triple = token != '`' && i + 2 < code.Length && code[i + 1] == token && code[i + 2] == token;
                i += triple ? 3 : 1;
                for (; i < code.Length; i++)
                {
                    if (code[i] == '\\') { i++; continue; }
                    if (code[i] == token && (!triple || i + 2 < code.Length && code[i + 1] == token && code[i + 2] == token))
                    {
                        if (triple) i += 2;
                        break;
                    }
                }
            }
            else syntax[i] = true;
        }
        return syntax;
    }

    // These are visible literal targets, not an evaluation of code, control flow, aliases or bindings.
    // All interpreter input retains audit-unverified even when a recognizable literal write is found.
    [GeneratedRegex("\\b(?<function>open|readFileSync|readFile|writeFileSync|writeFile|appendFileSync|appendFile|mkdirSync|mkdir|unlinkSync|unlink|rmSync|File\\.write|File\\.delete)\\s*\\(\\s*['\"](?<path>[^'\"]+)['\"](?:\\s*,\\s*(?:mode\\s*=\\s*)?['\"](?<mode>[^'\"]*)['\"])?", RegexOptions.CultureInvariant, 100)]
    private static partial Regex CodePaths();

    [GeneratedRegex("\\bPath\\s*\\(\\s*['\"](?<path>[^'\"]+)['\"]\\s*\\)\\s*\\.\\s*(?:write_text|write_bytes|touch|mkdir|unlink|rmdir)\\s*\\(", RegexOptions.CultureInvariant, 100)]
    private static partial Regex PathlibWrites();
}
