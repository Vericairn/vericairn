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
/// incident attention for the operator until the operator's triage names its current revision; a closed or withdrawn advisory is
/// one until an operator triage names it at any revision, so a report closed by anyone with administrator access cannot slip past.
/// While one waits, or a declared repository cannot be read completely or no longer resolves to a repository on GitHub, release
/// submission refuses. A new or changed published advisory, and a closed or withdrawn one already triaged, is information for a week
/// and holds nothing.
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

    // A declared resource, by the first id of its lineage; Repository is null when its current revision is no repository on GitHub.
    internal sealed record SecurityDeclaration(string Provider, string Resource, string? Repository)
    {
        internal bool Covers(string resource, string repository) =>
            Resource == resource && Repository is not null && string.Equals(Repository, repository, StringComparison.OrdinalIgnoreCase);
    }

    internal sealed record SecurityRead(string Repository, string Provider, string Resource, string At, bool Complete, string? Problem);

    internal sealed record SecurityObservation(string Repository, string Resource, string Identifier, string State, string Severity, string ContentHash,
        string FirstSeen, string ChangedAt);

    internal sealed record SecurityState(IReadOnlyList<SecurityRead> Reads, IReadOnlyList<SecurityObservation> Advisories)
    {
        internal static SecurityState Empty { get; } = new([], []);
    }

    // What an observed advisory asks of the operator now.
    private enum SecurityStanding
    {
        // Triaged where it must be, and quiet.
        Settled,

        // Holds every release: a report untriaged at its revision, or a closed or withdrawn one no operator triage names.
        Holds,

        // A new or changed advisory that holds nothing: information for a week.
        News,
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
            SecurityObservation? observed = Observed(state, SecurityDeclarations(model)).FirstOrDefault(a => a.Identifier == request.Advisory
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
    /// while any is unconfirmed or unresolvable or any advisory holds. Only the operator sees which; anyone else gets one objection
    /// that names, counts and describes nothing, whatever exists. With <paramref name="unread"/> nothing is read at all and a
    /// declaration alone answers with that objection: a caller that cannot vouch for the operator's view asks for it. Like the rest
    /// of admission it reads the canonical records without refreshing the live cache, and a check (<paramref name="store"/> false)
    /// keeps what it read in memory, leaving the store unchanged.
    /// </summary>
    internal IReadOnlyList<FindingDto> SecurityReportObjections(string? by, bool store, bool unread)
    {
        lock (gate)
        {
            if (SecurityDeclarations(session.Read()).Count == 0) return [];
            if (unread) return [new("error", "release.security-reports", "security-reports", SecurityHeldMessage)];
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
                if (declaration.Repository is null)
                {
                    findings.Add(new("error", "release.security-reports-unavailable", "security-reports:" + declaration.Resource,
                        $"Security reports of declared resource {declaration.Resource} are unconfirmed: its current revision is no repository on GitHub, so nothing reads them. Next: the operator restores the resource as the repository it was, or records a product revision that declares the repository to read, then check again."));
                    continue;
                }
                SecurityRead? read = LatestRead(state, declaration);
                if (read is not { Complete: true } || Stale(read, now))
                    findings.Add(new("error", "release.security-reports-unavailable", "security-reports:" + declaration.Repository,
                        $"Security reports on {declaration.Repository} are unconfirmed: {read?.Problem ?? "no complete read is recent enough"}."));
            }
            foreach (SecurityObservation advisory in Observed(state, declared).Where(a => Standing(model, a, now) == SecurityStanding.Holds))
                findings.Add(new("error", "release.security-report", "security-report:" + advisory.Repository + ":" + advisory.Identifier,
                    $"Security report {advisory.Identifier} on {advisory.Repository} (state {advisory.State}, severity {advisory.Severity}) {Waiting(advisory)} {advisory.ContentHash}."
                    + $" Next: review it on GitHub, then run vericairn security-reports triage {advisory.Identifier} --repository {advisory.Repository} --expected {advisory.ContentHash} --disposition <security|duplicate|no-action> --by <operator>, and check again."));
            if (findings.Count == 0 || model.HasRole(by, Roles.Operator)) return findings;
            return [new("error", "release.security-reports", "security-reports", SecurityHeldMessage)];
        }
    }

    private static string Waiting(SecurityObservation advisory) => Closes(advisory)
        ? "was closed or withdrawn without the operator's triage; triage it at its current revision"
        : "has no triage of its current revision";

    /// <summary>Attention for the operator: each advisory that holds, each one new or changed this week, and each declared repository whose reports are unconfirmed.</summary>
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
            if (declaration.Repository is null)
            {
                RecordEnvelope? named = model.ById.TryGetValue(declaration.Resource, out RecordEnvelope? found) ? model.Latest(found) : null;
                yield return ("failed check", "security reports of resource " + declaration.Resource,
                    $"Security reports of declared resource {declaration.Resource} are unconfirmed: its current revision is no repository on GitHub, so nothing reads them. Every release waits until the declaration names a repository again.",
                    "unconfirmed", "Restore the resource as the repository it was, or record a product revision that declares the repository to read.",
                    "security-reports", declaration.Resource, null, null,
                    named is null ? null : new AttentionReference(named.Type, named.Id, named.ComputedHash), declaration.Resource + ":unresolved");
                continue;
            }
            SecurityRead? read = LatestRead(state, declaration);
            if (read is { Complete: true } && !Stale(read, now)) continue;
            string why = read is null ? "they have not been read on this machine yet"
                : read.Complete ? "no read has completed since " + read.At : read.Problem + " (last attempt " + read.At + ")";
            yield return ("failed check", "security reports on " + declaration.Repository,
                $"Security reports on {declaration.Repository} are unconfirmed: {why}. Every release waits until they are read completely.",
                "unconfirmed", "vericairn security-reports poll --by <operator>", "security-reports", declaration.Repository, null, read?.At, null,
                declaration.Repository + ":" + (read?.Problem ?? (read is null ? "unread" : "stale")));
        }
        foreach (SecurityObservation advisory in Observed(state, declared))
        {
            SecurityStanding standing = Standing(model, advisory, now);
            if (standing == SecurityStanding.Settled) continue;
            RecordEnvelope? resource = model.ById.TryGetValue(advisory.Resource, out RecordEnvelope? named) ? model.Latest(named) : null;
            AttentionReference? target = resource is null ? null : new AttentionReference(resource.Type, resource.Id, resource.ComputedHash);
            if (standing == SecurityStanding.News)
            {
                yield return ("information", "security advisory " + advisory.Identifier + " on " + advisory.Repository,
                    $"A security advisory that holds no release is new or changed: {advisory.Identifier} on {advisory.Repository}, state {advisory.State}, severity {advisory.Severity}. It needs no triage.",
                    advisory.State, "Review it on GitHub if it is not the change you expected.", "security-report", advisory.Repository + ":" + advisory.Identifier,
                    advisory.ContentHash, advisory.ChangedAt, target, advisory.ContentHash);
                continue;
            }
            string waits = Closes(advisory)
                ? "A security report was closed or withdrawn without the operator's triage"
                : "A security report is new or changed and awaits the operator's triage";
            yield return ("incident", "security report " + advisory.Identifier + " on " + advisory.Repository,
                $"{waits}: {advisory.Identifier} on {advisory.Repository}, state {advisory.State}, severity {advisory.Severity}. Its text stays on GitHub; every release waits until it is triaged.",
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
            SecurityDeclaration[] readable = [.. declared.Where(d => d.Repository is not null)];
            if (readable.Length == 0) return prior;
            DateTimeOffset started = PolicyClock.GetUtcNow();
            if (Reusable(prior, readable, started, reuse)) return prior;
            if (!store && checkedState is { } remembered && Reusable(remembered, readable, started, reuse)) return remembered;

            var reads = new List<SecurityRead>();
            var listings = new List<(SecurityDeclaration Declaration, SecurityAdvisoryListing Listing)>();
            string at = RecordParser.FormatTimestamp(started);
            foreach (SecurityDeclaration declaration in readable)
            {
                string repository = declaration.Repository!;
                ISecurityAdvisoryReader? reader = Providers.SecurityAdvisories(declaration.Provider);
                string? problem = null;
                if (reader is null) problem = $"the security-report reader '{declaration.Provider}' has no implementation on this runtime's machine";
                else
                {
                    try
                    {
                        SecurityAdvisoryListing listing = reader.Read(repository);
                        if (!string.Equals(listing.Repository, repository, StringComparison.OrdinalIgnoreCase) || listing.Advisories is null)
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
                reads.Add(new(repository, declaration.Provider, declaration.Resource, at, problem is null, problem));
            }

            lock (gate)
            {
                SecurityState current = ReadSecurityState();
                var advisories = new List<SecurityObservation>();
                foreach (SecurityDeclaration declaration in readable)
                {
                    (SecurityDeclaration _, SecurityAdvisoryListing listing) = listings.FirstOrDefault(l => l.Declaration == declaration);
                    IEnumerable<SecurityObservation> kept = current.Advisories.Where(a => declaration.Covers(a.Resource, a.Repository));
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
                        advisories.Add(new(declaration.Repository!, declaration.Resource, advisory.Identifier, advisory.State, advisory.Severity, advisory.ContentHash,
                            seen?.FirstSeen ?? at, changed ? at : seen!.ChangedAt));
                    }
                }
                var stored = new SecurityState(
                    reads.Concat(current.Reads.Where(r => !reads.Any(n => n.Resource == r.Resource && n.Provider == r.Provider && n.Repository == r.Repository)
                        && readable.Any(d => d.Provider == r.Provider && d.Covers(r.Resource, r.Repository)))).ToArray(),
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

    /// <summary>
    /// Every declared resource, from every current product's declaration, by its current revision. A resource that no longer
    /// resolves to a repository on GitHub, or is missing, stays declared with no repository: it is never skipped, so it holds.
    /// </summary>
    internal static IReadOnlyList<SecurityDeclaration> SecurityDeclarations(EstateModel model)
    {
        var declared = new List<SecurityDeclaration>();
        foreach (RecordEnvelope product in model.Current("product").OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (!product.Body.TryGetProperty(SecurityReportRules.ProductField, out JsonElement reports) || reports.ValueKind != JsonValueKind.Object) continue;
            string provider = reports.TryGetProperty("provider", out JsonElement p) && p.ValueKind == JsonValueKind.String && Patterns.Slug.IsMatch(p.GetString()!)
                ? p.GetString()! : "unavailable";
            if (!reports.TryGetProperty("repositories", out JsonElement repositories) || repositories.ValueKind != JsonValueKind.Array) continue;
            foreach (JsonElement id in repositories.EnumerateArray())
            {
                if (id.ValueKind != JsonValueKind.String) continue;
                string? repository = null;
                string root = id.GetString()!;
                if (model.ById.TryGetValue(root, out RecordEnvelope? named))
                {
                    RecordEnvelope resource = model.Latest(named);
                    root = model.Chain(resource)[0];
                    repository = SecurityReportRules.GitHubLocation(resource);
                }
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

    // Every readable declaration's latest read completed within the reuse window, and none is dated after now.
    private static bool Reusable(SecurityState state, IReadOnlyList<SecurityDeclaration> readable, DateTimeOffset now, TimeSpan reuse) =>
        reuse > TimeSpan.Zero && readable.All(d => LatestRead(state, d) is { Complete: true } read
            && ReadAt(read.At, out DateTimeOffset at) && at <= now && now - at < reuse);

    // A read counts only for the declaration's resource at its current repository, so a read of an old location never stands for a new one.
    private static SecurityRead? LatestRead(SecurityState state, SecurityDeclaration declaration) =>
        state.Reads.Where(r => r.Provider == declaration.Provider && declaration.Covers(r.Resource, r.Repository))
            .OrderByDescending(r => r.At, StringComparer.Ordinal).FirstOrDefault();

    private static bool Stale(SecurityRead read, DateTimeOffset now) =>
        !ReadAt(read.At, out DateTimeOffset at) || now - at >= SecurityReportsStale;

    // The observed revisions of declared repositories at their current locations.
    private static IEnumerable<SecurityObservation> Observed(SecurityState state, IReadOnlyList<SecurityDeclaration> declared) =>
        state.Advisories.Where(a => declared.Any(d => d.Covers(a.Resource, a.Repository)));

    // A closed or withdrawn advisory was a report someone ended; the operator names it once, at any revision.
    private static bool Closes(SecurityObservation advisory) => advisory.State is "closed" or "withdrawn";

    // A published advisory is the estate's own disclosure and never holds. A closed or withdrawn one holds until an operator triage
    // names it at any revision. Every other state, unknown included, is a report that holds until a triage names its current revision.
    private static SecurityStanding Standing(EstateModel model, SecurityObservation advisory, DateTimeOffset now)
    {
        if (TriageOf(model, advisory, exact: true) is not null) return SecurityStanding.Settled;
        if (advisory.State != "published" && (!Closes(advisory) || TriageOf(model, advisory, exact: false) is null)) return SecurityStanding.Holds;
        return ReadAt(advisory.ChangedAt, out DateTimeOffset changed) && now - changed < SecurityReportsNews ? SecurityStanding.News : SecurityStanding.Settled;
    }

    private static RecordEnvelope? TriageOf(EstateModel model, SecurityObservation advisory, bool exact = true) =>
        model.Current("observation").Where(o => EstateModel.BodyString(o, "kind") == SecurityReportRules.TriageKind && model.HasRole(o.By, Roles.Operator)
            && o.Subject is { Type: "resource" } subject && model.ById.TryGetValue(subject.Id, out RecordEnvelope? resource) && model.Chain(model.Latest(resource))[0] == advisory.Resource
            && o.Body.TryGetProperty("facts", out JsonElement facts) && facts.ValueKind == JsonValueKind.Object
            && Fact(facts, "advisory") == advisory.Identifier && string.Equals(Fact(facts, "repository"), advisory.Repository, StringComparison.OrdinalIgnoreCase)
            && (!exact || Fact(facts, "contentHash") == advisory.ContentHash) && !RefusedByCheck(model, o)).OrderBy(o => o.At, StringComparer.Ordinal).LastOrDefault();

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
        var reads = declared.Select(d => d.Repository is null
            ? new SecurityReportRead(d.Resource, d.Provider, null, false, "the declared resource's current revision is no repository on GitHub")
            : LatestRead(state, d) is { } read
                ? new SecurityReportRead(d.Repository, d.Provider, read.At, read.Complete && !Stale(read, now), read.Problem ?? (read.Complete && Stale(read, now) ? "no read is recent enough" : null))
                : new SecurityReportRead(d.Repository, d.Provider, null, false, "not read on this machine yet")).ToArray();
        var reports = Observed(state, declared)
            .Select(a => new SecurityReportRow(a.Identifier, a.Repository, a.State, a.Severity, a.ContentHash, a.FirstSeen, a.ChangedAt, TriageOf(model, a)?.Id,
                Standing(model, a, now) == SecurityStanding.Holds)).ToArray();
        bool held = reads.Any(r => !r.Complete) || reports.Any(r => r.Holds);
        string summary = declared.Count == 0 ? "No product declares security reports, so none are read and no release waits for them."
            : held ? message + " Releases are held: " + (reads.Any(r => !r.Complete) ? "a declared repository is unconfirmed" : "a report awaits triage") + "."
            : message + " No report waits for triage; no release waits for security reports.";
        return new(true, summary, reads, reports, held);
    }

    private static SecurityReportsResponse SecurityProblem(string code, string message) => new(false, message, [], [], Problem: new Problem(code, message, []));
}
