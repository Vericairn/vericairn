// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Globalization;
using System.Text.Json;
using Json = System.Text.Json.Nodes;
using Vericairn.Packs.Software;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Providers;
using static Vericairn.Cli.CommandContext;

namespace Vericairn.Cli;

/// <summary>
/// docs pass --before (the estate's design/release-procedure.md § 7, step docs-before-freeze): checks the version-tracking guides a
/// release's set declares before the set is frozen, for prior-version tokens and preparation wording, and with --record records each
/// check's outcome as a verification of the product at the guides' commit, superseding the earlier verification of the same check and
/// version, so that the release flow's step and its traps read the latest outcome. It reads files; it changes none of them.
/// </summary>
internal sealed partial class DocsBeforeFreeze(CommandContext context)
{
    private static readonly string[] Methods = [GuidesBeforeFreeze.PriorVersionMethod, GuidesBeforeFreeze.PreparationMethod];

    public int Run(string root, string version, string declaration, string? guides, string[] previous, string? product, string? by, bool json)
    {
        if (!VersionPattern().IsMatch(version))
        {
            return context.Fail($"'{version}' is not a release version.");
        }

        if (previous.Any(p => !VersionPattern().IsMatch(p)))
        {
            return context.Fail("Each --previous is a release version.");
        }

        List<string> names;
        try
        {
            using JsonDocument declared = JsonDocument.Parse(File.ReadAllBytes(declaration));
            names = declared.RootElement.EnumerateArray().Select(n => n.GetString() ?? throw new InvalidOperationException("an entry is not a string")).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return context.Fail($"{declaration}: not a readable JSON array of document names: {exception.Message}");
        }

        string directory = Path.GetFullPath(guides ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(declaration))!, ".."));
        if (!Directory.Exists(directory))
        {
            return context.Fail($"{directory}: no such guides directory.");
        }

        if (previous.Length > 0 && by is null)
        {
            return Report(version, directory, names, previous, null, json);
        }

        return context.WithApi(root, api =>
        {
            (RecordResponse? subject, IReadOnlyList<string> ids, string? problem) = Product(api, product);
            if (problem is not null)
            {
                return context.Fail(problem);
            }

            IReadOnlyList<string> prior = previous.Length > 0 ? previous : Prior(api, ids, version);
            if (prior.Count == 0 && previous.Length == 0)
            {
                context.Error.WriteLine($"note: the estate records no published release of the product before {version}; only preparation wording can be found.");
            }

            return Report(version, directory, names, prior, by is null ? null : (api, subject!, ids, by), json);
        }, readOnly: by is null);
    }

    private int Report(string version, string directory, IReadOnlyList<string> names, IReadOnlyList<string> prior, (IEstateApi Api, RecordResponse Product, IReadOnlyList<string> Ids, string By)? recording, bool json)
    {
        GuidesBeforeFreeze.Scan scan;
        string label = GuidesLabel(directory);
        try
        {
            scan = GuidesBeforeFreeze.Run(directory, label, names, prior);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.DecoderFallbackException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return context.Fail("The guides could not be read: " + exception.Message);
        }

        var written = new List<RecordWritten>();
        string? commit = null;
        if (recording is { } r)
        {
            ISourceAdapter? git = CommandProviders.Git(context, out int failure);
            if (git is null)
            {
                return failure;
            }

            RepositoryObservation observed;
            try
            {
                observed = git.Inspect(directory);
            }
            catch (ProviderRefusedException exception)
            {
                return context.Fail("The guides' repository could not be read: " + exception.Message);
            }

            if (!observed.Clean)
            {
                return context.Fail($"The guides' clone has {observed.Modified} modified and {observed.Untracked} untracked files: commit the guides first, since a recorded check names the commit it read. Nothing was recorded.");
            }

            commit = observed.Commit;
            foreach (string method in Methods)
            {
                WriteOutcome outcome = Record(r.Api, r.Product, r.Ids, r.By, method, version, prior, commit, scan, (directory, label));
                if (!outcome.Ok)
                {
                    if (written.Count > 0) context.Error.WriteLine("Recorded before the refusal: " + string.Join(", ", written.Select(w => w.Id)) + ".");
                    return context.ReportOutcome(outcome, json);
                }

                written.Add(outcome.Written!);
            }
        }

        if (json)
        {
            context.Output.WriteLine(JsonSerializer.Serialize(new
            {
                version, prior, guides = label, read = scan.Read, history = scan.History, skipped = scan.Skipped, commit,
                checks = Methods.Select(m => new { method = m, outcome = scan.Of(m).Count == 0 ? "passed" : "failed", findings = scan.Of(m).Select(f => new { path = f.Path, line = f.Line, found = f.Found, rule = f.Rule, text = f.Text }) }),
                recorded = written,
            }, ProtocolJson.Options));
        }
        else
        {
            context.Output.WriteLine($"{version} before freezing: {scan.Read.Count} version-tracking guides read for {(prior.Count == 0 ? "no earlier release" : string.Join(", ", prior))}, {scan.History.Count} history documents read for preparation wording only, {scan.Skipped.Count} declared documents not in {label}/ (generated with the set).");
            foreach (GuidesBeforeFreeze.Finding finding in scan.Findings)
            {
                context.Output.WriteLine("  " + finding);
            }

            foreach (string method in Methods)
            {
                context.Output.WriteLine($"{method}: {(scan.Of(method).Count == 0 ? "passed" : $"failed, {scan.Of(method).Count.ToString(CultureInfo.InvariantCulture)} locations")}");
            }

            foreach (RecordWritten record in written)
            {
                context.Output.WriteLine($"recorded {record.Id} at {record.Hash}");
            }
        }

        return scan.Findings.Count == 0 ? Clean : HasErrors;
    }

    /// <summary>One check's verification of the product at the guides' commit, superseding the latest one of the same check and version.</summary>
    /// <summary>
    /// The guides directory as records and output name it: relative to the root of the repository that holds it, or by its own name
    /// outside one; never a path of this machine.
    /// </summary>
    internal static string GuidesLabel(string directory)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        for (DirectoryInfo? at = new(full); at is not null; at = at.Parent)
        {
            if (Directory.Exists(Path.Combine(at.FullName, ".git")) || File.Exists(Path.Combine(at.FullName, ".git")))
            {
                return Path.GetRelativePath(at.FullName, full).Replace('\\', '/');
            }
        }

        return Path.GetFileName(full);
    }

    private static WriteOutcome Record(IEstateApi api, RecordResponse product, IReadOnlyList<string> ids, string by, string method, string version, IReadOnlyList<string> prior, string commit, GuidesBeforeFreeze.Scan scan,
        (string Path, string Name) guides)
    {
        // A guide's line is quoted as it reads, less any path of this machine it names, so the record stays portable.
        string Quoted(GuidesBeforeFreeze.Finding finding) => PortableText.Of(finding.ToString(), guides);
        IReadOnlyList<GuidesBeforeFreeze.Finding> found = scan.Of(method);
        string what = method == GuidesBeforeFreeze.PriorVersionMethod
            ? $"no mention of {(prior.Count == 0 ? "an earlier release" : string.Join(", ", prior))} as the current release"
            : "no preparation wording";
        string summary = found.Count == 0
            ? $"Passed at {commit[..12]}: {what} in {scan.Read.Count + scan.History.Count} guides of the {version} set."
            : $"Failed at {commit[..12]}: {found.Count.ToString(CultureInfo.InvariantCulture)} locations break {what}: " + string.Join("; ", found.Take(20).Select(Quoted));
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["release"] = version, ["previous"] = ReleaseHistory.Joined(prior) };
        Json.JsonObject body = Checks.VerificationBody(method, commit, $"vericairn docs pass {version} --before", Checks.LocalEnvironment(environment), "self", found.Count == 0 ? "passed" : "failed",
            summary.Length > 8000 ? summary[..7997] + "..." : summary, found.Count == 0 ? 0 : 1, null, null, null,
            found.Take(50).Select(f => new Checks.Evidence(Quoted(f).Length > 2000 ? Quoted(f)[..2000] : Quoted(f), null, PortableText.Of($"{f.Path}:{f.Line.ToString(CultureInfo.InvariantCulture)}", guides))).ToList(),
            ["A check of the guides' text at this commit: it finds tokens and phrases, never whether a sentence is true.", "Only the documents the set declares that the guides directory holds were read; generated documents are checked by their generator."]);
        if (api.Health().RecordFormat >= 2)
        {
            body["evidenceClass"] = "observation";
            body["observer"] = "automation";
        }

        string? supersedes = api.Records("verification")
            .Where(v => v.Current && Field(v.Envelope, "method") == method && Environment(v.Envelope) == version && SubjectOf(v.Envelope) is string s && ids.Contains(s, StringComparer.Ordinal))
            .OrderBy(v => Text(v.Envelope, "at"), StringComparer.Ordinal).ThenBy(v => v.Id, StringComparer.Ordinal).LastOrDefault()?.Id;
        return api.NewRecord(new NewRecordRequest("verification", by, CommandJson.Element(body), $"product:{product.Id}:{product.Hash}", supersedes));
    }

    /// <summary>The product the check is of: named by id of any revision or by slug, or the estate's only product; with every revision's id.</summary>
    private static (RecordResponse? Product, IReadOnlyList<string> Ids, string? Problem) Product(IEstateApi api, string? named)
    {
        List<RecordResponse> products = api.Records("product").Where(p => p.Current).ToList();
        var chains = products.Select(p => (Product: p, Ids: (IReadOnlyList<string>)Chain(api, p))).ToList();
        if (named is null)
        {
            return chains.Count == 1 ? (chains[0].Product, chains[0].Ids, null) : (null, [], "Name the product with --product, by id or slug: the estate has " + (chains.Count == 0 ? "none" : "several") + ".");
        }

        List<(RecordResponse Product, IReadOnlyList<string> Ids)> matches = chains.Where(c => c.Ids.Contains(named, StringComparer.Ordinal) || Field(c.Product.Envelope, "slug") == named).ToList();
        return matches.Count == 1 ? (matches[0].Product, matches[0].Ids, null) : (null, [], $"No single current product has id or slug '{named}'.");
    }

    private static List<string> Chain(IEstateApi api, RecordResponse product)
    {
        var ids = new List<string> { product.Id };
        string? previous = Text(product.Envelope, "supersedes");
        while (previous is not null && !ids.Contains(previous, StringComparer.Ordinal) && api.Record(previous) is RecordResponse earlier)
        {
            ids.Add(earlier.Id);
            previous = Text(earlier.Envelope, "supersedes");
        }

        return ids;
    }

    /// <summary>The earlier versions the guides must not name as current, as the release flow computes them (<see cref="ReleaseHistory.Previous(IEnumerable{ValueTuple{string, string}}, string)"/>).</summary>
    internal static IReadOnlyList<string> Prior(IEstateApi api, IReadOnlyList<string> product, string version) =>
        ReleaseHistory.Previous(api.Records("release")
            .Where(r => r.Current && SubjectOf(r.Envelope) is string s && product.Contains(s, StringComparer.Ordinal))
            .Select(r => (Field(r.Envelope, "version") ?? string.Empty, Field(r.Envelope, "status") ?? string.Empty)), version);

    private static string? Environment(JsonElement envelope) =>
        envelope.TryGetProperty("body", out JsonElement body) && body.TryGetProperty("environment", out JsonElement environment) && environment.TryGetProperty("release", out JsonElement release)
            && release.ValueKind == JsonValueKind.String ? release.GetString() : null;

    private static string? SubjectOf(JsonElement envelope) =>
        envelope.TryGetProperty("subject", out JsonElement subject) && subject.TryGetProperty("id", out JsonElement id) ? id.GetString() : null;

    private static string? Field(JsonElement envelope, string name) =>
        envelope.TryGetProperty("body", out JsonElement body) ? Text(body, name) : null;

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [System.Text.RegularExpressions.GeneratedRegex(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z")]
    private static partial System.Text.RegularExpressions.Regex VersionPattern();
}
