# Release notes, Vericairn 0.5.1

Prepared by `vericairn docs notes` before the release build and curated by hand afterwards, as the end of the overview of the release contents lists, from the estate's records since the publication of 0.4.2 (2026-10-14 23:49 UTC) up to 2026-10-15 05:11 UTC; the records are the source and this page is a rendering. The publication of 0.5.1 is not yet recorded: its commit, workflow run, artifacts by hash and qualified platforms are in the release record the estate writes when the draft is observed and, once published, in the published revision; `limitations-and-platforms.md` of this set states what was known before the build.

## What the release contains

Release 0.5.1 is built from main under the operator's decisions 01M4YBR5PG3AAZ8WAKZZ0R10K1, 01M4YSTRMRCFFNYQCANY86QG8E and 01M4YZECP84H43ZMK607J39ZN3. It contains GOAL-01 (record format 2 and its migration, the goal records and the additions milestone M3 adopted), ID-01 (key custody that can need its owner's presence, device identity across renames and rotations, the first authority and emergency access) and DOCS-03 (living documents in the runtime), each accepted by the builder under its standing authority to accept its own ordinary work; ID-01 and DOCS-03 were accepted on their product work as the operator decided (decision 01M4YSTSM07RRZQVTGPV8NBFYA), and the steps they could not take before the estate runs this release, the first-party devices' move to the new custody and the first-party declarations, are carried into REL-050. Their results were written before 0.5.0 was withdrawn and say that their work is unreleased until 0.5.0; it ships in 0.5.1. It also contains DOCS-02's two pull requests, merged after the 0.4.2 release commit, and REL-050's preparation, which REL-050's result records after this set: the maintained documents brought current and the release workflow's tests run on the platform key stores (Vericairn/vericairn pull request 55); every admission of record format 2 signed, so that the first authority holds whatever the charter's signature policy, with `doctor` naming the custody a new device actually gets and presence as the default for a person's device (pull request 56); and the release workflow running the arm64 Secure Enclave helper it ships (the pull request that carries this set). The same work was built as 0.5.0 and withdrawn before acceptance and publication (01M4YZEDNG1GACFEDVP16RSQEZ), after the Reviewer principal's review 01M4YY52DR2GCV3EAXBWBFS4XE found that the first-authority rule bound signed admissions only; pull request 56 is the correction. The results of DESIGN-04 and DESIGN-05 are design documents of the estate and change nothing in the product. The release record of 0.5.1 is written when its draft is observed.

Changed by hand after generation, and nothing else: this overview; REL-040's entry moved under its own heading, with a sentence saying why; a note after each of the GOAL-01, ID-01 and DOCS-03 entries saying whose acceptance it rests on, the GOAL-01 note also saying which later record settles its first limitation; the section "Also in this release: recorded before 0.4.2 was published", which renders DOCS-02's result in full with a note on a later record; the section "Decided between the 0.4.2 notes and the 0.4.2 publication"; a note on the operator's milestone acceptances of DESIGN-04 and DESIGN-05; and a sentence in "Upgrading" on record format 2.

## Work accepted

### GOAL-01: Record format 2 and its migration: the goal records and M3's additions

Result 01M4YHPRARSNA16HW4PP4QSNVC (completed), recorded 2026-10-15 01:09 UTC. Record format 2 is in the product, unreleased until 0.5.0. Vericairn/vericairn pull request 42 (merge aa86cec): a record states the format it was written in and is validated under that format's rules; a writer writes its estate's own format; a type or field a later format introduced is refused in an older record; a record ahead of its estate and a manifest beyond a build's formats are refused; the upgrade's real step record-format-2 rewrites no record, moves the manifest to 2 and makes the migration record the first format-2 record; a decision states its stake and what it does not authorize, and a revision keeps the operator's words; new estates start at format 2. Pull request 43 (merge 48078f1): goal, goal-status, roster and ask; work orders' after and blockedBy; grant expiry, with authority judged at each record's time; agent-run operations with their cap and usage; decisions' answers. Pull request 44 (merge 1ad5fc3): possibility, discharge with decisions' obligations, credential-reference, qualification, and the evidence vocabularies on verifications, which check run and check observe fill; a golden format-1 estate written by the released 0.4.2 binary, which this build reads unchanged and moves to format 2 without rewriting a record, while 0.4.2 refuses the moved copy. docs/record-format-v2.md describes the format.

Limitations recorded:
- DOCS-03 adds the living-document records to format 2; until then format 2 holds the goal records and M3's additions only.
- The runtime does not yet act on goals, asks or rosters: GOAL-02 to GOAL-04 do; agent-run operations are written by GOAL-03's worker.
- The first-party estate stays at format 1 until it upgrades to release 0.5.0 (REL-050).
- Release 0.4.2's refusal names no format; only builds from 0.5.0 say which format an estate is at.

Accepted by the builder under its standing authority (acceptance 01M4YHPSA062FGHBRHVZ6Z3QJN, self, with no review as its basis). Later records settle the first limitation: DOCS-03 added the living-document records to format 2 (pull request 50), so the format this release writes holds them.

### ID-01: Key custody that needs the owner's presence, and device identity through rotation

Result 01M4YN66QRBF6JQNQQA3BAESVF (partial), recorded 2026-10-15 02:10 UTC. Key custody, device identity and emergency access are in the product, unreleased until 0.5.0. Vericairn/vericairn pull request 45 (merge 1f66900): the first authority, the root operator's earliest admission of a device that signed it itself, is the only self-admission accepted. Pull request 46 (merge 5a00a91): Secure Enclave custody on macOS through the vericairn-enclave helper (CryptoKit, with no entitlement and no developer signing), with presence by Touch ID or the login password; format-2 device records state their presence and purpose. Pull request 47 (merge a725839): a device keeps its identity, its first record, across renames and key rotations; a rotation is signed by the previous key and the new one; each signature verifies against the key the device held at the record's time; self-admission is compared by identity, so no revision hides one; devices rename and devices rotate, which also moves a key into other custody. Pull request 48 (merge 058c89e): emergency access through the operator's recovery device, whose key is kept offline in a file encrypted under his passphrase and signs only device records, admissions and revocations, each use shown in status until the operator reviews it. Pull request 49 (merge 37c09bd): the strongest custody each platform offers by default (CNG on Windows, the Secure Enclave on a Mac, the secret service on Linux, else an owner-only file), presence by default for a person's device where its custody enforces it, and Linux secret-service custody. design/identity-and-connectivity.md revision 5 records the recovery key's custody, the defaults and the state of Windows presence. Not done: the first-party devices moved to the new custody, which needs the estate on 0.5.0 and record format 2.

Limitations recorded:
- Windows presence (Windows Hello or a protection prompt) is not built: it cannot be qualified without a Windows device and a desktop session. Windows keys are non-exportable and sign without presence, and their device records say presence none.
- Secret-service custody protects a key at rest; while the keyring is unlocked, other processes of the same user can read it, as the custody kind records.
- A signature is judged by the time its record states, as revocation already was: a key that may have leaked is revoked by an administrator, not only rotated.
- Everything here is unreleased until 0.5.0 (REL-050).

Accepted by the builder on its product work under its standing authority, as the operator decided (acceptance 01M4YSZ49GXPFF7XRE0FB4QQ3Z, self, with no review as its basis); the device moves are carried into REL-050 (amendment 01M4YSWH988FHFYZ9RYZ9SSN0B), and Windows presence is work order ID-03.

### DOCS-03: Living documents in the runtime: declarations, due documents, clearance and the release gate

Result 01M4YQSNMGQG915TW4RPNNZAD6 (partial), recorded 2026-10-15 02:55 UTC. Living documents are in the product, unreleased until 0.5.0. Vericairn/vericairn pull request 50 (merge 1de8cda): the living-document and document-review records and the decision's documents field in record format 2. Pull request 51 (merge a48ad14): due documents derived from records and covered paths, at levels remind, warn and refuse, in status and the estate check, and the release gate that refuses a submission before its intent while a document of the product is due at refuse; record identifiers keep milliseconds so the records of one second sort as made. Pull request 52 (merge 04861b1): generated regions held by the estate check, the region generator vericairn.records/releases, the clearance checks names-version, links-resolve and matches-generator, vericairn docs living, review, declare and regenerate, GET /v1/documents and POST /v1/documents/{id}/reviews, and the MCP tools documents_due and document_review. Pull request 53 (merge 700e80e): documents of other repositories read at a commit through the software pack's git-file source from a clone registered with vericairn sources clone, and the content rule that refuses a release when a cleared document's bytes at the commit it builds differ from its review. Pull request 54 (merge a6b9086): Workbench's Documents page and journey J50. design/living-documents.md revision 2 records what was built. Not done: the first-party declarations, which wait for the estate to run 0.5.0.

Limitations recorded:
- Not built yet: the software pack's own checks (the references and the third-party notices against the build, and a README naming no unreleased capability), packs proposing declarations, retiring a declaration, and Workbench showing a document's content beside what made it due.
- A covered path of another repository is compared only where that repository's clone is registered; elsewhere at the commit reviewed or released.
- Everything here is unreleased until 0.5.0 (REL-050).

Accepted by the builder on its product work under its standing authority, as the operator decided (acceptance 01M4YSZ58RB6GY0PDZYJ7ZTK0D, self, with no review as its basis); the first-party declarations are carried into REL-050 (amendment 01M4YSWH9821QYHB6VJY9XHAC3), and the parts not built are work order DOCS-04.

## Also in this release: recorded before 0.4.2 was published

The 0.4.2 notes were prepared before the 0.4.2 build, from records up to 2026-10-14 22:06 UTC, and these notes start at the 0.4.2 publication, 2026-10-14 23:49 UTC, so the records between appear in neither set's generated notes; they are added here by hand. DOCS-02's are the only work of that window in this release.

### DOCS-02: Living documents: the foundation's design, documents that cannot fall behind, generated files held to their generator

Result 01M4YC8K30YDK193CE0F3VFK23 (completed), recorded 2026-10-14 23:34 UTC. The documents found stale now say only what holds, the agent orientation is held to its generator, and design/living-documents.md designs the foundation's living documents with the pack contract. Product pull request 40 (merge 2979f04) rewrote the README to name no release as current and completed the index of documentation sets; pull request 41 (merge 1f34525) took the work-order table, the pinned release and the date out of AGENTS.md, added `vericairn mcp orientation --check`, and added a test that holds the committed orientation files to a fresh generation. design/master-design.md revision 3, design/goals.md revision 2 and design/open-decisions.md revision 7 record the decisions of October 14, 2026; design/cli-and-sdk.md revision 5 and design/catalog.md revision 4 correct their status statements; the Vericairn/vericairn repository description names only what exists.

Limitations recorded:
- The declared policy (living-document and document-review records, due documents, the release gate) waits for record format 2 in 0.5.0 (DOCS-03); until then the builder reviews the documents of design/living-documents.md section 10 at each release by hand.
- The 0.4.2 tag will carry the README as it was at the candidate's commit, as v0.3.1's does; the README ships in no artifact the release record lists.
- design/platforms.md is revised when release 0.4.2 is published, in REL-040's closing.
- The command line lacks the product, project, review, accept, decide, assets and artifacts families the design binds; their outcomes go through records new, and adding the families or revising the design is a later decision.

Accepted by the builder under its standing authority (acceptances 01M4YC8K3060Z6BCJJSY0Z7VV1 of the work order and 01M4YC8M28QSY2FQB3M4X37W19 of the result, both self). Later records settle the first limitation: DOCS-03 built that policy, and this release ships it.

## Accepted in the same period, not in this release

REL-040 released 0.4.2 through the loop; its result was recorded after 0.4.2 was published, so the generator reads it into this period.

### REL-040: Release 0.4.0 through the loop: the brand, the placement and the fixes

Result 01M4YDNZ7RX6ZBTPFBEW0AD36A (completed), recorded 2026-10-14 23:59 UTC. The loop ran a fourth time, under the operator's decisions 01M4Y13EC89EMSTZ13AD744A39 and 01M4Y49G08BH7KW82CJWRXZDAK: release 0.4.2 carries BRAND-01, PLACEMENT-01, the first-launch fix of 0.3.1 carried to main, FIX-01 and FIX-02, and the license and notice texts of every bundled component, packages and shared frameworks alike. The same work was built and withdrawn twice before acceptance and publication: 0.4.0 (01M4Y49GZG04DQVJKHQB7DNWHG) after the Reviewer principal's review found its Workbench archives without the fonts' license texts, and 0.4.1 (01M4Y6WE9R8DMVJ4W94S5J173Z, amendment 01M4Y6WE9RA1A4YKWH4ZHC6BQQ) after its review found the ASP.NET Core shared framework's notices missing and the installation guide framing Workbench as qualified; Vericairn/vericairn pull requests 37, 38 and 39 put every component's license and notice texts in the archives with a test that holds them to the lock files and the shared frameworks, and made the release notes list only exactly-accepted results as accepted. The 0.4.2 documentation set was produced and checked before the build at the commit whose build is 0.4.2 (Vericairn/vericairn pull request 39, merged at 21033c3a5903, the release commit), after the 0.4.0 set fixed the reference generator that had dropped the --version option of release submit and observe from the 0.3.1 reference. The release was submitted with the estate's pinned 0.3.1 command line as intent (01M4Y7GVM077BMJDY48F2GBV8B) and observed (01M4Y7WEQ03TFVN6AWGY4423FJ): run 38645874518 verified on three platforms and drafted every distributable, and the candidate (01M4Y7WEQ0BHFR9MM8R76H9TFE) carried every distributable's platform without a hand correction. The Reviewer principal reviewed it in a frozen clone at the release commit with a native first launch of the shipped Workbench (01M4Y959BG6KZP0ECJE7KXQHSC: pass); the operator accepted the exact candidate (01M4YCSB78FNSXESXA5DBVBRH7) and published it by hand; the publication was observed (01M4YD5648NMG654P28R32K8MC). The estate was upgraded by the 0.4.2 binary, verified against the published record (01M4YD5TMGHVF0RB15BTCHMB39), and cold-started on three platforms (38656926892: success). The first-party product record now declares its release profile (01M4YDKF58WRVV8YCM6P12CBQG), recorded with 0.4.2, and design/platforms.md is at revision 6.

Limitations recorded:
- The release is an unsigned developer build; Workbench ships without a bundle, an installer or a signature.
- The release profile takes effect for the next release, which the estate submits with 0.4.2; 0.4.2 itself was submitted and observed with the pinned 0.3.1 command line.
- The operator's acceptance and publication are his own steps; the builder observed them. The agent harness's safety check refused the builder's commit of the upgrade, so the operator committed and merged it (estate pull request 102); the published revision, the migration and the manifest in it are the builder's.


## Work recorded, not yet accepted

Results of this period with no acceptance of their exact revision: claims to inspect, not part of what the release is known to contain. The operator accepted DESIGN-04 and DESIGN-05 as milestones, which closes them (acceptances 01M4YYT66R7789ZSH1HWCYN2MK and 01M4YYT66RGCCXXZ96NE78DEBM, under "Milestone and release acceptances" below); the generator lists a result as accepted only when the result itself is.

### DESIGN-04: Process profiles: optional ways of working on top of goals

Result 01M4YS9370BD0G2GFSE0317Y7C (completed), recorded 2026-10-15 03:21 UTC. design/process-profiles.md revision 1 designs process profiles as the operator asked (decision 01M4YS4CTG8SRVPBYZF8HXNF3B): optional, versioned ways of working that an estate adopts for a product or a goal, layered on goals and built from the records the estate already has. It answers the operator's question about making them optional: yes, with no default profile, the builder's loop as the baseline, and no profile removing a reserved action, an independence rule, evidence-based completion or a gate. It covers what a profile declares; how phases, iterations, cycles, flow and experiments map onto goals, milestone work orders, the roster, reviews, acceptances, decisions and living documents; nineteen profiles (Scrum, Kanban, Shape Up, Extreme Programming, continuous delivery, waterfall, V-model, Stage-Gate, spiral, Double Diamond, Lean experiments, dual-track, DMAIC, and the domain lifecycles of games, courses, books, video and campaigns), with what is not proposed and why; roles, gates, cadence, role briefs that boot each role's context, limits, views and measures; composing, switching and authoring; the records of format 3; the edition split; journeys J51 to J55; and work orders PROC-01 to PROC-04 after 0.6.0. design/open-decisions.md revision 8 lists decisions 34 to 36 for it, and records decisions 31 to 33 from ID-01 and DOCS-03 as open; the master design and the catalog name the proposal.

Limitations recorded:
- Design only: nothing is built, adopted or scheduled until the operator decides 34 to 36.
- Each profile's description summarizes an established method for mapping it onto records; it is not that method's official definition, and the pack that ships a profile cites its sources and lets the adopter rename its parts.

### DESIGN-04: Process profiles: optional ways of working on top of goals

Result 01M4YSJXNGZTBQMRK1PRH6NXPF (completed), recorded 2026-10-15 03:27 UTC. design/process-profiles.md revision 2 makes process profiles an optional configuration at every scope and usable in every domain, as the operator directed: the estate, a domain, a product, a project or a goal can adopt a profile, and the narrowest adoption applies. Any scope can adopt none, parameters inherit and can be overridden, every view shows the effective profile with its source, and work keeps the profile it started under. Profiles are domain-neutral unless a pack restricts them, and each Domain Pack contributes its vocabulary, its checks and its lifecycles. Record format 3 gains a project record so that a project can be a scope, and journey J56 covers scopes across domains. Decision 34 in design/open-decisions.md revision 9 names the scopes.

Limitations recorded:
- Design only: nothing is built, adopted or scheduled until the operator decides 34 to 36.
- A project scope needs project records, which no record format has yet; format 3 adds them with process-adoption.

### DESIGN-04: Process profiles: optional ways of working on top of goals

Result 01M4YSYDTRNRRCZBHWY94TRCC5 (completed), recorded 2026-10-15 03:33 UTC. The operator decided 34 to 36 as recommended (decisions 01M4YSTTK8KNE4MEQ03J14078V, 01M4YSTTK81YQGK2V0T7PGH461 and 01M4YSTTK8EKAV3P003ZR7G5F6): process profiles are optional and layered at every scope and in every domain; profiles, adoption, role briefs, gates, views and manual operation are Community, and the conductor's cadence, iterations, limits and replanning are Pro; PROC-01 to PROC-03 come after 0.6.0 with record format 3, starting with Kanban, Scrum, Shape Up, waterfall and Stage-Gate, and PROC-04 with the Domain Packs at M5. design/process-profiles.md revision 3 records them in section 19, and design/open-decisions.md revision 10 lists them as decided.

Limitations recorded:
- Design only: the PROC work orders are opened when their release is planned, after 0.6.0.

### DESIGN-05: Templates: what a piece of work contains, and how each part is made and judged

Result 01M4YTGD109QR6GD5GCX88WE9D (completed), recorded 2026-10-15 03:43 UTC. design/templates.md revision 1 designs the templating system the operator proposed (decision 01M4YSTVJGNEFJZGZGNC2YN6XN) and answers his question whether Vericairn should manage the designs' consistency: yes, with the estate's own documents as the first uses. A template defines an artifact's sections and fields and, for each, its role, synthesis (authored, synthesized, generated or quoted), sources, cohesion, fidelity, quality and options. An instance is a living document whose sections have states derived from records: missing, synthesized, drafted, reviewed, due or failing. Cohesion is checked where a check can decide it and judged by an evaluator otherwise, never scored by its author; agents work from section briefs; options are set at the scopes process profiles use; an instance pins its template's version. The design covers the design-document template, open decisions as asks with a generated view, generated indexes, the design-work and release-work profiles, what 0.5.0 already enables, surfaces, the edition split, the records of format 3, journeys J57 to J59 and work orders TPL-01 to TPL-04. design/open-decisions.md revision 11 lists decisions 37 to 39; the master design and the catalog name the proposal, and the catalog records DESIGN-04's additions as placed by decisions 34 to 36.

Limitations recorded:
- Design only: nothing is built or scheduled until the operator decides 37 to 39.
- The design-work profile and the estate's templates describe the estate's present practice; adopting them is TPL-02 and TPL-03.

### DESIGN-05: Templates: what a piece of work contains, and how each part is made and judged

Result 01M4YX0MD8PD15CYAVZTZ40WQ1 (completed), recorded 2026-10-15 04:27 UTC. The operator decided 37 to 39 as recommended (decisions 01M4YWZ5H8S8SCYR318BGVQT1V, 01M4YWZ6GG831XQK430FSDHEJM and 01M4YWZ6GG4NTSGHE3BXKVCKY0): templates are a Community foundation capability that process profiles, Foundries and packs build on, with the section as the unit of agent work and of review and the four guardrails; the estate's own documents come first, with the design-document template, open decisions as asks with a generated view, generated indexes and the design-work and release-work profiles; TPL-01 and TPL-02 come after 0.6.0 with record format 3, before or with PROC-01, TPL-03 with PROC-01 and TPL-04 with each pack, while from 0.5.0 decisions name the documents they change, the design documents are declared living and open decisions can be put as asks. design/templates.md revision 2 records them in section 17, design/open-decisions.md revision 12 lists them as decided, and design/catalog.md revision 7 marks DESIGN-05's additions as planned.

Limitations recorded:
- Design only: the TPL work orders are opened when their release is planned, after 0.6.0; the steps of section 11 begin once the estate runs 0.5.0.

## Decisions

- **Process profiles: waterfall, agile and others, optional on top of the plan** (2026-10-15 03:19 UTC, decision 01M4YS4CTG8SRVPBYZF8HXNF3B): Design process profiles (waterfall, agile and the other established ways of working the builder can add) as an optional layer on top of the planned goals work; the design and its decisions come back to the operator before anything is scheduled.
- **Release 0.5.0 with record format 2, key custody and living documents** (2026-10-15 03:31 UTC, decision 01M4YSTRMRCFFNYQCANY86QG8E): Release 0.5.0 now with record format 2, key custody and emergency access, and living documents (GOAL-01, ID-01, DOCS-03); GOAL-02 to GOAL-04 and ID-02 move to the next release, 0.6.0.
- **ID-01's device moves and DOCS-03's declarations happen in REL-050** (2026-10-15 03:31 UTC, decision 01M4YSTSM07RRZQVTGPV8NBFYA): ID-01's moves of the first-party devices to the new custody and DOCS-03's first-party declarations are carried into REL-050, after the estate's migration to record format 2; ID-01 and DOCS-03 are accepted on their product work.
- **Windows presence and DOCS-03's unbuilt parts after 0.5.0** (2026-10-15 03:31 UTC, decision 01M4YSTSM0WNS7X1PEYAPS6JBB): Presence for Windows devices and DOCS-03's unbuilt parts (the software pack's own checks, packs proposing declarations, retiring a declaration, a document's content in Workbench) are deferred to after 0.5.0.
- **Process profiles: the edition split** (2026-10-15 03:31 UTC, decision 01M4YSTTK81YQGK2V0T7PGH461): Profiles, adoption, role briefs, gates, views and the manual operation of a profile are Community; the conductor's cadence, iterations, work-in-progress enforcement and replanning are Pro.
- **Process profiles: placement and order** (2026-10-15 03:31 UTC, decision 01M4YSTTK8EKAV3P003ZR7G5F6): PROC-01 to PROC-03 are built after 0.6.0 with record format 3, starting with Kanban, Scrum, Shape Up, waterfall and Stage-Gate; the domain lifecycles (PROC-04) come with their Domain Packs at M5.
- **Process profiles are optional and layered, at every scope** (2026-10-15 03:31 UTC, decision 01M4YSTTK8KNE4MEQ03J14078V): Process profiles are optional and layered: no default profile, the builder's loop as the baseline, no profile removing a reserved action, an independence rule, evidence-based completion or a gate; an optional configuration at the estate, a domain, a product, a project or a goal, the narrowest applying, usable in every domain.
- **Templates: write the design as DESIGN-05** (2026-10-15 03:31 UTC, decision 01M4YSTVJGNEFJZGZGNC2YN6XN): Write the templating system up as work order DESIGN-05: templates and their sections with per-section role, synthesis, cohesion, fidelity, quality and options, with the design-document template and the design-work profile as the first uses.
- **Templates: a Community foundation capability with its guardrails** (2026-10-15 04:26 UTC, decision 01M4YWZ5H8S8SCYR318BGVQT1V): Templates are a Community foundation capability that process profiles, Foundries and packs build on: an artifact's sections and fields, each with its role, synthesis, sources, cohesion, fidelity, quality and options; the section as the unit of agent work and of review; quality and cohesion as evidence, never a self-score; a small, fixed vocabulary of section properties; a template grants nothing; an instance pins its template's version.
- **Templates: placement** (2026-10-15 04:26 UTC, decision 01M4YWZ6GG4NTSGHE3BXKVCKY0): TPL-01 and TPL-02 are built after 0.6.0 with record format 3, before or with PROC-01; TPL-03 with PROC-01; TPL-04 with each pack. Meanwhile, from 0.5.0, decisions name the documents they change, the design documents are declared living, and open decisions can be put to the operator as asks.
- **Templates: the estate's own documents first** (2026-10-15 04:26 UTC, decision 01M4YWZ6GG831XQK430FSDHEJM): The first templates are the first-party estate's: the design-document template, open decisions held as asks with a generated view, the generated design index, and the design-work and release-work profiles, so that the consistency of the estate's designs is managed by Vericairn rather than by the builder's memory.
- **Release 0.5.0: fix the first authority first, and release the work as 0.5.1** (2026-10-15 05:09 UTC, decision 01M4YZECP84H43ZMK607J39ZN3): Withdraw the 0.5.0 candidate before acceptance and release the same work as 0.5.1 with the admission fix: in record format 2 every admission, admitting or revoking, is signed, so the first authority holds whatever the charter's signature policy; then a new independent review and the operator's acceptance.

## Decided between the 0.4.2 notes and the 0.4.2 publication

Recorded on 2026-10-14 between 23:25 and 23:26 UTC, after the 0.4.2 notes were prepared and before 0.4.2 was published, and added here by hand:

- **Living documents: a foundation capability that packs extend** (decision 01M4YBR5PG2AFRN7FW04ZEBCDQ): a living-documents system in Community, which this release's DOCS-03 builds.
- **Goals: releases 0.5.0 to 0.7.0** (decision 01M4YBR5PG3AAZ8WAKZZ0R10K1): the sequencing this release follows, as revised by decision 01M4YSTRMRCFFNYQCANY86QG8E above.
- **Goals: the edition split** (decision 01M4YBR5PGM85TMKM00GVNWQ93), **Goals: how a subscription harness's usage counts against a budget** (decision 01M4YBR5PGCPQD94BVNC547EBA) and **The product repository's description names only what exists** (decision 01M4YBR6NRHCHHMGHDSVZEWT78).

The operator accepted DESIGN-03, the goals design, in the same window (acceptance 01M4YCSC6GTC4J3RBX1G1TC3QH).

## Work orders opened or revised

- **GOAL-01** (ordinary): Record format 2 and its migration: the goal records and M3's additions.
- **GOAL-02** (ordinary): Goals: the predicate evaluator, the evaluation, activation and acceptance, and the goal command.
- **GOAL-03** (ordinary): Goals: the worker contract and the coding-agent adapters.
- **GOAL-04** (ordinary): Goals: routing by roster, asks, the next-eligible view, the custody precondition and Workbench's Goals workspace.
- **ID-02** (ordinary): Pairing, revocation and local-network connections with mutual authentication.
- **ID-01** (ordinary): Key custody that needs the owner's presence, and device identity through rotation.
- **REL-050** (ordinary): Release 0.5.0 through the loop: record format 2, key custody, goals and living documents.
- **DOCS-03** (ordinary): Living documents in the runtime: declarations, due documents, clearance and the release gate.
- **DESIGN-04** (milestone): Process profiles: optional ways of working on top of goals.
- **M3** (milestone): Identity and Remote, releases 0.5.0, 0.6.0 and 0.7.0 (revised).
- **REL-050** (ordinary): Release 0.5.0 through the loop: record format 2, key custody and living documents (revised).
- **M4** (milestone): Unattended work, Pro programs, packages, websites (revised).
- **DOCS-04** (ordinary): Living documents: the software pack's checks, proposed declarations, retirement and the document view.
- **ID-03** (ordinary): Presence for Windows devices.
- **DESIGN-05** (milestone): Templates: what a piece of work contains, and how each part is made and judged.

## Milestone and release acceptances

- DESIGN-04: "I accept DESIGN-04 from the recorded evidence: design/process-profiles.md revision 3, process profiles as optional ways of working on top of goals, with the decisions 34 to 36 I took on October 15, 2026, all as recommended." (acceptance 01M4YYT66R7789ZSH1HWCYN2MK, 2026-10-15 04:58 UTC, signed)
- DESIGN-05: "I accept DESIGN-05 from the recorded evidence: design/templates.md revision 2, the templating system I proposed, with the decisions 37 to 39 I took on October 15, 2026, all as recommended." (acceptance 01M4YYT66RGCCXXZ96NE78DEBM, 2026-10-15 04:58 UTC, signed)

## Identity and policy

None in this window.

## Design documents revised

- Platform obligations revision 6 (`design/platforms.md`): Revision 6: the observations as of release 0.4.2: the headless profiles re-observed at the release commit and the 0.4.2 cold start, the shipped 0.4.2 Workbench's native first launch and probe, and the acting-principal display run headless on the three platforms.
- Identity and connectivity revision 4 (`design/identity-and-connectivity.md`): Revision 4: the first authority (a device admits itself for the operator once, for the estate's first device) and emergency access through an offline recovery device; custody that states whether each signature needs its owner's presence, with Secure Enclave keys on macOS through CryptoKit and no presence ever claimed by an agent's device; device identity kept across renames and key rotations, signed by both keys.
- Identity and connectivity revision 5 (`design/identity-and-connectivity.md`): Revision 5: the recovery device's custody is offline, its key written once encrypted under the operator's passphrase and opened in memory only for a recovery, and it is never the first authority; a new device gets the strongest custody its machine offers, a person's device requires presence wherever its custody enforces it, and Windows presence is not built yet, so a Windows device states presence none.
- Living documents revision 2 (`design/living-documents.md`): Revision 2, as DOCS-03 built it: regions take their generator's inputs; records of one second sort by identifier, which keeps the milliseconds they were made; the foundation adds the region generator vericairn.records/releases and the source and generator contracts packs implement; the software pack's git-file reads from a clone registered with vericairn sources clone, comparing covered paths at its head; the content rule reads documents at the commit the provider's ref names and records in the intent what was compared; Workbench's page and what is not built yet.
- Process profiles revision 1 (`design/process-profiles.md`): Revision 1: optional, versioned ways of working layered on goals: what a profile declares, adoption and switching, how goals, work orders, the roster, briefs, reviews, acceptances and living documents carry phases, iterations, cycles, flow and experiments; nineteen profiles in delivery, planned and gated, discovery, improvement and domain lifecycles; roles, gates, cadence, role briefs, limits, views and measures; the edition split, records, journeys J51 to J55, work orders PROC-01 to PROC-04, and decisions 34 to 36.
- Vericairn: master design revision 4 (`design/master-design.md`): Revision 4: an estate may opt into a process profile layered on the same records, and the design set names process-profiles.
- Open product decisions revision 8 (`design/open-decisions.md`): Revision 8: open decisions 31 to 33 after ID-01 and DOCS-03 (release 0.5.0 now; the device moves and declarations inside REL-050; deferring Windows presence and DOCS-03's unbuilt parts) and 34 to 36 requested by DESIGN-04 (optional profiles, the edition split, placement and order).
- Catalog revision 5 (`design/catalog.md`): Revision 5: the additions proposed by DESIGN-04 (process profiles in a practices pack, domain lifecycles as profiles of their packs, role briefs), outside the count of 173 until decisions 34 to 36 place them.
- Open product decisions revision 9 (`design/open-decisions.md`): Revision 9: decision 34 names the scopes, the estate, a domain, a product, a project or a goal, and the use in every domain.
- Process profiles revision 2 (`design/process-profiles.md`): Revision 2, scopes and domains as the operator directed: an optional configuration at the estate, a domain, a product, a project or a goal, the narrowest applying, with none to opt out, inherited parameters, the effective profile shown with its source, and work in flight keeping its profile; profiles domain-neutral unless a pack restricts them, with each Domain Pack contributing its vocabulary, checks and lifecycles; a project record in format 3; journey J56.
- Open product decisions revision 10 (`design/open-decisions.md`): Revision 10: decisions 31 to 36 decided October 15, 2026 as recommended, with their records; decision 27's sequencing marked as revised by decision 31.
- Process profiles revision 3 (`design/process-profiles.md`): Revision 3: the operator's decisions 34 to 36 (01M4YSTTK8KNE4MEQ03J14078V, 01M4YSTTK81YQGK2V0T7PGH461, 01M4YSTTK8EKAV3P003ZR7G5F6) recorded in section 19; the design stands as written.
- Vericairn: master design revision 5 (`design/master-design.md`): Revision 5: the first-party portfolio's release placement after decision 31: record format 2, key custody, emergency access and living documents in 0.5.0; goals' runtime and pairing in 0.6.0; Remote in 0.7.0.
- Goals: objectives an estate carries out with agents and people revision 3 (`design/goals.md`): Revision 3: the sequencing decision revised by decision 31: 0.5.0 carries GOAL-01, and GOAL-02 to GOAL-04 move to 0.6.0.
- CLI and SDK revision 6 (`design/cli-and-sdk.md`): Revision 6: devices gains rename, rotate and the recovery device in 0.5.0; pair and connections move to 0.6.0 with ID-02.
- Templates: what a piece of work contains, and how each part is made and judged revision 1 (`design/templates.md`): Revision 1: templates define an artifact's sections and fields and, for each, its role, synthesis, sources, cohesion, fidelity, quality and options; instances as living documents with section states; cohesion checked or judged; section briefs; options across scopes; versions; the estate's own templates and the design-work profile; surfaces, the edition split, the records of format 3, journeys J57 to J59 and work orders TPL-01 to TPL-04; three decisions for the operator.
- Vericairn: master design revision 6 (`design/master-design.md`): Revision 6: templates, equally optional, say what each kind of artifact contains and how each part is made and judged, and the design set names templates.
- Catalog revision 6 (`design/catalog.md`): Revision 6: DESIGN-04's additions placed by decisions 34 to 36, and the additions DESIGN-05 proposes (templates, the first-party estate's templates, the design-work and release-work profiles, pack templates), outside the count of 173 until decisions 37 to 39.
- Open product decisions revision 11 (`design/open-decisions.md`): Revision 11: the three decisions DESIGN-05 requests for templates (37 to 39).
- Templates: what a piece of work contains, and how each part is made and judged revision 2 (`design/templates.md`): Revision 2: the operator's decisions 37 to 39 (01M4YWZ5H8S8SCYR318BGVQT1V, 01M4YWZ6GG831XQK430FSDHEJM and 01M4YWZ6GG4NTSGHE3BXKVCKY0) recorded in section 17; the design stands as written.
- Open product decisions revision 12 (`design/open-decisions.md`): Revision 12: decisions 37 to 39 decided October 15, 2026 as recommended, with their records.
- Catalog revision 7 (`design/catalog.md`): Revision 7: the additions DESIGN-05 proposed are placed by decisions 37 to 39, still outside the count of 173.

## The release itself

- 0.5.0 candidate (01M4YW8SQ027ABDRFMBNVYV7PA, 2026-10-15 04:14 UTC); signing: unsigned developer build; qualified: linux-x64, osx-arm64, win-x64.
- 0.5.0 candidate (01M4YWAR70A94WFNREC8DMS8MD, 2026-10-15 04:15 UTC); signing: unsigned developer build; qualified: linux-x64, osx-arm64, win-x64.
- 0.5.0 withdrawn (01M4YZEDNG1GACFEDVP16RSQEZ, 2026-10-15 05:09 UTC); signing: unsigned developer build; qualified: linux-x64, osx-arm64, win-x64.

## Upgrading

- Migration 01M4YD5TMGHVF0RB15BTCHMB39: from release 0.3.1 to release 0.4.2, record format 1 to 1; migrations applied: none; changes: estate/manifest.json pinned to release 0.4.2 with the artifact hashes of release 01M4YD5648NMG654P28R32K8MC.

An estate on 0.4.2 upgrades with `vericairn estate upgrade --to 0.5.1 --by <principal>` run with the 0.5.1 binary once the published release record is in the estate; `--check` shows the plan first. The upgrade moves the estate to record format 2 (the step `record-format-2`) without rewriting a record; `install.md` section 6 says what changes for the clones that write to it.
