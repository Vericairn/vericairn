// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Vericairn.Records.Signing;

namespace Vericairn.Cli.Tests;

/// <summary>The packed tool supplies the registry's inputs and runs from an isolated local feed.</summary>
[Collection("cli")]
public class McpPackageTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string Marker = "<!-- mcp-name: io.github.vericairn/vericairn -->";

    [Theory]
    [InlineData("0.8.1", "0.8.1")]
    [InlineData("0.8.2-preview.1", "0.8.2-preview.1")]
    [InlineData("0.8.2.0+build", "0.8.2+build")]
    public async Task PackingWithoutBuildingUsesTheNormalizedPackageVersion(string requested, string expected)
    {
        string scratch = CliHarness.TempDirectory("mcp-package");
        try
        {
            string package = await Pack(scratch, requested);
            using ZipArchive archive = ZipFile.OpenRead(package);
            XDocument nuspec = XDocument.Parse(Read(archive, "dotnet-vc.nuspec"));
            XElement metadata = nuspec.Root!.Elements().Single(element => element.Name.LocalName == "metadata");
            XNamespace ns = metadata.Name.Namespace;
            Assert.Equal(expected, metadata.Element(ns + "version")!.Value);
            Assert.Equal(["DotnetTool", "McpServer"], metadata.Element(ns + "packageTypes")!.Elements()
                .Select(element => element.Attribute("name")!.Value).Order(StringComparer.Ordinal));
            Assert.Equal("README.md", metadata.Element(ns + "readme")!.Value);
            string readme = Read(archive, "README.md");
            Assert.Equal(File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "nuget-readme.md")), readme);
            Assert.Single(readme.Split('\n'), line => line.TrimEnd('\r') == Marker);

            JsonObject manifest = JsonNode.Parse(Read(archive, ".mcp/server.json"))!.AsObject();
            Assert.Equal(expected, manifest["version"]!.GetValue<string>());
            Assert.Equal("io.github.vericairn/vericairn", manifest["name"]!.GetValue<string>());
            Assert.Equal("https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json", manifest["$schema"]!.GetValue<string>());
            JsonNode server = Assert.Single(manifest["packages"]!.AsArray())!;
            Assert.Equal("nuget", server["registryType"]!.GetValue<string>());
            Assert.Equal("dotnet-vc", server["identifier"]!.GetValue<string>());
            Assert.Equal(expected, server["version"]!.GetValue<string>());
            Assert.Equal("stdio", server["transport"]!["type"]!.GetValue<string>());
            Assert.Equal(["--verbosity", "quiet"], Arguments(server, "", "", "runtimeArguments"));
            Assert.Equal(["--estate", "example-estate", "--as", "example-principal", "mcp", "serve"],
                Arguments(server, "example-estate", "example-principal"));
        }
        finally { CliHarness.Delete(scratch); }
    }

    [Fact]
    public async Task DnxStartsThePackedServerWithTheManifestInputsAndAnswersInitialize()
    {
        string scratch = CliHarness.TempDirectory("mcp-dnx");
        try
        {
            try
            {
                (int code, _, _) = await Run(Start(scratch, "dnx", "--help"));
                Assert.SkipWhen(code != 0, "dnx is unavailable; it ships with the .NET 10 SDK.");
            }
            catch (Win32Exception)
            {
                Assert.Skip("dnx is unavailable; it ships with the .NET 10 SDK.");
            }

            string package = await Pack(scratch, "0.8.1");
            using ZipArchive archive = ZipFile.OpenRead(package);
            JsonNode server = JsonNode.Parse(Read(archive, ".mcp/server.json"))!["packages"]![0]!;
            string estate = Path.Combine(scratch, "estate with spaces");
            string principal = CliHarness.Ok("--estate", estate, "--json", "estate", "init", "--name", "MCP package estate",
                "--slug", "mcp-package", "--purpose", "Exercise the local tool package.", "--operator-name", "Operator",
                "--organization", "Example", "--commit", Commit).Json.GetProperty("operator").GetString()!;
            string config = Path.Combine(scratch, "NuGet.Config");
            File.WriteAllText(config, "<configuration><packageSources><clear /></packageSources></configuration>");
            var arguments = new List<string> { "dnx" };
            arguments.AddRange(Arguments(server, estate, principal, "runtimeArguments"));
            arguments.AddRange(["dotnet-vc@0.8.1", "--source", Path.GetDirectoryName(package)!, "--configfile", config, "--"]);
            arguments.AddRange(Arguments(server, estate, principal));
            const string input = """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"package-test","version":"1"}}}
                {"jsonrpc":"2.0","method":"notifications/initialized"}
                {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"estate_health","arguments":{}}}
                """;
            (int exit, string output, string error) = await Run(Start(scratch, [.. arguments]), input + "\n");
            Assert.True(exit == 0, output + error);
            Assert.True(output.Split('\n', StringSplitOptions.RemoveEmptyEntries).All(line => line.StartsWith('{')), output + error);
            JsonNode[] replies = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToArray();
            Assert.Equal(2, replies.Length);
            Assert.Equal(1, replies[0]["id"]!.GetValue<int>());
            Assert.Equal("2025-06-18", replies[0]["result"]!["protocolVersion"]!.GetValue<string>());
            Assert.Equal("vericairn", replies[0]["result"]!["serverInfo"]!["name"]!.GetValue<string>());
            Assert.Equal(2, replies[1]["id"]!.GetValue<int>());
            Assert.False(replies[1]["result"]!["isError"]!.GetValue<bool>());
            Assert.Contains("acting as " + principal, error, StringComparison.Ordinal);
            Assert.Contains(estate, error, StringComparison.Ordinal);
        }
        finally { CliHarness.Delete(scratch); }
    }

    private static string[] Arguments(JsonNode server, string estate, string principal, string property = "packageArguments")
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["estate_root"] = estate, ["acting_principal"] = principal };
        var arguments = new List<string>();
        foreach (JsonNode? argument in server[property]!.AsArray())
        {
            Assert.True(argument!["isRequired"]!.GetValue<bool>());
            if (argument["type"]!.GetValue<string>() == "named") arguments.Add(argument["name"]!.GetValue<string>());
            string value = argument["value"]!.GetValue<string>();
            if (argument["variables"] is JsonObject variables)
            {
                foreach ((string name, JsonNode? variable) in variables)
                {
                    Assert.True(variable!["isRequired"]!.GetValue<bool>());
                    Assert.Equal("string", variable["format"]!.GetValue<string>());
                    Assert.Null(variable["default"]);
                    value = value.Replace("{" + name + "}", values[name], StringComparison.Ordinal);
                }
            }
            arguments.Add(value);
        }
        return [.. arguments];
    }

    private static async Task<string> Pack(string scratch, string version)
    {
        string feed = Path.Combine(scratch, "feed");
        ProcessStartInfo info = Start(scratch, "pack", Path.Combine(RepositoryRoot(), "src", "Vericairn.Cli"),
            "--no-build", "--no-restore", "--configuration", BuildConfiguration, "--output", feed,
            "-p:Version=0.0.1", "-p:PackageVersion=" + version, "-m:1", "-nr:false");
        (int code, string output, string error) = await Run(info);
        Assert.True(code == 0, output + error);
        return Assert.Single(Directory.GetFiles(feed, "dotnet-vc.*.nupkg"));
    }

    private static ProcessStartInfo Start(string scratch, params string[] arguments)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = RepositoryRoot(),
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment[VericairnHome.EnvironmentVariable] = Path.Combine(scratch, "home");
        info.Environment["DOTNET_CLI_HOME"] = Path.Combine(scratch, "dotnet-home");
        info.Environment["NUGET_PACKAGES"] = Path.Combine(scratch, "cache");
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        info.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true";
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["NUGET_CERT_REVOCATION_MODE"] = "offline";
        return info;
    }

    private static async Task<(int Code, string Output, string Error)> Run(ProcessStartInfo info, string input = "")
    {
        using Process process = Process.Start(info)!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.StandardInput.WriteAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static string Read(ZipArchive archive, string name)
    {
        ZipArchiveEntry? entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string RepositoryRoot()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "Vericairn.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return root;
    }

#if DEBUG
    private const string BuildConfiguration = "Debug";
#else
    private const string BuildConfiguration = "Release";
#endif
}
