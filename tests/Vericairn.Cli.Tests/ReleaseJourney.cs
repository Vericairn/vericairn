// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;
using Vericairn.Adapters.Git;
using Vericairn.Runtime.Providers;
using Vericairn.Records.Estate;
using Vericairn.Runtime.Service;
using Vericairn.Runtime.Release;

namespace Vericairn.Cli.Tests;

// Release journeys prepare an exact commit and product revision, with isolated admitted test signers.
internal static class ReleaseJourney
{
    internal static CliHarness.Outcome Ok(string commit, params string[] arguments) => Invoke(commit, arguments, true);
    internal static CliHarness.Outcome Run(string commit, params string[] arguments) => Invoke(commit, arguments, false);

    // A submission reads the policy clock for its check and again for its recheck. On a slow runner the two reads can fall in
    // different seconds, so the submission retains two bases where a journey asserts one. These hold the clock at the command's
    // start, as one instant; real time still moves between commands.
    internal static CliHarness.Outcome OkHeld(string commit, params string[] arguments) => Invoke(commit, arguments, true, held: true);
    internal static CliHarness.Outcome RunHeld(string commit, params string[] arguments) => Invoke(commit, arguments, false, held: true);

    private static CliHarness.Outcome Invoke(string commit, string[] arguments, bool ok, bool held = false)
    {
        ISourceAdapter source = ProviderRegistry.Current.Source("git") ?? new GitCommandAdapter();
        try
        {
            string[] prepared = Prepare(commit, arguments);
            if (!held) return ok ? CliHarness.Ok(prepared) : CliHarness.Run(prepared);
            using EstateService api = EstateService.Open(arguments[Array.IndexOf(arguments, "--estate") + 1]);
            api.PolicyClock = new HeldClock(DateTimeOffset.UtcNow);
            CliHarness.Outcome outcome = CliHarness.RunWithApi(api, null, prepared);
            if (ok) Assert.True(outcome.Code == 0, $"vericairn {string.Join(' ', prepared)}\n{outcome.Out}\n{outcome.Err}");
            return outcome;
        }
        finally { ProviderRegistry.Current.Register(source); }
    }

    private sealed class HeldClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

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
        string? repository = Array.IndexOf(arguments, "--repository") >= 0 ? Value("--repository")
            : ReleaseProfile.FromProductBody(api.Session.Load().ById[Value("--product")].Body).Destination?.Settings.GetValueOrDefault("repository");
        if (repository is not null) ReleaseGateSource.Prepare(api, repository, commit);
        return [.. arguments, "--commit", commit, "--expected", api.Record(Value("--product"))!.Hash];
    }
}
