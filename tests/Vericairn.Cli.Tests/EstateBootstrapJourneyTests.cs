// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Vericairn.Records.Hashing;
using Vericairn.Records.Signing;
using Vericairn.Records.Tests;
using Vericairn.Records.Updates;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class EstateBootstrapJourneyTests
{
    public static IEnumerable<object[]> NativeShells()
    {
        foreach (bool release in new[] { false, true })
        {
            if (OperatingSystem.IsWindows())
            {
                yield return ["powershell", release];
                yield return ["pwsh", release];
            }
            else yield return ["sh", release];
        }
    }

    [Theory]
    [MemberData(nameof(NativeShells))]
    public async Task NewlyInitializedEstateColdStartsAndReusesItsVerifiedToolchain(string shell, bool release)
    {
        string scratch = CliHarness.TempDirectory("bootstrap-journey");
        string root = Path.Combine(scratch, "estate with spaces");
        string home = Path.Combine(scratch, "home");
        string source = Path.Combine(scratch, "source");
        string download = Path.Combine(scratch, "release");
        try
        {
            string payload = CreatePayload(scratch);
            string commit = CreateSource(source);
            using var fixture = new PublishedReleaseFixture();
            string archive = ReleaseArchive.FileName(fixture.Build.Version, ReleaseBuild.Platform);
            if (release)
            {
                byte[] bytes = CreateArchive(payload);
                fixture.Files[archive] = bytes;
                string sums = Encoding.UTF8.GetString(fixture.Files["SHA256SUMS"]);
                fixture.Files["SHA256SUMS"] = Encoding.UTF8.GetBytes(string.Join('\n', sums.Split('\n').Select(line =>
                    line.EndsWith("  " + archive, StringComparison.Ordinal) ? ContentHash.Sha256(bytes)["sha256:".Length..] + "  " + archive : line)));
                fixture.WriteDirectory(download);
            }

            string[] arguments = ["--estate", root, "--json", "estate", "init", "--name", "Example", "--slug", "example",
                "--purpose", "Bootstrap cold start.", "--operator-name", "Operator", "--organization", "Example"];
            CliHarness.Outcome initialized = EstateInitCommandTests.WithVersion(
                release ? fixture.Build.Identity : "0.8.3+" + commit,
                release ? [.. arguments, "--release", "--from", download] : arguments);
            Assert.True(initialized.Code == 0, initialized.Out + initialized.Err);
            Assert.Equal(EstateBootstrapTests.WrittenJson, initialized.Json.GetProperty("bootstrap").GetRawText());
            EstateBootstrapTests.AssertInstalled(root);
            Assert.False(Directory.Exists(Path.Combine(root, ".vericairn", "toolchain")));

            CliHarness.Outcome cold = await RunBootstrap(shell, root, home, source, payload, download);
            Assert.True(cold.Code == 0, cold.Out + cold.Err);
            Assert.Contains($"{(release ? 3 : 2)} records: 0 errors, 0 warnings", cold.Out, StringComparison.Ordinal);
            Assert.Contains(release ? "bootstrap: fetching " : "bootstrap: building ", cold.Out + cold.Err, StringComparison.Ordinal);
            string toolchain = Path.Combine(root, ".vericairn", "toolchain");
            if (release)
            {
                Assert.Equal(ContentHash.Sha256(fixture.Files[archive])["sha256:".Length..],
                    File.ReadAllText(Path.Combine(toolchain, "release", fixture.Build.Version, ReleaseBuild.Platform, ".sha256")));
                File.Delete(Path.Combine(download, archive));
            }
            else
                Assert.Equal($"commit={commit}\ndirty=false\noverride=false\npin={commit}",
                    File.ReadAllText(Path.Combine(toolchain, "source", commit, "bin", ".source")));

            CliHarness.Outcome warm = await RunBootstrap(shell, root, home, source, payload, download, "estate", "check");
            Assert.True(warm.Code == 0, warm.Out + warm.Err);
            Assert.Contains($"{(release ? 3 : 2)} records: 0 errors, 0 warnings", warm.Out, StringComparison.Ordinal);
            Assert.DoesNotContain("bootstrap: fetching ", warm.Out + warm.Err, StringComparison.Ordinal);
            Assert.DoesNotContain("bootstrap: building ", warm.Out + warm.Err, StringComparison.Ordinal);
            EstateBootstrapTests.AssertInstalled(root);
        }
        finally { CliHarness.Delete(scratch); }
    }

    private static string CreatePayload(string scratch)
    {
        // Exercise the real command line and estate checker without downloading a published release.
        string payload = Path.Combine(scratch, "payload");
        string bin = Path.Combine(payload, "bin");
        Directory.CreateDirectory(bin);
        string runtime = Path.Combine(AppContext.BaseDirectory, "runtimes");
        IEnumerable<string> files = Directory.EnumerateFiles(AppContext.BaseDirectory)
            .Where(file => file == CliHarness.BuiltExecutable || Path.GetExtension(file) is ".dll" or ".json");
        if (Directory.Exists(runtime)) files = files.Concat(Directory.EnumerateFiles(runtime, "*", SearchOption.AllDirectories));
        foreach (string file in files)
        {
            string target = Path.Combine(bin, Path.GetRelativePath(AppContext.BaseDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return payload;
    }

    private static string CreateSource(string source)
    {
        string project = Path.Combine(source, "src", "Vericairn.Cli");
        Directory.CreateDirectory(project);
        // This local source fixture supplies the already built CLI as its build output. The
        // bootstrap still checks the git pin and invokes dotnet build, with no package restore.
        File.WriteAllText(Path.Combine(project, "Vericairn.Cli.csproj"), """
            <Project DefaultTargets="Build">
              <Target Name="Restore" />
              <Target Name="Build">
                <ItemGroup>
                  <RuntimeFile Include="$(BootstrapTestRuntime)/**/*" />
                </ItemGroup>
                <Copy SourceFiles="@(RuntimeFile)" DestinationFiles="@(RuntimeFile->'$(OutputPath)/%(RecursiveDir)%(Filename)%(Extension)')" />
              </Target>
            </Project>
            """);
        ContextJourneyTests.Git(source, "init", "-q", "-b", "main");
        ContextJourneyTests.Git(source, "config", "core.autocrlf", "false");
        ContextJourneyTests.Git(source, "add", ".");
        ContextJourneyTests.Git(source, "commit", "-q", "-m", "Bootstrap source fixture");
        return File.ReadAllText(Path.Combine(source, ".git", "refs", "heads", "main")).Trim();
    }

    private static byte[] CreateArchive(string payload)
    {
        using var bytes = new MemoryStream();
        if (OperatingSystem.IsWindows()) ZipFile.CreateFromDirectory(payload, bytes);
        else
        {
            using var gzip = new GZipStream(bytes, CompressionLevel.Fastest, leaveOpen: true);
            TarFile.CreateFromDirectory(payload, gzip, includeBaseDirectory: false);
        }
        return bytes.ToArray();
    }

    private static async Task<CliHarness.Outcome> RunBootstrap(string shell, string root, string home,
        string source, string payload, string download, params string[] arguments)
    {
        Directory.CreateDirectory(home);
        var start = new ProcessStartInfo(shell == "sh" ? Path.Combine(root, "bootstrap") : shell)
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (shell != "sh")
            foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(root, "bootstrap.ps1") })
                start.ArgumentList.Add(argument);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment[VericairnHome.EnvironmentVariable] = home;
        start.Environment["VERICAIRN_SOURCE"] = source;
        start.Environment["VERICAIRN_SOURCE_OVERRIDE"] = "0";
        start.Environment["VERICAIRN_RELEASE_BASE"] = download;
        start.Environment["BootstrapTestRuntime"] = Path.Combine(payload, "bin");
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true";
        using Process process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            await Task.WhenAll(output, error).WaitAsync(TestTiming.HangTimeout, TestContext.Current.CancellationToken);
            return new(process.ExitCode, await output, await error);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }
}
