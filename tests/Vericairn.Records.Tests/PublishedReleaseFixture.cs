// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Vericairn.Records.Updates;

namespace Vericairn.Records.Tests;

/// <summary>Recorded GitHub response shapes and deterministic archive bytes; no live service is contacted.</summary>
public sealed class PublishedReleaseFixture : HttpMessageHandler
{
    public const string Version = "9.2.0";
    public const string Commit = "0123456789abcdef0123456789abcdef01234567";
    public ReleaseBuild Build { get; set; } = new(Version, Commit, [1, 2, 3], 3);
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    public List<string> Requests { get; } = [];
    public byte[]? SecondTag { get; set; }
    public bool Offline { get; set; }
    public Action<string>? BeforeResponse { get; set; }
    private int tagReads;

    public PublishedReleaseFixture(string version = Version)
    {
        Build = new(version, Commit, [1, 2, 3], 3);
        string root = Path.Combine(AppContext.BaseDirectory, "PublishedReleaseFixtures");
        foreach (string name in new[] { "release.json", "tag.json", "SHA256SUMS" })
            Files[name] = Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root, name)).Replace(Version, version, StringComparison.Ordinal));
        foreach (string platform in new[] { "linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64" })
        {
            Files[ReleaseArchive.FileName(version, platform)] = File.ReadAllBytes(Path.Combine(root, platform + ".txt"));
            Files[ReleaseArchive.WorkbenchFileName(version, platform)] = File.ReadAllBytes(Path.Combine(root, "workbench-" + platform + ".txt"));
        }
    }

    public void ChangeRelease(Action<JsonObject> change)
    {
        JsonObject release = JsonNode.Parse(Files["release.json"])!.AsObject();
        change(release);
        Files["release.json"] = Encoding.UTF8.GetBytes(release.ToJsonString());
    }

    public void Fault(string fault, string platform)
    {
        switch (fault)
        {
            case "checksum":
                Files["SHA256SUMS"] = Encoding.UTF8.GetBytes(string.Join('\n', Encoding.UTF8.GetString(Files["SHA256SUMS"]).Split('\n').Select(line =>
                    line.EndsWith(ReleaseArchive.FileName(Build.Version, platform), StringComparison.Ordinal) ? new string('0', 64) + line[64..] : line)));
                break;
            case "archive": Files[ReleaseArchive.FileName(Build.Version, platform)] = "changed archive"u8.ToArray(); break;
            case "missing-sums": ChangeRelease(r => r["assets"]!.AsArray().Remove(r["assets"]!.AsArray().Single(a => a!["name"]!.GetValue<string>() == "SHA256SUMS"))); break;
            case "missing-platform": Files["SHA256SUMS"] = Encoding.UTF8.GetBytes(string.Join('\n', Encoding.UTF8.GetString(Files["SHA256SUMS"]).Split('\n').Where(line => !line.Contains(platform, StringComparison.Ordinal)))); break;
            case "duplicate-line": Files["SHA256SUMS"] = [.. Files["SHA256SUMS"], .. Files["SHA256SUMS"]]; break;
            case "draft": ChangeRelease(r => r["draft"] = true); break;
            case "prerelease": ChangeRelease(r => r["prerelease"] = true); break;
            case "release-commit": ChangeRelease(r => r["target_commitish"] = new string('b', 40)); break;
            case "tag-commit": Files["tag.json"] = ChangedTag(); break;
            case "moved-tag": SecondTag = ChangedTag(); break;
            case "offline": Offline = true; break;
            case "malformed": Files["release.json"] = "[]"u8.ToArray(); break;
            default: throw new ArgumentException("Unknown recorded fault.", nameof(fault));
        }
    }

    public void WriteDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach ((string name, byte[] bytes) in Files) File.WriteAllBytes(Path.Combine(directory, name), bytes);
    }

    public Task<VerifiedRelease> VerifyAsync(string platform = "linux-x64") => PublishedReleaseVerifier.VerifyAsync(Build, platform,
        (name, _) => Task.FromResult(Read(name)), async (name, token) =>
        {
            using var stream = new MemoryStream(Read(name));
            return await PublishedReleaseVerifier.HashArchiveAsync(stream, token);
        }, TestContext.Current.CancellationToken);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Null(request.Content);
        var header = Assert.Single(request.Headers);
        Assert.Equal("User-Agent", header.Key);
        Assert.Equal("Vericairn", Assert.Single(header.Value));
        Uri uri = request.RequestUri!;
        Assert.Equal("https", uri.Scheme);
        Assert.True(uri.Host is "api.github.com" or "github.com");
        string name = uri.AbsolutePath switch
        {
            var path when path == "/repos/Vericairn/vericairn/releases/tags/v" + Build.Version => "release.json",
            var path when path == "/repos/Vericairn/vericairn/git/ref/tags/v" + Build.Version => "tag.json",
            var path when path.StartsWith("/repos/Vericairn/vericairn/git/tags/", StringComparison.Ordinal) => "tag-" + path.Split('/')[^1] + ".json",
            var path when path.StartsWith("/Vericairn/vericairn/releases/download/v" + Build.Version + "/", StringComparison.Ordinal) => path.Split('/')[^1],
            _ => throw new InvalidOperationException("Unexpected recorded release request."),
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Read(name)) });
    }

    private byte[] Read(string name)
    {
        Requests.Add(name);
        BeforeResponse?.Invoke(name);
        if (Offline) throw new HttpRequestException("Recorded unavailable network.");
        if (name == "tag.json" && ++tagReads % 2 == 0 && SecondTag is not null) return SecondTag;
        return Files[name];
    }

    private static byte[] ChangedTag() => Encoding.UTF8.GetBytes("{\"ref\":\"refs/tags/v9.2.0\",\"object\":{\"sha\":\"" + new string('b', 40) + "\",\"type\":\"commit\"}}");
}
