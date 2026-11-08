// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Records.Templates;

/// <summary>
/// The software pack's two flows (the estate's design/release-procedure.md § 7, as design/flow.md revises it), shipped as built-in
/// template versions: <c>vericairn.software/release</c>, a software product's release from its placed work to its closed carrier, with
/// its withdrawal branch, and <c>vericairn.software/upgrade</c>, an estate's upgrade to a published Vericairn release. Each step names its
/// owner role, its kind, its reserved act where it has one, its preconditions and done predicates over the records, its recovery and
/// its traps: the checks of 0.8.1 to 0.8.4's lessons, read from the records where a record says it and otherwise named as the check the
/// step's action runs before it acts. Installing a flow grants nothing; an estate extends one in its own <c>docs/templates</c>.
/// </summary>
public static class SoftwareFlows
{
    /// <summary>The release flow's identity.</summary>
    public const string Release = "vericairn.software/release";

    /// <summary>The upgrade flow's identity.</summary>
    public const string Upgrade = "vericairn.software/upgrade";

    /// <summary>The version both ship at.</summary>
    public const string Version = "1.0.0";

    private const string UpgradeHash = "{upgrade-hash}";

    /// <summary>The two flows: the upgrade flow first, since the release flow's upgrade-estate step pins it by hash.</summary>
    public static IReadOnlyList<TemplateDefinition> Definitions { get; } = Read();

    /// <summary>The release flow as shipped.</summary>
    public static TemplateDefinition ReleaseFlow => Definitions.Single(d => d.Id == Release);

    /// <summary>The upgrade flow as shipped.</summary>
    public static TemplateDefinition UpgradeFlow => Definitions.Single(d => d.Id == Upgrade);

    private static TemplateDefinition[] Read()
    {
        TemplateDefinition upgrade = Parse(UpgradeContent);
        TemplateDefinition release = Parse(ReleaseContent.Replace(UpgradeHash, upgrade.Hash, StringComparison.Ordinal));
        return [upgrade, release];
    }

    private static TemplateDefinition Parse(string text)
    {
        using JsonDocument json = JsonDocument.Parse(text);
        return TemplateDefinition.Read(json.RootElement);
    }

    private const string UpgradeContent = """
        {
          "format": "vericairn.template/1",
          "kind": "flow",
          "id": "vericairn.software/upgrade",
          "version": "1.0.0",
          "domain": "software",
          "purpose": "Upgrade an estate to a published Vericairn release: check it, apply it, leave no runtime of an older release serving the estate, verify the estate with the pinned release, have the root operator adopt history checkpoints once, and clear the living documents the upgrade made due. Installing or running it grants nothing.",
          "subject": {
            "summary": "an estate's upgrade to one published Vericairn release",
            "fields": [
              { "name": "version", "type": "text" }
            ]
          },
          "roles": [
            { "id": "upgrader", "holder": "either", "brief": "Upgrade the estate to the release with that release's own command line, replace a runtime of an older release that still serves the estate on this machine, verify the estate with the pinned release and clear what the upgrade made due. Restart workers only after their active work has settled." },
            { "id": "root-operator", "holder": "person", "brief": "Sign the estate's adoption checkpoint once, after reading what it would witness and anchor: the root operator's act, never an agent's." }
          ],
          "never": [
            "Never upgrade with a command line of another release than the one the estate is upgraded to.",
            "Never leave a runtime of an older release serving the estate after its upgrade.",
            "Never adopt history checkpoints for the root operator: adoption is the root operator's signature."
          ],
          "steps": [
            {
              "key": "check",
              "title": "Upgrade checked",
              "kind": "operation",
              "role": "upgrader",
              "required": true,
              "action": "With the release's own command line, run vericairn estate upgrade --to <version> --check: it verifies the release's archive against its checksums and lists the migrations, changing nothing. A check records nothing, so the step counts once the upgrade it checked is recorded.",
              "done": [
                { "type": "observation", "fields": { "kind": "vericairn-release", "facts.version": "{subject.version}" } }
              ],
              "recovery": "Settle every refusal the check lists, such as a checksum that does not match or a migration this release cannot apply, and check again; nothing was changed."
            },
            {
              "key": "upgrade",
              "title": "Estate upgraded",
              "kind": "operation",
              "role": "upgrader",
              "required": true,
              "after": [],
              "action": "With the release's own command line, run vericairn estate upgrade --to <version> --by <you>: it writes the pin's observation of the release and any migration record, and in an estate that adopted history checkpoints a checkpoint with trigger upgrade. It does not wait on the check's record, since a check records none.",
              "done": [
                { "type": "observation", "fields": { "kind": "vericairn-release", "facts.version": "{subject.version}" } }
              ],
              "recovery": "An upgrade that stops before its records are stored changes nothing; run it again with the same release. One whose records are stored is not repeated: rerunning it only runs its runtime step."
            },
            {
              "key": "runtime",
              "title": "No runtime of an older release serves the estate",
              "kind": "operation",
              "role": "upgrader",
              "required": true,
              "action": "The upgrade's runtime step (OPS-05) stops a daemon of an older release that serves the estate on this machine and starts the pinned release's in its place; when it warns, stop and start them as the warning says. Then record what serves the estate here: vericairn estate verify --by <you>.",
              "done": [
                { "type": "verification", "fields": { "method": "vericairn.software/serving-runtime", "environment.release": "{subject.version}", "outcome": "passed" } }
              ],
              "traps": [
                {
                  "name": "older-runtime",
                  "summary": "No runtime of an older release keeps serving the estate on this machine: after 0.8.2's upgrade a 0.8.0 daemon answered new operations with bare 404s, and 0.8.4's submission check tripped on a stale 0.8.3 daemon.",
                  "holds": [
                    { "type": "verification", "fields": { "method": "vericairn.software/serving-runtime", "environment.release": "{subject.version}", "outcome": "failed" }, "max": 0 }
                  ]
                }
              ],
              "recovery": "Stop the older daemon as the user that started it (vericairn runtime stop), start the pinned release's (./bootstrap runtime start in the estate's directory), and verify again; the newer verification supersedes the failed one."
            },
            {
              "key": "verify",
              "title": "Estate verified with the pinned release",
              "kind": "operation",
              "role": "upgrader",
              "required": true,
              "action": "vericairn estate verify --by <you> runs the estate check and derives its status with the pinned release's runtime and records the outcome; where the estate runs a cold start in its repository's checks, that run passes too.",
              "done": [
                { "type": "verification", "fields": { "method": "vericairn.software/estate-check", "environment.release": "{subject.version}", "outcome": "passed" } }
              ],
              "traps": [
                {
                  "name": "estate-check",
                  "summary": "The estate's records are valid under the pinned release and its status derives: no error of the estate check stands after the upgrade.",
                  "holds": [
                    { "type": "verification", "fields": { "method": "vericairn.software/estate-check", "environment.release": "{subject.version}", "outcome": "failed" }, "max": 0 }
                  ]
                }
              ],
              "recovery": "Settle each error the verification lists, under its record type's rules, and verify again; nothing is rewritten."
            },
            {
              "key": "adopt",
              "title": "History checkpoints adopted",
              "kind": "operation",
              "role": "root-operator",
              "required": true,
              "action": "Once, in an estate with no adoption checkpoint: vericairn estate checkpoint --adopt --check shows what adoption would witness and anchor, and the root operator signs it with vericairn estate checkpoint --adopt --by <root-operator> (OPS-07). An estate born with one, as estate init writes it, has this step done.",
              "done": [
                { "type": "history-checkpoint", "fields": { "trigger": "adoption" } }
              ],
              "recovery": "Adoption is signed once, with the root operator's device; read what the check lists, settle any record it would not anchor, and adopt again. Nothing is adopted twice."
            },
            {
              "key": "docs",
              "title": "Living documents the upgrade made due cleared",
              "kind": "operation",
              "role": "upgrader",
              "required": true,
              "after": ["verify"],
              "action": "Clear every living document the upgrade made due: vericairn docs living lists them; review each that still holds, and revise and review each that does not.",
              "done": [
                { "type": "living-document", "state": "due", "max": 0 }
              ],
              "recovery": "A document that needs change stays due until it is revised and reviewed."
            }
          ]
        }
        """;

    private const string ReleaseContent = """
        {
          "format": "vericairn.template/1",
          "kind": "flow",
          "id": "vericairn.software/release",
          "version": "1.0.0",
          "domain": "software",
          "purpose": "Release a software product from its placed work to its closed carrier: the guides revised and the documentation set frozen before submission, the candidate submitted, observed and reviewed, the operator's acceptance and publication, the documentation pass after it and the carrier closed, with every trap of earlier releases checked before the step it guards. Installing or running it grants nothing; acceptance, publication and a draft's deletion stay the operator's.",
          "subject": {
            "summary": "a release of a software product: its carrier by its original identity, the product, the version, and the repository its release is published from, owner/name",
            "fields": [
              { "name": "carrier", "type": "record", "recordType": "work-order" },
              { "name": "product", "type": "record", "recordType": "product" },
              { "name": "version", "type": "text" },
              { "name": "repository", "type": "text" }
            ]
          },
          "roles": [
            { "id": "release-manager", "holder": "either", "brief": "Carry the release from its placed work to its closed carrier: revise the version-tracking guides, prepare and freeze the documentation set, merge it, check and observe the submission, run the documentation pass after publication, and offer the release's results for closing. Accept, publish, delete a draft and post nothing yourself: those are the operator's." },
            { "id": "submitter", "holder": "either", "brief": "Submit the candidate from the release commit with the release commit's own command line: the operator, until the operator issues a bounded release-manager grant." },
            { "id": "reviewer", "holder": "either", "brief": "Review the exact candidate from its release review brief, independently of every author of its work, and record the review at the candidate's exact hash." },
            { "id": "decider", "holder": "person", "brief": "Accept the exact candidate the review passed, with that review as the acceptance's basis, and close the carrier once nothing placed in it is unaccepted and nothing it made due is open." },
            { "id": "publisher", "holder": "person", "brief": "Publish the accepted candidate with the provider's command shown when the step is next, and record the observed publication; delete a withdrawn candidate's draft." }
          ],
          "options": [
            { "id": "review", "values": ["none", "self", "independent"], "default": "none" }
          ],
          "never": [
            "Never accept, publish, delete a draft or post an announcement for the operator: each is a reserved act whose command appears only when its step is next.",
            "Never show a later reserved step's command in a status message, a brief or a list of what comes next.",
            "Never describe a fix under a disclosure hold in a guide or anywhere public before the documentation set carries its approved security section."
          ],
          "steps": [
            {
              "key": "placed-work",
              "title": "Placed work accepted",
              "kind": "operation",
              "role": "release-manager",
              "required": true,
              "action": "List every work order placed in the carrier (vericairn status, vericairn work observe <key>): each is accepted, or moved to another carrier with a decision (vericairn work move-plan). List the open pull requests of the carrier's work and leave none unmerged.",
              "done": [
                { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "accepted" },
                { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "planned", "max": 0 },
                { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "active", "max": 0 },
                { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "paused", "max": 0 },
                { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "submitted", "max": 0 },
                { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "accepted (earlier revision)", "max": 0 }
              ],
              "recovery": "Accept each finished work order after its review, or move unfinished work to a later carrier with a decision; the step counts once no work order placed in the carrier is unaccepted."
            },
            {
              "key": "docs-before-freeze",
              "title": "Version-tracking guides revised for the new version",
              "kind": "operation",
              "role": "release-manager",
              "required": true,
              "action": "Revise the version-tracking guides for the new version, keeping what earlier releases changed in history sections and labels, commit them, and check and record them from the product's clean clone: vericairn docs pass <version> --before --documents docs/releases/documents.json --record --by <you> --product <product>. A recorded check names the earlier releases it read, which the estate's records give; one of other versions does not count.",
              "done": [
                { "type": "verification", "subject": "{subject.product}", "fields": { "method": "vericairn.software/prior-version-tokens", "environment.release": "{subject.version}", "environment.previous": "{previous}", "outcome": "passed" } },
                { "type": "verification", "subject": "{subject.product}", "fields": { "method": "vericairn.software/preparation-wording", "environment.release": "{subject.version}", "environment.previous": "{previous}", "outcome": "passed" } }
              ],
              "outputs": [
                { "name": "check", "kind": "verification", "required": true }
              ],
              "traps": [
                {
                  "name": "prior-version-tokens",
                  "summary": "No guide names an earlier release as the current one: no token of the two releases before this one, or of a candidate withdrawn since, outside history sections and labels. 0.8.4 froze guides that still named 0.8.3, and its notes-window sentence still named 0.8.2.",
                  "holds": [
                    { "type": "verification", "subject": "{subject.product}", "fields": { "method": "vericairn.software/prior-version-tokens", "environment.release": "{subject.version}", "outcome": "failed" }, "max": 0 }
                  ]
                },
                {
                  "name": "preparation-wording",
                  "summary": "No guide carries preparation wording, such as notes that are a placeholder or what the builder must still do: 0.7.0's frozen set called its notes a placeholder.",
                  "holds": [
                    { "type": "verification", "subject": "{subject.product}", "fields": { "method": "vericairn.software/preparation-wording", "environment.release": "{subject.version}", "outcome": "failed" }, "max": 0 }
                  ]
                }
              ],
              "recovery": "Revise each location the check lists, commit, and check again; a passing check supersedes the failed one. Nothing is frozen until it passes."
            },
            {
              "key": "docs-set",
              "title": "Documentation set assembled, frozen and indexed",
              "kind": "work",
              "role": "release-manager",
              "required": true,
              "action": "Set VersionPrefix to the new version with the documents the set declares, then prepare the set with the build tooling (vericairn-docs prepare: assemble, the notes from the records, the operator's approved security section byte for byte, compare, freeze, index), and hand on its manifest and its notes by hash. A newer check of the guides, such as one for a corrective version, makes the set stale, and it is prepared again.",
              "inputs": [
                { "name": "guides", "from": "step", "step": "docs-before-freeze", "output": "check" }
              ],
              "outputs": [
                { "name": "manifest", "kind": "file", "mediaType": "application/json", "required": true },
                { "name": "notes", "kind": "file", "mediaType": "text/markdown", "required": true }
              ],
              "traps": [
                {
                  "name": "guides-revised",
                  "summary": "The set freezes only guides revised for this version: the documentation pass before freezing passed. 0.8.4's first set froze its guides before that pass and had to be prepared again.",
                  "holds": [
                    { "type": "verification", "subject": "{subject.product}", "fields": { "method": "vericairn.software/prior-version-tokens", "environment.release": "{subject.version}", "environment.previous": "{previous}", "outcome": "passed" } },
                    { "type": "verification", "subject": "{subject.product}", "fields": { "method": "vericairn.software/preparation-wording", "environment.release": "{subject.version}", "environment.previous": "{previous}", "outcome": "passed" } }
                  ]
                },
                {
                  "name": "notes-window",
                  "summary": "The notes are rendered once the carrier's work has closed, so their window ends after every closing of the release: 0.8.3's notes were rendered before the decisions of its closing had all landed.",
                  "holds": [
                    { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "planned", "max": 0 },
                    { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "active", "max": 0 },
                    { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "paused", "max": 0 },
                    { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "submitted", "max": 0 }
                  ]
                },
                {
                  "name": "security-section",
                  "summary": "A security section for fixes under a disclosure hold is approved by the operator through an ask about the carrier, answered by the operator's decision before the set is frozen: no ask about the carrier still waits, open or notified. Whether a release needs a section, what the deciding decision says and why an ask was withdrawn, the records do not tell; the release manager asks whenever a fix is under a hold.",
                  "holds": [
                    { "type": "ask", "subject": "{subject.carrier}", "state": "open", "max": 0 },
                    { "type": "ask", "subject": "{subject.carrier}", "state": "notified", "max": 0 }
                  ]
                }
              ],
              "recovery": "A set is corrected before it is frozen; once frozen and indexed, a correction goes into the next set. Each preparation command changes nothing when run again, so prepare again from the same inputs."
            },
            {
              "key": "release-commit",
              "title": "Set merged: the release commit",
              "kind": "work",
              "role": "release-manager",
              "required": true,
              "action": "Merge the set's pull request once its three-platform run passes; its merge commit is the release commit. Hand on the frozen index at that commit by hash, naming the commit.",
              "inputs": [
                { "name": "manifest", "from": "step", "step": "docs-set", "output": "manifest" }
              ],
              "outputs": [
                { "name": "frozen-index", "kind": "file", "mediaType": "application/json", "required": true }
              ],
              "recovery": "A failing run is fixed on the set's branch and run again; a merged commit is never rewritten."
            },
            {
              "key": "reviews-at-commit",
              "title": "Every declared document cleared at the release commit",
              "kind": "operation",
              "role": "release-manager",
              "required": true,
              "action": "Review each living document due at the release commit (vericairn docs living, vericairn docs review), including those of other repositories, such as a profile, that changed since their review. The step counts once the submission's intent is recorded, which release submit records only while no declared document is held.",
              "done": [
                { "type": "operation", "fields": { "action": "dispatch-release-workflow", "target.version": "{subject.version}" } }
              ],
              "traps": [
                {
                  "name": "documents-cleared",
                  "summary": "No living document of the product is due: each declared document is cleared by a review of its content at the release commit. 0.8.2 and 0.8.3 found documents changed after their clearing reviews only at the submission check.",
                  "holds": [
                    { "type": "living-document", "product": "{subject.product}", "state": "due", "max": 0 }
                  ]
                }
              ],
              "recovery": "Review each document the trap names at the release commit, then check again."
            },
            {
              "key": "gate",
              "title": "Submission check passes at the release commit",
              "kind": "operation",
              "role": "release-manager",
              "required": true,
              "action": "Build the command line from the release commit and, against an estate clone that no runtime of another build serves, run vericairn release submit --check with it: every objection with its next action, nothing recorded. In an estate that adopted history checkpoints, the runtime writes one with trigger release, signed by the device then writing, before the check (OPS-07). The step counts once the submission's intent is recorded.",
              "done": [
                { "type": "operation", "fields": { "action": "dispatch-release-workflow", "target.version": "{subject.version}" } }
              ],
              "traps": [
                {
                  "name": "client-build",
                  "summary": "The client is the release commit's build and no runtime of another build serves the estate: 0.8.4's check refused while a stale 0.8.3 daemon served the estate clone.",
                  "check": "release.client-build"
                },
                {
                  "name": "default-head",
                  "summary": "Every registered clone the check reads has its default head: 0.8.3's first check refused two clones without a symbolic origin/HEAD.",
                  "check": "release.default-head"
                },
                {
                  "name": "document-hold",
                  "summary": "No declared document is held by a change after its clearing review, in this repository or another, such as the profile's README in 0.8.3.",
                  "check": "document.release-hold"
                },
                {
                  "name": "withdrawn-draft",
                  "summary": "No withdrawn candidate's draft is left at the provider: every draft-deletion intent of the repository is settled, so no withdrawn build can be published by mistake. The 0.6.0 and 0.7.0 drafts lingered until the operator asked, and 0.8.4's abandoned draft waited on the operator before its corrective submission.",
                  "holds": [
                    { "type": "operation", "fields": { "action": "delete-release-draft", "target.repository": "{subject.repository}", "status": "not-started" }, "max": 0 },
                    { "type": "operation", "fields": { "action": "delete-release-draft", "target.repository": "{subject.repository}", "status": "dispatched" }, "max": 0 },
                    { "type": "operation", "fields": { "action": "delete-release-draft", "target.repository": "{subject.repository}", "status": "uncertain" }, "max": 0 }
                  ]
                }
              ],
              "recovery": "Settle each objection the check names with its next action and check again; nothing was recorded, retained or dispatched."
            },
            {
              "key": "submit",
              "title": "Candidate submitted",
              "kind": "operation",
              "role": "submitter",
              "required": true,
              "action": "From the release commit, with its command line, run vericairn release submit: it checks again, records the intent and dispatches the provider's release workflow, which builds a draft.",
              "done": [
                { "type": "operation", "fields": { "action": "dispatch-release-workflow", "target.version": "{subject.version}" } },
                { "type": "operation", "fields": { "action": "dispatch-release-workflow", "target.version": "{subject.version}", "status": "not-started" }, "max": 0 },
                { "type": "operation", "fields": { "action": "dispatch-release-workflow", "target.version": "{subject.version}", "status": "uncertain" }, "max": 0 },
                { "type": "operation", "fields": { "action": "dispatch-release-workflow", "target.version": "{subject.version}", "status": "known-failure" }, "max": 0 },
                { "type": "operation", "fields": { "action": "dispatch-release-workflow", "target.version": "{subject.version}", "status": "canceled" }, "max": 0 }
              ],
              "traps": [
                {
                  "name": "uncertain-submission",
                  "summary": "An uncertain submission stops the release until a reconciliation reads the release record: release observe settles it, and nothing is retried blindly.",
                  "holds": [
                    { "type": "operation", "fields": { "action": "dispatch-release-workflow", "target.version": "{subject.version}", "status": "uncertain" }, "max": 0 }
                  ]
                }
              ],
              "recovery": "After an uncertain dispatch, observe first (vericairn release observe) and retry only after reconciling; after a known failure or a cancellation, submit again with --reconciliation."
            },
            {
              "key": "candidate",
              "title": "Candidate observed",
              "kind": "operation",
              "role": "release-manager",
              "required": true,
              "action": "Once the release workflow's run settles, observe it: vericairn release observe <version> --by <you> records the candidate with every distributable by hash.",
              "done": [
                { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}" } }
              ],
              "outputs": [
                { "name": "candidate", "kind": "record", "recordType": "release", "required": true }
              ],
              "recovery": "A run still in progress stays pending; observe again once it settles. A failed run is the submission's known failure, submitted again with its reconciliation."
            },
            {
              "key": "review",
              "title": "Candidate reviewed",
              "kind": "operation",
              "role": "reviewer",
              "required": true,
              "action": "Review the exact candidate from its release review brief (vericairn release review-brief), under the operator's release-review decision, independently of every author of its work, and record the review at the candidate's exact hash.",
              "inputs": [
                { "name": "candidate", "from": "step", "step": "candidate", "output": "candidate" }
              ],
              "done": [
                { "type": "review", "subject": "{input.candidate.id}", "fields": { "outcome": "pass" } }
              ],
              "traps": [
                {
                  "name": "not-withdrawn",
                  "summary": "The candidate is not withdrawn: a withdrawn candidate is never reviewed, accepted or published, and the release continues in the withdraw branch.",
                  "holds": [
                    { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}", "status": "withdrawn" }, "max": 0 }
                  ]
                }
              ],
              "recovery": "Findings or a failure go back to the release manager: a corrective candidate is a withdrawal and a new version, in the withdraw branch."
            },
            {
              "key": "accept",
              "title": "Candidate accepted",
              "kind": "operation",
              "role": "decider",
              "required": true,
              "reserved": "accept-release",
              "action": "vericairn records new acceptance --by <operator-id> --subject release:{input.candidate.id}:{input.candidate.hash} --body accept-release.json",
              "inputs": [
                { "name": "candidate", "from": "step", "step": "candidate", "output": "candidate" }
              ],
              "done": [
                { "type": "acceptance", "subject": "{input.candidate.id}" }
              ],
              "traps": [
                {
                  "name": "reviewed",
                  "summary": "The acceptance's basis is a passing review of this exact candidate.",
                  "holds": [
                    { "type": "review", "subject": "{input.candidate.id}", "fields": { "outcome": "pass" } }
                  ]
                },
                {
                  "name": "not-withdrawn",
                  "summary": "The candidate is not withdrawn.",
                  "holds": [
                    { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}", "status": "withdrawn" }, "max": 0 }
                  ]
                }
              ],
              "recovery": "The operator accepts only the exact candidate the review passed, at its exact hash; the body states the acceptance and names that review as its basis."
            },
            {
              "key": "publish",
              "title": "Candidate published",
              "kind": "operation",
              "role": "publisher",
              "required": true,
              "reserved": "publish",
              "action": "gh release edit v{subject.version} -R {subject.repository} --draft=false --latest && vericairn release observe {subject.version} --by <operator-id>",
              "inputs": [
                { "name": "candidate", "from": "step", "step": "candidate", "output": "candidate" }
              ],
              "done": [
                { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}", "status": "published" } }
              ],
              "traps": [
                {
                  "name": "accepted",
                  "summary": "The operator's acceptance names this exact candidate, so the publication command is computed only after it: 0.8.3 was published while its review still ran, from a command shown early.",
                  "holds": [
                    { "type": "acceptance", "subject": "{input.candidate.id}" }
                  ]
                },
                {
                  "name": "stable-channel",
                  "summary": "The command is the GitHub Releases provider's for the stable channel: a candidate of another channel is published with the command its release observe shows, never this one.",
                  "holds": [
                    { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}", "channel": "prerelease" }, "max": 0 },
                    { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}", "channel": "nightly" }, "max": 0 },
                    { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}", "channel": "lts" }, "max": 0 }
                  ]
                },
                {
                  "name": "not-withdrawn",
                  "summary": "The candidate is not withdrawn.",
                  "holds": [
                    { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}", "status": "withdrawn" }, "max": 0 }
                  ]
                }
              ],
              "recovery": "Publish exactly the accepted draft. If the observation still finds a draft, publish and observe again; the runtime never publishes and never undoes a publication."
            },
            {
              "key": "observe",
              "title": "Publication observed",
              "kind": "operation",
              "role": "release-manager",
              "required": true,
              "action": "Confirm the observed publication (vericairn release observe <version>) and observe each package destination it starts (vericairn packages observe).",
              "done": [
                { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}", "status": "published" } }
              ],
              "recovery": "Observe again once the provider shows the publication; a package destination still indexing is observed again later."
            },
            {
              "key": "upgrade-estate",
              "title": "Producing estate upgraded",
              "kind": "flow",
              "role": "release-manager",
              "optional": true,
              "default": "off",
              "flow": { "id": "vericairn.software/upgrade", "version": "1.0.0", "hash": "{upgrade-hash}" },
              "action": "Run the estate upgrade flow for the producing estate: a goal on vericairn.software/upgrade for this version, which its sponsor proposes and activates. An estate that runs the product it releases, as Vericairn's does, turns this step on.",
              "done": [
                { "type": "observation", "fields": { "kind": "vericairn-release", "facts.version": "{subject.version}" } }
              ],
              "recovery": "The upgrade flow carries its own recovery; this step counts once the estate's observation of the pinned version exists."
            },
            {
              "key": "docs-after",
              "title": "Documentation pass after publication",
              "kind": "operation",
              "role": "release-manager",
              "required": true,
              "action": "Run the documentation pass for the published version (vericairn docs pass <version>, which assigns each document the publication made due for review) and clear every one, including design documents that name the version as planned.",
              "done": [
                { "type": "living-document", "product": "{subject.product}", "state": "due", "max": 0 }
              ],
              "recovery": "Review each due document against the publication; one that needs change stays due until it is revised and reviewed."
            },
            {
              "key": "close",
              "title": "Carrier closed",
              "kind": "operation",
              "role": "decider",
              "required": true,
              "action": "The release manager offers its self-reviewed results for closing in one selection (vericairn operator close), a principal who authored none of the release's goal steps evaluates its goals, and the operator closes the carrier once every placed work order is accepted and nothing is due.",
              "done": [
                { "type": "acceptance", "subject": "{subject.carrier}" }
              ],
              "traps": [
                {
                  "name": "placed-work-accepted",
                  "summary": "Nothing placed in the carrier is unaccepted.",
                  "holds": [
                    { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "planned", "max": 0 },
                    { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "active", "max": 0 },
                    { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "paused", "max": 0 },
                    { "type": "work-order", "fields": { "placement.carrier": "{subject.carrier}" }, "state": "submitted", "max": 0 }
                  ]
                },
                {
                  "name": "nothing-due",
                  "summary": "Nothing the publication made due is open: the product's living documents are current.",
                  "holds": [
                    { "type": "living-document", "product": "{subject.product}", "state": "due", "max": 0 }
                  ]
                }
              ],
              "recovery": "Close only once nothing placed in the carrier is unaccepted and nothing it made due is open; work found afterwards goes to the next carrier."
            },
            {
              "key": "withdraw",
              "title": "Candidate withdrawn",
              "kind": "operation",
              "role": "release-manager",
              "required": true,
              "after": ["candidate"],
              "action": "Withdraw the exact candidate with its reason: vericairn release withdraw <candidate> --expected <hash> --provider <provider> --reason <why> --by <you> records the withdrawal and the draft's deletion intent together.",
              "done": [
                { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}", "status": "withdrawn" } },
                { "type": "operation", "fields": { "action": "delete-release-draft", "target.version": "{subject.version}", "target.repository": "{subject.repository}" } }
              ],
              "outputs": [
                { "name": "intent", "kind": "record", "recordType": "operation", "required": true }
              ],
              "recovery": "An interrupted withdrawal reports the receipts already stored; reconcile its batch before withdrawing again."
            },
            {
              "key": "delete-draft",
              "title": "Withdrawn draft deleted",
              "kind": "operation",
              "role": "publisher",
              "required": true,
              "reserved": "publish",
              "after": ["withdraw"],
              "action": "gh release delete v{subject.version} -R {subject.repository} --yes",
              "inputs": [
                { "name": "intent", "from": "step", "step": "withdraw", "output": "intent" }
              ],
              "done": [
                { "type": "operator-request-close", "subject": "{request}", "fields": { "outcome": "done" } }
              ],
              "recovery": "The operator deletes the draft at the provider and closes this step's request as done, saying if it was already gone; the runtime deletes nothing."
            },
            {
              "key": "observe-withdrawal",
              "title": "Draft's absence observed",
              "kind": "operation",
              "role": "release-manager",
              "required": true,
              "after": ["delete-draft"],
              "action": "Record the operator's deletion as the intent's dispatch (vericairn records new operation --supersedes <intent>, status dispatched), then observe the draft's absence: vericairn release observe --version <version> --withdrawal <intent> --by <you>. The sponsor then revises the run for the corrective version.",
              "inputs": [
                { "name": "intent", "from": "step", "step": "withdraw", "output": "intent" }
              ],
              "done": [
                { "type": "operation", "fields": { "action": "delete-release-draft", "target.version": "{subject.version}", "target.repository": "{subject.repository}", "status": "observed-success" } }
              ],
              "recovery": "A draft still present stays pending, and the operator is asked again; a provider's failure settles nothing."
            }
          ],
          "branches": [
            {
              "name": "withdraw",
              "when": [
                { "type": "release", "subject": "{subject.product}", "fields": { "version": "{subject.version}", "status": "withdrawn" } }
              ],
              "steps": ["withdraw", "delete-draft", "observe-withdrawal"],
              "returnTo": "docs-set"
            }
          ]
        }
        """;
}
