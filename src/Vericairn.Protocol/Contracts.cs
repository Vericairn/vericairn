// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vericairn.Protocol;

/// <summary>The API version every route carries.</summary>
public static class ApiVersion
{
    /// <summary>Route prefix.</summary>
    public const string Prefix = "/v1";

    /// <summary>The version string reported by the daemon.</summary>
    public const string Current = "1";
}

/// <summary>One validation or check finding.</summary>
/// <param name="Severity">info, warning or error.</param>
/// <param name="Code">Stable code.</param>
/// <param name="Location">Repository-relative location.</param>
/// <param name="Message">Explanation.</param>
public sealed record FindingDto(string Severity, string Code, string Location, string Message);

/// <summary>Why a request was not carried out: a reason and, for rejected records, the findings.</summary>
/// <param name="Code">Stable code: refused, rejected, not-found, invalid, denied (a loopback or local-network request not signed by a device admitted now and permitted there, or a pairing code that is unknown, expired, used, exhausted or does not match) or provider (a release provider's refusal, inside a release response); conflict is reserved and nothing answers with it yet.</param>
/// <param name="Message">Explanation with the permitted next action.</param>
/// <param name="Findings">Findings behind a rejection; may be empty.</param>
public sealed record Problem(string Code, string Message, IReadOnlyList<FindingDto> Findings);

/// <summary>A record that was written.</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="Id">Record id.</param>
/// <param name="Hash">Content hash.</param>
public sealed record RecordWritten(string Path, string Id, string Hash);

/// <summary>The outcome of a write: written, or a problem. Never both.</summary>
/// <param name="Written">The record, when written.</param>
/// <param name="Problem">The problem, when not.</param>
public sealed record WriteOutcome(RecordWritten? Written, Problem? Problem)
{
    /// <summary>True when written.</summary>
    [JsonIgnore]
    public bool Ok => Written is not null;

    /// <summary>A written outcome.</summary>
    public static WriteOutcome Success(RecordWritten written) => new(written, null);

    /// <summary>A failed outcome.</summary>
    public static WriteOutcome Fail(Problem problem) => new(null, problem);
}

/// <summary>The daemon's identity and what it serves.</summary>
/// <param name="Api">API version.</param>
/// <param name="Vericairn">Product version.</param>
/// <param name="Estate">Estate id.</param>
/// <param name="Root">Estate root on the daemon's machine.</param>
/// <param name="Profile">Storage profile.</param>
/// <param name="RecordFormat">Record format.</param>
/// <param name="StartedAt">When the daemon started.</param>
/// <param name="ProcessId">Process id.</param>
public sealed record HealthResponse(string Api, string Vericairn, string Estate, string Root, string Profile, long RecordFormat, string StartedAt, int ProcessId);

/// <summary>The estate's status: the human text and the canonical JSON that is identical on every machine.</summary>
/// <param name="Text">Rendered status.</param>
/// <param name="Json">Canonical status document.</param>
/// <param name="HasErrors">Whether errors exist.</param>
public sealed record StatusResponse(string Text, JsonElement Json, bool HasErrors);

/// <summary>
/// The progress report: where the estate stands, its work orders by state, each published release and
/// what it contained, what comes next by release, the next eligible work and what waits on the operator. The same on every machine for
/// the same records in its record-derived sections; agentClaims adds local contributor claims and runSignals adds worker observations.
/// </summary>
/// <param name="Text">The report as text, with agent claims and worker observations labelled separately.</param>
/// <param name="Json">The report as canonical JSON (format vericairn.progress/1).</param>
/// <param name="HasErrors">Whether the check the report shows found errors.</param>
public sealed record ProgressResponse(string Text, JsonElement Json, bool HasErrors);

/// <summary>Findings of a validation or check.</summary>
/// <param name="Summary">For example '161 records'.</param>
/// <param name="Errors">Error count.</param>
/// <param name="Warnings">Warning count.</param>
/// <param name="Findings">Sorted findings.</param>
public sealed record FindingsResponse(string Summary, int Errors, int Warnings, IReadOnlyList<FindingDto> Findings);

/// <summary>A record as stored.</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="Id">Id.</param>
/// <param name="Type">Type.</param>
/// <param name="Hash">Content hash.</param>
/// <param name="Current">Whether no later record supersedes it.</param>
/// <param name="Envelope">The whole record.</param>
public sealed record RecordResponse(string Path, string Id, string Type, string Hash, bool Current, JsonElement Envelope);

/// <summary>A request to author a record; the runtime signs it with the author's admitted device on its machine.</summary>
/// <param name="Type">Record type.</param>
/// <param name="By">Authoring principal.</param>
/// <param name="Body">Body object.</param>
/// <param name="Subject">Subject as type:id or type:id:hash.</param>
/// <param name="Supersedes">Predecessor id.</param>
/// <param name="At">Authoring time.</param>
/// <param name="Id">Explicit id (root operator only).</param>
public sealed record NewRecordRequest(string Type, string By, JsonElement Body, string? Subject = null, string? Supersedes = null, string? At = null, string? Id = null);

/// <summary>A brief: everything a contributor needs, from the records alone.</summary>
/// <param name="Markdown">Human form.</param>
/// <param name="Json">Machine form.</param>
/// <param name="MarkdownMeasurement">Exact measurement and identity of the complete Markdown, when supplied by a context-aware runtime.</param>
/// <param name="Problem">A context refusal; null on a complete brief.</param>
/// <param name="JsonMeasurement">Identity and measurement of the complete canonical machine brief, including its context package and accounting.</param>
public sealed record BriefResponse(string Markdown, JsonElement Json, ContextTextMeasurement? MarkdownMeasurement = null, Problem? Problem = null, ContextTextMeasurement? JsonMeasurement = null);

/// <summary>What an observer sees of a work order.</summary>
/// <param name="Text">Human form.</param>
/// <param name="Json">Machine form (the brief document, which carries attempts and results).</param>
public sealed record ObserveResponse(string Text, JsonElement Json);

/// <summary>Delegate a work order to a contributor.</summary>
/// <param name="WorkOrder">Key or id.</param>
/// <param name="Contributor">Contributor principal.</param>
/// <param name="By">Assigning principal.</param>
/// <param name="Brief">The brief object.</param>
/// <param name="Note">Free text.</param>
public sealed record AssignRequest(string WorkOrder, string Contributor, string By, JsonElement Brief, string? Note = null);

/// <summary>Record the start of an attempt.</summary>
/// <param name="Assignment">Assignment id.</param>
/// <param name="By">The contributor.</param>
/// <param name="Workspace">Where the work happens.</param>
/// <param name="Note">Intent.</param>
/// <param name="At">Start time.</param>
public sealed record AttemptRequest(string Assignment, string By, string Workspace, string Note, string? At = null);

/// <summary>A contributor's latest progress claim; only a completed phase is a record.</summary>
/// <param name="Attempt">The contributor's attempt.</param>
/// <param name="By">The contributing principal.</param>
/// <param name="Phase">A stable lower-case phase name.</param>
/// <param name="Note">The progress claim.</param>
/// <param name="Completed">Whether to record the phase's completion, at most once.</param>
public sealed record WorkProgressRequest(string Attempt, string By, string Phase, string Note, bool Completed = false);

/// <summary>A progress claim, explicitly separate from the worker's observations.</summary>
/// <param name="Attempt">The attempt.</param>
/// <param name="By">The contributor.</param>
/// <param name="Phase">The named phase.</param>
/// <param name="Note">What the contributor claims.</param>
/// <param name="At">When it was submitted.</param>
/// <param name="Completed">Whether the phase was claimed complete.</param>
/// <param name="Record">The completion record, if any.</param>
public sealed record WorkProgressClaim(string Attempt, string By, string Phase, string Note, string At, bool Completed, string? Record = null);

/// <summary>A progress submission; a successful interim note has no written record.</summary>
/// <param name="Claim">The claim now shown.</param>
/// <param name="Written">The single phase completion record, if requested.</param>
/// <param name="Problem">Why nothing was submitted, if refused.</param>
public sealed record WorkProgressResponse(WorkProgressClaim? Claim, RecordWritten? Written = null, Problem? Problem = null)
{
    /// <summary>Whether the claim was submitted.</summary>
    public bool Ok => Claim is not null && Problem is null;
}

/// <summary>Submit the result of an attempt.</summary>
/// <param name="Attempt">Attempt id.</param>
/// <param name="By">The contributor.</param>
/// <param name="Body">Result body.</param>
public sealed record ResultRequest(string Attempt, string By, JsonElement Body);

/// <summary>Request an amendment of an assignment.</summary>
/// <param name="Assignment">Assignment id.</param>
/// <param name="By">Requesting principal.</param>
/// <param name="Body">Amendment body.</param>
public sealed record AmendRequest(string Assignment, string By, JsonElement Body);

/// <summary>One device as the estate and this machine see it.</summary>
/// <param name="Id">Device id.</param>
/// <param name="Principal">Principal id.</param>
/// <param name="PrincipalName">Principal name.</param>
/// <param name="Name">Device name.</param>
/// <param name="Platform">Platform.</param>
/// <param name="Custody">Custody kind.</param>
/// <param name="Admission">admitted, revoked or not admitted.</param>
/// <param name="HeldHere">Whether this machine holds its key.</param>
/// <param name="Presence">required or none: whether each signature needs the owner's presence (record format 2).</param>
/// <param name="Purpose">signing or recovery (record format 2).</param>
/// <param name="Profiles">The connectivity profiles the device is permitted, from 0.6.0.</param>
public sealed record DeviceRow(string Id, string Principal, string? PrincipalName, string? Name, string? Platform, string? Custody, string Admission, bool HeldHere, string? Presence = null, string? Purpose = null, IReadOnlyList<string>? Profiles = null);

/// <summary>Create a device for a principal on the runtime's machine.</summary>
/// <param name="Principal">Principal id.</param>
/// <param name="Name">Device name.</param>
/// <param name="Custody">Custody: default (the strongest this machine offers), file, cng on Windows, secure-enclave on a Mac, or secret-service on Linux.</param>
/// <param name="Profiles">Permitted profiles.</param>
/// <param name="Presence">Whether every signature needs the owner's presence, where the custody enforces it; when omitted, a person's device needs it wherever its custody enforces it and an agent's or a service's never does.</param>
public sealed record DeviceCreateRequest(string Principal, string Name, string? Custody = null, IReadOnlyList<string>? Profiles = null, bool? Presence = null);

/// <summary>Admit a device.</summary>
/// <param name="Device">Device id.</param>
/// <param name="By">Admitting principal.</param>
/// <param name="Note">Why.</param>
/// <param name="Recovery">Absolute path of the operator's recovery key, to sign with it when no admitted device of the operator is left; from record format 2.</param>
/// <param name="Passphrase">The recovery key's passphrase.</param>
public sealed record AdmitRequest(string Device, string By, string Note, string? Recovery = null, string? Passphrase = null);

/// <summary>Make the operator's recovery device: a key made in memory, written once encrypted to a file for offline keeping, never held by this machine; from record format 2.</summary>
/// <param name="Principal">The operator the device acts for.</param>
/// <param name="Name">Descriptive name, never a machine path.</param>
/// <param name="Output">Absolute path of the new key file; an existing file is never overwritten.</param>
/// <param name="Passphrase">The passphrase that encrypts the key, of at least twelve characters.</param>
public sealed record DeviceRecoveryRequest(string Principal, string Name, string Output, string Passphrase);

/// <summary>Rename a device, keeping its identity; from record format 2.</summary>
/// <param name="Device">Device id: its identity or any revision.</param>
/// <param name="Name">The new descriptive name.</param>
public sealed record DeviceRenameRequest(string Device, string Name);

/// <summary>Give a device a new key, keeping its identity, signed by the previous key and the new one; from record format 2.</summary>
/// <param name="Device">Device id: its identity or any revision.</param>
/// <param name="Custody">Custody of the new key: file, cng, or secure-enclave on a Mac; the current custody when omitted.</param>
/// <param name="Presence">Whether every signature with the new key needs the owner's presence, Secure Enclave custody only; when omitted, the device keeps its presence where the new custody enforces it.</param>
public sealed record DeviceRotateRequest(string Device, string? Custody = null, bool? Presence = null);

/// <summary>A declared living document and its derived state.</summary>
/// <param name="Path">The document's repository-relative path.</param>
/// <param name="Repository">The resource record of the repository that holds it; null for the estate's own repository.</param>
/// <param name="Declaration">The current declaration's id.</param>
/// <param name="Title">The document's title.</param>
/// <param name="State">current, due or unavailable.</param>
/// <param name="Level">The level it is due at: remind, warn or refuse; null when it is current.</param>
/// <param name="Reasons">Every trigger newer than its latest clearance.</param>
/// <param name="Clearance">The latest clearance's record id, or null.</param>
/// <param name="Unavailable">The pack contributions it names that no installed pack provides.</param>
/// <param name="Notes">What could not be evaluated here, and why.</param>
public sealed record DocumentRow(string Path, string? Repository, string Declaration, string? Title, string State, string? Level, IReadOnlyList<DocumentReason> Reasons, string? Clearance, IReadOnlyList<string> Unavailable, IReadOnlyList<string> Notes);

/// <summary>Why a living document is due.</summary>
/// <param name="On">The trigger.</param>
/// <param name="Level">remind, warn or refuse.</param>
/// <param name="Summary">What fired it.</param>
/// <param name="Record">The record that fired it; null for covered paths.</param>
public sealed record DocumentReason(string On, string Level, string Summary, string? Record);

/// <summary>Review a living document: the runtime reads its content, records the covered paths' identity and the triggers it answers, and runs its declared checks.</summary>
/// <param name="By">The reviewing principal.</param>
/// <param name="Outcome">current, revised or needs-change.</param>
/// <param name="Summary">What was checked.</param>
/// <param name="Findings">What was found: severity (error, warning or info), summary and optional location.</param>
/// <param name="Commit">For a document of another repository, the commit reviewed.</param>
/// <param name="Independence">For a document of another repository, whose author the estate's records do not show, the reviewer's independence of its content: self, independent-agent or operator; without it, self for a principal that holds the builder role, else judged from the estate's records. An estate file's revisions decide its reviewer's independence, and a statement that differs is refused. From 0.6.0.</param>
public sealed record DocumentReviewRequest(string By, string Outcome, string Summary, IReadOnlyList<DocumentFinding>? Findings = null, string? Commit = null, string? Independence = null);

/// <summary>A finding of a document review.</summary>
/// <param name="Severity">error, warning or info.</param>
/// <param name="Summary">What was found.</param>
/// <param name="Location">Where, optionally.</param>
public sealed record DocumentFinding(string Severity, string Summary, string? Location = null);

/// <summary>One goal: its current revision, title and status, and its sponsor.</summary>
/// <param name="Id">The current revision's record id.</param>
/// <param name="Hash">The current revision's hash.</param>
/// <param name="Title">The title.</param>
/// <param name="Status">proposed, active, paused, budget-limited, blocked, impossible, abandoned or achieved.</param>
/// <param name="Sponsor">The sponsor's principal id.</param>
/// <param name="SponsorName">The sponsor's name.</param>
public sealed record GoalRow(string Id, string Hash, string Title, string Status, string Sponsor, string? SponsorName);

/// <summary>
/// A goal as <c>vericairn goal</c> shows it: its objective, sponsor, status and granted budget, each condition's verdict (met, not-met
/// or unknown) with the records it read beside its current evaluation's verdict, its steps, and what stands between it and its
/// acceptance. The same on every machine for the same records.
/// </summary>
/// <param name="Text">Human form.</param>
/// <param name="Json">Machine form, canonical.</param>
public sealed record GoalResponse(string Text, JsonElement Json);

/// <summary>
/// A goal's next steps routed from the runtime's machine (the next-eligible view with routing and budget): each of its steps with the
/// state status derives, what holds each planned one, and for each eligible one the principal its role's roster reaches, or why none
/// is available, with whether the goal's budget allows it; the goal's ledger and its asks. It reads that machine's launchers and keys,
/// so it is not the same on every machine; reading it records and starts nothing.
/// </summary>
/// <param name="Text">Human form.</param>
/// <param name="Json">Machine form, canonical: format vericairn.next/1.</param>
public sealed record NextResponse(string Text, JsonElement Json);

/// <summary>
/// Take a goal's next step, one at a time (the smallest form of Community's conductor): the first eligible step whose route acts, routed
/// from the runtime's machine. An agent's step is assigned to the agent when no assignment holds it and its run starts through the
/// worker, within the custody precondition; a person's step is assigned to the person and asked of them; a step no principal is
/// available for is asked of the sponsor. Nothing else is dispatched, and nothing is accepted. Refused over the local network,
/// since the worker runs on this machine. A device-signed request acts only for its device's principal.
/// </summary>
/// <param name="By">The principal recording the assignment, the ask and the run: an operator, the person sponsoring the goal, or a principal with local-engineering. A device-signed request names its device's principal.</param>
/// <param name="Repository">For an agent's step, the repository resource whose registered clone its workspace is made from.</param>
/// <param name="Settings">For an agent's step, the settings of the launcher routing chose, for example command.</param>
/// <param name="WallMinutes">For an agent's step, the run's cap in wall minutes; what remains of the goal's budget when omitted, and never more.</param>
/// <param name="Branch">For an agent's step, the work branch; work/ and the work order's key when omitted.</param>
/// <param name="Base">For an agent's step, what a new work branch starts from.</param>
public sealed record GoalStepRequest(string By, string? Repository = null, IReadOnlyDictionary<string, string>? Settings = null, long? WallMinutes = null, string? Branch = null, string? Base = null);

/// <summary>What taking a goal's next step did: what was recorded and started, never more. A problem means nothing was dispatched.</summary>
/// <param name="Goal">The goal's current revision.</param>
/// <param name="Outcome">run (an agent's run started), ask (an ask recorded, with an assignment for a person), or none (nothing to dispatch).</param>
/// <param name="Message">What happened and what follows, in words.</param>
/// <param name="Ok">Whether the step was taken as routed, or there was nothing to take.</param>
/// <param name="Step">The step taken: its work order's key.</param>
/// <param name="Route">Its route: agent, person, reserved or unavailable.</param>
/// <param name="Principal">The principal it reached: the agent, or the person asked.</param>
/// <param name="Assignment">The assignment written to the chosen principal, when this call wrote one.</param>
/// <param name="Ask">The ask written, when this call wrote one.</param>
/// <param name="Run">For an agent, what starting its run did.</param>
/// <param name="Problem">Why nothing was dispatched.</param>
public sealed record GoalStepResponse(string Goal, string Outcome, string Message, bool Ok, string? Step = null, string? Route = null, string? Principal = null, RecordWritten? Assignment = null, RecordWritten? Ask = null, RunResponse? Run = null, Problem? Problem = null);

/// <summary>Take over a step an agent holds: the hand-over is a new assignment of the step to a person, carrying the agent's brief and naming its assignment.</summary>
/// <param name="Assignment">The agent's assignment, which holds the step.</param>
/// <param name="By">The person recording the hand-over: the one taking over, the goal's sponsor or an operator; never an agent. A device-signed request names its device's principal.</param>
/// <param name="Contributor">The person who takes the step over; the recording principal when omitted.</param>
/// <param name="Note">Why; a sentence naming the hand-over when omitted.</param>
public sealed record TakeoverRequest(string Assignment, string By, string? Contributor = null, string? Note = null);

/// <summary>What a hand-over did: the assignment written, and the stop asked of every run of the agent's assignment still in flight.</summary>
/// <param name="Message">What happened, in words.</param>
/// <param name="Ok">Whether the hand-over was recorded.</param>
/// <param name="Assignment">The person's assignment, the hand-over record.</param>
/// <param name="Stops">The stops asked of the agent's runs in flight, each with what was observed.</param>
/// <param name="Problem">Why nothing was recorded.</param>
public sealed record TakeoverResponse(string Message, bool Ok, RecordWritten? Assignment = null, IReadOnlyList<RunStopResponse>? Stops = null, Problem? Problem = null);

/// <summary>
/// What waits on a principal, or on every principal: the asks still open or notified, oldest first, at most three delivered to each
/// principal with the recommendation first and the consequence of leaving it unanswered, and the rest waiting in order behind them.
/// The same on every machine for the same records; reading it answers nothing.
/// </summary>
/// <param name="Text">Human form.</param>
/// <param name="Json">Machine form, canonical: format vericairn.asks/1.</param>
public sealed record AsksResponse(string Text, JsonElement Json);

/// <summary>
/// Propose a goal, or a revision of one; a proposal authorizes and funds nothing, and a revision starts again from its sponsor's
/// activation. Anyone may propose a new goal; a revision of an existing one is recorded only by its sponsor when the sponsor is a
/// person, and by an operator when an agent sponsors it (decision 01M50A8KHJT1MSSS84KZKHKRHB).
/// </summary>
/// <param name="By">The proposing principal; for a revision, who decides for the goal it supersedes.</param>
/// <param name="Body">The goal record's body: title, objective, completion, constraints, kind, sponsor, budget, plan and inputs, and optionally roster, roles and limits. limits.deadline and limits.consecutiveFailures are validated and recorded but not yet enforced.</param>
/// <param name="Supersedes">The goal this proposal revises, by the id of its current revision.</param>
public sealed record GoalProposeRequest(string By, JsonElement Body, string? Supersedes = null);

/// <summary>Activate, pause, resume or stop a goal.</summary>
/// <param name="By">The principal acting: the goal's sponsor, or an operator for a goal an agent sponsors.</param>
/// <param name="Note">Why, and what remains; a sentence naming the action when omitted.</param>
public sealed record GoalActionRequest(string By, string? Note = null);

/// <summary>Evaluate a goal: the runtime gives each record predicate its verdict, and the evaluator each evidence statement.</summary>
/// <param name="By">The evaluating principal, who authored none of the goal's steps.</param>
/// <param name="Summary">What was reviewed and how.</param>
/// <param name="Verdicts">The evaluator's verdicts: one for each evidence statement, and impossible for a record predicate the runtime does not find met, where the evaluator judges so.</param>
public sealed record GoalEvaluateRequest(string By, string Summary, IReadOnlyList<GoalVerdict>? Verdicts = null);

/// <summary>An evaluator's verdict on one completion condition.</summary>
/// <param name="Condition">The condition's position in the goal, from 1.</param>
/// <param name="Verdict">met, not-yet or impossible.</param>
/// <param name="Reason">Why.</param>
/// <param name="Read">The ids of the records the evaluator read; an evidence statement found met names at least one.</param>
public sealed record GoalVerdict(int Condition, string Verdict, string Reason, IReadOnlyList<string>? Read = null);

/// <summary>Accept a goal at its current revision, relying on its current evaluation.</summary>
/// <param name="By">The accepting principal: the sponsor when the sponsor is a person, or the principal it delegated an ordinary goal's acceptance to; an operator when an agent sponsors it.</param>
/// <param name="Statement">What is accepted and on what basis.</param>
public sealed record GoalAcceptRequest(string By, string Statement);

/// <summary>Revoke a device's admission.</summary>
/// <param name="Device">Device id.</param>
/// <param name="By">Revoking principal.</param>
/// <param name="Reason">Why.</param>
/// <param name="Recovery">Absolute path of the operator's recovery key, to sign with it when no admitted device of the operator is left; from record format 2.</param>
/// <param name="Passphrase">The recovery key's passphrase.</param>
public sealed record RevokeRequest(string Device, string By, string Reason, string? Recovery = null, string? Passphrase = null);

/// <summary>One release record.</summary>
/// <param name="Version">Version.</param>
/// <param name="Status">Status.</param>
/// <param name="Commit">Commit.</param>
/// <param name="Id">Record id.</param>
/// <param name="Hash">Record hash.</param>
/// <param name="Signing">Signing state.</param>
public sealed record ReleaseRow(string? Version, string? Status, string? Commit, string Id, string Hash, string? Signing);

/// <summary>
/// Submit a release as intent before effect: the operation record is written, then the build is dispatched through the provider;
/// nothing is published. The provider is the one the request names, else the one the product record declares; a request with
/// neither is refused, and a provider with no implementation on the runtime's machine is refused, never replaced by another.
/// </summary>
/// <param name="Product">Product record id the release belongs to.</param>
/// <param name="Version">The version to release, for example 0.4.0.</param>
/// <param name="Repository">The destination for the GitHub provider (owner/name); the same as the setting repository. Optional when the product declares it.</param>
/// <param name="By">Principal id recording the operation.</param>
/// <param name="Ref">The setting ref (branch or tag to build from); the provider's default when absent.</param>
/// <param name="Workflow">The setting workflow (the workflow file); the provider's default when absent.</param>
/// <param name="Reconciliation">How an earlier failed, canceled or uncertain attempt under the same identity was settled; required for a retry.</param>
/// <param name="Provider">The provider id, for example github-releases; the product's declared provider when absent.</param>
/// <param name="Settings">The provider's settings by name; they override the product's declared settings and the provider's defaults.</param>
public sealed record ReleaseSubmitRequest(string Product, string Version, string? Repository, string By, string? Ref = null, string? Workflow = null, string? Reconciliation = null, string? Provider = null, IReadOnlyDictionary<string, string>? Settings = null);

/// <summary>
/// Observe a release's run, draft and publication and record what settled; or cancel an intent that had no effect. The operation
/// is found by its provider and destination, or by the product and version, or (as in 0.3.0) by the destination alone when one
/// current operation matches; it is observed through the provider it records.
/// </summary>
/// <param name="Version">The version.</param>
/// <param name="Repository">The destination (for GitHub, owner/name).</param>
/// <param name="By">Principal id recording the observation.</param>
/// <param name="Qualified">Platforms whose qualification is recorded on the candidate.</param>
/// <param name="Limitations">Known limitations recorded on the candidate.</param>
/// <param name="Signing">Signing state recorded on the candidate.</param>
/// <param name="Cancel">A reason that settles an intent never dispatched, or an outcome reconciled as having had no effect.</param>
/// <param name="Product">The product, to find the operation by product and version.</param>
/// <param name="Provider">The provider, to find the operation by provider and destination.</param>
public sealed record ReleaseObserveRequest(string Version, string? Repository, string By, IReadOnlyList<string>? Qualified = null, IReadOnlyList<string>? Limitations = null, string Signing = "unsigned developer build", string? Cancel = null, string? Product = null, string? Provider = null);

/// <summary>A setting a provider declares.</summary>
/// <param name="Name">The setting's name.</param>
/// <param name="Summary">What it is.</param>
/// <param name="Required">Whether an operation is refused without it.</param>
/// <param name="Default">The provider's own default, or null.</param>
public sealed record ProviderSettingRow(string Name, string Summary, bool Required, string? Default);

/// <summary>A provider or adapter implementation available to the runtime on its machine, with what it declares.</summary>
/// <param name="Provider">The provider or adapter it serves: for example github-releases, or git.</param>
/// <param name="Kind">release, source, launcher (from 0.6.0, a coding-agent launcher) or workspace (from 0.6.0).</param>
/// <param name="Implementation">The implementation id, for example github-releases-gh.</param>
/// <param name="Contract">The versioned contract it implements.</param>
/// <param name="Status">candidate, implemented, qualified or available.</param>
/// <param name="Operations">The operations it supports.</param>
/// <param name="Limitations">What it does not do.</param>
/// <param name="Settings">The settings an operation through it takes.</param>
/// <param name="Destination">The setting that names the destination, or null.</param>
/// <param name="Locations">The resource location keys it serves.</param>
public sealed record ProviderRow(string Provider, string Kind, string Implementation, string Contract, string Status, IReadOnlyList<string> Operations, IReadOnlyList<string> Limitations, IReadOnlyList<ProviderSettingRow> Settings, string? Destination, IReadOnlyList<string> Locations);

/// <summary>
/// The outcome of a release operation: what was recorded and what the provider showed, never more. Ok is true when the request
/// advanced as asked or there is nothing to record yet; false when the attempt ended refused, failed or uncertain. A problem
/// means nothing could proceed (with findings when a record was rejected).
/// </summary>
/// <param name="Identity">The operation's identity (provider:repository:tag).</param>
/// <param name="Status">The status of the operation, or of the release once one is recorded, after this call.</param>
/// <param name="Message">What happened and the permitted next step, in words.</param>
/// <param name="Ok">Whether the request advanced as asked or there is nothing to record yet.</param>
/// <param name="Operation">The operation revision written by this call.</param>
/// <param name="Candidate">The candidate release record written by this call.</param>
/// <param name="Release">The published release revision written by this call.</param>
/// <param name="ReleaseId">The id of the release record already on record for this version.</param>
/// <param name="Run">The workflow run identified.</param>
/// <param name="Pending">What is still outstanding when nothing settled.</param>
/// <param name="Publish">The operator's manual publication command; the runtime never runs it.</param>
/// <param name="PublishedAt">When the release was observed published.</param>
/// <param name="Url">Where the release or run is.</param>
/// <param name="Problem">Why nothing could proceed.</param>
public sealed record ReleaseResponse(string Identity, string Status, string Message, bool Ok, RecordWritten? Operation = null, RecordWritten? Candidate = null, RecordWritten? Release = null, string? ReleaseId = null, string? Run = null, string? Pending = null, string? Publish = null, string? PublishedAt = null, string? Url = null, Problem? Problem = null);

/// <summary>
/// Start a coding agent's run on an assignment through a launcher (the worker contract, vericairn.worker/1): the runtime prepares a
/// workspace on a work branch of the repository, records the run's intent with its cap from the goal's budget, launches the harness
/// with the brief and the MCP server for the agent's principal, observes it and settles it. It accepts nothing.
/// </summary>
/// <param name="Assignment">The assignment, whose contributor is the agent.</param>
/// <param name="Launcher">The launcher, for example claude-code, codex or generic.</param>
/// <param name="Agent">The agent principal the harness acts for: the assignment's contributor.</param>
/// <param name="By">The principal recording the run: the operator, the goal's sponsor when the sponsor is a person, or a principal with the local-engineering scope.</param>
/// <param name="Repository">The repository resource whose clone, registered on the runtime's machine, the workspace is made from.</param>
/// <param name="Goal">The goal the run is charged to, when the assignment's work belongs to more than one active goal.</param>
/// <param name="Branch">The work branch; work/ and the work order's key when omitted.</param>
/// <param name="Base">What a new work branch starts from; the clone's HEAD when omitted.</param>
/// <param name="WallMinutes">The run's cap in wall minutes; what remains of the goal's budget when omitted, and never more.</param>
/// <param name="Settings">The launcher's settings by name, for example command.</param>
public sealed record RunStartRequest(string Assignment, string Launcher, string Agent, string By, string Repository, string? Goal = null, string? Branch = null, string? Base = null, long? WallMinutes = null, IReadOnlyDictionary<string, string>? Settings = null);

/// <summary>Ask a run to stop.</summary>
/// <param name="By">The principal asking.</param>
/// <param name="Reason">Why.</param>
/// <param name="GraceSeconds">How long the harness has to stop once the stop is transmitted before its process tree is killed; the worker's default when omitted.</param>
public sealed record RunStopRequest(string By, string? Reason = null, int? GraceSeconds = null);

/// <summary>
/// A stop's facts, each one present only once observed: requested, transmitted to the harness, acknowledged by it, escalated, and
/// observed as contained. A stop that is not observed is reported as requested.
/// </summary>
/// <param name="RequestedAt">When it was asked.</param>
/// <param name="RequestedBy">The principal that asked, or worker for a cap the run reached.</param>
/// <param name="Reason">Why, when given.</param>
/// <param name="TransmittedAt">When the worker delivered it to the harness.</param>
/// <param name="Transmission">How it was delivered.</param>
/// <param name="AcknowledgedAt">When the harness acknowledged it.</param>
/// <param name="Acknowledgment">How it acknowledged it.</param>
/// <param name="EscalatedAt">When the worker escalated it, the harness not having stopped within the grace period.</param>
/// <param name="Escalation">What the escalation did.</param>
/// <param name="ContainedAt">When the worker observed the whole launch boundary exited.</param>
public sealed record RunStop(string RequestedAt, string RequestedBy, string? Reason = null, string? TransmittedAt = null, string? Transmission = null, string? AcknowledgedAt = null, string? Acknowledgment = null, string? EscalatedAt = null, string? Escalation = null, string? ContainedAt = null)
{
    /// <summary>The furthest fact observed: contained, escalated, acknowledged, transmitted or requested.</summary>
    [JsonIgnore]
    public string State => ContainedAt is not null ? "contained" : EscalatedAt is not null ? "escalated" : AcknowledgedAt is not null ? "acknowledged" : TransmittedAt is not null ? "transmitted" : "requested";
}

/// <summary>What starting a run observed and recorded. An uncertain launch retains its reservation even without established supervision.</summary>
/// <param name="Run">The run's id: its intent's record id, once recorded.</param>
/// <param name="Status">The run's operation status after this call: dispatched when launched.</param>
/// <param name="Message">What happened and what follows, in words.</param>
/// <param name="Ok">Whether launch was observed and supervision established.</param>
/// <param name="Operation">The latest operation revision written.</param>
/// <param name="Cap">The run's cap, as its intent records it.</param>
/// <param name="Workspace">The workspace, a repository and branch.</param>
/// <param name="Problem">Why start was refused or failed, when reported.</param>
public sealed record RunResponse(string Run, string Status, string Message, bool Ok, RecordWritten? Operation = null, JsonElement? Cap = null, string? Workspace = null, Problem? Problem = null);

/// <summary>What asking a run to stop observed: the stop's facts, the furthest one its state.</summary>
/// <param name="Run">The run.</param>
/// <param name="State">requested, transmitted, acknowledged, escalated or contained.</param>
/// <param name="Message">What was observed, in words.</param>
/// <param name="Ok">Whether the request stands; a stop that was not observed stands as requested.</param>
/// <param name="Stop">The stop's facts.</param>
/// <param name="Problem">Why no stop was asked.</param>
public sealed record RunStopResponse(string Run, string State, string Message, bool Ok, RunStop? Stop = null, Problem? Problem = null);

/// <summary>Event-stream liveness observed by the worker, never asserted by the harness.</summary>
/// <param name="StallSeconds">Silence limit; attention at one limit and stop at two.</param>
/// <param name="LastEventAt">When stdout last produced an event, or launch before any event.</param>
/// <param name="AttentionAt">When the worker first observed silence reach the limit.</param>
/// <param name="Attention">The attention ask, once recorded.</param>
/// <param name="StoppedAt">When the worker stopped the run for twice-limit silence.</param>
public sealed record RunLiveness(int StallSeconds, string LastEventAt, string? AttentionAt = null, string? Attention = null, string? StoppedAt = null);

/// <summary>The worker's comparison of workspace files before and after the run, against the assignment's permitted paths.</summary>
/// <param name="Changed">Files added, removed or changed, including committed, untracked and ignored files.</param>
/// <param name="OutsideCount">How many changed paths fall outside the permitted paths.</param>
/// <param name="Outside">Repository-relative outside paths, at most one thousand; unsafe names are represented by their hash.</param>
/// <param name="Problem">Why the audit could not establish the whole scope, when unavailable or incomplete.</param>
public sealed record RunScope(int Changed, int OutsideCount, IReadOnlyList<string> Outside, string? Problem = null);

/// <summary>A bounded worker observation of a run; raw commands, paths and credential values are never included.</summary>
/// <param name="Code">The kind of observation.</param>
/// <param name="Severity">warning needs attention; critical asks the existing stop path to contain the run.</param>
/// <param name="At">When the worker first observed this signal.</param>
/// <param name="Summary">A portable description of the observation.</param>
/// <param name="Fingerprint">A hash of an action's identity and input, if relevant.</param>
/// <param name="Count">How many occurrences triggered the signal.</param>
/// <param name="Attention">The ask recording the attention item, once written.</param>
public sealed record RunSignal(string Code, string Severity, string At, string Summary, string? Fingerprint = null, int Count = 1, string? Attention = null);

/// <summary>One agent run as the records and this machine show it.</summary>
/// <param name="Run">The run's id: its intent's record id.</param>
/// <param name="Identity">Its operation's identity.</param>
/// <param name="Launcher">The launcher it ran through.</param>
/// <param name="Agent">The agent principal.</param>
/// <param name="Assignment">The assignment it carried.</param>
/// <param name="Goal">The goal it is charged to.</param>
/// <param name="Workspace">The workspace, a repository and branch.</param>
/// <param name="Status">Its operation's status.</param>
/// <param name="Cap">Its cap, recorded before it started.</param>
/// <param name="Usage">Its observed usage once settled, with the units not reported.</param>
/// <param name="Charged">What it is charged, by unit: its usage, its cap for what was not reported, and its whole cap while in flight.</param>
/// <param name="Stop">A stop's facts, recorded or observed here.</param>
/// <param name="Heartbeat">When the worker on this machine last observed it running.</param>
/// <param name="Observed">Whether a worker on this machine observes it now.</param>
/// <param name="Scope">The settlement's workspace scope audit.</param>
/// <param name="Signals">Worker observations from the harness event stream, with no raw arguments.</param>
/// <param name="Liveness">The worker's event-stream observation and any stall attention.</param>
/// <param name="Generic">Whether it ran through a generic launcher, which is not complete agent support.</param>
public sealed record RunRow(string Run, string Identity, string Launcher, string? Agent, string? Assignment, string? Goal, string? Workspace, string Status, JsonElement Cap, JsonElement? Usage, JsonElement Charged, RunStop? Stop, string? Heartbeat, bool Observed, bool Generic, RunLiveness? Liveness = null, RunScope? Scope = null, IReadOnlyList<RunSignal>? Signals = null);

/// <summary>One unit of a goal's ledger.</summary>
/// <param name="Unit">wallMinutes, tokens or currency.</param>
/// <param name="Budget">What the goal's budget grants.</param>
/// <param name="Spent">What its runs are charged, runs in flight at their caps.</param>
/// <param name="Remaining">What remains.</param>
/// <param name="Currency">The currency, for currency.</param>
public sealed record LedgerUnit(string Unit, long Budget, long Spent, long Remaining, string? Currency = null);

/// <summary>A goal's agent-run ledger.</summary>
/// <param name="Goal">The goal's current revision.</param>
/// <param name="Title">Its title.</param>
/// <param name="Status">Its status.</param>
/// <param name="Units">Each charged unit its budget states.</param>
/// <param name="InFlight">Its runs in flight.</param>
/// <param name="Concurrency">How many may be in flight at once.</param>
/// <param name="AttemptsPerStep">How many runs each step may have.</param>
/// <param name="Fenced">Why a new run of it would not start now; empty when one would.</param>
public sealed record LedgerRow(string Goal, string? Title, string Status, IReadOnlyList<LedgerUnit> Units, int InFlight, int Concurrency, int AttemptsPerStep, IReadOnlyList<string> Fenced);

/// <summary>The agent runs and the ledgers of the goals they are charged to.</summary>
/// <param name="Runs">Every run, oldest first.</param>
/// <param name="Goals">The ledger of every goal with a run or active now.</param>
public sealed record RunsResponse(IReadOnlyList<RunRow> Runs, IReadOnlyList<LedgerRow> Goals);

/// <summary>Plan or apply an upgrade.</summary>
/// <param name="Version">Target version.</param>
/// <param name="By">Applying principal (unless check).</param>
/// <param name="Check">Plan only.</param>
public sealed record UpgradeRequest(string Version, string? By, bool Check);

/// <summary>An upgrade plan, and whether it was applied.</summary>
/// <param name="FromPin">Pin before.</param>
/// <param name="ToPin">Pin after.</param>
/// <param name="FromFormat">Format before.</param>
/// <param name="ToFormat">Format after.</param>
/// <param name="Migrations">Migrations.</param>
/// <param name="Release">Release record id.</param>
/// <param name="Artifacts">Artifact hash per platform.</param>
/// <param name="Applied">Whether applied.</param>
/// <param name="Migration">The migration record, when applied.</param>
/// <param name="Anchored">The admissions recorded without a signature that the move to record format 2 anchors, by id; the migration that lists them is signed by the operator or a principal with the 'administer' scope.</param>
public sealed record UpgradeResponse(string FromPin, string ToPin, long FromFormat, long ToFormat, IReadOnlyList<string> Migrations, string Release, IReadOnlyDictionary<string, string> Artifacts, bool Applied, RecordWritten? Migration, IReadOnlyList<string>? Anchored = null);

/// <summary>A reconcile task.</summary>
/// <param name="Code">Code.</param>
/// <param name="Location">Location.</param>
/// <param name="Message">Message.</param>
public sealed record ReconcileTask(string Code, string Location, string Message);

/// <summary>One object held.</summary>
/// <param name="Hash">Hash.</param>
public sealed record ObjectRow(string Hash);

/// <summary>The outcome of storing an object.</summary>
/// <param name="Hash">Hash.</param>
/// <param name="Bytes">Size.</param>
/// <param name="Store">Store kind.</param>
public sealed record ObjectStored(string Hash, long Bytes, string Store);

/// <summary>Write a verified export of the estate to a path on the runtime's machine.</summary>
/// <param name="Output">Path of the export file to create; never overwritten.</param>
/// <param name="By">Principal recorded as the exporter.</param>
public sealed record ExportRequest(string Output, string? By = null);

/// <summary>What an export carried.</summary>
/// <param name="Id">Export id.</param>
/// <param name="Estate">Estate id.</param>
/// <param name="CreatedAt">When.</param>
/// <param name="Profile">Storage profile.</param>
/// <param name="Records">Record count.</param>
/// <param name="Objects">Object count.</param>
/// <param name="Documents">Document count.</param>
/// <param name="InventoryHash">Hash of the inventory.</param>
/// <param name="Output">Where the file was written on the runtime's machine.</param>
/// <param name="HadErrors">Whether the estate had errors when exported.</param>
public sealed record ExportResponse(string Id, string Estate, string CreatedAt, string Profile, int Records, int Objects, int Documents, string InventoryHash, string Output, bool HadErrors);

/// <summary>Current decisions: the human list and the machine form.</summary>
/// <param name="Text">Rendered list.</param>
/// <param name="Json">Machine form.</param>
public sealed record DecisionsResponse(string Text, JsonElement Json);

/// <summary>One decision rendered cleanly, with its body.</summary>
/// <param name="Text">Rendered decision.</param>
/// <param name="Body">The record body.</param>
public sealed record DecisionResponse(string Text, JsonElement Body);

/// <summary>JSON conventions shared by the daemon and the SDK.</summary>
public static class ProtocolJson
{
    /// <summary>Camel-case names, no indentation, strings for enums.</summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = false };
}
