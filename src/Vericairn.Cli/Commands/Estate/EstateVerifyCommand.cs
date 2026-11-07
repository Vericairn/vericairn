// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.CommandLine;
using System.Text.Json;
using Json = System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Runtime.Providers;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

/// <summary>
/// estate verify (the estate's design/release-procedure.md § 7, the upgrade flow's runtime and verify steps): on this machine, which
/// runtime serves the estate against the release its manifest pins, and the estate check and status with that runtime, each recorded
/// as a verification of the estate's charter for the pinned release, superseding the earlier one. It stops and starts nothing; the
/// upgrade's runtime step does that.
/// </summary>
internal sealed class EstateVerifyCommand : Command
{
    /// <summary>The verification method of the serving runtime's check.</summary>
    internal const string ServingRuntimeMethod = "vericairn.software/serving-runtime";

    /// <summary>The verification method of the estate check with the pinned release.</summary>
    internal const string EstateCheckMethod = "vericairn.software/estate-check";

    public EstateVerifyCommand(CommandContext context)
        : base("verify", "After an upgrade, check on this machine that the runtime serving the estate is the release its manifest pins, and that the estate check and status pass with it; record each outcome as a verification of the estate for the pinned release, superseding the earlier one. Stops and starts nothing.")
    {
        var by = new Option<string>("--by") { Required = true, Description = "The principal whose admitted device signs the verifications." };
        Options.Add(by);
        SetAction(parse => new EstateVerification(context).Run(parse.GetValue(context.EstateOption)!, parse.GetValue(by)!, parse.GetValue(context.JsonOption)));
    }
}

internal sealed class EstateVerification(CommandContext context)
{
    public int Run(string root, string by, bool json)
    {
        string full = Path.GetFullPath(root);
        if (ServingRuntime.Pin(full) is not { Mode: "release", Version: string pinned } manifest)
        {
            return context.Fail("estate verify checks an estate pinned to a published release; this estate's manifest pins none. Upgrade it with vericairn estate upgrade --to <version>.");
        }

        ISourceAdapter? git = CommandProviders.Git(context, out int failure);
        if (git is null)
        {
            return failure;
        }

        RepositoryObservation observed;
        try
        {
            observed = git.Inspect(full);
        }
        catch (ProviderRefusedException exception)
        {
            return context.Fail("The estate's repository could not be read, and a verification names the commit it checked: " + exception.Message);
        }

        return context.WithApi(full, api =>
        {
            HealthResponse health = api.Health();
            string served = ServingRuntime.ReleaseOf(health.Vericairn);
            string where = context.UsingDaemon ? $"the daemon serving it here (process {health.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)})" : "this command line's own runtime, since no daemon serves it here";
            bool elsewhere = !SamePath(health.Root, full) || health.Estate != manifest.Estate;
            bool runtimePassed = !elsewhere && served == pinned;
            string runtime = elsewhere
                ? $"The daemon answering this estate's endpoint serves estate {health.Estate} at {health.Root}, not this estate. See 'vericairn --estate {ServingRuntime.Quote(health.Root)} runtime status'."
                : runtimePassed
                ? $"Vericairn {served} serves the estate on this machine, through {where}: the release its manifest pins."
                : $"Vericairn {served} serves the estate on this machine, through {where}; the estate pins release {pinned}. Stop it with '{ServingRuntime.StopCommand(full)}', then start the pinned release's runtime with {ServingRuntime.StartCommand(full, pinned)}, and verify again.";

            FindingsResponse check = api.Check();
            string? statusProblem = null;
            try
            {
                _ = api.Status();
            }
            catch (Exception exception) when (exception is InvalidOperationException or JsonException or IOException)
            {
                statusProblem = exception.Message;
            }

            bool checkPassed = runtimePassed && check.Errors == 0 && statusProblem is null;
            string estate = !runtimePassed
                ? $"Not checked with the pinned release: {runtime}"
                : check.Errors > 0
                ? $"The estate check with Vericairn {served} found {check.Errors} errors: " + string.Join("; ", check.Findings.Where(f => f.Severity == "error").Take(20).Select(f => $"{f.Code} in {f.Location}: {f.Message}"))
                : statusProblem is not null
                ? $"The estate's status does not derive with Vericairn {served}: {statusProblem}"
                : $"The estate check with Vericairn {served} found no error ({check.Warnings} warnings), and its status derives.";

            RecordResponse? charter = api.Records("estate").Where(r => r.Current).OrderBy(r => r.Id, StringComparer.Ordinal).LastOrDefault();
            if (charter is null)
            {
                return context.Fail("The estate has no current charter to verify.");
            }

            var limitations = new List<string> { "What serves the estate on this machine only, as its runtime reported its own release; another machine is verified there." };
            if (!observed.Clean)
            {
                limitations.Add($"The estate's working tree had {observed.Modified} modified and {observed.Untracked} untracked files beside commit {observed.Commit[..12]}.");
            }

            var written = new List<RecordWritten>();
            foreach ((string method, bool passed, string summary) in new[] { (EstateVerifyCommand.ServingRuntimeMethod, runtimePassed, runtime), (EstateVerifyCommand.EstateCheckMethod, checkPassed, estate) })
            {
                WriteOutcome outcome = Record(api, charter, by, method, passed, summary, pinned, served, observed.Commit, limitations);
                if (!outcome.Ok)
                {
                    if (written.Count > 0) context.Error.WriteLine("Recorded before the refusal: " + string.Join(", ", written.Select(w => w.Id)) + ".");
                    return context.ReportOutcome(outcome, json);
                }

                written.Add(outcome.Written!);
            }

            if (json)
            {
                context.Output.WriteLine(JsonSerializer.Serialize(new
                {
                    pinned, served, daemon = context.UsingDaemon, commit = observed.Commit,
                    checks = new[] { new { method = EstateVerifyCommand.ServingRuntimeMethod, outcome = runtimePassed ? "passed" : "failed", summary = runtime }, new { method = EstateVerifyCommand.EstateCheckMethod, outcome = checkPassed ? "passed" : "failed", summary = estate } },
                    recorded = written,
                }, ProtocolJson.Options));
            }
            else
            {
                context.Output.WriteLine($"{EstateVerifyCommand.ServingRuntimeMethod}: {(runtimePassed ? "passed" : "failed")}: {runtime}");
                context.Output.WriteLine($"{EstateVerifyCommand.EstateCheckMethod}: {(checkPassed ? "passed" : "failed")}: {estate}");
                foreach (RecordWritten record in written)
                {
                    context.Output.WriteLine($"recorded {record.Id} at {record.Hash}");
                }
            }

            return runtimePassed && checkPassed ? Clean : HasErrors;
        });
    }

    private static WriteOutcome Record(IEstateApi api, RecordResponse charter, string by, string method, bool passed, string summary, string pinned, string served, string commit, IReadOnlyList<string> limitations)
    {
        Json.JsonObject body = Checks.VerificationBody(method, commit, "vericairn estate verify", Checks.LocalEnvironment(new Dictionary<string, string>(StringComparer.Ordinal) { ["release"] = pinned, ["served"] = served }),
            "self", passed ? "passed" : "failed", summary.Length > 8000 ? summary[..7997] + "..." : summary, passed ? 0 : 1, null, null, null, [], limitations);
        if (api.Health().RecordFormat >= 2)
        {
            body["evidenceClass"] = "observation";
            body["observer"] = "automation";
        }

        string? supersedes = api.Records("verification")
            .Where(v => v.Current && Body(v.Envelope, "method") == method && Release(v.Envelope) == pinned)
            .OrderBy(v => v.Envelope.TryGetProperty("at", out JsonElement at) ? at.GetString() : null, StringComparer.Ordinal).ThenBy(v => v.Id, StringComparer.Ordinal).LastOrDefault()?.Id;
        return api.NewRecord(new NewRecordRequest("verification", by, CommandJson.Element(body), $"estate:{charter.Id}:{charter.Hash}", supersedes));
    }

    private static string? Body(JsonElement envelope, string name) =>
        envelope.TryGetProperty("body", out JsonElement body) && body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Release(JsonElement envelope) =>
        envelope.TryGetProperty("body", out JsonElement body) && body.TryGetProperty("environment", out JsonElement environment) && environment.TryGetProperty("release", out JsonElement release)
            && release.ValueKind == JsonValueKind.String ? release.GetString() : null;

    private static bool SamePath(string left, string right)
    {
        static string Normal(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return string.Equals(Normal(left), Normal(right), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
