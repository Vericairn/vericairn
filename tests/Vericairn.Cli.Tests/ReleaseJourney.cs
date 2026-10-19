// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Service;

namespace Vericairn.Cli.Tests;

// Release journeys prepare an exact commit and product revision, with isolated admitted test signers.
internal static class ReleaseJourney
{
    internal static CliHarness.Outcome Ok(string commit, params string[] arguments) => CliHarness.Ok(Prepare(commit, arguments));
    internal static CliHarness.Outcome Run(string commit, params string[] arguments) => CliHarness.Run(Prepare(commit, arguments));

    private static string[] Prepare(string commit, string[] arguments)
    {
        string Value(string key) => arguments[Array.IndexOf(arguments, key) + 1];
        using var api = EstateService.Open(Value("--estate"));
        string op = EstateModel.BodyString(api.Session.Load().CurrentEstate!, "operator")!;
        foreach (string principal in new[] { op, Value("--by") }.Distinct(StringComparer.Ordinal))
        {
            if (api.Session.Load().Current("device").Any(d => EstateModel.BodyString(d, "principal") == principal && EstateModel.BodyString(d, "name") == "Fixture release signer")) continue;
            WriteOutcome device = api.DeviceCreate(new(principal, "Fixture release signer", "file"));
            Assert.True(device.Ok, device.Problem?.Message);
            WriteOutcome admitted = api.DeviceAdmit(new(device.Written!.Id, op, "Admit the fixture signer."));
            Assert.True(admitted.Ok, admitted.Problem?.Message);
        }
        return [.. arguments, "--commit", commit, "--expected", api.Record(Value("--product"))!.Hash];
    }
}
