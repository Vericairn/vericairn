// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Protocol;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;
using static Vericairn.Runtime.Service.EstateServiceValues;

namespace Vericairn.Runtime.Service;

/// <summary>
/// Security reports read before every release (OPS-11). A product's <c>securityReports</c> declaration names repositories; their
/// advisories, drafts and private reports included, are read through the declared reader on a schedule the daemon keeps and on
/// demand. Each observation keeps an advisory's identifier, state, severity and response hash in this machine's live store,
/// under the user's own Vericairn home, never its text. A report (an advisory in triage or draft state, or an unknown state) is
/// incident attention for the operator until the operator's triage names its current revision, and while one is untriaged, or a
/// declared repository cannot be read completely, release submission refuses. A new or changed advisory the estate has already
/// published, closed or withdrawn is information for a week and holds nothing.
/// </summary>
public sealed partial class EstateService
{
    /// <summary>The live-store key of this machine's security-report observations.</summary>
    internal const string SecurityReportsKey = "security-reports";

    /// <summary>How often the daemon reads the declared repositories.</summary>
    public static readonly TimeSpan SecurityReportsInterval = TimeSpan.FromMinutes(5);

    // A gate read younger than this is reused, so repeated checks spend no more of the person's allowance.
    internal static readonly TimeSpan SecurityReportsReuse = TimeSpan.FromSeconds(60);

    // Without a complete read younger than three intervals, a declared repository's reports are unconfirmed.
    internal static readonly TimeSpan SecurityReportsStale = TimeSpan.FromMinutes(15);

    // How long a new or changed advisory that holds nothing stays information in the operator's attention.
    internal static readonly TimeSpan SecurityReportsNews = TimeSpan.FromDays(7);

    private const string SecurityHeldMessage = "Release submission is held until the operator confirms that every security report on the declared repositories is read and triaged.";

    // Reads are serialized apart from the service gate: a provider read never holds the gate, so every other operation keeps answering.
    private readonly object securityReads = new();

    // A check's read, kept in memory only so that repeated checks within the reuse window read GitHub once.
    private SecurityState? checkedState;

    internal sealed record SecurityDeclaration(string Provider, string Resource, string Repository);

    internal sealed record SecurityRead(string Repository, string Provider, string Resource, string At, bool Complete, string? Problem);

    internal sealed record SecurityObservation(string Repository, string Resource, string Identifier, string State, string Severity, string ContentHash,
        string FirstSeen, string ChangedAt);

    internal sealed record SecurityState(IReadOnlyList<SecurityRead> Reads, IReadOnlyList<SecurityObservation> Advisories)
    {
        internal static SecurityState Empty { get; } = new([], []);
    }

    /// <inheritdoc />
    public SecurityReportsResponse SecurityReportsPoll(SecurityReportsPollRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            if (SecurityReaderRefusal(session.Load(), request.By) is { } denied) return denied;
        }

        SecurityState state = RefreshSecurityReports(TimeSpan.Zero);
        lock (gate)
        {
            EstateModel model = session.Load();
            return SecurityAnswer(model, state, "Security reports were read; nothing was recorded or posted.");
        }
    }

    /// <inheritdoc />
    public SecurityReportsResponse SecurityReportsTriage(SecurityReportTriageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Advisory is null || !Patterns.GitHubAdvisory.IsMatch(request.Advisory) || request.Repository is null || !Patterns.GitHubRepository.IsMatch(request.Repository)
            || !ContentHash.IsValid(request.Expected) || !SecurityReportRules.Dispositions.Contains(request.Disposition, StringComparer.Ordinal)
            || request.Reason is { Length: 0 or > SecurityReportRules.MaxReason } || request.Disposition == "no-action" && request.Reason is null)
            return SecurityProblem("refused", "Name the advisory by its GHSA identifier, its repository as owner/name and its observed content hash, with a disposition of "
                + string.Join(", ", SecurityReportRules.Dispositions) + " and a reason of at most " + SecurityReportRules.MaxReason + " characters, required for no-action.");
        lock (gate)
        {
            EstateModel model = session.Load();
            if (SecurityReaderRefusal(model, request.By) is { } denied) return denied;
            SecurityState state = ReadSecurityState();
            SecurityObservation? observed = state.Advisories.FirstOrDefault(a => a.Identifier == request.Advisory
                && string.Equals(a.Repository, request.Repository, StringComparison.OrdinalIgnoreCase));
            if (observed is null)
                return SecurityProblem("not-found", "This machine has observed no such advisory in a declared repository; read them with vericairn security-reports poll first.");
            if (observed.ContentHash != request.Expected)
                return SecurityProblem("conflict", "The advisory's observed revision is " + observed.ContentHash + ", not the expected one; review its current revision, then triage that.");
            if (!model.ById.TryGetValue(observed.Resource, out RecordEnvelope? named) || model.Latest(named) is not { } resource || SecurityReportRules.GitHubLocation(resource) is null)
                return SecurityProblem("refused", "The advisory's repository resource is no longer a repository on GitHub; correct the declaration, then read again.");
            var facts = new JsonObject
            {
                ["advisory"] = observed.Identifier, ["repository"] = observed.Repository, ["contentHash"] = observed.ContentHash, ["disposition"] = request.Disposition,
            };
            if (request.Reason is not null) facts["reason"] = request.Reason;
            DateTimeOffset now = PolicyClock.GetUtcNow();
            var body = new JsonObject
            {
                ["kind"] = SecurityReportRules.TriageKind, ["asOf"] = RecordParser.FormatTimestamp(now),
                ["summary"] = $"The operator triaged security advisory {observed.Identifier} on {observed.Repository}: {request.Disposition}.",
                ["facts"] = facts,
                ["limitations"] = new JsonArray("Names the advisory by identifier, repository and the hash of the provider's response only; its text stays with the provider."),
            };
            WriteOutcome written = Write(new RecordDraft("observation", request.By, Element(body), resource.Reference, null, now));
            if (!written.Ok) return SecurityProblem(written.Problem!.Code, "The triage was refused; nothing was recorded. " + written.Problem.Message) with { Problem = written.Problem };
            return SecurityAnswer(session.Load(), state, "The triage of " + observed.Identifier + " at " + observed.ContentHash + " is recorded.") with { Triage = written.Written };
        }
    }

    /// <summary>The daemon's scheduled read: nothing at all unless a product declares security reports.</summary>
    public void SecurityReportsScheduled() => RefreshSecurityReports(SecurityReportsReuse, scheduled: true);

    /// <summary>
    /// The release gate's objections: reads the declared repositories unless a complete read is younger than a minute, then refuses
    /// while any is unconfirmed or any observed revision is untriaged. Only the operator sees which; anyone else gets one objection
    /// that names, counts and describes nothing, whatever exists. Like the rest of admission it reads the canonical records without
    /// refreshing the live cache, and a check (<paramref name="store"/> false) keeps what it read in memory, leaving the store unchanged.
    /// </summary>
    internal IReadOnlyList<FindingDto> SecurityReportObjections(string? by, bool store)
    {
        lock (gate)
        {
            if (SecurityDeclarations(session.Read()).Count == 0) return [];
        }

        SecurityState state = RefreshSecurityReports(SecurityReportsReuse, store: store);
        lock (gate)
        {
            EstateModel model = session.Read();
            IReadOnlyList<SecurityDeclaration> declared = SecurityDeclarations(model);
            if (declared.Count == 0) return [];
            var findings = new List<FindingDto>();
            DateTimeOffset now = PolicyClock.GetUtcNow();
            foreach (SecurityDeclaration declaration in declared)
            {
                SecurityRead? read = LatestRead(state, declaration);
                if (read is not { Complete: true } || Stale(read, now))
                    findings.Add(new("error", "release.security-reports-unavailable", "security-reports:" + declaration.Repository,
                        $"Security reports on {declaration.Repository} are unconfirmed: {read?.Problem ?? "no complete read is recent enough"}."));
            }
            foreach (SecurityObservation advisory in Holding(model, state, declared))
                findings.Add(new("error", "release.security-report", "security-report:" + advisory.Repository + ":" + advisory.Identifier,
                    $"Security report {advisory.Identifier} on {advisory.Repository} (state {advisory.State}, severity {advisory.Severity}) has no triage of its current revision {advisory.ContentHash}."
                    + $" Next: review it on GitHub, then run vericairn security-reports triage {advisory.Identifier} --repository {advisory.Repository} --expected {advisory.ContentHash} --disposition <security|duplicate|no-action> --by <operator>, and check again."));
            if (findings.Count == 0 || model.HasRole(by, Roles.Operator)) return findings;
            return [new("error", "release.security-reports", "security-reports", SecurityHeldMessage)];
        }
    }

    /// <summary>Incident attention for the operator: each untriaged revision, and each declared repository whose reports are unconfirmed.</summary>
    private IEnumerable<(string Kind, string Subject, string Why, string State, string Next, string Type, string Id, string? Hash, string? At, AttentionReference? Target, string Basis)>
        SecurityNotices(EstateModel model, string principal)
    {
        if (!model.HasRole(principal, Roles.Operator)) yield break;
        IReadOnlyList<SecurityDeclaration> declared = SecurityDeclarations(model);
        if (declared.Count == 0) yield break;
        SecurityState state = ReadSecurityState();
        DateTimeOffset now = PolicyClock.GetUtcNow();
        foreach (SecurityDeclaration declaration in declared)
        {
            SecurityRead? read = LatestRead(state, declaration);
            if (read is { Complete: true } && !Stale(read, now)) continue;
            string why = read is null ? "they have not been read on this machine yet"
                : read.Complete ? "no read has completed since " + read.At : read.Problem + " (last attempt " + read.At + ")";
            yield return ("failed check", "security reports on " + declaration.Repository,
                $"Security reports on {declaration.Repository} are unconfirmed: {why}. Every release waits until they are read completely.",
                "unconfirmed", "vericairn security-reports poll --by <operator>", "security-reports", declaration.Repository, null, read?.At, null,
                declaration.Repository + ":" + (read?.Problem ?? (read is null ? "unread" : "stale")));
        }
        foreach (SecurityObservation advisory in Untriaged(model, state, declared))
        {
            RecordEnvelope? resource = model.ById.TryGetValue(advisory.Resource, out RecordEnvelope? named) ? model.Latest(named) : null;
            AttentionReference? target = resource is null ? null : new AttentionReference(resource.Type, resource.Id, resource.ComputedHash);
            if (!Holds(advisory))
            {
                if (!ReadAt(advisory.ChangedAt, out DateTimeOffset changed) || now - changed >= SecurityReportsNews) continue;
                yield return ("information", "security advisory " + advisory.Identifier + " on " + advisory.Repository,
                    $"A security advisory the estate has disclosed or settled is new or changed: {advisory.Identifier} on {advisory.Repository}, state {advisory.State}, severity {advisory.Severity}. It holds no release and needs no triage.",
                    advisory.State, "Review it on GitHub if it is not the change you expected.", "security-report", advisory.Repository + ":" + advisory.Identifier,
                    advisory.ContentHash, advisory.ChangedAt, target, advisory.ContentHash);
                continue;
            }
            yield return ("incident", "security report " + advisory.Identifier + " on " + advisory.Repository,
                $"A security report is new or changed and awaits the operator's triage: {advisory.Identifier} on {advisory.Repository}, state {advisory.State}, severity {advisory.Severity}. Its text stays on GitHub; every release waits until it is triaged.",
                advisory.State, $"Review it on GitHub, then vericairn security-reports triage {advisory.Identifier} --repository {advisory.Repository} --expected {advisory.ContentHash} --disposition <security|duplicate|no-action> --by <operator>",
                "security-report", advisory.Repository + ":" + advisory.Identifier, advisory.ContentHash, advisory.ChangedAt, target, advisory.ContentHash);
        }
    }

    // One read of every declared repository, serialized with other reads, never under the service gate.
    private SecurityState RefreshSecurityReports(TimeSpan reuse, bool scheduled = false, bool store = true)
    {
        lock (securityReads)
        {
            IReadOnlyList<SecurityDeclaration> declared;
            SecurityState prior;
            lock (gate)
            {
                declared = SecurityDeclarations(store ? session.Load() : session.Read());
                prior = ReadSecurityState();
            }
            if (declared.Count == 0) return prior;
            DateTimeOffset started = PolicyClock.GetUtcNow();
            if (Reusable(prior, declared, started, reuse)) return prior;
            if (!store && checkedState is { } remembered && Reusable(remembered, declared, started, reuse)) return remembered;

            var reads = new List<SecurityRead>();
            var listings = new List<(SecurityDeclaration Declaration, SecurityAdvisoryListing Listing)>();
            string at = RecordParser.FormatTimestamp(started);
            foreach (SecurityDeclaration declaration in declared)
            {
                ISecurityAdvisoryReader? reader = Providers.SecurityAdvisories(declaration.Provider);
                string? problem = null;
                if (reader is null) problem = $"the security-report reader '{declaration.Provider}' has no implementation on this runtime's machine";
                else
                {
                    try
                    {
                        SecurityAdvisoryListing listing = reader.Read(declaration.Repository);
                        if (!string.Equals(listing.Repository, declaration.Repository, StringComparison.OrdinalIgnoreCase) || listing.Advisories is null)
                            throw new ProviderRefusedException("The reader answered for another repository.");
                        foreach (SecurityAdvisory advisory in listing.Advisories) advisory.Validate();
                        if (listing.Advisories.Select(a => a.Identifier).Distinct(StringComparer.Ordinal).Count() != listing.Advisories.Count)
                            throw new ProviderRefusedException("The reader listed one advisory twice.");
                        listings.Add((declaration, listing));
                    }
                    catch (ProviderRefusedException refused) { problem = refused.Message.TrimEnd('.'); }
                    catch (Exception failed) when (failed is not OutOfMemoryException)
                    {
                        problem = "the reader failed before its listing was complete";
                    }
                }
                reads.Add(new(declaration.Repository, declaration.Provider, declaration.Resource, at, problem is null, problem));
            }

            lock (gate)
            {
                SecurityState current = ReadSecurityState();
                var advisories = new List<SecurityObservation>();
                foreach (SecurityDeclaration declaration in declared)
                {
                    (SecurityDeclaration _, SecurityAdvisoryListing listing) = listings.FirstOrDefault(l => l.Declaration == declaration);
                    IEnumerable<SecurityObservation> kept = current.Advisories.Where(a => a.Resource == declaration.Resource
                        && string.Equals(a.Repository, declaration.Repository, StringComparison.OrdinalIgnoreCase));
                    if (listing is null)
                    {
                        // An incomplete read changes nothing it saw before: those revisions still stand and still hold.
                        advisories.AddRange(kept);
                        continue;
                    }
                    foreach (SecurityAdvisory advisory in listing.Advisories)
                    {
                        SecurityObservation? seen = kept.FirstOrDefault(a => a.Identifier == advisory.Identifier);
                        bool changed = seen is null || seen.ContentHash != advisory.ContentHash || seen.State != advisory.State || seen.Severity != advisory.Severity;
                        advisories.Add(new(declaration.Repository, declaration.Resource, advisory.Identifier, advisory.State, advisory.Severity, advisory.ContentHash,
                            seen?.FirstSeen ?? at, changed ? at : seen!.ChangedAt));
                    }
                }
                var stored = new SecurityState(
                    reads.Concat(current.Reads.Where(r => !reads.Any(n => n.Resource == r.Resource && n.Provider == r.Provider)
                        && declared.Any(d => d.Resource == r.Resource && d.Provider == r.Provider))).ToArray(),
                    advisories.OrderBy(a => a.Repository, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Identifier, StringComparer.Ordinal).ToArray());
                if (store && !session.IsReadOnly)
                {
                    session.Live.SetMeta(SecurityReportsKey, JsonSerializer.Serialize(stored, ProtocolJson.Options));
                    // The operator's attention changes with no record written; ask the event journal to derive it now.
                    MarkEventsDirty();
                }
                checkedState = store ? null : stored;
                SecurityReportsRead?.Invoke(scheduled);
                return stored;
            }
        }
    }

    /// <summary>A test's observer of each completed read: true when the daemon's schedule started it.</summary>
    internal Action<bool>? SecurityReportsRead { get; set; }

    /// <summary>Every declared repository, from every current product's declaration, by its current resource revision.</summary>
    internal static IReadOnlyList<SecurityDeclaration> SecurityDeclarations(EstateModel model)
    {
        var declared = new List<SecurityDeclaration>();
        foreach (RecordEnvelope product in model.Current("product").OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (!product.Body.TryGetProperty(SecurityReportRules.ProductField, out JsonElement reports) || reports.ValueKind != JsonValueKind.Object) continue;
            string? provider = reports.TryGetProperty("provider", out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (provider is null || !Patterns.Slug.IsMatch(provider) || !reports.TryGetProperty("repositories", out JsonElement repositories) || repositories.ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement id in repositories.EnumerateArray())
            {
                if (id.ValueKind != JsonValueKind.String || !model.ById.TryGetValue(id.GetString()!, out RecordEnvelope? named)) continue;
                RecordEnvelope resource = model.Latest(named);
                if (SecurityReportRules.GitHubLocation(resource) is not { } repository) continue;
                string root = model.Chain(resource)[0];
                if (!declared.Any(d => d.Provider == provider && d.Resource == root))
                    declared.Add(new(provider, root, repository));
            }
        }
        return declared;
    }

    private SecurityState ReadSecurityState()
    {
        try
        {
            SecurityState? state = session.Live.Meta(SecurityReportsKey) is string json ? JsonSerializer.Deserialize<SecurityState>(json, ProtocolJson.Options) : null;
            if (state?.Reads is null || state.Advisories is null) return SecurityState.Empty;
            // The store is this user's own, but only closed forms are shown or matched.
            return new(state.Reads.Where(r => r is not null && Patterns.GitHubRepository.IsMatch(r.Repository ?? "") && Ulid.IsValid(r.Resource ?? "")).ToArray(),
                state.Advisories.Where(a => a is not null && Patterns.GitHubAdvisory.IsMatch(a.Identifier ?? "") && Patterns.GitHubRepository.IsMatch(a.Repository ?? "")
                    && Ulid.IsValid(a.Resource ?? "") && ContentHash.IsValid(a.ContentHash)
                    && (SecurityAdvisory.States.Contains(a.State) || a.State == "unknown") && (SecurityAdvisory.Severities.Contains(a.Severity) || a.Severity == "unknown")).ToArray());
        }
        catch (JsonException) { return SecurityState.Empty; }
    }

    private static bool ReadAt(string? text, out DateTimeOffset at)
    {
        at = default;
        return RecordParser.IsTimestamp(text) && DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out at);
    }

    // Every declared repository's latest read completed within the reuse window, and none is dated after now.
    private static bool Reusable(SecurityState state, IReadOnlyList<SecurityDeclaration> declared, DateTimeOffset now, TimeSpan reuse) =>
        reuse > TimeSpan.Zero && declared.All(d => LatestRead(state, d) is { Complete: true } read
            && ReadAt(read.At, out DateTimeOffset at) && at <= now && now - at < reuse);

    private static SecurityRead? LatestRead(SecurityState state, SecurityDeclaration declaration) =>
        state.Reads.Where(r => r.Resource == declaration.Resource && r.Provider == declaration.Provider).OrderByDescending(r => r.At, StringComparer.Ordinal).FirstOrDefault();

    private static bool Stale(SecurityRead read, DateTimeOffset now) =>
        !ReadAt(read.At, out DateTimeOffset at) || now - at >= SecurityReportsStale;

    // Observed revisions of declared repositories that no current triage by the operator names by identifier, repository and hash.
    private static IEnumerable<SecurityObservation> Untriaged(EstateModel model, SecurityState state, IReadOnlyList<SecurityDeclaration> declared) =>
        state.Advisories.Where(a => declared.Any(d => d.Resource == a.Resource) && TriageOf(model, a) is null);

    // Untriaged reports: what holds a release. A published, closed or withdrawn advisory is the estate's own and holds nothing.
    private static IEnumerable<SecurityObservation> Holding(EstateModel model, SecurityState state, IReadOnlyList<SecurityDeclaration> declared) =>
        Untriaged(model, state, declared).Where(Holds);

    // A report in triage or draft state holds, and so does a state this build does not know, the conservative reading.
    private static bool Holds(SecurityObservation advisory) => !SecurityAdvisory.SettledStates.Contains(advisory.State, StringComparer.Ordinal);

    private static RecordEnvelope? TriageOf(EstateModel model, SecurityObservation advisory) =>
        model.Current("observation").Where(o => EstateModel.BodyString(o, "kind") == SecurityReportRules.TriageKind && model.HasRole(o.By, Roles.Operator)
            && o.Subject is { Type: "resource" } subject && model.ById.TryGetValue(subject.Id, out RecordEnvelope? resource) && model.Chain(model.Latest(resource))[0] == advisory.Resource
            && o.Body.TryGetProperty("facts", out JsonElement facts) && facts.ValueKind == JsonValueKind.Object
            && Fact(facts, "advisory") == advisory.Identifier && string.Equals(Fact(facts, "repository"), advisory.Repository, StringComparison.OrdinalIgnoreCase)
            && Fact(facts, "contentHash") == advisory.ContentHash && !RefusedByCheck(model, o)).OrderBy(o => o.At, StringComparer.Ordinal).LastOrDefault();

    // A triage the estate check refuses never clears a report, even before submission refuses the invalid estate.
    private static bool RefusedByCheck(EstateModel model, RecordEnvelope record) => model.Findings.Any(f => f.Severity == Severity.Error
        && (f.Location == record.ExpectedPath || f.Location.StartsWith(record.ExpectedPath + "#", StringComparison.Ordinal)));

    private static string? Fact(JsonElement facts, string name) => facts.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // Restricted and nonexistent answer alike: any principal other than the operator receives this one refusal, whatever exists.
    private static SecurityReportsResponse? SecurityReaderRefusal(EstateModel model, string? by) =>
        by is not null && Ulid.IsValid(by) && model.HasRole(by, Roles.Operator) ? null
            : SecurityProblem("denied", "Security reports are shown to and triaged by the operator only.");

    private SecurityReportsResponse SecurityAnswer(EstateModel model, SecurityState state, string message)
    {
        IReadOnlyList<SecurityDeclaration> declared = SecurityDeclarations(model);
        DateTimeOffset now = PolicyClock.GetUtcNow();
        var reads = declared.Select(d => LatestRead(state, d) is { } read
            ? new SecurityReportRead(d.Repository, d.Provider, read.At, read.Complete && !Stale(read, now), read.Problem ?? (read.Complete && Stale(read, now) ? "no read is recent enough" : null))
            : new SecurityReportRead(d.Repository, d.Provider, null, false, "not read on this machine yet")).ToArray();
        var reports = state.Advisories.Where(a => declared.Any(d => d.Resource == a.Resource))
            .Select(a => new SecurityReportRow(a.Identifier, a.Repository, a.State, a.Severity, a.ContentHash, a.FirstSeen, a.ChangedAt, TriageOf(model, a)?.Id, Holds(a))).ToArray();
        bool held = reads.Any(r => !r.Complete) || reports.Any(r => r.Holds && r.Triage is null);
        string summary = declared.Count == 0 ? "No product declares security reports, so none are read and no release waits for them."
            : held ? message + " Releases are held: " + (reads.Any(r => !r.Complete) ? "a declared repository is unconfirmed" : "a report awaits triage") + "."
            : message + " Every report in triage or draft state is triaged; no release waits for security reports.";
        return new(true, summary, reads, reports, held);
    }

    private static SecurityReportsResponse SecurityProblem(string code, string message) => new(false, message, [], [], Problem: new Problem(code, message, []));
}
