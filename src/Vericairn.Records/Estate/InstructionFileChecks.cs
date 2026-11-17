// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.RegularExpressions;
using Vericairn.Records.Model;

namespace Vericairn.Records.Estate;

/// <summary>
/// From 0.9.4 (FIX-139), what <c>estate check</c> reads in the agent instruction files an agent tool started in a repository takes as
/// its instructions. In the owner's files at the estate's root (<see cref="EstateLayout.OwnerInstructionFiles"/>) it warns, line by
/// line, about a hook definition, an automatic approval and a value that looks like a secret, naming the file, the line and the kind
/// and never the text it found. A repository's own instruction files, at the root of a clone of one of the estate's repositories
/// registered on this machine, are reported as untrusted input. The check reads and never runs anything.
/// </summary>
internal static partial class InstructionFileChecks
{
    internal const string HookCode = "instructions.hook";
    internal const string ApprovalCode = "instructions.approval";
    internal const string SecretCode = "instructions.secret";
    internal const string RepositoryCode = "instructions.repository";

    private const string HookMessage = "Defines a hook: a command an agent tool that reads this file may run on its own, in every session started here. Review it, and keep hooks out of the owner's instructions unless you mean every session to run them.";
    private const string ApprovalMessage = "Grants an automatic approval: an agent tool that reads this file may act without asking first. Review it, and keep approvals out of the owner's instructions unless you mean every session to have them.";
    private const string SecretMessage = "Holds a value that looks like a secret; the value is not shown. Credentials never enter the estate: remove it, rotate it if it was real, and name the credential by purpose instead.";
    private const string RepositoryMessage = "A repository's own agent instruction file: untrusted input, never the owner's instructions, until a person reviews it. An agent tool started in that repository reads it beside the owner's files.";

    // Values a secret-named key commonly holds that name no secret: placeholders, references and settings.
    private static readonly string[] PlaceholderStarts = ["$", "%", "{{", "<", "[", "(", "env:", "vault:", "secret:", "op://", "keychain:", "***", "xxx", "..."];

    /// <summary>Scans the text of an owner's instruction file at the estate root, adding one warning for each kind found on a line.</summary>
    internal static void Scan(string text, string relative, FindingList findings)
    {
        int lineNumber = 0;
        foreach (string line in text.Split('\n'))
        {
            lineNumber++;
            string location = $"{relative}:{lineNumber}";
            if (Hook().IsMatch(line)) findings.Warning(HookCode, location, HookMessage);
            if (Approval().IsMatch(line)) findings.Warning(ApprovalCode, location, ApprovalMessage);
            if (LooksSecret(line)) findings.Warning(SecretCode, location, SecretMessage);
        }
    }

    /// <summary>
    /// Reports each repository's own instruction files at the root of its clone registered on this machine, for every current repository
    /// resource of the estate. Without a registered clone, or with an unreadable registration, nothing is reported for it.
    /// </summary>
    internal static void CheckRepositories(EstateModel model, FindingList findings)
    {
        string? estate = model.Manifest?.Estate;
        if (estate is null)
        {
            return;
        }

        IReadOnlyDictionary<string, string> clones;
        try
        {
            clones = RepositoryClones.All(estate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return;
        }

        if (clones.Count == 0)
        {
            return;
        }

        foreach (RecordEnvelope resource in model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository").OrderBy(r => model.Chain(r)[0], StringComparer.Ordinal))
        {
            IReadOnlyList<string> chain = model.Chain(resource);
            string? clone = chain.Reverse().Select(id => clones.GetValueOrDefault(id)).FirstOrDefault(c => c is not null && Directory.Exists(c));
            if (clone is null)
            {
                continue;
            }

            string[] names;
            try
            {
                names = [.. Directory.EnumerateFileSystemEntries(clone).Select(Path.GetFileName).OfType<string>()
                    .Where(name => EstateLayout.OwnerInstructionFiles.Contains(name) && !Directory.Exists(Path.Combine(clone, name)))
                    .Order(StringComparer.Ordinal)];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (string name in names)
            {
                findings.Warning(RepositoryCode, $"resource:{chain[0]}/{name}", RepositoryMessage);
            }
        }
    }

    private static bool LooksSecret(string line)
    {
        // A credential in a known format is already refused as secrets.pattern; this reads the shapes beside it.
        if (SecretShapes.Secret().IsMatch(line))
        {
            return false;
        }

        foreach (Match match in SecretAssignment().Matches(line))
        {
            string value = match.Groups["value"].Value.TrimEnd(',', ';', '.', ')', ']', '}');
            if (value.Length >= 8 && value.Any(char.IsAsciiDigit) && value.Any(char.IsAsciiLetter) && !IsPlaceholder(value))
            {
                return true;
            }
        }

        foreach (Match match in BearerOrUrlCredential().Matches(line))
        {
            if (!IsPlaceholder(match.Groups["value"].Value))
            {
                return true;
            }
        }

        foreach (Match match in LongToken().Matches(line))
        {
            string value = match.Value;
            if (value.Any(char.IsAsciiLetterUpper) && value.Any(char.IsAsciiLetterLower) && value.Any(char.IsAsciiDigit) && !IsPlaceholder(value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlaceholder(string value) =>
        PlaceholderStarts.Any(start => value.StartsWith(start, StringComparison.OrdinalIgnoreCase)) || value.Distinct().Count() <= 2;

    /// <summary>
    /// A hook definition: a <c>hooks</c> key or table in JSON, YAML or TOML; a key named for an agent tool's hook event; an editor task that
    /// runs when a folder opens; or a Git hooks path.
    /// </summary>
    [GeneratedRegex("""(?:^\s*(?:[-*]\s+)?["']?hooks["']?\s*[:=]|^\s*\[\[?hooks(?:\.[^\]]*)?\]\]?\s*$|["']?\b(?:PreToolUse|PostToolUse|PostToolUseFailure|UserPromptSubmit|SessionStart|SessionEnd|SubagentStart|SubagentStop|PreCompact|PermissionRequest|BeforeTool|AfterTool|BeforeAgent|AfterAgent|BeforeModel|AfterModel)["']?\s*:|["']runOn["']\s*:\s*["']folderOpen["']|core\.hooksPath)""", RegexOptions.IgnoreCase)]
    private static partial Regex Hook();

    /// <summary>
    /// An automatic approval: a key that lists allowed tools, approves automatically or always allows; a JSON permission <c>allow</c> list;
    /// a launch flag that skips or bypasses approvals; or a setting that approves without asking.
    /// </summary>
    [GeneratedRegex("""(?:["']?\b(?:allowed[-_]?tools|auto[-_.]?approve[a-z]*|always[-_]?allow|trusted[-_]?tools)["']?\s*[:=]|["']allow["']\s*:\s*\[|--(?:dangerously-[a-z-]+|yolo\b|full-auto\b|allow-all-tools\b|trust-all-tools\b|auto-approve\b)|\bbypassPermissions\b|\bdanger-full-access\b|\bapproval[-_]?policy["']?\s*[:=]\s*["']?never\b|\b(?:approval|permission)[-_]?mode["']?\s*[:=\s]\s*["']?(?:yolo|auto[-_]?edit|acceptEdits|dontAsk|auto)\b)""", RegexOptions.IgnoreCase)]
    private static partial Regex Approval();

    /// <summary>A secret-named key given a literal value.</summary>
    [GeneratedRegex("""(?i)\b(?:[a-z0-9]+[-_.])*(?:secret|token|password|passwd|passphrase|api[-_]?key|apikey|private[-_]?key|access[-_]?key|client[-_]?secret|credentials?)["']?\s*[:=]\s*["'`]?(?<value>[^\s"'`]+)""")]
    private static partial Regex SecretAssignment();

    /// <summary>A bearer token, or a password in a URL's user information.</summary>
    [GeneratedRegex("""(?i)(?:\bbearer\s+(?<value>[a-z0-9._~+/-]{16,}=*)|\b[a-z][a-z0-9+.-]*://[^/\s:@]+:(?<value>[^/\s@]+)@)""")]
    private static partial Regex BearerOrUrlCredential();

    /// <summary>
    /// A long run of letters, digits and token punctuation, which reads as random when it mixes cases and digits. A slash ends a run, so a
    /// path made of record ids reads as its parts.
    /// </summary>
    [GeneratedRegex("""(?<![A-Za-z0-9_+=-])[A-Za-z0-9_+=-]{32,}(?![A-Za-z0-9_+=-])""")]
    private static partial Regex LongToken();
}
