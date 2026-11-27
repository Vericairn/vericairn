// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using Vericairn.Cli;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

/// <summary>
/// FIX-169 (0.9.5): a command line beside a vericairn-enclave helper older than itself refuses before its first signature, naming the
/// helper's version, its own and that the helper comes from the same release archive, and doctor reports the helper's version beside the
/// command line's and flags the mismatch. A 0.9.4 command line beside a helper before 0.9.4 failed every Secure Enclave signature with
/// "the device's key store refused to sign, InvalidOperationException" and wrote nothing. The helpers are stand-ins of an older protocol
/// on macOS, shell scripts without key access.
/// </summary>
[Collection("cli")]
public sealed class EnclaveHelperCommandTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    [SupportedOSPlatform("macos")]
    public void ASignatureWithAnEnclaveKeyBesideAHelperBefore094IsRefusedBeforeItIsAskedNamingBothVersions()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The Secure Enclave helper runs on macOS only.");
        using var f = new OperatorCloseFixture();
        RecordEnvelope request = OperatorRequestTests.Request(f);
        int before = f.Estate.Load().Records.Count;
        using EnclaveHelperStandIn helper = EnclaveHelperStandIn.Before094();
        string enclaveHome = CliHarness.TempDirectory("enclave-helper-home");
        try
        {
            // The operator's device key is held by Secure Enclave custody whose helper is the earlier release's.
            Directory.CreateDirectory(Path.Combine(enclaveHome, "keys"));
            File.WriteAllBytes(Path.Combine(enclaveHome, "keys", f.Device + ".se"), [1, 2, 3]);
            f.Api.KeyStores = [new SecureEnclaveKeyStore(enclaveHome, helper: helper.Path)];

            CliHarness.Outcome refused = CliHarness.RunWithApi(f.Api, null, "--estate", f.Estate.Root, "operator", "done", request.Id, "--by", f.Estate.OperatorId);

            Assert.NotEqual(0, refused.Code);
            string error = CliHarness.Unfold(refused.Err);
            Assert.Contains("The record could not be signed on this machine. The vericairn-enclave helper beside this command line cannot sign for it: the helper reports no "
                + "version and serves no sign-record, so it comes from a release before 0.9.4, and this command line is version " + EnclaveHelperCheck.CommandLineVersion
                + ", which needs vericairn-enclave 0.9.5 or later. The helper and the command line come from the same release archive: put the vericairn-enclave from "
                + "this command line's archive beside it. Nothing was signed.", error, StringComparison.Ordinal);
            Assert.DoesNotContain("key store refused to sign", error, StringComparison.Ordinal);
            Assert.Equal(before, f.Estate.Load().Records.Count);
            Assert.Equal(["version"], helper.Asked);
        }
        finally { CliHarness.Delete(enclaveHome); }
    }

    /// <summary>
    /// The built command line as an operator runs it, beside a helper the environment names (a development build honors
    /// VERICAIRN_ENCLAVE_HELPER), in a Vericairn home of its own with the release check off: doctor flags the older helper with both
    /// versions, and a new Secure Enclave device is refused before its key is made.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task DoctorReportsTheHelpersVersionBesideTheCommandLinesAndADeviceIsRefusedBesideAnOlderHelper()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The Secure Enclave helper runs on macOS only.");
        string home = CliHarness.TempDirectory("enclave-helper-built-home");
        string root = CliHarness.TempDirectory("enclave-helper-built");
        new ReleaseUpdateStore(home).Configure(false);
        string version = EnclaveHelperCheck.CommandLineVersion;
        try
        {
            using (EnclaveHelperStandIn current = EnclaveHelperStandIn.Reporting(SecureEnclaveCustody.HelperVersion, [.. SecureEnclaveCustody.HelperCommands]))
            {
                (int code, string output, string error) = await Built(home, current.Path, "--estate", root, "doctor");
                Assert.True(code == 0, error);
                Assert.Contains($"\nhelper    vericairn-enclave {SecureEnclaveCustody.HelperVersion}; vericairn {version} expects vericairn-enclave {SecureEnclaveCustody.HelperVersion}\n", output, StringComparison.Ordinal);
                Assert.DoesNotContain("mismatch", output, StringComparison.Ordinal);
                JsonElement probe = await Doctor(home, current.Path, root);
                Assert.Equal(("present", SecureEnclaveCustody.HelperVersion), (probe.GetProperty("state").GetString(), probe.GetProperty("value").GetString()));
            }

            using EnclaveHelperStandIn older = EnclaveHelperStandIn.Before094();
            string expected = "vericairn-enclave reports no version and serves no sign-record, so it comes from a release before 0.9.4; vericairn " + version
                + " expects vericairn-enclave 0.9.5: mismatch, so Secure Enclave signatures are refused until the vericairn-enclave from this command line's release archive is beside it";
            (int doctorCode, string report, _) = await Built(home, older.Path, "--estate", root, "doctor");
            Assert.Equal(0, doctorCode);
            Assert.Contains("\nhelper    " + expected + "\n", report, StringComparison.Ordinal);
            JsonElement flagged = await Doctor(home, older.Path, root);
            Assert.Equal("unavailable", flagged.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, flagged.GetProperty("value").ValueKind);
            Assert.Equal(expected, flagged.GetProperty("detail").GetString());

            (int initCode, string init, string initError) = await Built(home, older.Path, "--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example",
                "--purpose", "Refuse an older helper.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            Assert.True(initCode == 0, initError);
            string op = JsonDocument.Parse(init).RootElement.GetProperty("operator").GetString()!;
            int records = CommandContext.LoadCopy(root).Records.Count;
            (int created, string createdOut, string createdError) = await Built(home, older.Path, "--estate", root, "devices", "create", "--principal", op, "--name", "Operator Mac",
                "--custody", "secure-enclave", "--presence", "false");
            Assert.NotEqual(0, created);
            Assert.Contains("this command line is version " + version + ", which needs vericairn-enclave 0.9.5 or later", CliHarness.Unfold(createdError), StringComparison.Ordinal);
            Assert.Contains("come from the same release archive", CliHarness.Unfold(createdError), StringComparison.Ordinal);
            Assert.Equal(string.Empty, createdOut);
            Assert.Equal(records, CommandContext.LoadCopy(root).Records.Count);
            Assert.False(Directory.Exists(Path.Combine(home, "keys")) && Directory.EnumerateFiles(Path.Combine(home, "keys"), "*.se").Any());
            Assert.DoesNotContain("create", older.Asked);
            Assert.DoesNotContain("sign-record", older.Asked);
            Assert.Contains("version", older.Asked);
        }
        finally { CliHarness.Delete(root, home); }
    }

    /// <summary>
    /// Beside a helper that is there but cannot be started, here a stand-in without execute permission, doctor reports the helper
    /// unavailable and devices create refuses with the helper version check's refusal, both naming no path. Up to 0.9.4, and in FIX-169's
    /// first build, both ended with an unhandled start failure from the Secure Enclave probe, a stack trace naming the helper's path and
    /// the working directory, before the version check ran.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task DoctorAndDevicesCreateRefuseAHelperThatCannotBeStartedNamingNoPath()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The Secure Enclave helper runs on macOS only.");
        string home = CliHarness.TempDirectory("enclave-helper-stopped-home");
        string root = CliHarness.TempDirectory("enclave-helper-stopped");
        new ReleaseUpdateStore(home).Configure(false);
        string version = EnclaveHelperCheck.CommandLineVersion;
        try
        {
            using EnclaveHelperStandIn stopped = EnclaveHelperStandIn.Reporting(SecureEnclaveCustody.HelperVersion, [.. SecureEnclaveCustody.HelperCommands]);
            File.SetUnixFileMode(stopped.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            string directory = Path.GetDirectoryName(stopped.Path)!;
            string expected = "vericairn-enclave could not complete its availability check; vericairn " + version + " expects vericairn-enclave 0.9.5: mismatch, so Secure "
                + "Enclave signatures are refused until the vericairn-enclave from this command line's release archive is beside it";

            (int doctorCode, string report, string doctorError) = await Built(home, stopped.Path, "--estate", root, "doctor");
            Assert.True(doctorCode == 0, doctorError);
            Assert.Contains("\nhelper    " + expected + "\n", report, StringComparison.Ordinal);
            Assert.Contains("\nenclave   Secure Enclave custody unavailable (the vericairn-enclave helper beside the command line could not complete its availability check)\n", report, StringComparison.Ordinal);
            Assert.Contains("custody   macos-secure-enclave (default custody unavailable until", report, StringComparison.Ordinal);
            Assert.DoesNotContain(directory, report + doctorError, StringComparison.Ordinal);
            JsonElement probe = await Doctor(home, stopped.Path, root);
            Assert.Equal("unavailable", probe.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, probe.GetProperty("value").ValueKind);
            Assert.Equal(expected, probe.GetProperty("detail").GetString());

            (int initCode, string init, string initError) = await Built(home, stopped.Path, "--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example",
                "--purpose", "Refuse a helper that cannot be started.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
            Assert.True(initCode == 0, initError);
            string op = JsonDocument.Parse(init).RootElement.GetProperty("operator").GetString()!;
            int records = CommandContext.LoadCopy(root).Records.Count;
            string refusal = "error: The vericairn-enclave helper beside this command line cannot sign for it: the helper could not complete its availability check, and this "
                + "command line is version " + version + ", which needs vericairn-enclave 0.9.5 or later. The helper and the command line come from the same release archive: "
                + "put the vericairn-enclave from this command line's archive beside it. Nothing was signed.";
            foreach (string[] custody in new[] { Array.Empty<string>(), ["--custody", "secure-enclave"] })
            {
                (int created, string createdOut, string createdError) = await Built(home, stopped.Path, ["--estate", root, "devices", "create", "--principal", op, "--name", "Operator Mac", .. custody]);
                Assert.True(created == 2, createdError);
                Assert.Equal(refusal, CliHarness.Unfold(createdError).Trim());
                Assert.Equal(string.Empty, createdOut);
            }

            Assert.Equal(records, CommandContext.LoadCopy(root).Records.Count);
            Assert.False(Directory.Exists(Path.Combine(home, "keys")) && Directory.EnumerateFiles(Path.Combine(home, "keys"), "*.se").Any());
            Assert.Empty(stopped.Asked);
        }
        finally { CliHarness.Delete(root, home); }
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("signal")]
    [InlineData("timeout")]
    [SupportedOSPlatform("macos")]
    public async Task DevicesCreateRefusesAFailedAvailabilityCheckBeforeMakingAnyKey(string failure)
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The Secure Enclave helper runs on macOS only.");
        string home = CliHarness.TempDirectory("enclave-failed-home");
        string root = CliHarness.TempDirectory("enclave-failed-estate");
        using EnclaveHelperStandIn helper = EnclaveHelperStandIn.Failing("available", failure);
        new ReleaseUpdateStore(home).Configure(false);
        try
        {
            string op = await Init(home, helper.Path, root);
            int before = CommandContext.LoadCopy(root).Records.Count;
            (int code, string output, string error) = await Built(home, helper.Path, "--estate", root, "--json", "devices", "create",
                "--principal", op, "--name", "Operator Mac");

            Assert.True(code == 2, error + output);
            Assert.Empty(output);
            AssertHelperRefusal(error, helper.Path);
            Assert.Equal(before, CommandContext.LoadCopy(root).Records.Count);
            string keys = Path.Combine(home, "keys");
            Assert.False(Directory.Exists(keys) && Directory.EnumerateFiles(keys).Any());
            Assert.DoesNotContain("create", helper.Asked);
        }
        finally { CliHarness.Delete(root, home); }
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("signal")]
    [InlineData("timeout")]
    [SupportedOSPlatform("macos")]
    public async Task DoctorReportsDefaultCustodyUnavailableWhenTheAvailabilityCheckFails(string failure)
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The Secure Enclave helper runs on macOS only.");
        string home = CliHarness.TempDirectory("enclave-doctor-home");
        string root = CliHarness.TempDirectory("enclave-doctor-estate");
        using EnclaveHelperStandIn helper = EnclaveHelperStandIn.Failing("available", failure);
        new ReleaseUpdateStore(home).Configure(false);
        try
        {
            (int code, string output, string error) = await Built(home, helper.Path, "--estate", root, "--json", "doctor");
            Assert.True(code == 0, error);
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement probes = document.RootElement;
            Assert.Equal("unavailable", probes.GetProperty("custody").GetProperty("state").GetString());
            Assert.Equal("unavailable", probes.GetProperty("enclave").GetProperty("state").GetString());
            Assert.Equal("unavailable", probes.GetProperty("helper").GetProperty("state").GetString());
            Assert.Contains("release archive", probes.GetProperty("custody").GetProperty("detail").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Path.GetDirectoryName(helper.Path)!, output + error, StringComparison.Ordinal);
        }
        finally { CliHarness.Delete(root, home); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SupportedOSPlatform("macos")]
    public async Task DevicesCreateKeepsFileCustodyWhenTheHelperIsAbsentOrReportsNoEnclave(bool present)
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The Secure Enclave helper runs on macOS only.");
        string home = CliHarness.TempDirectory("enclave-missing-home");
        string root = CliHarness.TempDirectory("enclave-missing-estate");
        using EnclaveHelperStandIn helper = EnclaveHelperStandIn.Failing("available", "no-enclave");
        string path = present ? helper.Path : Path.Combine(root, "missing", SecureEnclaveCustody.HelperName);
        new ReleaseUpdateStore(home).Configure(false);
        try
        {
            string op = await Init(home, path, root);
            (int code, string output, string error) = await Built(home, path, "--estate", root, "--json", "devices", "create",
                "--principal", op, "--name", "Operator Mac");
            Assert.True(code == 0, error + output);
            RecordEnvelope device = Assert.Single(CommandContext.LoadCopy(root).OfType("device"));
            Assert.Equal(("file-owner-only", "none"), (device.Body.GetProperty("custody").GetString(), device.Body.GetProperty("presence").GetString()));

            (int doctorCode, string diagnosis, string doctorError) = await Built(home, path, "--estate", root, "--json", "doctor");
            Assert.True(doctorCode == 0, doctorError);
            using JsonDocument report = JsonDocument.Parse(diagnosis);
            JsonElement custody = report.RootElement.GetProperty("custody");
            Assert.Equal(("present", "file-owner-only"), (custody.GetProperty("state").GetString(), custody.GetProperty("value").GetString()));
        }
        finally { CliHarness.Delete(root, home); }
    }

    private static async Task<string> Init(string home, string helper, string root)
    {
        (int code, string output, string error) = await Built(home, helper, "--estate", root, "--json", "estate", "init",
            "--name", "Example", "--slug", "example", "--purpose", "Check the default custody.", "--operator-name", "Operator", "--organization", "Example", "--commit", Commit);
        Assert.True(code == 0, error);
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("operator").GetString()!;
    }

    private static void AssertHelperRefusal(string error, string helper)
    {
        Assert.Contains("The vericairn-enclave helper beside this command line cannot sign for it", CliHarness.Unfold(error), StringComparison.Ordinal);
        Assert.Contains("Nothing was signed.", CliHarness.Unfold(error), StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetDirectoryName(helper)!, error, StringComparison.Ordinal);
    }

    [Fact]
    public void DoctorsHelperProbeIsMissingWithoutAHelperAndNamesBothVersionsWithOne()
    {
        MachineDiagnostics.Probe missing = MachineDiagnostics.Helper(null, "0.9.5+abc");
        Assert.Equal(new MachineDiagnostics.Probe("missing", null, "no vericairn-enclave helper beside the command line"), missing);
        MachineDiagnostics.Probe older = MachineDiagnostics.Helper(EnclaveHelperCheck.Read(2, EnclaveHelperStandIn.Usage094), "0.9.5+abc");
        Assert.Equal(("unavailable", null), (older.State, older.Value));
        Assert.StartsWith("vericairn-enclave reports no version, so it comes from a release before 0.9.5; vericairn 0.9.5+abc expects vericairn-enclave 0.9.5: mismatch", older.Detail, StringComparison.Ordinal);
        MachineDiagnostics.Probe newer = MachineDiagnostics.Helper(EnclaveHelperCheck.Read(0, """{"commands":["available","create","public","session","sign","sign-record","version"],"version":"0.9.6"}"""), "0.9.5+abc");
        Assert.Equal(("present", "0.9.6"), (newer.State, newer.Value));
        Assert.Contains(": mismatch, the helper is newer than this command line", newer.Detail, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> Doctor(string home, string helper, string root)
    {
        (int code, string output, string error) = await Built(home, helper, "--estate", root, "--json", "doctor");
        Assert.True(code == 0, error);
        using JsonDocument document = JsonDocument.Parse(output);
        return document.RootElement.GetProperty("helper").Clone();
    }

    private static async Task<(int Code, string Output, string Error)> Built(string home, string helper, params string[] args)
    {
        var info = new ProcessStartInfo(CliHarness.BuiltExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in args) info.ArgumentList.Add(argument);
        info.Environment["HOME"] = home;
        info.Environment[VericairnHome.EnvironmentVariable] = home;
        info.Environment[SecureEnclaveCustody.HelperVariable] = helper;
        using Process process = Process.Start(info)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(TestContext.Current.CancellationToken), output, error)
                .WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }

        return (process.ExitCode, await output, await error);
    }
}
