// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Protocol;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Tests;

namespace Vericairn.Cli.Tests;

[Collection("cli")]
public sealed class PolicyAdoptionCommandsTests
{
    [Fact]
    public void CompleteAdoptionUsesTheBuiltCommandsWithoutDiscoveryOrProviders()
    {
        CliHarness.Ok("policies", "--help"); // Creates this suite's isolated home before fixture signers.
        PolicyAdoptionJourney.Run(api => new Commands(api));
    }

    private sealed class Commands(EstateService api) : PolicyAdoptionJourney.Surface(api)
    {
        private CliHarness.Outcome Invoke(string? body, params string[] arguments) =>
            CliHarness.RunWithApi(api, body, ["--estate", api.Session.Root, "--json", .. arguments]);
        private T Read<T>(params string[] arguments)
        {
            CliHarness.Outcome result = Invoke(null, arguments);
            Assert.Equal(0, result.Code);
            return JsonSerializer.Deserialize<T>(result.Out, ProtocolJson.Options)!;
        }
        private WriteOutcome Write(string body, params string[] arguments)
        {
            // The CLI reads --body - from the raw standard-input stream, which Console.SetIn cannot feed, so the body goes in a file.
            string directory = CliHarness.TempDirectory("policy-adoption-body");
            try
            {
                CliHarness.Outcome result = Invoke(null, [.. arguments, "--body", CliHarness.WriteJson(directory, "body.json", body)]);
                Assert.True(result.Code == 0, result.Err + result.Out);
                return new(JsonSerializer.Deserialize<RecordWritten>(result.Out, ProtocolJson.Options), null);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
        public override WriteOutcome DeclarePolicy(PolicyChangeRequest request) => Write(request.Body.GetRawText(), "policies", "declare", "--by", request.By);
        public override WriteOutcome Except(string key, PolicyChangeRequest request) => Write(request.Body.GetRawText(), "policies", "except", key, "--by", request.By, "--expected", request.Expected);
        public override WriteOutcome WritingAdd(PolicyChangeRequest request) => Write(request.Body.GetRawText(), "estate", "writing-exceptions", "add", "--by", request.By);
        public override WriteOutcome Result(ResultRequest request) => Write(request.Body.GetRawText(), "work", "result", request.Attempt, "--by", request.By, "--expected", request.Expected!);
        public override WriteOutcome Review(ResultReviewRequest request) => Write(request.Body.GetRawText(), "work", "review", request.Result, "--by", request.By, "--expected", request.Expected);
        public override PolicyResponse Evaluate(string? at = null, string? release = null)
        {
            List<string> args = ["policies", "check"];
            if (at is not null) args.AddRange(["--as-of", at]);
            if (release is not null) args.AddRange(["--release", release]);
            return Read<PolicyResponse>([.. args]);
        }
        public override WritingExceptionsResponse Writing() => Read<WritingExceptionsResponse>("estate", "writing-exceptions", "list");
        public override ReleaseResponse Submit(ReleaseSubmitRequest request)
        {
            CliHarness.Outcome result = Invoke(null, "release", "submit", "--product", request.Product, "--version", request.Version, "--commit", request.Commit!, "--expected", request.Expected!, "--by", request.By);
            ReleaseResponse response = JsonSerializer.Deserialize<ReleaseResponse>(result.Out, ProtocolJson.Options)!;
            Assert.Equal(response.Ok ? 0 : 2, result.Code);
            return response;
        }
        public override FindingsResponse Check() => Read<FindingsResponse>("estate", "check");
        public override StatusResponse Status() => new("", Read<JsonElement>("status"), false);
        public override ProgressResponse Progress() => new("", Read<JsonElement>("progress"), false);
    }
}
