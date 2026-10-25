// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Providers.NuGet;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.TestSupport;

internal sealed class RecordedNuGetHandler : HttpMessageHandler
{
    internal string Registration { get; set; } = "listed";
    internal string Versions { get; set; } = "versions";
    internal string PackageVersion { get; set; } = "1.0.0";
    internal Action<HttpRequestMessage>? BeforeRequest { get; set; }
    internal Func<string, HttpResponseMessage?>? Override { get; set; }
    internal List<string> Requests { get; } = [];
    internal static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NuGetFixtures", name + ".json"));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BeforeRequest?.Invoke(request);
        if (request.Method != HttpMethod.Get || request.Content is not null || request.Headers.Any()) throw new InvalidOperationException("Only anonymous GETs are expected.");
        string url = request.RequestUri!.AbsoluteUri;
        Requests.Add(url);
        if (Override?.Invoke(url) is { } replacement) return Task.FromResult(replacement);
        string name = url switch
        {
            "https://api.nuget.org/v3/index.json" => "service-index",
            "https://api.nuget.org/v3-flatcontainer/example.widget/index.json" => Versions,
            "https://api.nuget.org/v3/registration5-gz-semver2/example.widget/index.json" => Registration,
            var page when page == $"https://api.nuget.org/v3/registration5-gz-semver2/example.widget/page/{PackageVersion}/{PackageVersion}.json" => "page",
            _ => throw new InvalidOperationException("No recorded response exists for this request."),
        };
        if (name == "timeout")
        {
            using JsonDocument timeout = JsonDocument.Parse(Fixture(name));
            if (timeout.RootElement.GetProperty("transport").GetString() == "timeout") throw new TaskCanceledException("Recorded timeout.");
        }
        if (name == "oversized")
        {
            using JsonDocument oversized = JsonDocument.Parse(Fixture(name));
            var content = new StringContent(oversized.RootElement.GetProperty("body").GetString()!);
            content.Headers.ContentLength = oversized.RootElement.GetProperty("contentLength").GetInt64();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
        byte[] bytes = Fixture(name);
        if (PackageVersion != "1.0.0") bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("1.0.0", PackageVersion, StringComparison.Ordinal));
        return Task.FromResult(new HttpResponseMessage(name == "not-found" ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}

internal sealed class PackageTestEstate : IDisposable
{
    internal TestEstate Estate { get; } = new(3);
    internal RecordedNuGetHandler Handler { get; } = new();
    internal HttpClient Client { get; }
    internal IPackageRegistryProvider Provider { get; }
    internal EstateService Service { get; private set; }
    internal RecordEnvelope Product { get; }
    private readonly RecordEnvelope? release;
    internal RecordEnvelope Release => release ?? throw new InvalidOperationException("A reserved package fixture has no release.");
    internal bool Reserved { get; }
    internal string Version => Reserved ? "0.0.1-reserved" : "1.0.0";

    internal PackageTestEstate(bool reserved = false)
    {
        Reserved = reserved;
        Handler.PackageVersion = Version;
        JsonNode product = JsonNode.Parse("""
            {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],
             "surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],"packages":{"provider":"nuget-org","ids":["Example.Widget"]}}
            """)!;
        if (reserved) product["packages"]!["reserved"] = new JsonArray(Version);
        Product = Estate.Add("product", Estate.OperatorId, product.ToJsonString());
        if (!reserved) release = Estate.Add("release", Estate.OperatorId, """
            {"version":"1.0.0","repository":"Example/widget","commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"candidate",
             "artifacts":[],"recordFormat":{"reads":[3],"writes":3},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}
            """, Product.Reference);
        Client = new(Handler);
        Provider = new NuGetRegistryProvider(Client);
        Service = Open();
    }

    internal PackagesObserveRequest Observe => new(Estate.OperatorId, Product.Id, Version) { Reserved = Reserved };
    internal PackagesUnlistRequest Unlist => new(Product.Id, Version, "Example.Widget", Estate.OperatorId) { Reserved = Reserved };
    internal PackagesDeprecateRequest Deprecate => new(Product.Id, Version, "Example.Widget", Estate.OperatorId,
        ["Legacy", "Other"], "Use the replacement package.", "Example.Replacement", "[2.0.0,)") { Reserved = Reserved };
    internal void Reopen() { Service.Dispose(); Service = Open(); }
    private EstateService Open()
    {
        var service = EstateService.Open(Estate.Root);
        service.Providers = new();
        service.Providers.Register(Provider);
        return service;
    }
    public void Dispose() { Service.Dispose(); Client.Dispose(); Handler.Dispose(); Estate.Dispose(); }
}
