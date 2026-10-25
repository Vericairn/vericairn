// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Text.Json;
using Vericairn.Adapters.Git;
using Vericairn.Protocol;
using Vericairn.Records.Hashing;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

public sealed class DecisionFixture : IDisposable
{
    private static readonly string[] GitOptions = ["-c", "core.autocrlf=false", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false"];
    public const string Document = "design/guide.md";
    public TestEstate Estate { get; } = new(3);
    public EstateService Service { get; }
    public RecordEnvelope Ask { get; }
    public string BaseCommit { get; }
    public RecordWritten InitialPin { get; }

    public DecisionFixture()
    {
        Ask = Estate.Add("ask", Estate.BuilderId, JsonSerializer.Serialize(new
        {
            to = Estate.OperatorId, question = "Revise the guide?", recommendation = "Revise it.",
            consequence = "The guide stays as it is.", state = "open",
        }));
        Estate.WriteFile(Document, "# Guide\n\nInitial.\n");
        Service = EstateService.Open(Estate.Root);
        Service.Providers = new ProviderRegistry();
        Service.Providers.Register(new GitCommandAdapter());
        DocumentPinResponse pin = Service.DocumentPin(new(Document, Estate.OperatorId, "Initial edition.", "Guide"));
        Assert.True(pin.Ok, pin.Problem?.Message);
        InitialPin = pin.Written!;
        Git("init", "-q");
        Git("config", "core.autocrlf", "false");
        Git("add", ".");
        Git("commit", "-qm", "Initial fixture");
        BaseCommit = Git("rev-parse", "HEAD").Trim();
        Estate.WriteFile(Document, "# Guide\r\n\r\nRevised.\r\n");
    }

    public DecisionNewRequest Request => new(Estate.OperatorId, Ask.Id, "  Revise the guide.\nKeep these exact words.  ",
        RecordParser.FormatTimestamp(DateTimeOffset.UtcNow.AddSeconds(-5)), BaseCommit, "Revise the guide", "Record the revised guide.",
        "The operator answered the ask and revised the guide.", ["Revise the guide."], [Document], ExpectedAsk: Ask.ComputedHash);

    public Dictionary<string, string> Records() => Directory.EnumerateFiles(Path.Combine(Estate.Root, "records"), "*.json", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(Estate.Root, p), ContentHash.Sha256File, StringComparer.Ordinal);

    public string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Estate.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in GitOptions.Concat(arguments)) start.ArgumentList.Add(arg);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output + error);
        return output;
    }

    public void Dispose()
    {
        Service.Dispose();
        Estate.Dispose();
    }
}
