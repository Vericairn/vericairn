// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public class CommunicationHandoffInvariantTests
{
    public static TheoryData<string, string> Profiles
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (string frequency in CommunicationResolution.Frequencies)
            {
                foreach (string depth in CommunicationResolution.Depths) { data.Add(frequency, depth); }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void HandoffRefusalAndResumeAreUnchangedAtEveryDepthAndFrequency(string frequency, string depth)
    {
        using var estate = new HandoffEstate();
        CliHarness.Ok("--estate", estate.Root, "communication", "set", "--as", estate.OperatorId, "--progress", frequency, "--status", depth, "--handoffs", depth);
        string[] prefix = ["--estate", estate.Root, "--recipient", estate.OperatorId, "--depth", depth];
        using EstateService api = EstateService.Open(estate.Root);
        string scratch = CliHarness.TempDirectory("comms-handoff-invariant");
        try
        {
            string path = Path.Combine(scratch, "basis.json");
            string saved = CliHarness.Ok([.. prefix, "--json", "handoff", "--out", path]).Out;
            HandoffPackage basis = JsonSerializer.Deserialize<HandoffPackage>(saved, ProtocolJson.Options)!;
            Assert.Equal(saved, CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(api.Handoff().Package, ProtocolJson.Options)));
            Assert.Equal(saved, File.ReadAllText(path));
            int before = estate.Load().Records.Count;
            Assert.Equal(2, CliHarness.Run([.. prefix, "handoff", "--out", path]).Code);
            Assert.Equal(saved, File.ReadAllText(path));
            string refusedPath = Path.Combine(scratch, "refused.json");
            Assert.Equal(2, CliHarness.Run([.. prefix, "handoff", "--max-bytes", "1", "--out", refusedPath]).Code);
            Assert.False(File.Exists(refusedPath));
            Assert.Equal("handoff-over-budget", api.Handoff(1).Problem!.Code);
            Assert.Null(api.Handoff(1).Package);
            Assert.Equal(before, estate.Load().Records.Count);
            Assert.False(CliHarness.Ok([.. prefix, "--json", "resume", path]).Json.GetProperty("changed").GetBoolean());

            var ask = estate.Add("ask", estate.BuilderId, $$"""{"to":"{{estate.OperatorId}}","question":"Continue?","recommendation":"Inspect the retained sources.","consequence":"Work waits.","state":"open"}""");
            int after = estate.Load().Records.Count;
            JsonElement resume = CliHarness.Ok([.. prefix, "--json", "resume", path]).Json;
            Assert.Equal(ask.Id, Assert.Single(resume.GetProperty("added").EnumerateArray()).GetProperty("id").GetString());
            Assert.Equal(CanonicalJson.SerializePrettyUtf8(api.ResumeHandoff(basis).Json!.Value), CanonicalJson.SerializePrettyUtf8(resume));
            Assert.Equal(after, estate.Load().Records.Count);
            Assert.Equal(saved, File.ReadAllText(path));
            Assert.Equal(JsonValueKind.Null, api.Status().Json.GetProperty("lastObservedByPerson").ValueKind);
            Assert.Empty(api.Records("operation"));
            Assert.Empty(api.Records("acceptance"));

            // An invalid package is refused independently of how the recipient reads it.
            string corrupt = CliHarness.WriteJson(scratch, "invalid.json", JsonSerializer.Serialize(basis with { Bytes = 1 }, ProtocolJson.Options));
            Assert.Equal(2, CliHarness.Run([.. prefix, "resume", corrupt]).Code);
            Assert.Equal(after, estate.Load().Records.Count);
        }
        finally
        {
            CliHarness.Delete(scratch);
        }
    }
}
