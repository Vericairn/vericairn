// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Vericairn.Records.Identity;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// FIX-169 (0.9.5): before its first signature through the vericairn-enclave helper the command line asks the helper its version and the
/// commands it serves, and refuses a helper that serves no sign-record or is older than the helper version it was built with, naming
/// both versions and that the helper comes from the same release archive. A 0.9.4 command line beside an older helper failed every
/// signature with the helper's usage line instead. The answers are read on every platform; the stand-ins of an older protocol, shell
/// scripts without key access, run on macOS, where the store runs its helper.
/// </summary>
public sealed partial class EnclaveHelperVersionTests : IDisposable
{
    private const string Current = """{"commands":["available","create","public","session","sign","sign-record","version"],"version":"0.9.5"}""";
    private readonly string home = Path.Combine(Path.GetTempPath(), "vericairn-enclave-version-" + Ulid.NewUlid().Value);

    [Fact]
    public void AHelperBefore094IsRefusedNamingBothVersionsAndTheArchive()
    {
        EnclaveHelperReport report = EnclaveHelperCheck.Read(2, EnclaveHelperStandIn.UsageBefore094);
        Assert.Equal(EnclaveHelperAnswer.Usage, report.Answer);
        Assert.Null(report.Version);
        Assert.Equal(["available", "create", "public", "sign"], report.Commands);
        Assert.Equal(["sign-record", "session"], report.Missing);
        Assert.True(report.Older);
        Assert.True(report.Refused);
        Assert.Equal(
            "The vericairn-enclave helper beside this command line cannot sign for it: the helper reports no version and serves no sign-record, so it comes "
            + "from a release before 0.9.4, and this command line is version 0.9.5+abc, which needs vericairn-enclave 0.9.5 or later. The helper and the "
            + "command line come from the same release archive: put the vericairn-enclave from this command line's archive beside it. Nothing was signed.",
            report.Refusal("0.9.5+abc"));
        Assert.Equal(
            "vericairn-enclave reports no version and serves no sign-record, so it comes from a release before 0.9.4; vericairn 0.9.5+abc expects "
            + "vericairn-enclave 0.9.5: mismatch, so Secure Enclave signatures are refused until the vericairn-enclave from this command line's release archive is beside it",
            report.Doctor("0.9.5+abc"));
    }

    [Fact]
    public void The094HelperServesSignRecordButIsOlderThanThisCommandLine()
    {
        EnclaveHelperReport report = EnclaveHelperCheck.Read(2, EnclaveHelperStandIn.Usage094 + "\n");
        Assert.Equal(EnclaveHelperAnswer.Usage, report.Answer);
        Assert.Equal(["available", "create", "public", "sign", "sign-record", "session"], report.Commands);
        Assert.Empty(report.Missing);
        Assert.True(report.Refused);
        Assert.Equal("reports no version, so it comes from a release before 0.9.5", report.Description);
        Assert.Contains("this command line is version 0.9.5+abc, which needs vericairn-enclave 0.9.5 or later", report.Refusal("0.9.5+abc"), StringComparison.Ordinal);
    }

    [Fact]
    public void AHelperOfThisReleaseSignsAndANewerOneSignsWithTheMismatchFlagged()
    {
        EnclaveHelperReport current = EnclaveHelperCheck.Read(0, Current);
        Assert.Equal((EnclaveHelperAnswer.Version, "0.9.5"), (current.Answer, current.Version));
        Assert.False(current.Refused);
        Assert.False(current.Newer);
        Assert.Null(current.Refusal("0.9.5+abc"));
        Assert.Equal("vericairn-enclave 0.9.5; vericairn 0.9.5+abc expects vericairn-enclave 0.9.5", current.Doctor("0.9.5+abc"));

        EnclaveHelperReport newer = EnclaveHelperCheck.Read(0, Current.Replace("0.9.5", "0.9.6", StringComparison.Ordinal));
        Assert.False(newer.Refused);
        Assert.True(newer.Newer);
        Assert.Equal("vericairn-enclave 0.9.6; vericairn 0.9.5+abc expects vericairn-enclave 0.9.5: mismatch, the helper is newer than this command line; take both from the same release archive", newer.Doctor("0.9.5+abc"));
    }

    [Theory]
    [InlineData("""{"commands":["available","create","public","session","sign","sign-record","version"],"version":"0.9.4"}""", "reports version 0.9.4")]
    [InlineData("""{"commands":["available","create","public","sign","version"],"version":"0.9.5"}""", "reports version 0.9.5 but serves no sign-record or session")]
    [InlineData("""{"commands":["available","create","public","session","sign","sign-record","version"],"version":"/opt/vericairn/bin/vericairn-enclave"}""", "reports a version this command line cannot read")]
    [InlineData("""not json /opt/vericairn/bin""", "reports a version this command line cannot read")]
    public void AHelperThatIsOlderUnreadableOrMissingACommandIsRefusedInTheCommandLinesOwnWords(string answer, string description)
    {
        EnclaveHelperReport report = EnclaveHelperCheck.Read(0, answer);
        Assert.True(report.Refused);
        Assert.Equal(description, report.Description);
        // Nothing of the helper's beyond a version and command names of a fixed form reaches the refusal.
        Assert.DoesNotContain("/opt/", report.Refusal("0.9.5+abc"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4, "The Secure Enclave refused: something.")]
    [InlineData(2, "something else entirely")]
    [InlineData(126, "")]
    public void AHelperThatDoesNotAnswerIsRefusedAsNotHavingAnswered(int code, string text)
    {
        EnclaveHelperReport report = EnclaveHelperCheck.Read(code, text);
        Assert.Equal(EnclaveHelperAnswer.None, report.Answer);
        Assert.True(report.Refused);
        Assert.Equal("did not answer when asked for its version", report.Description);
        Assert.DoesNotContain(text.Length == 0 ? "\u0000" : text, report.Refusal("0.9.5+abc"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The helper's source and the command line agree: its version is the one the command line was built with, it serves every command
    /// the command line calls and version, and its usage line, which an older command line reads, lists each of them.
    /// </summary>
    [Fact]
    public void TheHelpersSourceStatesTheVersionAndCommandsTheCommandLineExpects()
    {
        string source = File.ReadAllText(EnclaveSessionBuild.Source);
        Assert.Equal(SecureEnclaveCustody.HelperVersion, HelperVersion().Match(source).Groups["version"].Value);
        string[] commands = [.. Regex.Matches(HelperCommandList().Match(source).Groups["commands"].Value, "\"([a-z-]+)\"").Select(m => m.Groups[1].Value)];
        Assert.Equal(commands.Order(StringComparer.Ordinal), commands);
        Assert.Equal(SecureEnclaveCustody.HelperCommands.Append("version").Order(StringComparer.Ordinal), commands);
        EnclaveHelperReport usage = EnclaveHelperCheck.Read(2, Usage().Match(source).Groups["usage"].Value);
        Assert.Equal(commands.Order(StringComparer.Ordinal), usage.Commands.Order(StringComparer.Ordinal));
        Assert.NotNull(EnclaveHelperCheck.Parse(SecureEnclaveCustody.HelperVersion));
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public void TheHelperBuiltFromThisSourceAnswersItsVersionAndIsNotRefused()
    {
        string helper = EnclaveSessionBuild.RequireTestBuild();
        ProcessResult answered = ProcessResult.Run(helper, ["version"], environment: null, input: null);
        Assert.True(answered.Code == 0, answered.Error);
        EnclaveHelperReport report = EnclaveHelperCheck.Read(answered.Code, answered.Text);
        Assert.Equal((EnclaveHelperAnswer.Version, SecureEnclaveCustody.HelperVersion), (report.Answer, report.Version));
        Assert.False(report.Refused);
        Assert.Equal(2, ProcessResult.Run(helper, ["version", "extra"], environment: null, input: null).Code);
    }

    [Theory]
    [InlineData(false, "reports no version and serves no sign-record, so it comes from a release before 0.9.4")]
    [InlineData(true, "reports no version, so it comes from a release before 0.9.5")]
    [SupportedOSPlatform("macos")]
    public void AStoreBesideAnOlderHelperSignsAndCreatesNothingAndNamesBothVersions(bool of094, string description)
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The Secure Enclave helper runs on macOS only.");
        using EnclaveHelperStandIn helper = of094 ? EnclaveHelperStandIn.Of094() : EnclaveHelperStandIn.Before094();
        var store = new SecureEnclaveKeyStore(home, helper: helper.Path);
        string device = Ulid.NewUlid().Value;
        Directory.CreateDirectory(Path.Combine(home, "keys"));
        File.WriteAllBytes(Path.Combine(home, "keys", device + ".se"), [1, 2, 3]);
        byte[] content = "{}"u8.ToArray();
        byte[] digest = SHA256.HashData(content);

        var refused = Assert.Throws<EnclaveHelperMismatchException>(() => store.SignRecord(device, content, digest, "Signature 1 of 1"));
        Assert.Contains("the helper " + description + ", and this command line is version " + EnclaveHelperCheck.CommandLineVersion + ", which needs vericairn-enclave 0.9.5 or later", refused.Message, StringComparison.Ordinal);
        Assert.Contains("The helper and the command line come from the same release archive", refused.Message, StringComparison.Ordinal);
        Assert.Equal(refused.Message, Assert.Throws<EnclaveHelperMismatchException>(() => store.Sign(device, digest)).Message);
        string next = Ulid.NewUlid().Value;
        Assert.Equal(refused.Message, Assert.Throws<EnclaveHelperMismatchException>(() => store.Create(next)).Message);
        Assert.False(store.Has(next));

        // Asked once for its version, whose answer is kept for the helper file as it is, and never for a signature or a key.
        Assert.Equal(["version"], helper.Asked);
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public void AStoreBesideAHelperOfThisReleaseSignsThroughIt()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The Secure Enclave helper runs on macOS only.");
        using EnclaveHelperStandIn helper = EnclaveHelperStandIn.Reporting(SecureEnclaveCustody.HelperVersion, [.. SecureEnclaveCustody.HelperCommands]);
        var store = new SecureEnclaveKeyStore(home, helper: helper.Path);
        string device = Ulid.NewUlid().Value;
        Directory.CreateDirectory(Path.Combine(home, "keys"));
        File.WriteAllBytes(Path.Combine(home, "keys", device + ".se"), [1, 2, 3]);
        byte[] content = "{}"u8.ToArray();
        Assert.Equal(new byte[64], store.SignRecord(device, content, SHA256.HashData(content), null));
        Assert.Equal(["version", "sign-record"], helper.Asked);
    }

    public void Dispose()
    {
        if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
    }

    [GeneratedRegex("""^let helperVersion = "(?<version>[^"]+)"$""", RegexOptions.Multiline)]
    private static partial Regex HelperVersion();

    [GeneratedRegex("""^let helperCommands = \[(?<commands>[^\]]*)\]$""", RegexOptions.Multiline)]
    private static partial Regex HelperCommandList();

    [GeneratedRegex("""^let usage = "(?<usage>[^"]+)"$""", RegexOptions.Multiline)]
    private static partial Regex Usage();
}
