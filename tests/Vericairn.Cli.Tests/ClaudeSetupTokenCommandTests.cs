// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class ClaudeSetupTokenCommandTests : IDisposable
{
    private readonly string scratch = CliHarness.TempDirectory("claude-token-import");

    [Theory]
    [InlineData(100)]
    [InlineData(1024)]
    public async Task ImportAcceptsSetupTokensAtBothLengthBounds(int length)
    {
        string token = Token(length);
        string home = Path.Combine(scratch, "worker-home");
        CliHarness.Outcome result = await Import(home, token + "\r\n", "pro");

        Assert.Equal(0, result.Code);
        using var stored = JsonDocument.Parse(File.ReadAllText(Provision(home)));
        Assert.True(stored.RootElement.GetProperty("accessToken").GetString() == token);
        Assert.False((result.Out + result.Err).Contains(token, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("pro", "\n")]
    [InlineData("max", "\r\n")]
    [InlineData("pro", "")]
    public async Task ImportStdinStoresOnlyAnOwnerOnlyProvisionAndReportsTheOperatorsStatement(string account, string ending)
    {
        string token = Token();
        string home = Path.Combine(scratch, "worker-home");
        string provision = Provision(home);
        CliHarness.Outcome result = await Import(home, token + ending, account);

        Assert.Equal(0, result.Code);
        Assert.Equal("setup-token", result.Json.GetProperty("signInForm").GetString());
        Assert.Equal(account, result.Json.GetProperty("accountType").GetString());
        Assert.Equal("operator-statement", result.Json.GetProperty("accountTypeSource").GetString());
        Assert.DoesNotContain(token, result.Out + result.Err, StringComparison.Ordinal);
        Assert.Equal(provision, Assert.Single(Directory.EnumerateFiles(Path.Combine(home, "worker"))));
        using var stored = JsonDocument.Parse(File.ReadAllText(provision));
        Assert.True(stored.RootElement.GetProperty("accessToken").GetString() == token);
        Assert.Equal(account, stored.RootElement.GetProperty("accountType").GetString());
        Assert.InRange(DateTimeOffset.FromUnixTimeMilliseconds(stored.RootElement.GetProperty("expiresAt").GetInt64()),
            DateTimeOffset.UtcNow.AddDays(364), DateTimeOffset.UtcNow.AddDays(366));

        if (OperatingSystem.IsWindows())
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            FileSecurity security = new FileInfo(provision).GetAccessControl();
            Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
            FileSystemAccessRule rule = Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>());
            Assert.Equal(identity.User, rule.IdentityReference);
        }
        else
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(provision));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(provision)!));
        }
    }

    [Fact]
    public async Task ConfigureShowsOnlyProvisionMetadataAndCanRemoveTheProvision()
    {
        string token = Token();
        string home = Path.Combine(scratch, "worker-home");
        string config = Directory.CreateDirectory(Path.Combine(scratch, "legacy")).FullName;
        string credentialsFile = Path.Combine(config, ".credentials.json");
        const string credentials = """{"claudeAiOauth":{"subscriptionType":"pro"}}""";
        File.WriteAllText(credentialsFile, credentials);
        CliHarness.Outcome absent = await Invoke(home, "", config, "--json", "worker", "configure");
        Assert.False(absent.Json.GetProperty("setupToken").GetProperty("provisioned").GetBoolean());

        DateTimeOffset before = DateTimeOffset.UtcNow;
        Assert.Equal(0, (await Import(home, token, "max", config)).Code);
        DateTimeOffset after = DateTimeOffset.UtcNow;
        foreach (string[] arguments in new[]
        {
            new[] { "--json", "worker", "configure" },
            new[] { "--json", "worker", "configure", "--launcher", "claude-code" },
        })
        {
            CliHarness.Outcome shown = await Invoke(home, "", config, arguments);
            Assert.Equal(0, shown.Code);
            Assert.False((shown.Out + shown.Err).Contains(token, StringComparison.Ordinal));
            JsonElement status = shown.Json.GetProperty("setupToken");
            Assert.True(status.GetProperty("provisioned").GetBoolean());
            Assert.Equal("max", status.GetProperty("accountType").GetString());
            Assert.Equal("operator-statement", status.GetProperty("accountTypeSource").GetString());
            DateTimeOffset imported = status.GetProperty("importedAt").GetDateTimeOffset();
            Assert.InRange(imported, before.AddMilliseconds(-1), after);
            Assert.Equal(imported.AddDays(365), status.GetProperty("expiresAt").GetDateTimeOffset());
            Assert.False(status.TryGetProperty("accessToken", out _));
        }
        CliHarness.Outcome text = await Invoke(home, "", config, "worker", "configure", "--launcher", "claude-code");
        Assert.Contains("provisioned for an operator-stated max account", text.Out, StringComparison.Ordinal);
        Assert.Contains("Imported", text.Out, StringComparison.Ordinal);
        Assert.Contains("Expires", text.Out, StringComparison.Ordinal);
        Assert.False((text.Out + text.Err).Contains(token, StringComparison.Ordinal));

        CliHarness.Outcome combined = await Invoke(home, "", config, "worker", "configure", "--remove-setup-token", "--import-setup-token");
        Assert.Equal(2, combined.Code);
        Assert.True(File.Exists(Provision(home)));

        CliHarness.Outcome removed = await Invoke(home, "", config, "--json", "worker", "configure", "--remove-setup-token");
        Assert.Equal(0, removed.Code);
        Assert.True(removed.Json.GetProperty("removed").GetBoolean());
        Assert.False((removed.Out + removed.Err).Contains(token, StringComparison.Ordinal));
        Assert.False(File.Exists(Provision(home)));
        Assert.Equal(credentials, File.ReadAllText(credentialsFile));
        CliHarness.Outcome repeated = await Invoke(home, "", config, "--json", "worker", "configure", "--remove-setup-token");
        Assert.Equal(0, repeated.Code);
        Assert.False(repeated.Json.GetProperty("removed").GetBoolean());
        CliHarness.Outcome afterRemoval = await Invoke(home, "", config, "--json", "worker", "configure", "--launcher", "claude-code");
        Assert.False(afterRemoval.Json.GetProperty("setupToken").GetProperty("provisioned").GetBoolean());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ASetupTokenMistakenlyPassedAsAnArgumentIsNeverEchoed(bool encoded)
    {
        string token = Token();
        string argument = encoded ? token.Replace("-", "%2D", StringComparison.Ordinal) : token;
        string home = Path.Combine(scratch, "worker-home");
        CliHarness.Outcome result = await Invoke(home, "", null, "worker", "configure", "--launcher", "claude-code",
            "--import-setup-token", "--account-type", "pro", argument);

        Assert.NotEqual(0, result.Code);
        Assert.False((result.Out + result.Err).Contains(token, StringComparison.Ordinal));
        Assert.False((result.Out + result.Err).Contains(argument, StringComparison.Ordinal));
        Assert.Contains("hidden prompt or use --token-stdin", result.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(Provision(home)));
    }

    [Theory]
    [InlineData("missing", "pro", "bare Claude Code setup-token")]
    [InlineData("wrong-shape", "pro", "bare Claude Code setup-token")]
    [InlineData("short", "pro", "bare Claude Code setup-token")]
    [InlineData("long", "pro", "bare Claude Code setup-token")]
    [InlineData("extra-line", "pro", "bare Claude Code setup-token")]
    [InlineData("valid", "team", "unmanaged Claude Code account type")]
    [InlineData("valid", "enterprise", "unmanaged Claude Code account type")]
    public async Task ImportRefusesInvalidInputWithoutRetainingIt(string shape, string account, string expected)
    {
        string token = Token();
        string input = shape switch
        {
            "missing" => "",
            "wrong-shape" => "not-a-setup-token",
            "short" => Token(99),
            "long" => Token(1025),
            "extra-line" => token + "\nanother line",
            _ => token,
        };
        string home = Path.Combine(scratch, "worker-home");
        CliHarness.Outcome result = await Import(home, input, account);

        Assert.Equal(2, result.Code);
        Assert.Contains(expected, result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(token, result.Out + result.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(Provision(home)));
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("estate")]
    [InlineData("config")]
    [InlineData("default-config")]
    [InlineData("linked-config")]
    public async Task ImportRefusesProvisioningInsideARepositoryEstateOrHarnessConfiguration(string location)
    {
        string outside = Path.Combine(scratch, location == "default-config" ? ".claude" : "location");
        Directory.CreateDirectory(outside);
        if (location == "repository") Directory.CreateDirectory(Path.Combine(outside, ".git"));
        if (location == "estate")
        {
            Directory.CreateDirectory(Path.Combine(outside, "estate"));
            File.WriteAllText(Path.Combine(outside, "estate", "manifest.json"), "{}");
        }
        string? config = location is "config" or "linked-config" ? outside : null;
        if (location == "linked-config")
        {
            if (OperatingSystem.IsWindows()) Assert.Skip("Creating directory symbolic links requires a Windows runner grant.");
            config = Path.Combine(scratch, "config-link");
            Directory.CreateSymbolicLink(config, outside);
        }
        string token = Token();
        string home = Path.Combine(outside, "worker-home");
        CliHarness.Outcome result = await Import(home, token, "pro", config);

        Assert.Equal(2, result.Code);
        Assert.Contains(location is "repository" or "estate" ? "outside repositories and estates" : "outside harness configuration directories",
            result.Err, StringComparison.Ordinal);
        Assert.DoesNotContain(token, result.Out + result.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(Provision(home)));
    }

    private Task<CliHarness.Outcome> Import(string home, string input, string account, string? config = null) =>
        Invoke(home, input, config, "--json", "worker", "configure", "--launcher", "claude-code", "--import-setup-token",
            "--account-type", account, "--token-stdin");

    private async Task<CliHarness.Outcome> Invoke(string home, string input, string? config, params string[] arguments)
    {
        string user = Path.Combine(scratch, "user");
        Directory.CreateDirectory(user);
        var info = new ProcessStartInfo(CliHarness.BuiltExecutable)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = scratch,
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment[VericairnHome.EnvironmentVariable] = home;
        info.Environment["HOME"] = user;
        info.Environment["USERPROFILE"] = user;
        info.Environment.Remove("VERICAIRN_RUN");
        info.Environment.Remove("CODEX_HOME");
        info.Environment.Remove("GEMINI_CLI_HOME");
        info.Environment["CLAUDE_CONFIG_DIR"] = config ?? Path.Combine(user, ".claude");
        using Process process = Process.Start(info)!;
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellation);
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellation);
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellation).WaitAsync(TestTiming.HangTimeout, cancellation);
            return new(process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static string Token(int length = 215)
    {
        string prefix = string.Concat("sk-ant-", "oat01-");
        return prefix + RandomNumberGenerator.GetHexString(length - prefix.Length);
    }

    private static string Provision(string home) => Path.Combine(home, "worker", "claude-code-setup-token.json");

    public void Dispose() => CliHarness.Delete(scratch);
}
