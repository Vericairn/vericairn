// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vericairn.Records.Signing;

/// <summary>How the vericairn-enclave helper answered its availability or version check.</summary>
internal enum EnclaveHelperAnswer
{
    /// <summary>With its version and commands: a helper from 0.9.5 on.</summary>
    Version,

    /// <summary>With its usage line, which lists the commands it serves: a helper from before 0.9.5, which has no version command.</summary>
    Usage,

    /// <summary>Not at all: it did not start, did not answer in time or failed in another way.</summary>
    None,

    /// <summary>The helper did not complete its availability check.</summary>
    AvailabilityFailed,
}

/// <summary>
/// From 0.9.5 (FIX-169): what the vericairn-enclave helper beside the command line says of itself, asked before the first signature
/// through it, and whether this command line may sign through it. A helper that does not serve a command the command line calls, or
/// whose version is older than <see cref="SecureEnclaveCustody.HelperVersion"/>, is refused, naming both versions and that the helper and
/// the command line come from the same release archive. Its words are this command line's own: the helper's output is read only for a
/// version and command names of a fixed form, so no path or other text of the helper's reaches a refusal.
/// </summary>
/// <param name="Answer">How the helper answered.</param>
/// <param name="Version">The version it reported, in the form <c>major.minor.patch</c>; null when it reported none or one of another form.</param>
/// <param name="Commands">The commands it serves, from its version answer or, for a helper before 0.9.5, its usage line.</param>
internal sealed record EnclaveHelperReport(EnclaveHelperAnswer Answer, string? Version, ImmutableArray<string> Commands)
{
    internal static EnclaveHelperReport FailedAvailability { get; } = new(EnclaveHelperAnswer.AvailabilityFailed, null, []);

    /// <summary>Whether the helper serves a command.</summary>
    internal bool Serves(string command) => Commands.Contains(command, StringComparer.Ordinal);

    /// <summary>The commands this command line calls that the helper does not serve.</summary>
    internal ImmutableArray<string> Missing => [.. SecureEnclaveCustody.HelperCommands.Where(command => !Serves(command))];

    /// <summary>Whether the helper is older than the helper version this command line was built with; one that reports no version is.</summary>
    internal bool Older => EnclaveHelperCheck.Parse(Version) is not System.Version reported || reported < EnclaveHelperCheck.Expected;

    /// <summary>Whether the helper is newer than the helper version this command line was built with: it signs, and doctor flags it.</summary>
    internal bool Newer => EnclaveHelperCheck.Parse(Version) is System.Version reported && reported > EnclaveHelperCheck.Expected;

    /// <summary>Whether this command line refuses to sign through the helper: it is older, or it does not serve a command the command line calls.</summary>
    internal bool Refused => Older || !Missing.IsEmpty;

    /// <summary>What the helper said of itself, as a phrase that follows "the helper".</summary>
    internal string Description => Answer switch
    {
        EnclaveHelperAnswer.None => "did not answer when asked for its version",
        EnclaveHelperAnswer.AvailabilityFailed => "could not complete its availability check",
        EnclaveHelperAnswer.Usage when !Serves("sign-record") => "reports no version and serves no sign-record, so it comes from a release before 0.9.4",
        EnclaveHelperAnswer.Usage => "reports no version, so it comes from a release before 0.9.5",
        _ when Version is null => "reports a version this command line cannot read",
        _ when !Missing.IsEmpty => $"reports version {Version} but serves no {string.Join(" or ", Missing)}",
        _ => "reports version " + Version,
    };

    /// <summary>
    /// The refusal of a helper this command line does not sign through, naming the helper's version, the command line's, the version it
    /// needs and where the right helper comes from; null when it signs through it.
    /// </summary>
    internal string? Refusal(string commandLine) => Refused ? Refusal(commandLine, Description) : null;

    internal static string Refusal(string commandLine, string description) =>
        $"The {SecureEnclaveCustody.HelperName} helper beside this command line cannot sign for it: the helper {description}, and this command line is "
        + $"version {commandLine}, which needs {SecureEnclaveCustody.HelperName} {SecureEnclaveCustody.HelperVersion} or later. The helper and the command "
        + $"line come from the same release archive: put the {SecureEnclaveCustody.HelperName} from this command line's archive beside it. Nothing was signed.";

    /// <summary>
    /// doctor's account of the helper beside the command line: its version beside the command line's, and a mismatch flagged, when it is
    /// older, newer or does not serve a command the command line calls.
    /// </summary>
    internal string Doctor(string commandLine)
    {
        string helper = Answer == EnclaveHelperAnswer.Version && Version is not null && Missing.IsEmpty
            ? $"{SecureEnclaveCustody.HelperName} {Version}"
            : $"{SecureEnclaveCustody.HelperName} {Description}";
        string expected = $"vericairn {commandLine} expects {SecureEnclaveCustody.HelperName} {SecureEnclaveCustody.HelperVersion}";
        if (Refused)
            return $"{helper}; {expected}: mismatch, so Secure Enclave signatures are refused until the {SecureEnclaveCustody.HelperName} from this command line's release archive is beside it";
        return Newer
            ? $"{helper}; {expected}: mismatch, the helper is newer than this command line; take both from the same release archive"
            : $"{helper}; {expected}";
    }
}

/// <summary>From 0.9.5 (FIX-169): asks the vericairn-enclave helper its version and reads the answer (<see cref="EnclaveHelperReport"/>).</summary>
internal static partial class EnclaveHelperCheck
{
    private const string UsagePrefix = "usage: " + SecureEnclaveCustody.HelperName + " ";
    private static readonly ConcurrentDictionary<string, (long Length, DateTime Written, EnclaveHelperReport Report)> Asked = new(StringComparer.Ordinal);

    /// <summary>The helper version this command line was built with, as a number.</summary>
    internal static Version Expected { get; } = Parse(SecureEnclaveCustody.HelperVersion) ?? throw new InvalidOperationException("The expected helper version is malformed.");

    /// <summary>This command line's version, as <c>vericairn version</c> prints it.</summary>
    internal static string CommandLineVersion { get; } =
        typeof(EnclaveHelperCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    internal static EnclaveHelperMismatchException Refuse(string description) =>
        new(EnclaveHelperReport.Refusal(CommandLineVersion, description));

    /// <summary>
    /// Asks a helper its version and the commands it serves, once for each helper file as it is: an answer is kept while the file's size
    /// and modification time are unchanged, and no answer, from a helper that could not be started or did not answer in time, is never kept.
    /// </summary>
    [SupportedOSPlatform("macos")]
    internal static EnclaveHelperReport Ask(string helper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(helper);
        string path = Path.GetFullPath(helper);
        var file = new FileInfo(path);
        (long length, DateTime written) = file.Exists ? (file.Length, file.LastWriteTimeUtc) : (-1, DateTime.MinValue);
        if (Asked.TryGetValue(path, out var known) && known.Length == length && known.Written == written) return known.Report;
        EnclaveHelperReport report;
        try
        {
            (int code, string text) = SecureEnclaveKeyStore.Run(path, TimeSpan.FromSeconds(30), "version");
            report = Read(code, text);
        }
        catch (Exception exception) when (CouldNotStart(exception))
        {
            // A helper that cannot be started answers nothing, as one that does not answer in time.
            report = new(EnclaveHelperAnswer.None, null, []);
        }

        if (report.Answer != EnclaveHelperAnswer.None) Asked[path] = (length, written, report);
        return report;
    }

    /// <summary>
    /// Whether running the helper failed because it could not be started, such as a helper file without execute permission, as opposed
    /// to a fault of the command line: such a failure names the helper's path, so it is never shown.
    /// </summary>
    internal static bool CouldNotStart(Exception exception) =>
        exception is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException;

    /// <summary>
    /// Reads a helper's answer to <c>version</c>: its exit code and its standard output when it succeeded, else its standard error. A
    /// version answer is one compact JSON object of a version and command names; a helper before 0.9.5 answers with its usage line on
    /// standard error and exit code 2.
    /// </summary>
    internal static EnclaveHelperReport Read(int code, string text)
    {
        if (code == 0) return ReadVersion(text);
        if (code == 2 && text.StartsWith(UsagePrefix, StringComparison.Ordinal) && text.Length <= 4096)
        {
            string line = text.Split('\n')[0].Trim();
            ImmutableArray<string> commands = [.. line[UsagePrefix.Length..].Split(" | ").Select(entry => entry.Split(' ')[0]).Where(IsCommand).Distinct(StringComparer.Ordinal)];
            return new(EnclaveHelperAnswer.Usage, null, commands);
        }

        return new(EnclaveHelperAnswer.None, null, []);
    }

    /// <summary>A version of the form <c>major.minor.patch</c>, optionally followed by a hyphen and a label, as a number; null otherwise.</summary>
    internal static Version? Parse(string? version)
    {
        if (version is null) return null;
        Match match = VersionForm().Match(version);
        return match.Success ? new Version(Number(match, 1), Number(match, 2), Number(match, 3)) : null;

        static int Number(Match match, int group) => int.Parse(match.Groups[group].ValueSpan, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static EnclaveHelperReport ReadVersion(string text)
    {
        var unreadable = new EnclaveHelperReport(EnclaveHelperAnswer.Version, null, []);
        if (text.Length > 4096) return unreadable;
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("commands", out JsonElement listed) || listed.ValueKind != JsonValueKind.Array) return unreadable;
            ImmutableArray<string> commands = [.. listed.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).Where(IsCommand).Distinct(StringComparer.Ordinal)];
            string? version = root.TryGetProperty("version", out JsonElement reported) && reported.ValueKind == JsonValueKind.String ? reported.GetString() : null;
            return new(EnclaveHelperAnswer.Version, Parse(version) is null ? null : version, commands);
        }
        catch (JsonException)
        {
            return unreadable;
        }
    }

    private static bool IsCommand(string name) => CommandForm().IsMatch(name);

    [GeneratedRegex(@"\A([0-9]{1,5})\.([0-9]{1,5})\.([0-9]{1,5})(?:-[0-9A-Za-z.-]{1,32})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionForm();

    [GeneratedRegex(@"\A[a-z][a-z-]{0,31}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CommandForm();
}

/// <summary>
/// From 0.9.5 (FIX-169): the refusal of a vericairn-enclave helper this command line does not sign through. Its message names only
/// versions, command names and the remedy, never a path, so the signing boundary shows it whole where it hides other signing failures.
/// </summary>
internal sealed class EnclaveHelperMismatchException : InvalidOperationException
{
    /// <summary>Creates the refusal with its message.</summary>
    internal EnclaveHelperMismatchException(string message)
        : base(message)
    {
    }
}
