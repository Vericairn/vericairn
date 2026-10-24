# Release documentation sets

The current published release is **0.8.3**, published on October 24, 2026 at 14:24:07 UTC,
with tag `v0.8.3` at `fb5b497df0686079e917ec5eac375a7d48d27504`
(estate release record `01M5Q7AE5C6VRH8YH9JBP19GQS`). It follows 0.8.2, published on October 22, 2026
at 11:24:13 UTC with tag `v0.8.2` at `e000a12ea980a4ac0c7e7a127a714f6423e27d7f`
(estate release record `01M5HNSF7RT7FZDBKM9RCY655V`). 0.8.3 was published before the Reviewer's review of
its candidate finished and before the operator's acceptance was recorded (decision
`01M5Q4Y34CFCY24W4F3A6ZD2SP`); the Reviewer's review `01M5Q6D7PJFDMSB6ST7AAVQ7QV` then passed with four informational findings, and the operator's acceptance followed. 0.8.3's [Security section](0.8.3/release-notes.md#security)
discloses FIX-60's correction to checked pushes, and 0.8.2's [Security section](0.8.2/release-notes.md#security)
the corrections from 0.8.0's security review. 0.8.2's known issue, a policy basis that differed by platform
([issue 184](https://github.com/Vericairn/vericairn/issues/184)), is fixed in 0.8.3 (FIX-61).
0.7.2's [Security section](0.7.2/release-notes.md#security)
discloses the export-restore integrity defect in releases 0.1.0 through 0.7.1 and its correction in 0.7.2.

## Readiness disclosures

Read these before relying on the current published release, 0.8.3. They moved here from the repository's
README, which links to this section; the [limitations and platforms](../limitations-and-platforms.md) guide
gives every current limit.

- **Security.** 0.8.3's release notes open with a [Security](0.8.3/release-notes.md#security) section:
  FIX-60 corrects how `changes push` ran git in 0.8.0 through 0.8.2, with the clone's own and the user's
  configuration and any protocol, and says what to check in a clone used with it before 0.8.3; the user's system and global git
  configuration, and files they include, applied before 0.8.3 too, so check them as well. 0.8.2's
  [Security](0.8.2/release-notes.md#security) section: FIX-34 through FIX-38 harden the worker's command audit and settlement, the release gate, checked pushes
  and merges, and the upgrade, and CI-03 builds releases with only the tokens each step needs. 0.7.2's
  [Security](0.7.2/release-notes.md#security) section discloses F-01, which affected export restoration in
  every published release from 0.1.0 through 0.7.1. Upgrade to 0.7.2 or later before restoring an export.
  The fix binds restored bytes to the verified inventory; it does not authenticate the export's producer.
- **Order of review and publication.** 0.8.3 was published before the Reviewer's independent review of
  its candidate finished and before the operator's acceptance was recorded, because the publication command
  ran early (decision 01M5Q4Y34CFCY24W4F3A6ZD2SP). The Reviewer's review `01M5Q6D7PJFDMSB6ST7AAVQ7QV` then passed with four informational
  findings, and the operator's acceptance followed on October 24, 2026. Its assets are exactly the reviewed
  candidate's.
- **Known issue.** On a slower Windows machine, `status` can exceed its 30-second read deadline for a large
  estate and fail with "The request timed out." (exit code 124): Vericairn's own estate, about 4,100 records,
  takes over 30 seconds on GitHub's Windows runner, against 16 seconds on macOS and 25 on Linux. Other
  commands are not affected. 0.8.4 fixes it (FIX-63,
  [issue 205](https://github.com/Vericairn/vericairn/issues/205)).
- **Interfaces.** [API compatibility](../api-compatibility-0.8.3.md) lists 0.8.3's interface additions, among
  them `IEstateApi.WorkNew` and `IEstateApi.ReleaseCheck` without default implementations and a nullable
  `ReleaseBasisResponse.Evidence`.
- **Record format 3.** Upgrading an estate to record format 3 is forward-only, and a format-2-only binary
  refuses the upgraded estate, so every writer and clone moves to 0.8.0 or later together. The upgrade
  declares no policy or duty; the operator adopts them explicitly, as the
  [adoption guide](../policy-adoption.md) describes.
- **Sources and guides.** Ordinary source builds retain `0.8.3-dev`. The maintained guides describe 0.8.3;
  earlier release sets keep their original bytes.
- **Release checks.** Release checks reach only GitHub, ignore system proxies, are disabled with
  `vericairn updates configure --enabled false` and retain cached information when disabled.
- **Signals and attention.** Command observations remain signals, not an enforcement boundary. Required
  attention cannot infer closure from prose or missing evidence; its [rules](../required-attention.md) keep
  undecidable items visible as unknown.
- **Platforms and downloads.** For 0.8.3, the command line is qualified on `linux-x64`, `osx-arm64` and
  `win-x64`, where the release's tests ran. The `linux-arm64`, `osx-x64` and `win-arm64` archives are
  provided but unqualified. Workbench is not qualified on any platform. The archives are unsigned developer
  builds, without code signing or notarization; verify downloads as the [install guide](0.8.3/install.md)
  describes. 0.8.3's `SHA256SUMS` also lists `vericairn-0.8.3-companions.zip`, the readable build reports,
  which the release run keeps in its CI artifact and does not attach; check the attached assets with
  `--ignore-missing`, as the guide does.
- **NuGet.** All eight command-line and library packages are available at 0.8.3 on nuget.org. The `dotnet-vc`
  tool is framework-dependent; the self-contained archives continue to ship `vericairn` for machines without
  the SDK, and the macOS archives also carry the Secure Enclave helper. The NuGet tool does not bundle that
  helper or Workbench. The worker's agent-side MCP command still resolves `vericairn`; keep the archive
  executable on PATH for worker runs. A NuGet-only worker installation is not qualified by the package
  installation checks. `Vericairn` brings `Vericairn.Sdk`, `Vericairn.Protocol` and `Vericairn.Records`,
  preserving their namespaces and assemblies; `Vericairn.Runtime`, `Vericairn.Daemon` and `Vericairn.Mcp`
  are also available, and the SDK front door does not embed a runtime or daemon. The packages contain
  Community code only. Commands written as `vericairn ...` in the archive documentation take the same
  arguments as `dotnet vc ...`.
- **Worker setup.** The native worker baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code
  `2.1.296`; another exact version needs a person's authorized RUN-03 qualification. Generic worker dispatch
  is refused. Before a native run, configure that machine's worker with
  `vericairn worker configure --launcher codex --model <model> --effort <effort>` (or
  `--launcher claude-code`); omit model and effort to show it. Repeated `--allow-model` and `--allow-effort`
  options narrow the permitted choices and never widen the qualified harness inventory. FIX-18 adds Codex's
  `max` effort in 0.7.2; the harness validates model/effort support at launch, and an observed refusal is
  recorded as a failed run. Configuration is per machine and is never recorded in the estate. Its Vericairn
  home must be outside every Git repository and estate; if your home directory is a repository, set
  `VERICAIRN_HOME` elsewhere for the CLI and daemon before starting them. If your home holds `.claude` or
  `.codex` settings, move workspaces outside it with
  `vericairn worker configure --workspaces <absolute-directory>`; `vericairn worker configure` shows the
  root. The root applies to every launcher, must be outside repositories, estates and configured ancestors,
  and is reread by the CLI and daemon for each run. New roots are owner-only. The harness configuration scan
  remains strict. Codex also refuses the user's own `~/.agents` customization, independently of workspace
  placement or `CODEX_HOME`; use a worker account without that discovery root. An empty ordinary directory or
  one containing only recognized instruction files is allowed by the native scan; skills, other entries and
  uninspected links are refused. Harness qualification is machine-local too: paired local-network devices
  are refused. The [software guide](../software.md#explicit-execution-and-environment-admission-run-01-070)
  describes the setup and admission checks.
- **Live qualification.** The seventh live Codex run committed the permitted file in its independent
  workspace on macOS; settlement audited the commit and fetched its branch into the registered clone. Only
  the workspace's own `.git` is added to Codex's writable roots. This live qualification covers the reported
  macOS path, not other harness versions, accounts, platforms, provider billing or acceptance of the work.
  0.8.3 changes no worker behavior; the estate's fourteen 0.8.3 worker runs, on macOS, used its pinned 0.8.2
  worker, so 0.8.2's worker changes, FIX-32 through FIX-35, now have live runs on that platform. The
  [software guide](../software.md#native-inventory-qualification-run-02-and-run-03) retains all seven
  observations.
- **Limits.** Worker spending limits are soft, and unknown usage is charged at the reserved cap. The
  launchers are tested with stand-in processes; live coding-agent sessions and sign-in are not qualified by
  those tests. Process containment and the workspace audit have platform limits, detailed in the
  [software guide](../software.md#launch-accounting-and-containment). The efficiency Adapter is absent until
  the planned 0.8.4 work, Remote is not built, and context tokens remain unknown without a qualified local
  tokenizer. Spending and containment limits remain as described in the guides.

## Documentation sets

The [0.8.3 set](0.8.3/README.md) documents the current published release, published on October 24, 2026 at tag `v0.8.3`, commit `fb5b497df0686079e917ec5eac375a7d48d27504` (estate release record `01M5Q7AE5C6VRH8YH9JBP19GQS`), after published 0.8.2: FIX-47 and CODE-07 for a first estate without hand-written JSON, CODE-08's withdrawal and plan moves, FIX-52's verified pins of a published Vericairn release ([issue 177](https://github.com/Vericairn/vericairn/issues/177)), FIX-55's submission check, FIX-60's correction to how a checked push runs git, FIX-61's platform-independent libraries ([issue 184](https://github.com/Vericairn/vericairn/issues/184)), FIX-62's warning about an older background runtime, FIX-50 and FIX-56 through FIX-59, CI-04 and BRAND-04. An estate at format 3 upgrades to it without a format step. The source version is `0.8.3-dev`. Its references retain build commit `a1aac5a1849570994a6f676dfd5242e5ee19a748`, with 150 commands, 108 API operations, 42 record types and 34 required documents. Its notes render the estate's records with `--from 0.8.2 --to 0.8.3` and open with the operator's approved Security section. The set is frozen for its release build, with every file's checksum in `frozen-sha256.json`; it records no publication, tag, package availability or platform qualification, which the estate's release record and this index establish.

The [0.8.2 set](0.8.2/README.md) documents an earlier published release, published on October 22, 2026 at tag `v0.8.2`, commit `e000a12ea980a4ac0c7e7a127a714f6423e27d7f` (estate release record `01M5HNSF7RT7FZDBKM9RCY655V`). It carries the work of the withdrawn 0.8.1 candidate, after published 0.8.0, with
FIX-53, which lets the macOS and Linux single-file command lines start processes again, and FIX-54, which rechecks a
retained basis's record digest when it is read. That work is FIX-34 through FIX-38 and CI-03, the corrections from
0.8.0's security review; FIX-39 through FIX-46 and FIX-48, found while adopting estate policies;
FIX-29, FIX-32, FIX-49 and CI-01 for test timing and CI; FIX-51, the release gate's retained basis by reference; DIST-02, DIST-03 and DIST-04 for package registry
observations, the package icon and MCP Registry discovery; DOCS-07's exact-byte document pins; and
BRAND-02's README openings. An estate at format 3 upgrades to it without a
format step. The source version is `0.8.2-dev`.
The set was frozen before publication, so it records no publication, tag, package availability or platform qualification; the estate's release record and this index do.
Its references retain build commit `479634f023cd377aba29861312a52ba855795eda`,
with 147 commands, 104 API operations, 42 record types and 32 required documents.
Its notes render the estate's records with `--from 0.8.0 --to 0.8.2` and open with the operator's
approved Security section. The set is frozen for its release build, with every file's checksum in
`frozen-sha256.json`.

The [0.8.1 set](0.8.1/README.md) stays frozen and indexed in `frozen-sha256.json`. Its candidate, built from `d9adbc9faff0a59131a78d9909a9484fb1366f5a` by release run 39640015331, was withdrawn before publication under decision `01M5GAS5XYGCRMMJ99ZXCQDSWF`, recorded by withdrawal `01M5GASTQG0DY20QJN9274CNZ5`, following review `01M5FVAK36D8J2FV1QH5JP1RVC`: its self-contained single-file command lines for macOS and Linux could start no process. It is not a published release or an upgrade target, and the operator deletes its unpublished draft. Neither the set nor its checksums are rewritten to correct its historical wording. Release 0.8.2 carries the same work with FIX-53, FIX-54 and corrected documentation, as 0.7.1 replaced withdrawn 0.7.0.

The [0.8.0 set](0.8.0/README.md) documents POLICY-01 through POLICY-04, DOCS-05,
CODE-02 through CODE-06, READ-01 through READ-04, FIX-23 through FIX-28, FIX-30, FIX-31, OPS-01 and
MERGE-01 after published 0.7.3: estate policies and recurring duties, the combined
release gate and explicit adoption proposals; cancellable clients and public API
ownership; readable companions and retained reference provenance; worker audit,
cleanup and state-publication corrections; exact local context originals; and
signed operator actions and checked repository changes. It explains the upgrade
to record format 3 and the policy definitions, writing grant and declarations that
remain the operator's work after upgrading. The source version is `0.8.0-dev`.
Its references retain build commit `0d9cc5404308f4e73909074069629b05be5510f1`,
with 141 commands, 99 API operations, 42 record types and 31 required documents.
Its notes render the estate's records with `--from 0.7.3 --to 0.8.0`. The set was first frozen at
`462dd57`; after FIX-31 it was prepared again at that fix's merge commit and frozen again, replacing
only its own checksum entries, which no release asset had contained. It was frozen before publication,
so its conditional publication and NuGet wording remains historical; every file's checksum is in
`frozen-sha256.json`. The release also carries CI-02, which retries a failed test once in CI and the
release workflow and reports the retry, and FIX-33, which pins the release's SBOM tool at a specification
its components report reads; both followed the freeze and changed no build source, so the set and its
notes name neither. The estate's release record establishes publication and platform qualification.

The [0.7.3 set](0.7.3/README.md) documents FIX-19, UPDATE-01, FIX-20, FIX-21 and FIX-22 after published 0.7.2:
independent worker workspace repositories and sandboxed Codex commits; a disclosed, machine-local daily
check for newer releases; broader credential and write recognition in the command audit; safe concurrent
use of loaded and rotated runtime certificates with handshake diagnostics; and compact, current required
attention across surfaces. Its notes render the estate's records with `--from 0.7.2 --to 0.7.3`. The set was frozen for its release build before publication, so its conditional publication and NuGet wording remains historical; every file's checksum is in `frozen-sha256.json`.
The estate's release record establishes publication and platform qualification. Its source build retained
`0.7.3-dev`; the set and its checksums remain unchanged.

The [0.7.2 set](0.7.2/README.md) documents an earlier published release, published on October 17, 2026 at tag `v0.7.2`, commit `d1363d92f3cc5cae84a65787326897929582fce9` (estate release record `01M55S0Q6H5ZFNA4DG3JDBCEE3`), with FIX-13, FIX-14, FIX-15, FIX-16 and FIX-18 after published 0.7.1: ordered identifiers, exact-commit release document checks, grouped notes, settled worker attention, workflow policy checks and a bounded Workbench first screen; inventory-bound export restore (F-01); claimed release dispatch and bounded Git/gh processes; validation before record publication and visible unreadable journals; and the corrected Codex worker path at max effort. Its notes render the estate's records with `--from 0.7.1 --to 0.7.2 --since 2026-10-17T05:25:00Z`, with the operator's approved Security section on F-01 first. The estate's release record establishes publication and qualification; this set does not. Earlier sets and their checksums remain unchanged. The set is frozen for its release build, with every file's checksum in `frozen-sha256.json`.

Vericairn's documentation sets start at 0.2.0, one directory per version under `docs/releases/<version>/`; from 0.3.1, every published release also carries its set as a release asset. A set is written for the release it documents and then frozen: nothing in a released set is edited afterwards, and a correction goes into the next version's set with a note that says what it corrects. The maintained documents at `docs/` are the drafts for the next release; the frozen copies in a set record what was prepared for that version, including candidates withdrawn before publication.

`frozen-sha256.json`, outside the version directories, indexes every file of each frozen set by SHA-256. The test suite refuses changed, missing or added files in an indexed set. When a set freezes for its release build, add its complete file list and hashes to the index; keep those entries after withdrawal or publication. A current set under preparation is not indexed and remains editable until that freeze. Existing hashes are never regenerated to accommodate edits. FIX-09 restored only `0.6.0/cli-reference.md` to the bytes from commit `3d796f7`, undoing the accidental FIX-08 edit before recording its checksum. That restoration and the checksum guard are on main after the 0.6.1 tag; they do not change the published 0.6.1 set.

The [0.7.1 set](0.7.1/README.md) documents an earlier published release, published on October 17, 2026 at tag `v0.7.1`, commit `25e3bba824cf6b99fb43cc2c86f8937294331b44` (estate release record `01M54RPC299GWV2PY96PHD5J8N`). It follows published 0.6.1 and carries 0.7.0's work with FIX-12: a machine-local workspace root outside ancestors holding harness configuration, the current-holder check for every worker run, and credential scanning that excludes only validated public-key and signature encodings. It retains hand-over and resume, watched runs, personal communication settings, measured context, explicit execution, qualified inventories and human requalification, and the NuGet tool and SDK distribution. The 0.7.1 packages are available on nuget.org. The set was frozen before publication, so its conditional publication and NuGet wording remains historical; every file's checksum stays in `frozen-sha256.json`.

The [0.7.0 set](0.7.0/README.md) stays frozen and indexed in `frozen-sha256.json`. Its candidate was withdrawn before publication under decision `01M53S6DYFCPJMG0AQRSN8ZFSP`, recorded by withdrawal `01M53S6FN1GANBY5HSNDKE5N08`, following review `01M53R2Y38J97MNSEWNK1965XA`. It is not a published release or an upgrade target. Its draft remains unpublished; neither the set nor its checksums are rewritten to correct its historical wording. Release 0.7.1 carries the same work with FIX-12 and corrected documentation, as 0.6.1 replaced withdrawn 0.6.0. From the 0.7.0 set, `documents.json` also requires the communication adoption guide and qualification report.

The [0.6.1 set](0.6.1/README.md) documents an earlier published release, published on October 16, 2026 at tag `v0.6.1`, commit `6923d55dcdca184d33092aeda209561c4bbcb44d` (estate release record `01M51PADA7DHK0A0YS97ZVJNDT`). It follows published 0.5.2. It covers Community goals and evaluations, the worker and coding-agent launchers, roster routing and asks, the progress report, and pairing with a runtime over the local network, with FIX-08's corrections to settled run facts, committed-path auditing, loopback body reads, pairing-code revocation and release-note classification. Its notes are rendered by 0.6.1's own command line and begin where 0.5.2's notes ended. The set was frozen before the build, so its overview still describes acceptance and publication as future steps; the estate records their completion, and the frozen wording is retained.

The [0.6.0 set](0.6.0/README.md) stays frozen. Its candidate was withdrawn before publication after the Reviewer principal's independent review (withdrawal record `01M51CE4J8Q2XR92Z8M2EPZF13`, under decision `01M51CDST3GS2CDFXD22Z3NKJP`); it is not a published release. 0.6.1 carries the same work with FIX-08 and corrected documentation. Both sets keep the two record-format documents under their own names, as 0.5.2 did; from 0.6.0 `documents.json` explicitly requires both alongside `record-format.md`.

## NuGet publication setup

The 0.8.3 release event's Trusted Publishing run, [run 40102325701](https://github.com/Vericairn/vericairn/actions/runs/40102325701),
pushed all eight packages with their symbol packages at 0.8.3. The 0.8.2 release event's Trusted Publishing run, [run 39770313785](https://github.com/Vericairn/vericairn/actions/runs/39770313785),
pushed all eight packages with their symbol packages, and each lists 0.8.2 on nuget.org:
`dotnet-vc`, `Vericairn`, `Vericairn.Daemon`, `Vericairn.Mcp`, `Vericairn.Protocol`, `Vericairn.Records`,
`Vericairn.Runtime` and `Vericairn.Sdk`. Installation of 0.8.2 is available. 0.8.0's own run,
[run 39415232377](https://github.com/Vericairn/vericairn/actions/runs/39415232377), published 0.8.0 the same way.
The recovery below belongs to 0.7.1's first publication, not to the 0.7.2, 0.7.3, 0.8.0, 0.8.2 or 0.8.3 events.

On October 17, 2026, [run 39028643979](https://github.com/Vericairn/vericairn/actions/runs/39028643979)
of `nuget.yml` pushed `dotnet-vc` and the seven Vericairn packages at 0.7.1, with their produced symbol packages,
to nuget.org through Trusted Publishing. Installation of 0.7.1 is available. The release event's own run could
not start because the organization allowed only GitHub's own actions. The operator allowed
`NuGet/login@8d196754b4036150537f80ac539e15c2f1028841` and ran the workflow by hand for the published,
immutable release. [Pull request 95](https://github.com/Vericairn/vericairn/pull/95) added that recovery path
after the release tag; it changed no release asset or frozen documentation set.

For 0.7.1 and later, before publishing the accepted draft, the operator configures nuget.org Trusted
Publishing with repository owner **Vericairn**, repository **vericairn**, workflow file **nuget.yml**, and
the **Vericairn** organization as package owner. Set the GitHub repository variable **NUGET_USER** to
Jason Furr's nuget.org user name, not the organization name. The organization's Actions policy must also allow
the pinned `NuGet/login` action. No stored API-key secret is needed or used.

The draft already holds the release's `.nupkg` and `.snupkg` assets under `SHA256SUMS`. Publishing the
release triggers `nuget.yml` to verify and push those exact files; it never rebuilds them. Check that run
and package availability after the first publication. If the release event could not run the workflow, for example because an
organization setting refused one of its actions, a person may run it by hand for that published, immutable
release, `gh workflow run nuget.yml -R Vericairn/vericairn -f release_id=<release id>`, which verifies and pushes
the same assets. A withdrawn candidate stays off NuGet while its draft remains unpublished. The workflow runs for any published release, including a prerelease, and verifies packages against that release's own `SHA256SUMS`; the operator binds the upload to estate acceptance by publishing exactly the accepted draft. Existing package versions are skipped, never replaced. Details are
in the [publication draft](../software.md#vericairns-nuget-publication); the [0.8.0 installation guide](0.8.0/install.md#9-nuget-command-line-and-sdk)
contains the tool and SDK package commands. The NuGet tool remains framework-dependent and includes neither
the macOS Secure Enclave helper nor Workbench.

## What a set holds

| Document | Written by | What it is |
|---|---|---|
| `README.md` | a person | The overview: what the release is, what the set holds, how to tell you are running it |
| `install.md` | a person | Installation and cold start on Linux, macOS and Windows: the estate bootstrap, verified direct download, and from 0.7.0 the NuGet tool and SDK |
| `operator-guide.md` | a person | The operator's journeys: create an estate, devices and admission, the work loop, review and acceptance, releases, upgrades, recovery, the daemon |
| `cli-reference.md` | the build tooling (`vericairn-docs generate`) | Every command and verb with its arguments and options, from the build's own command tree |
| `api-reference.md` | the build tooling (`vericairn-docs generate`) | Every API operation with parameters, bodies and responses, from the OpenAPI document the daemon serves |
| `record-reference.md` | the build tooling (`vericairn-docs generate`) | Every record type with its subject rule and body fields, from the record type registry |
| `record-format.md` | a person | The index relating the frozen format documents and the release's compatibility |
| `record-format-v1.md`, `record-format-v2.md` | a person, frozen copies | The base format and format 2's changes, both required by the declaration from 0.6.0 |
| `record-format-v3.md` | a person, frozen copy | From 0.8.0, policies, duties, generated collections, checked changes and the forward migration |
| `api.md`, `api-v1.md` | a person, frozen copies | The API document as released: transports, outcomes, problem codes; both names are required from 0.8.0 |
| `software.md` | a person, frozen copy | The software journey as released: the git adapter, checks, the release provider |
| `changes.md`, `operator-actions.md` | a person, frozen copies | From 0.8.0, checked repository changes and the operator's signed one-record journey |
| `workbench.md` | a person, frozen copy | Workbench as released: what exists, the keyboard, running it, the probe, limitations (declared from 0.4.0) |
| `mcp.md` | a person, frozen copy | The MCP server as released: protocol subset, tools, running it, qualification, limitations (declared from 0.4.0) |
| `required-attention.md` | a person, frozen copy | Included from 0.7.3 and required from 0.8.0: exact resolution rules, portable status and local worker observations, including unknown cases |
| `communication-adoption.md` | a person, frozen copy | The first-party operator's explicit personal-setting adoption procedure (declared from 0.7.0) |
| `communication-qualification.md` | a person, frozen copy | The tested communication/context surfaces and deferred qualifications (declared from 0.7.0) |
| `client-operations.md`, `api-compatibility-0.8.0.md`, `api-compatibility-0.8.2.md`, `api-compatibility-0.8.3.md` | a person, frozen copies | From 0.8.0, cancellation, deadlines, uncertain writes and source/binary compatibility changes; from 0.8.2, also that release's interface changes, which the withdrawn 0.8.1 set lists as `api-compatibility-0.8.1.md`; from 0.8.3, that release's interface additions |
| `cli-client-outcomes.md` | the build | From 0.8.0, the generated interrupted-request excerpt with retained input provenance |
| `policies.md`, `policy-evaluator.md` | a person, frozen copies | From 0.8.0, declarations, writing exceptions, recurring duties, evaluation and release admission |
| `policy-adoption.md`, `policy-adoption-proposals.md` | a person, frozen copies | From 0.8.0, explicit adoption, exact proposed definitions and unresolved operator inputs |
| `readable-companions.md`, `reference-inventory.md` | a person, frozen copies | From 0.8.0, generator contracts, reading surfaces and the inventory/coverage contract |
| `reference-inventory.json`, `reference-index.md`, `inputs/` | the build | From 0.8.0, exact reference membership, explicit register mappings and retained generator inputs |
| `release-notes.md` | `vericairn docs notes` | What changed, from the estate's records since the previous set's notes ended (before 0.6.0, since the previous publication) |
| `limitations-and-platforms.md` | a person, from the release record | Remaining limits, signing and expected artifacts; the release record supplies authoritative hashes and platform qualification |
| `manifest.json` | the build tooling (`vericairn-docs generate`); its `notes` by `vericairn docs notes` | The set's version, commit, generator, the documents the product declares, commands, operations and record types; from 0.6.0 also the record formats the build reads and writes (`recordFormat`) and the window of records the notes cover (`notes`) |

## How a set is produced and checked

For 0.8.0, commit the version first, then reconcile and commit the maintained guides
and required-document declaration. Generate the references from that committed
Release build, retaining their source pin, complete inputs and generated bytes.
Copy the maintained guides byte for byte, add the release overview, and commit the
set before updating this index. The CLI client-outcome excerpt is generated with
the references; its maintained copy retains those same bytes. The builder renders
the notes from the estate's records, adds the notes window to the manifest, then
freezes the complete set by checksum before the release build. Publication and
qualification remain established by the release record and its evidence.

The 0.6.1 and 0.7.0 preparation sequence commits the version and index first, builds that commit, and generates references with `--no-build --configuration Release`. Maintained drafts are then made current and committed, followed by their byte-for-byte copies and the authored guides. The manifest's commit is the generated references' provenance, not the later documentation or final release commit. After FIX-11, the 0.7.0 references were regenerated with the same command from the Release build of merge commit `5dbc5022ae4878bc53e7818d4d788cd1a739aeff`, which includes main at `447ddc8`; the maintained and frozen drafts and guides were then reconciled with that code. For 0.7.0 the release builder rendered the notes from the estate's main after the final documentation round, then froze the set and indexed its checksums without changing any earlier entry.

For local suite validation, use a short temporary directory outside any Git checkout. Fixtures create their own repositories and instruction files; putting their temporary root inside the product checkout makes otherwise empty fixtures inherit that repository and its ancestor instructions. On macOS, a short `TMPDIR` also keeps daemon socket names within the platform limit. The runner and daemon tests need local IPC/socket access.

1. Before the release build, from the Release build of the version commit (or the subsequent correction merge described above): `dotnet run --no-build --configuration Release --project tools/Vericairn.DocsTool -- generate --out docs/releases/<version> --documents docs/releases/documents.json` writes the three references and the manifest from the build (from 0.4.0 the generator is build tooling, not part of the shipped command line, and the documents every set holds are declared in `docs/releases/documents.json`; 0.4.0 declares `workbench.md` and `mcp.md` beside the eleven of earlier sets; from 0.6.0 the manifest also states the record formats the build reads and writes, which `vericairn release observe` reads from the draft's documentation archive and records on the candidate as the release's own); `vericairn docs notes --estate <estate> --from <previous> --to <version> --out docs/releases/<version>` writes the release notes from the estate's records since the notes of the previous set, `docs/releases/<previous>`, ended, and records their window in the manifest as `notes: {from, to}`: the records dated after `from` and up to `to`. The next set begins at this one's `to`, so no record falls between two sets; before 0.6.0 the notes began at the previous publication, so the records between the previous set's notes and that publication fell into neither set (the 0.5.2 notes list by hand those between the 0.4.2 notes and the 0.4.2 publication). For a previous set that records no window (0.5.2 and earlier) the notes begin at the end its `release-notes.md` states, which is to the minute, so a record of that minute may be in both sets; with no previous set beside this one the notes begin at the previous publication; and `--since <timestamp>` may name an earlier beginning to include records the previous set missed. The manifest records that start and the introduction explains the overlap. A later start is refused, as is a previous set whose end cannot be read, since neither can guarantee a gap-free window. Before the build the version's own publication is not yet recorded, and the notes say so; they end at the second before they are generated, so a record of that second falls in the next set (the 0.2.0 notes were generated after the fact between two publications). A window holds the records dated inside it that the estate held when the notes were generated, so a record that reaches the estate later with an earlier date, from a clone merged after the notes, needs an explicitly widened next window. The 0.7.1 notes use `--from 0.6.1 --to 0.7.1 --since 2026-10-16T04:29:42Z` to include all nine records dated 04:29–04:34 UTC that 0.6.1's source clone lacked. The start is exclusive, so it is one second before the record dated exactly 04:29:43Z that 0.7.0's window missed; see the [software journey](../software.md). The authored documents are written and the frozen copies taken.
2. `vericairn docs check docs/releases/<version> --documents docs/releases/documents.json` refuses the set when a document the manifest or the declaration requires is missing or empty, when the manifest drops a declared document, when the command-line, API or record reference lacks a heading for anything the manifest lists, or when the overview does not name the version; `dotnet run --project tools/Vericairn.DocsTool -- compare docs/releases/<version>` refuses it when the manifest disagrees with the build's command tree, API document, record types or, from 0.6.0, record formats. Every gap is listed at once.
3. The release workflow runs both before it builds a draft, so an incomplete set refuses the release; it then packages the set as `vericairn-<version>-docs.zip` with the other assets, listed in the release record by hash.
4. The websites publish the sets once they exist (milestone M4); until then the set lives here and as the release asset.

The 0.2.0 set was written on October 13, 2026, after the release day, from the 0.2.0 binary and the estate's records; its overview says so. From 0.3.0 the set is produced before the build, and a set may carry further frozen copies beyond the required documents (0.3.0 adds `workbench.md` and `mcp.md`). Because the set is frozen before the build, its overview states the acceptance, the publication and the cold start as the steps that follow the freeze, never as past facts; the 0.3.0 overview put them in the past tense, which its release record notes as a limitation. Release 0.3.0 was withdrawn before publication (Workbench did not open an estate on a first launch); its set stays frozen as the record of what was built and reviewed, and 0.3.1, the corrective release built on the release/0.3 branch from the 0.3.0 commit and the fix, carries its own set with the release notes running from 0.2.0, the last published release. Release 0.4.0 was likewise withdrawn before publication, after its independent review found that the Workbench archives lacked the license texts of the fonts Workbench embeds; its set stays frozen, and 0.4.1 carries the same work with every bundled component's license texts and its own set, its notes running from 0.3.1. Release 0.4.1 was withdrawn in turn, after its review found the third-party notices of the ASP.NET Core shared framework missing from the archives; 0.4.2 carries them, its set frozen like the others and its notes running from 0.3.1. Release 0.5.0 is the first that writes record format 2, whose document describes only what differs from format 1: its set carries both documents as frozen copies under their own names, `record-format-v1.md` and `record-format-v2.md`, so that the links between them resolve, and its `record-format.md` says which rules apply to which records. Its notes run from 0.4.2. Release 0.5.0 was withdrawn before acceptance and publication, after its independent review found that the first-authority rule bound signed admissions only, so that while a charter left signatures optional an unsigned admission still admitted a device; 0.5.1 carries the same work with every admission of record format 2 signed, its set frozen like the others, carrying both record-format documents in the same way, and its notes running from 0.4.2. Release 0.5.1 was withdrawn in turn, after its review found that its correction rested on the format each record claims, so that an unsigned admission written in format 1, or dated before a required-signature policy, still counted; 0.5.2 rests it on a signature, an unsigned admission counting in an estate at format 2 only when the operator's signed move to format 2 lists it, and its set, frozen like the others, runs its notes from 0.4.2.
