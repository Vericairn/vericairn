// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text.Json;
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
            "https://api.nuget.org/v3/registration5-gz-semver2/example.widget/page/1.0.0/1.0.0.json" => "page",
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
        return Task.FromResult(new HttpResponseMessage(name == "not-found" ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new ByteArrayContent(Fixture(name)) });
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
    internal RecordEnvelope Release { get; }

    internal PackageTestEstate()
    {
        Product = Estate.Add("product", Estate.OperatorId, """
            {"name":"Widget","slug":"widget","summary":"A widget.","editions":[{"name":"Community","slug":"community","summary":"Free."}],
             "surfaces":[{"name":"CLI","slug":"cli","summary":"Commands."}],"packages":{"provider":"nuget-org","ids":["Example.Widget"]}}
            """);
        Release = Estate.Add("release", Estate.OperatorId, """
            {"version":"1.0.0","repository":"Example/widget","commit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","status":"candidate",
             "artifacts":[],"recordFormat":{"reads":[3],"writes":3},"evidence":[],"qualified":[],"signing":"unsigned developer build","limitations":[]}
            """, Product.Reference);
        Client = new(Handler);
        Provider = new NuGetRegistryProvider(Client);
        Service = Open();
    }

    internal PackagesObserveRequest Observe => new(Estate.OperatorId, Product.Id, "1.0.0");
    internal PackagesUnlistRequest Unlist => new(Product.Id, "1.0.0", "Example.Widget", Estate.OperatorId);
    internal PackagesDeprecateRequest Deprecate => new(Product.Id, "1.0.0", "Example.Widget", Estate.OperatorId,
        ["Legacy", "Other"], "Use the replacement package.", "Example.Replacement", "[2.0.0,)");
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
