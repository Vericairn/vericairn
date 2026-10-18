// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Json = System.Text.Json.Nodes;
using Vericairn.Daemon;
using Vericairn.Mcp;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Canonical;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Records.Types;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Portability;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Service;
using Vericairn.Packs.Software;
using Vericairn.Runtime.Store;
using Vericairn.Sdk;

namespace Vericairn.Cli;

/// <summary>
/// Command implementations. Every operation on an open estate goes through the API: the daemon's when one serves the estate on
/// this machine, the embedded runtime otherwise, so that one owner holds the live state and one implementation evaluates
/// authority. Exit codes: 0 clean, 1 findings with errors, 2 usage or failure.
/// </summary>
internal static partial class Commands
{
    internal const int Clean = CommandContext.Clean;
    internal const int HasErrors = CommandContext.HasErrors;
    internal const int Failed = CommandContext.Failed;

    private static readonly CommandContext Context = new();


    public static int Status(string root, bool json, bool observe = false, string? by = null, CommunicationSelection? presentation = null)
    {
        if (observe || by is not null)
        {
            if (!observe || string.IsNullOrWhiteSpace(by))
            {
                return Fail("Recording an observation requires both --observe and --by with a human principal id.");
            }

            return WithApi(root, api =>
            {
                WriteOutcome written = api.ObserveEstate(new EstateObserveRequest(by));
                if (!written.Ok)
                {
                    return ReportOutcome(written, json);
                }

                StatusResponse observed = api.Status();
                Console.Out.Write(json ? CanonicalJson.SerializePretty(observed.Json) : CommandPresentation.Status(api, observed, presentation));
                return observed.HasErrors ? HasErrors : Clean;
            });
        }

        if (!IsEstate(root))
        {
            return WithFiles(root, model =>
            {
                FindingList checks = EstateChecks.Run(model);
                Console.Out.Write(json ? EstateStatus.RenderJson(model, checks) : EstateStatus.Render(model, checks));
                return model.Findings.HasErrors || checks.HasErrors ? HasErrors : Clean;
            });
        }

        return WithApi(root, api =>
        {
            StatusResponse status = api.Status();
            Console.Out.Write(json ? CanonicalJson.SerializePretty(status.Json) : CommandPresentation.Status(api, status, presentation));
            return status.HasErrors ? HasErrors : Clean;
        });
    }

    /// <summary>Recorded progress with labelled local claims and worker observations, through the API; exit 1 when the check it shows found errors, as status does.</summary>
    public static int Progress(string root, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        ProgressResponse progress = api.Progress();
        Console.Out.Write(json ? CanonicalJson.SerializePretty(progress.Json) : CommandPresentation.Protected(api, "status", "estate progress", progress.Text, presentation) + CommunicationPresentation.RenderAttention(CommunicationPresentation.Attention(api, presentation?.Recipient, () => progress)));
        return progress.HasErrors ? HasErrors : Clean;
    });

    public static int Resume(string root, string input, bool json) => WithBody(input, body => WithApi(root, api =>
    {
        HandoffPackage? package = body.Deserialize<HandoffPackage>(ProtocolJson.Options);
        if (package is null)
        {
            return Fail("A retained hand-over package is required.");
        }

        ResumeResponse response = api.ResumeHandoff(package);
        Console.Out.Write(json ? response.Json is JsonElement view ? CanonicalJson.SerializePretty(view) : JsonSerializer.Serialize(response.Problem, ProtocolJson.Options) : response.Text);
        return response.Problem is not null ? Failed : response.HasErrors ? HasErrors : Clean;
    }));

    public static int Handoff(string root, string? output, long? maxBytes, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        HandoffResponse response = api.Handoff(maxBytes);
        if (response.Package is null)
        {
            Console.Out.Write(json ? JsonSerializer.Serialize(response.Problem, ProtocolJson.Options) : response.Text);
            return Failed;
        }

        string package = CanonicalJson.SerializePretty(JsonSerializer.SerializeToElement(response.Package, ProtocolJson.Options));
        if (output is not null)
        {
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(Encoding.UTF8.GetBytes(package));
        }

        Console.Out.Write(json ? package : ContextTextMeasurement.Presentation(CommandPresentation.Protected(api, "handoff", response.Package.Hash, response.Text, presentation)).Text);
        return Clean;
    });

    /// <summary>What waits on a principal, or on every principal, through the API: at most three asks in one delivery to each, the recommendation first.</summary>
    public static int Ask(string root, string? to, bool json) => WithApi(root, api =>
    {
        if (to is not null && !Records.Identity.Ulid.IsValid(to))
        {
            return Fail($"--to names a principal by its ULID, not '{to}'.");
        }

        AsksResponse asks = api.Asks(to);
        Console.Out.Write(json ? CanonicalJson.SerializePretty(asks.Json) : asks.Text);
        return Clean;
    });










    /// <summary>What <c>vericairn goal propose</c> was given.</summary>







    /// <summary>What <c>vericairn worker run</c> was given.</summary>









    private static RunRow? Follow(IEstateApi api, string run, string by) => RunConsole.Follow(api, run, by);

    private static void PrintSettled(RunRow? row) => RunConsole.PrintSettled(Context, row);

    /// <summary>What <c>vericairn goal step</c> was given.</summary>






    private static void PrintRun(RunRow row) => RunConsole.PrintRun(Context, row);

    private static void PrintStop(RunStop stop) => RunConsole.PrintStop(Context, stop);

    private static string Units(JsonElement? caps) => RunConsole.Units(caps);


    public static int Decisions(string root, string? id, bool json, CommunicationSelection? presentation = null) => WithApi(root, api =>
    {
        if (id is null)
        {
            DecisionsResponse decisions = api.Decisions();
            Console.Out.Write(json ? CanonicalJson.SerializePretty(decisions.Json) : CommandPresentation.Protected(api, "decision", "decision index", decisions.Text, presentation));
            return Clean;
        }

        DecisionResponse? decision = api.Decision(id);
        if (decision is null)
        {
            return Fail($"No decision has id {id}.");
        }

        Console.Out.Write(json ? CanonicalJson.SerializePretty(decision.Body) : CommandPresentation.Protected(api, "decision", id, decision.Text, presentation));
        return Clean;
    });

    public static int Doctor(string root)
    {
        Console.Out.WriteLine($"vericairn {VersionText()}");
        Console.Out.WriteLine($"runtime   .NET {Environment.Version} on {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Console.Out.WriteLine($"git       {Probe("git", "--version") ?? "not found on PATH"}");
        Console.Out.WriteLine($"dotnet    {Probe("dotnet", "--version") ?? "not found on PATH (needed only to build from source)"}");
        Console.Out.WriteLine(IsEstate(root) ? $"estate    {Path.GetFullPath(root)} (manifest present)" : $"estate    {Path.GetFullPath(root)} is not an estate root (no {EstateLayout.ManifestPath})");
        long? format = IsEstate(root) ? EstateManifest.Load(root, new FindingList())?.RecordFormat : null;
        Console.Out.WriteLine(format is < 2
            ? $"custody   {DeviceKeyStores.Default(enclave: false).Kind} (the default for this estate, at record format {format}, which cannot name the Secure Enclave; {DeviceKeyStores.Default().Kind} once it is at format 2); per-user state at {VericairnHome.Path()}"
            : $"custody   {DeviceKeyStores.Default().Kind} (default on this platform); per-user state at {VericairnHome.Path()}");
        if (OperatingSystem.IsMacOS())
        {
            Console.Out.WriteLine(SecureEnclaveKeyStore.Available()
                ? "enclave   Secure Enclave custody available (record format 2): a person's device asks for Touch ID or the login password at every signature unless it is created with --presence false; an agent's never asks"
                : $"enclave   Secure Enclave custody unavailable ({(SecureEnclaveCustody.HelperPath() is null ? $"no {SecureEnclaveCustody.HelperName} helper beside the command line" : "no Secure Enclave available to this process")})");
        }
        Console.Out.WriteLine($"records   reads record formats {string.Join(", ", RecordFormats.Reads)}; writes an estate's own format, at most {RecordFormats.Current}");
        if (IsEstate(root))
        {
            DaemonEndpoint? endpoint = DaemonHost.Discover(root);
            Console.Out.WriteLine(endpoint is not null && VericairnClient.IsAlive(endpoint)
                ? $"daemon    process {endpoint.ProcessId} serves this estate since {endpoint.StartedAt} ({endpoint.Kind}{(endpoint.Loopback is null ? string.Empty : ", loopback " + endpoint.Loopback.Port)})"
                : "daemon    none; commands run the embedded runtime");
        }

        return Clean;
    }

    public static int Version()
    {
        Console.Out.WriteLine(VersionText());
        return Clean;
    }


    /// <summary>The name of the daemon's log beside the estate's live store.</summary>










































    internal static ISourceAdapter? SourceAdapter
    {
        get => CommandProviders.SourceAdapter;
        set => CommandProviders.SourceAdapter = value;
    }

    internal static IReleaseProvider? ReleaseProvider
    {
        get => CommandProviders.ReleaseProvider;
        set => CommandProviders.ReleaseProvider = value;
    }

    private static ISourceAdapter? GitAdapter(out int failure) => CommandProviders.Git(context: Context, out failure);










    private static Dictionary<string, string>? ParseSettings(string[] settings, out string? problem) => CommandSettings.Parse(settings, out problem);



    private static JsonElement Element(Json.JsonObject body) => CommandJson.Element(body);

    /// <summary>Runs an operation against the estate's API: the daemon's when one answers for this estate, the embedded runtime otherwise.</summary>
    private static int WithApi(string root, Func<IEstateApi, int> operation) =>
        Context.WithApi(root, operation);


    /// <summary>A bare records directory without a manifest is still validated from its files; no live state is involved.</summary>
    private static int WithFiles(string root, Func<EstateModel, int> operation) =>
        Context.WithFiles(root, operation);

    private static int WithBody(string bodyPath, Func<JsonElement, int> operation) =>
        Context.WithBody(bodyPath, operation);

    internal static EstateModel LoadCopy(string root) => CommandContext.LoadCopy(root);

    private static bool IsEstate(string root) => CommandContext.IsEstate(root);

    private static string NotAnEstate(string root) => CommandContext.NotAnEstate(root);




    private static string VersionText() => CommandContext.VersionText();

    private static string? Probe(string tool, string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool) { ArgumentList = { argument }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (process is null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(10_000);
            return process.ExitCode == 0 ? output.Split('\n')[0] : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static FindingsResponse ToFindings(FindingList findings, string summary) =>
        CommandContext.ToFindings(findings, summary);

    private static int ReportOutcome(WriteOutcome outcome, bool json) =>
        Context.ReportOutcome(outcome, json);

    internal static int ReportFindings(FindingsResponse findings, bool json) =>
        Context.ReportFindings(findings, json);

    internal static int Fail(string message) =>
        Context.Fail(message);


}
