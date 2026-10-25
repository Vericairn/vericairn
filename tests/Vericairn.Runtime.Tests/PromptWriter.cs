// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;

namespace Vericairn.Runtime.Tests;

/// <summary>An isolated signed library fixture shared by transport and command tests.</summary>
public sealed class PromptWriter : IDisposable
{
    public PolicyFixture Fixture { get; } = new();
    public EstateService Api { get; }
    public string Operator => Fixture.Estate.OperatorId;
    public string Builder => Fixture.Estate.BuilderId;
    public EstateModel Model => Api.Session.Load();
    public PromptWriter()
    {
        Api = EstateService.Open(Fixture.Estate.Root);
        Api.Providers = new ProviderRegistry();
        Api.Providers.Prompts.Register(SoftwarePrompts.All);
        foreach (string principal in new[] { Operator, Builder })
        {
            RecordWritten device = PromptLibraryTests.Ok(Api.DeviceCreate(new(principal, "Prompt fixture writer", "file")));
            Fixture.Estate.SetClock(Model.ById[device.Id].AtTime.AddSeconds(-1));
            Fixture.Add("admission", new JsonObject { ["principal"] = principal, ["status"] = "admitted", ["note"] = "Prompt fixture admission." }, subject: Model.ById[device.Id].Reference);
        }
        Fixture.Estate.SetClock(DateTimeOffset.UtcNow.AddMinutes(-1));
    }
    public PromptRow Add()
    {
        PromptLibraryTests.Ok(Api.PromptSave(new(Operator, PolicyFixture.Element(PromptRecordsTests.Definition()))));
        return Api.Prompts(new()).Prompts.Single(p => p.Source == "estate");
    }
    public AssignRequest Assignment()
    {
        RecordEnvelope attempt = Fixture.BeginAttempt();
        RecordEnvelope assignment = Model.ById[attempt.Subject!.Id];
        return new(assignment.Subject!.Id, Builder, Operator, assignment.Body.GetProperty("brief").Clone());
    }
    public void Dispose() { Api.Dispose(); Fixture.Dispose(); }
}
