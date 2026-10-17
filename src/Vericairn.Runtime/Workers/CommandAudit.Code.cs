// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Runtime.Workers;

public static partial class CommandAudit
{
    private static void InspectCode(string code, List<InvocationPath> paths, HashSet<string> codes, CommandContext context)
    {
        codes.Add("audit-unverified");
        Check(() => { if (NetworkCode().IsMatch(code)) codes.Add("network"); }, codes);
        Check(() => { if (SecretEnvironment().IsMatch(code)) codes.Add("credentials"); }, codes);
        Check(() =>
        {
            foreach (Match word in ShellWords().Matches(code))
            {
                string text = Value(word.Value);
                if (NetworkCommands.Contains(text)) codes.Add("network");
                if (SecretPath().IsMatch(text.Replace('\\', '/'))) codes.Add("credentials");
            }
        }, codes);
        Check(() =>
        {
            foreach (Match match in CodePaths().Matches(code))
            {
                string function = match.Groups["function"].Value;
                string mode = match.Groups["mode"].Value;
                PathAccess access = function is "open" ? mode.IndexOfAny(['w', 'a', 'x', '+']) >= 0 ? PathAccess.Write : PathAccess.Read
                    : function.StartsWith("read", StringComparison.Ordinal) ? PathAccess.Read : PathAccess.Write;
                paths.Add(new InvocationPath(match.Groups["path"].Value, access, context.Working));
            }
            foreach (Match match in PathlibWrites().Matches(code)) paths.Add(new InvocationPath(match.Groups["path"].Value, PathAccess.Write, context.Working));
        }, codes);
    }

    // These are visible literal targets, not an evaluation of code, control flow, aliases or bindings.
    // All interpreter input retains audit-unverified even when a recognizable literal write is found.
    [GeneratedRegex("\\b(?<function>open|readFileSync|readFile|writeFileSync|writeFile|appendFileSync|appendFile|mkdirSync|mkdir|unlinkSync|unlink|rmSync|File\\.write|File\\.delete)\\s*\\(\\s*['\"](?<path>[^'\"]+)['\"](?:\\s*,\\s*(?:mode\\s*=\\s*)?['\"](?<mode>[^'\"]*)['\"])?", RegexOptions.CultureInvariant, 100)]
    private static partial Regex CodePaths();

    [GeneratedRegex("\\bPath\\s*\\(\\s*['\"](?<path>[^'\"]+)['\"]\\s*\\)\\s*\\.\\s*(?:write_text|write_bytes|touch|mkdir|unlink|rmdir)\\s*\\(", RegexOptions.CultureInvariant, 100)]
    private static partial Regex PathlibWrites();
}
