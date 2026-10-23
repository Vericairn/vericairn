// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.TestSupport;

// Scripted source observations isolate policy and dispatch journeys from Git; real clone behavior has separate tests.
internal sealed class ReleaseGateSource(string commit) : ISourceAdapter
{
    public ProviderDescriptor Descriptor { get; } = new("fixture-source", ISourceAdapter.Contract, "implemented", ["inspect"], ["Scripted fixture; observes no real repository."]);
    public bool IsRepository(string path) => true;
    public RepositoryObservation Inspect(string path) => throw new InvalidOperationException("Only release inspection is expected.");
    public ReleaseSourceObservation? InspectRelease(string path) => new(true, "main", commit);

    internal static void Prepare(EstateService service, string repository, string commit)
    {
        var model = service.Session.Load();
        var resource = model.Current("resource").FirstOrDefault(r => EstateModel.BodyString(r, "kind") == "repository" &&
            r.Body.TryGetProperty("location", out JsonElement location) && location.TryGetProperty("github", out JsonElement value) && value.GetString() == repository);
        string id;
        if (resource is null)
        {
            string by = EstateModel.BodyString(model.CurrentEstate!, "operator")!;
            var body = JsonSerializer.SerializeToElement(new { kind = "repository", name = "Fixture release source", role = "Release fixture source.", location = new { github = repository }, visibility = "public" });
            WriteOutcome result = service.NewRecord(new("resource", by, body));
            Assert.True(result.Ok, result.Problem?.Message + " " + string.Join("; ", result.Problem?.Findings.Select(f => f.Code + ": " + f.Message) ?? []));
            id = result.Written!.Id;
        }
        else id = resource.Id;
        RepositoryClones.Register(model.Manifest!.Estate, id, service.Root);
        service.Providers.Register(new ReleaseGateSource(commit));
    }
}
