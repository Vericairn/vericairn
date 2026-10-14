# Release notes, Vericairn 0.4.1

Prepared by `vericairn docs notes` before the release build (three entries moved and two notes on later records added after generation, as the overview of the release contents says), from the estate's records since the publication of 0.3.1 (2026-10-14 11:35 UTC) up to 2026-10-14 21:19 UTC; the records are the source and this page is a rendering. The publication of 0.4.1 is not yet recorded: its commit, workflow run, artifacts by hash and qualified platforms are in the release record the estate writes when the draft is observed and, once published, in the published revision; `limitations-and-platforms.md` of this set states what was known before the build.

## What the release contains

Release 0.4.1 is built from main under the operator's decisions 01M4Y13EC89EMSTZ13AD744A39 and 01M4Y49G08BH7KW82CJWRXZDAK. It contains the work built as 0.4.0, which was withdrawn before acceptance and publication because its Workbench archives lacked the license texts of the two embedded fonts: BRAND-01 and PLACEMENT-01, which were accepted before 0.3.1 was published and so appear in the 0.3.1 notes as work of that period not in that release; the first-launch fix of 0.3.1, carried to main; and FIX-01 and FIX-02. New in 0.4.1, every archive carries the license and notice texts of the components it bundles in its `licenses` directory, the fonts' among them. Release 0.4.0 was never published, so these notes run from 0.3.1. The release record of 0.4.1 is written when its draft is observed.

## Work accepted

### BRAND-01: The brand direction applied: the mark, the first token values and the contrast measurement

Result 01M4WAVTB0M58N14YX0V9301C1 (completed), recorded 2026-10-14 04:31 UTC. The brand direction is applied (Vericairn/vericairn pull request 31 as 5d4c9b27a2ec). The cairn mark, the horizontal and stacked lockups and the wordmark exist as first-party SVG in brand/ with a rights note (the name, the wordmark and the mark are the operator's; the files are first-party from no third-party material; the product's license grants no right to them as marks). Inter 4.1 and JetBrains Mono 2.304 travel with Workbench under the SIL Open Font License with their license texts and are bound as the type roles; the first color values are bound in light and dark variants; the probe now measures the contrast of every pair the roles form and reports it beside the target sizes, the headless theme test holds the minimums and the bundled faces on three platforms, and the native macOS measurement is recorded. The arrival page shows the mark drawn as geometry and the wordmark. design/brand.md revision 2 records the direction, the values, the measured contrast and every asset by hash. The measurement exposed the placeholder outlines at 1.59 and 1.88 against a minimum of 3; the values were darkened before the record.

Limitations recorded:
- The wordmark's glyphs are not outlined in the SVG; a renderer without Inter falls back to its sans-serif.
- Workbench has no raster application icon yet; the mark exists as SVG and as drawn geometry on the arrival page.
- The websites inherit the identity at M4; nothing public shows it yet.
- The other four measurement cells remain non-claims.

### PLACEMENT-01: Packs, providers and adapters placed where they belong

Result 01M4WCFCQGDR3JPSTMPQ9FGXMG (completed), recorded 2026-10-14 04:59 UTC. The placement decision is carried out in the product (Vericairn/vericairn pull request 32 as 9cc16a20e0e0, version 0.4.0 in development). The foundation keeps the contracts and a registry the host fills, and ships no provider: the GitHub provider is Vericairn.Providers.GitHub, the git adapter is Vericairn.Adapters.Git, and repository adoption with build and test checks is Vericairn.Packs.Software, each its own assembly shipped inside the Community distributable; a test holds that the runtime, record and protocol assemblies reference none of them. The command line registers them as available, and being available selects nothing: a release goes through the provider its request names or its product record declares, a request with neither is refused, and a provider with no implementation on the runtime's machine is refused, never replaced. Providers declare their settings, defaults, destination and served locations, listed by GET /v1/providers and 'vericairn release providers'; the release workspace renders the selected provider's settings and preselects only the product's declared provider. The product record gains an optional release object (destination, checksum file, artifacts with roles and platforms) that the release flow reads, so a second product releases with its own artifact set, proven by a journey; a product that declares nothing is read by a generic rule that classifies Vericairn's own assets as before and also labels the Workbench distributables with their platforms. 'check observe' resolves its provider from the resource's recorded location. Reference generation moved to build tooling (tools/Vericairn.DocsTool) and the shipped 'docs check' reads the required documents from the set's manifest and the product's declaration (docs/releases/documents.json), with the release workflow running both. Endpoint discovery moved to the SDK and Workbench no longer references the daemon assembly. The architecture, packages, command-line and catalog documents are revised where the placement is now mechanical.

Limitations recorded:
- The modules are assemblies inside one distributable, not packages: installation, activation and removal arrive with M4's package manager and registry.
- The release record still names its source repository as owner/name (the format 1 release type's pattern); a destination of another shape needs a record-format change.
- The 0.4.0 release itself is submitted by the 0.3.0 binary the estate runs, so its candidate is classified by 0.3.0's compiled names and the Workbench distributables will again need their platforms stated by a superseding revision; the generic rule and declared profiles take effect from the 0.5.0 release.

Later records settle that limitation: the estate runs 0.3.1, whose observer states every Workbench distributable's platform, so the 0.4.0 candidate needed no superseding revision for its labels, and 0.4.1 is submitted and observed the same way.
- Only one release provider exists (GitHub); the contract is generic and a second provider is not implemented.


### FIX-01: Workbench names the acting principal truthfully; a failed daemon start says why

Result 01M4Y13HA0JRVT6G81FMPYDGT5 (completed), recorded 2026-10-14 20:19 UTC. Workbench's target bar now names the acting principal by its current revision with the device the runtime signs with: a principal named with --as selects its own first admitted device held here, in the runtime's order; with none held, the bar says its writes are refused where the estate requires signatures and unsigned where it does not, read from the estate record's policy as the runtime reads it; choosing another device in the selector makes its principal the acting one. runtime start opens the daemon's log before it starts the daemon, and a daemon that stops before serving is reported with what it wrote there and where the log is; a detached daemon writes every failure to the log, including unexpected exceptions. The Workbench tests take their own Vericairn home before any test runs. Merged by the operator as Vericairn/vericairn pull request 35 (merge 9866afc7980b), with FIX-01 and FIX-02 in one pull request so that one merge carried both.

Limitations recorded:
- Native desktop runs of the corrected target bar are not recorded; the behavior is proven headless on three platforms.
- The label states the signature policy as the estate record says at the last refresh.

### FIX-02: The runtime tests keep one Vericairn home for their whole run

Result 01M4Y13J9840GSG3N7J2W2CJY2 (completed), recorded 2026-10-14 20:19 UTC. The runtime test process takes one Vericairn home before any test runs and removes it after the last; EstateSessionTests and PerformanceBudgetTests no longer set the process-wide variable or delete a home while other classes run, and the live-store location test checks against the shared home. Merged by the operator as Vericairn/vericairn pull request 35 (merge 9866afc7980b), with FIX-01 and FIX-02 in one pull request so that one merge carried both.

Limitations recorded:
- A race that shows once in many runs is not proven absent by passing runs; the cause is removed by construction.

## Accepted in the same period, not in this release

WB-04 released 0.3.1 through the loop; its result was recorded after 0.3.1 was published, so the generator reads it into this period.

### WB-04: Release 0.3.0 through the loop, reviewed independently

Result 01M4X39NW0ZWWJV6G7FHCEX2BD (completed), recorded 2026-10-14 11:38 UTC. The loop ran a third time as intent then observed effect, with release 0.3.1 in place of 0.3.0 under decision 01M4WEZF7GTD3VNHAE3JP5JMGT and amendment 01M4WEZF7GFJYR2NWQTW815M1J. Release 0.3.0 was built, recorded as a candidate and reviewed independently; testing the shipped Workbench before accepting it, the operator found that it never opened an estate on a first launch with no daemon running. The builder reproduced the cause with the shipped binaries (Workbench waited for the end of the output of `vericairn runtime start`, which the daemon inherited and held open), the operator decided to fix it and release 0.3.1, and the 0.3.0 candidate was withdrawn (01M4WEZF7G864F6500WPY3WC89) without acceptance or publication. Release 0.3.1 was built from the release/0.3 branch at the 0.3.0 commit with the fix, two tests that run the real command line the way Workbench does, the Workbench platform labels and the 0.3.1 documentation set (Vericairn/vericairn pull request 33, commit cd80a94858e9; continuous integration run 38519833521 on three platforms). It was submitted through the GitHub Releases provider with the 0.2.0 command line as intent (01M4WG77P81JF85YMKG5Q0ZZG0) and observed (01M4WGMMD0V87NYPNBTYKJ6W63): run 38520141851 verified 144 tests on each of three platforms with none failed and drafted every distributable. The candidate was recorded from the draft (01M4WGMMD01EA9P83P121TF8FV) and revised before review to state each Workbench distributable's platform (01M4WGMMD08GTBA9VVR7TSWNXQ). The Reviewer principal reviewed the candidate and the milestone in a frozen clone at the release commit, including a native first launch of the shipped Workbench on macOS (01M4WJ89Q864DJJR3GCRSBJ1VQ, 01M4WJ8GJ0AY62Q56XFN93XTRN); the operator accepted the exact candidate in the shipped Workbench 0.3.1 through the daemon (01M4X2XQ20961FEVXAN4E4GB6M) and published it by hand; the publication was observed (01M4X33JJ0CYV7A2BFPY7NX8KW). The estate was upgraded by the 0.3.1 binary, fetched and verified against the published record before it touched the estate (01M4X33NFRZ1XV79WC8CGF3QZP); the cold start ran from the 0.3.1 pin on three platforms (38557691412: success). Carrying the fix to main for 0.4.0 (Vericairn/vericairn pull request 34) is still open; see the unresolved effects.

Limitations recorded:
- Release 0.3.0 was withdrawn after its review and before acceptance; its GitHub draft stays unpublished until the operator deletes it.
- The headless Workbench tests had replaced the runtime manager with a stand-in, which is how the first-launch path went unexercised in 0.3.0; the new tests use the real command line, and native first launches are observed only where a review records them (macOS).
- The release is an unsigned developer build; Workbench ships without a bundle, an installer or a signature.
- The operator's acceptance and publication are his own steps; the builder observed them.


Later records settle WB-04's open effect: the operator merged pull request 34, and main's run at that merge passed on Linux, macOS and Windows (verification 01M4X4H3K06FP1S0HN54ED4A7S).

## Decisions

- **Release 0.4.0 now; identity and Remote move to 0.5.0** (2026-10-14 20:19 UTC, decision 01M4Y13EC89EMSTZ13AD744A39): Release 0.4.0 soon from main with what main holds: the operator's brand direction (BRAND-01), the providers, adapters and the software pack outside the foundation (PLACEMENT-01), the first-launch fix carried from 0.3.1, and FIX-01 and FIX-02. Milestone M3, identity and Remote, becomes release 0.5.0.
- **The builder merges product pull requests after the three-platform run passes** (2026-10-14 20:19 UTC, decision 01M4Y13FBGEF251MR8BADCWT7Q): The builder merges pull requests in the Vericairn product repository once continuous integration on the pull request's head commit has passed on Linux, macOS and Windows; releases and milestones stay the operator's to accept, and publication and the other reserved actions stay his.
- **The builder reviews and manages releases for this session** (2026-10-14 21:15 UTC, decision 01M4Y49G089STFRF7STP18X9DT): For the working session of October 14, 2026 the builder runs releases end to end: the documentation set, the build, the candidate, the independent review by the Reviewer principal, corrective releases and the withdrawal of a candidate that fails review, publication after the operator's acceptance, the observation of the publication, the estate's upgrade and its cold start. The operator's acceptance of each release stays his own signed record.
- **0.4.0 withdrawn; release 0.4.1 with the license texts** (2026-10-14 21:15 UTC, decision 01M4Y49G08BH7KW82CJWRXZDAK): The 0.4.0 candidate is withdrawn without acceptance or publication because its Workbench archives do not carry the license texts the SIL Open Font License requires for the two embedded fonts; main is released as 0.4.1 with those texts and with the license and notice texts of every component the distributables bundle.

## Work orders opened or revised

- **FIX-01** (ordinary): Workbench names the acting principal truthfully; a failed daemon start says why.
- **FIX-02** (ordinary): The runtime tests keep one Vericairn home for their whole run.
- **REL-040** (ordinary): Release 0.4.0 through the loop: the brand, the placement and the fixes.
- **M3** (milestone): Identity and Remote, release 0.5.0 (revised).

## Milestone and release acceptances

- M2-WORKBENCH: "I accept milestone M2-WORKBENCH from the recorded evidence, with release 0.3.1 in place of 0.3.0 under decision 01M4WEZF7GTD3VNHAE3JP5JMGT: Workbench exists as a client of the daemon with the work and release screens proven headless on three platforms and a native first launch observed on macOS, release 0.3.1 ran through the loop as intent then observed effect with the Reviewer's independent review and my manual publication, and the estate runs on 0.3.1. The findings on record, including Workbench's higher action count for journey J01, are accepted as known." (acceptance 01M4X3HMR8NX9BQBK04JVT5SJK, 2026-10-14 11:42 UTC, signed)

## Identity and policy

None in this window.

## Design documents revised

- Platform obligations revision 5 (`design/platforms.md`): Revision 5: the observations as of release 0.3.1, including the first measurement of a shipped Workbench build, the first native first launch, and the first native Workbench write to the first-party estate; the headless profiles re-observed at the 0.3.1 commit and cold start.

## The release itself

- 0.4.0 candidate (01M4Y26KC89SY817H9TAVZBE6T, 2026-10-14 20:38 UTC); signing: unsigned developer build; qualified: linux-x64, osx-arm64, win-x64.
- 0.4.0 withdrawn (01M4Y49GZG04DQVJKHQB7DNWHG, 2026-10-14 21:15 UTC); signing: unsigned developer build; qualified: linux-x64, osx-arm64, win-x64.

## Upgrading

- Migration 01M4X33NFRZ1XV79WC8CGF3QZP: from release 0.2.0 to release 0.3.1, record format 1 to 1; migrations applied: none; changes: estate/manifest.json pinned to release 0.3.1 with the artifact hashes of release 01M4X33JJ0CYV7A2BFPY7NX8KW.

An estate on 0.3.1 upgrades with `vericairn estate upgrade --to 0.4.1 --by <principal>` run with the 0.4.1 binary once the published release record is in the estate; `--check` shows the plan first.
