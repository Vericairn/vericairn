// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.RegularExpressions;

namespace Vericairn.Runtime.Workers;

public static partial class CommandAudit
{
    private sealed record ShellToken(string Text, string? Input = null, bool ExpandInput = false, int Start = -1, int End = -1);
    private sealed record HereDocument(int Token, string Delimiter, bool StripTabs, bool Expand);

    // Consume input bodies before tokenizing any of their contents. A body can contain shell-looking
    // operators, commands and unmatched quotes without making any of them shell operands.
    private static ShellToken[] ShellTokens(string command, HashSet<string> codes)
    {
        var tokens = new List<ShellToken>();
        var pending = new List<HereDocument>();
        int cursor = 0;
        while (cursor < command.Length)
        {
            Match match = ShellWords().Match(command, cursor);
            if (!match.Success) break;
            cursor = match.Index + match.Length;
            string word = match.Value;
            if (word is "\n" or "\r\n" && pending.Count > 0)
            {
                foreach (HereDocument document in pending)
                {
                    var body = new StringBuilder();
                    bool terminated = false;
                    while (cursor < command.Length)
                    {
                        int end = command.IndexOf('\n', cursor);
                        if (end < 0) end = command.Length;
                        string line = command[cursor..end].TrimEnd('\r');
                        cursor = end < command.Length ? end + 1 : end;
                        if (document.StripTabs) line = line.TrimStart('\t');
                        if (line == document.Delimiter)
                        {
                            terminated = true;
                            break;
                        }

                        body.Append(line).Append('\n');
                    }

                    tokens[document.Token] = tokens[document.Token] with { Input = body.ToString(), ExpandInput = document.Expand };
                    if (!terminated) codes.Add("audit-unverified");
                }

                pending.Clear();
            }

            if (word is "<<" or "<<-" or "<<<")
            {
                Match argument = ShellWords().Match(command, cursor);
                if (!argument.Success || Separator(argument.Value) || argument.Value.StartsWith('<') || argument.Value.StartsWith('>'))
                {
                    codes.Add("audit-unverified");
                    tokens.Add(new ShellToken(word, ""));
                    continue;
                }

                cursor = argument.Index + argument.Length;
                if (word == "<<<")
                {
                    tokens.Add(new ShellToken(word, argument.Value.Trim('"', '\''), !argument.Value.StartsWith('\'')));
                }
                else
                {
                    string delimiter = argument.Value.Replace("'", "", StringComparison.Ordinal).Replace("\"", "", StringComparison.Ordinal).Replace("\\", "", StringComparison.Ordinal);
                    pending.Add(new HereDocument(tokens.Count, delimiter, word == "<<-", !argument.Value.Contains('"') && !argument.Value.Contains('\'') && !argument.Value.Contains('\\')));
                    tokens.Add(new ShellToken(word, ""));
                }
            }
            else
            {
                tokens.Add(new ShellToken(word, Start: match.Index, End: cursor));
            }
        }

        if (pending.Count > 0) codes.Add("audit-unverified");
        return tokens.ToArray();
    }
}
