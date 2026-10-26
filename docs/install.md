# Installing Vericairn 0.8.4

The 0.8.4 workflow builds self-contained distributables per platform: no separately installed .NET runtime is required. The operating system's native libraries are still required, and Workbench needs a desktop session. There are two distributables per platform: `vericairn-0.8.4-<platform>` holds the command line (`bin/vericairn`, and on macOS also `bin/vericairn-enclave`, the helper the command line calls for Secure Enclave custody), and `vericairn-workbench-0.8.4-<platform>` holds the desktop (`bin/vericairn-workbench`) with a copy of the command line beside it, because Workbench drives the daemon through that command line. `git` is needed for the commands that read or adopt repositories and for a repository-synced estate; the GitHub CLI `gh`, logged in as you, is needed for release and checked-change providers and for observing workflow runs, because Vericairn never holds a token of its own. On Linux, secret-service custody needs `secret-tool` (from libsecret) and a running secret service such as GNOME Keyring; without them device keys are kept in owner-only files.

The archives install `vericairn`. Introduced in published 0.7.1, the NuGet tool installs `dotnet-vc`, canonically invoked as `dotnet vc`; the two command lines take the same estate arguments. Workbench is installed by direct download only (section 3). The SDK is a separate NuGet package (section 9).

Use the 0.8.4 archives after the operator accepts and publishes the release. NuGet installation goes live when that publication triggers the Trusted Publishing workflow and it succeeds; package availability follows that workflow, not installation of a local build. The `0.0.1-reserved` packages reserve names and are not the product. Withdrawn candidates 0.5.0, 0.5.1, 0.6.0, 0.7.0 and 0.8.1 are not installation targets.

0.8.4 follows published 0.8.3 with declarative document templates and their sections, reviews and
cohesion (TPL-01, TPL-02 and TPL-COMMS-01), a shared library of saved prompts (PROMPT-01), a signed
`operator close` action (OPS-02) and durable operator requests (OPS-03), a release-review brief
generated from the records and run as an assignment (CODE-09), a guided decision from an answered ask
(CODE-10), a release documentation pass (DOCS-12), read-only MCP resources and prompts (MCP-02), the
first-party bootstrap for every estate (FIX-68), and FIX-64, FIX-65, FIX-66, FIX-69 and FIX-72, with
time limits on CI steps and jobs (CI-05). It also hardens how a checked push reads its local objects
(FIX-67), as the release notes' Security section describes.
0.8.3 followed published 0.8.2 with `estate init` pinning the running build's exact source commit
(FIX-47), work orders created from flags (CODE-07), candidate withdrawal and moving planned work
(CODE-08), verified pins of a published Vericairn release (FIX-52), a submission check that records
nothing (FIX-55), the same library builds in every platform's archive (FIX-61), a warning when an older
background runtime still serves an upgraded estate (FIX-62), FIX-50 and FIX-56 through FIX-59, CI that
runs each platform's published command line (CI-04), and a correction to how a checked push runs git
(FIX-60). Earlier published releases, and the withdrawn candidates 0.5.0, 0.5.1, 0.6.0, 0.7.0 and
0.8.1, are recorded in their own frozen sets.
0.8.4 reads record formats 1, 2 and 3 and writes the estate's own format, at most 3; an estate at format 3
upgrades from 0.8.0, 0.8.2 or 0.8.3 without a format step. The format-3 upgrade from 0.7.3 preserves
historical records and requires explicit adoption afterwards. 0.8.3's, 0.8.2's and 0.8.0's features and
earlier corrections remain. Earlier sets stay frozen.

The repository adapter and release provider share one bounded runner: 30 seconds per adapter Git
command, two minutes per provider gh command, and
4,194,304 characters per stdout or stderr stream. The deadline includes launch, concurrent output reads
and exit. Overflow fails instead of accepting partial output. Timeout or cancellation terminates the
owned POSIX process group or Windows Job Object, including descendants holding output pipes. Cleanup
waits at most one additional second; unconfirmed exit retains native ownership for later cleanup.
On POSIX the runtime must run under an init process, not as PID 1, without ignored `SIGCHLD`,
`SA_NOCLDWAIT` or a separate child reaper. Unsupported hosts refuse before launch. Process-group escape
and the existing containment limits still apply.

## 1. Route A: as part of an estate, through its bootstrap

From 0.8.4, `vericairn estate init`, with or without `--release`, writes `bootstrap` and
`bootstrap.ps1` at the estate root and names both in its receipt. These are the first-party scripts
embedded in the installed Vericairn command line, from `src/Vericairn.Cli/Bootstrap/` in the product
repository. Every release from 0.8.4 carries them. Both use LF line endings; `bootstrap` is executable
on Linux and macOS. The same commands add `bootstrap text eol=lf` and `bootstrap.ps1 text eol=lf` to the
estate's `.gitattributes`, appending only missing lines, because Git for Windows converts text to CRLF on
checkout by default, which would break the POSIX script. Commit the scripts and `.gitattributes` with the
estate so a fresh clone can use Route A.

For an existing estate, use an installed command line from 0.8.4 or later:

```sh
vericairn --estate <estate root> estate bootstrap
```

The command reports each script and `.gitattributes` as `written` or `unchanged`. It checks both files before writing and
refuses to replace a differing copy unless you add `--force`; preserve any estate customizations
before doing so. It restores the POSIX script's executable permission even when its bytes are
unchanged. With `--json`, the `bootstrap` array gives each file's `path` and `status`. This refresh
uses the installed command line's copies and leaves the manifest's pin in place.

The scripts read `estate/manifest.json`. A release pin supplies the version and SHA-256 of every
distributable; the bootstrap fetches that release, verifies it and runs it. A source pin builds the
exact source commit as described below. A newly initialized estate can use the same commands as a clone.

Linux and macOS:

```sh
git clone <the estate repository> my-estate
cd my-estate
./bootstrap estate check
./bootstrap status
```

Windows, in PowerShell 5.1 or PowerShell 7:

```powershell
git clone <the estate repository> my-estate
cd my-estate
./bootstrap.ps1 estate check
./bootstrap.ps1 status
```

What the bootstrap does, step by step:

1. Reads `estate/manifest.json`. In release mode it finds the pinned version and the hash recorded for this machine's platform (linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64 or win-arm64).
2. Fetches `vericairn-<pinned version>-<platform>.tar.gz` (or `.zip` on Windows) from the release page, unless `VERICAIRN_RELEASE_BASE` names another directory or URL to fetch from.
3. Computes the archive's SHA-256 and refuses to continue when it differs from the manifest. The hash comes from the manifest, never from the tag name alone.
4. Unpacks the archive under `.vericairn/toolchain/release/<pinned version>/<platform>/` inside the clone and remembers the verified hash beside it so later runs skip the fetch. Keep `.vericairn/` in the estate's `.gitignore` so these local downloads stay out of commits.
5. Runs `bin/vericairn --estate <the clone> <your arguments>`; with no arguments it runs `estate check`.

Expected observation: `estate check` ends with a line like `N records: 0 errors, 0 warnings`, and `status` renders the estate: its work orders, releases, assignments, decisions, documents and the living documents that are due. The bootstrap fetches the version its manifest pins, chosen by `estate init --release` or a recorded upgrade.

If the manifest pins a source commit instead of a release (the default for `estate init`), the bootstrap builds Vericairn from that commit and needs the .NET SDK named by the source's `global.json`; set `VERICAIRN_SOURCE` to a clean local clone at that exact commit to build from it instead of fetching.

## 2. Route B: direct download of the command line

1. After publication, from https://github.com/Vericairn/vericairn/releases/tag/v0.8.4 download the distributable for your platform and `SHA256SUMS`.
2. Verify the download. Linux: `sha256sum -c SHA256SUMS --ignore-missing`. macOS: `shasum -a 256 -c SHA256SUMS --ignore-missing`. Windows PowerShell: `(Get-FileHash .\vericairn-0.8.4-win-x64.zip).Hash` and compare with the line in `SHA256SUMS`. The expected hashes are in the estate's published release record, which is the authority: a tag can be moved, a record cannot.
3. Unpack: `tar -xzf vericairn-0.8.4-<platform>.tar.gz -C <directory>` on Linux and macOS; `Expand-Archive` on Windows. The archive holds `bin/vericairn` (with `bin/vericairn-enclave` beside it on macOS), `LICENSE`, `NOTICE`, the third-party notices and `licenses/` with the license and notice texts of every component the command line bundles.
4. Run `<directory>/bin/vericairn version` (`bin\vericairn.exe` on Windows). It prints `0.8.4+<commit>`.
5. Put `bin` on your `PATH` if you want to type `vericairn` alone. Every command takes `--estate <path>`; without it the current directory is the estate. Keep `vericairn-enclave` beside `vericairn`: the command line looks for the helper next to itself, and without it Secure Enclave custody is unavailable.

Platform notes, stated honestly because the build is unsigned:

- macOS may refuse to run a binary that a browser downloaded, because it carries the quarantine attribute and is not notarized. Downloading with `curl` avoids the attribute; otherwise remove it with `xattr -dr com.apple.quarantine <directory>` after verifying the checksum, which covers the helper too. The estate bootstrap fetches with `curl`, so Route A does not hit this. The helper uses CryptoKit's Secure Enclave keys, which need no entitlement and no developer signature; when the Mac does not expose a usable Secure Enclave, `vericairn doctor` reports it unavailable and new keys use file custody. The helper is built for macOS 13.0 or later; availability is probed at runtime, including in virtual machines.
- Windows SmartScreen may warn about an unsigned program the first time. PowerShell may require `-ExecutionPolicy Bypass` to run `bootstrap.ps1` when the policy is restricted.
- Linux: `tar` preserves the executable bit. Workbench uses X11 or XWayland; a headless machine can use the command line. Secret-service custody needs the service and `secret-tool` described above.

## 3. Workbench, the desktop

1. From the same release page download `vericairn-workbench-0.8.4-<platform>.tar.gz` (`.zip` on Windows) and verify it against `SHA256SUMS` as above.
2. Unpack it to a directory of its own. It holds `bin/vericairn-workbench` and `bin/vericairn` (with `.exe` on Windows, and with `bin/vericairn-enclave` on macOS), `LICENSE`, `NOTICE`, the third-party notices and `licenses/` with the license and notice texts of every bundled component, the fonts' among them.
3. Run it:

```sh
<directory>/bin/vericairn-workbench --estate <estate root>
```

Workbench opens the estate's root (the directory with `estate/manifest.json`), connects to the daemon that serves it, and starts one through the command line beside it when none answers; nothing runs in the daemon's place inside Workbench. Without `--estate` it shows the first-run page, where the root is typed or chosen from the recent estates. `--as <principal id>` names the principal Workbench acts for: the selector in the target bar then shows that principal's admitted device held on this machine, the one the runtime signs with, and choosing another device in the selector changes the acting principal. When the named principal has no device held here, the target bar says whether its writes are refused (the estate requires signatures) or written unsigned. Without `--as`, Workbench chooses among the admitted devices held here and says when it is read-only. `VERICAIRN_CLI` names another command-line executable to drive; by default the one beside Workbench is used, then one on the `PATH`. A device whose key requires presence asks for Touch ID or the login password when the runtime signs with it. Automated tests do not establish the interactive presence experience; consult the estate's platform qualifications (`limitations-and-platforms.md`).

Workbench is an unsigned developer build with no application bundle, installer or signature. The same checksum and platform checks apply as for the command line. Headless tests cover its work loop, releases, documents, Goals workspace and progress report; native desktop and screen-reader qualification needs separate observations for each platform. See `limitations-and-platforms.md` and `workbench.md`.

FIX-13 reduces Workbench's first-screen work by sharing status and progress observations, avoiding a
duplicate refresh after daemon startup and reusing parsing and hashes for unchanged original bytes.
First-screen API reads no longer grow with unshown record history, but opening still validates every
record once. Large estates can still take time. Wait for loading before treating an absent page as empty;
headless tests establish no fixed native startup-time bound or desktop responsiveness qualification.

## 4. The MCP server for coding agents

A coding agent reaches an estate through `vericairn mcp serve`, a server over standard input and output for compatible MCP harnesses. Start the daemon first when several clients will share the estate. The server uses it when available, or holds an embedded runtime for its session otherwise; the adapter holds no key of its own, and its write tools exist only for a principal whose admitted device key this machine holds:

```sh
vericairn --estate <estate root> mcp serve --as <principal id>
```

Claude Code: `claude mcp add vericairn -- vericairn --estate <estate root> mcp serve --as <principal id>`. `vericairn mcp orientation --out <directory>` writes the orientation file an agent reads first (`AGENTS.md`, generated from the estate's identity and the tool registry, with `CLAUDE.md` and `GEMINI.md` as pointers), and `--check` says whether existing copies are current; `mcp.md` in this set has the protocol subset, the tools and the limitations. An agent's device never needs its owner's presence, so an agent signs without a prompt.

Optional charter signatures do not waive record-specific requirements. Policy and writing changes,
duty results/reviews, strict generated declarations and the operator one-step action still require
their admitted signatures and exact authority.

## 5. After installing

`vericairn doctor` reports the version, the runtime, `git` and `dotnet` if present, the estate it sees, the key custody a new device gets by default in the estate it sees, whether Secure Enclave custody is available on a Mac and why not when it is not, the record formats the build reads (1, 2 and 3) and the newest it writes (3) and whether a daemon serves the estate. Per-user state (device keys or, for enclave keys, their enclave-wrapped blobs, each estate's live store and daemon endpoint, logs, Workbench's layout preference) lives in the per-user application-support directory `doctor` prints; set `VERICAIRN_HOME` to use another. A device key created in an estate still at record format 1 is kept in the strongest custody format 1 can name (the platform key store on Windows, the secret service on Linux where one runs, otherwise an owner-only file); the Secure Enclave and presence need the estate at record format 2 or later (section 6), after which `vericairn devices rotate <device> --custody secure-enclave` moves a key there.

Nothing runs in the background until you ask: commands run the embedded runtime unless a daemon serves the estate (`vericairn runtime status` says which). A daemon started by `vericairn runtime start` (or by Workbench, through that command) writes its output to `daemon.log` beside the estate's live store, never to the terminal or the program that started it. When the daemon stops before it serves, `runtime start` exits with an error that quotes what the daemon wrote to its log and says where the log is. And `vericairn runtime install --plan` shows what installing the daemon as a per-user service would do before `runtime install` does it; Workbench's runtime workspace shows the same plan before it installs or uninstalls. The operator guide covers both.

## 6. Pinning and upgrading a published release

From 0.8.3, the published-dependency commands below let an estate other than Vericairn's own pin a
published release without authoring a product release record.

Install and checksum-verify the target command line by Route B. If a daemon serves the estate,
restart it with the same target build before checking or applying the upgrade. From the estate
directory on the daemon's machine, run `vericairn runtime stop`, then `vericairn runtime start`.
Restart workers after their active work has settled. Installing a binary alone does not move an
estate's pin or replace a running process.

```sh
vericairn estate upgrade --to <published-version> --check
vericairn estate upgrade --to <published-version> --by <your principal id>
```

The command uses a recorded Vericairn release when one exists. Otherwise it anonymously
reads the target's GitHub release, its tag and `SHA256SUMS`, downloads this machine's platform archive
and verifies its SHA-256. It checks the tag's commit against the target build's full source commit
before and after downloading. The target build supplies the record formats it reads and writes.
A draft, prerelease, changed tag, missing checksum file, mismatched archive, unreadable record format
or older writable format refuses without changing the pin or writing an observation or migration.
The refusal names the next action. An unavailable network asks for a retry or the offline inputs below.
These explicit downloads use the release check's anonymous transport rules: no credentials, cookies,
proxy or trace propagation; asset redirects are limited to GitHub's HTTPS download hosts.

`--check` writes nothing. Applying creates a `vericairn-release` observation and a migration record,
then moves the manifest to release mode, pinning that exact observation by hash and carrying the
published archive hashes. A source pin takes the same path; the migration names both pins. The
observation states that the builds are unsigned developer builds and that only the current platform's
CLI archive was verified. Review and commit the estate change. The bootstrap's release mode stays the
same: it verifies the downloaded archive against the manifest hash.

For a fresh estate, run the published build with `--release`:

```sh
mkdir my-estate && cd my-estate && git init
vericairn estate init --release --name "My Venture" --slug my-venture \
  --purpose "What this estate exists to do." \
  --operator-name "Your Name" --organization "Your Organization"
vericairn status
```

Verification finishes before initialization writes anything. The new estate has an operator, a
charter and the dependency observation, with a release-mode manifest. `--profile local-only` supports
the same path. Without `--release`, initialization needs no network and pins the running build's
source commit; `--commit <40-hex-commit>` can override that source pin. Development builds without
an exact recorded commit require `--commit`. A release pin requires a stable published target build,
and refuses a different `--repository` or `--commit` override.

### Offline inputs

Add `--from <download-directory>` to either upgrade command, or to `estate init --release`.
The directory is read on the runtime's machine, so copy it there when a daemon serves the command.
It must contain the downloaded files below, from one release; their contents are never authored
as release records. The same verification runs without contacting GitHub or changing these files.

| File | Download source |
| --- | --- |
| `release.json` | GitHub API `repos/Vericairn/vericairn/releases/tags/v<version>` |
| `tag.json` | GitHub API `repos/Vericairn/vericairn/git/ref/tags/v<version>` |
| `tag-<sha>.json`, when the tag reference points to a tag object | GitHub API `repos/Vericairn/vericairn/git/tags/<sha>`; include each annotated object until it points to a commit |
| `SHA256SUMS` | The release's download assets |
| `vericairn-<version>-<platform>.tar.gz` (`.zip` on Windows) | The same release's platform archive |

The API addresses start with `https://api.github.com/`. Save their response bodies unchanged.
Checksums and an archive alone cannot establish a release's published status or its tag commit.
Offline verification checks the supplied tag snapshot twice; it cannot establish whether GitHub
changed after those responses were downloaded. Refresh the responses when current publication
knowledge is needed. The builds remain unsigned in both paths.

An estate running 0.8.0 or 0.8.2 at format 3 applies no format step. From 0.8.2 (FIX-38) the upgrade appends its
migration record before it replaces the manifest, and replaces it atomically: an upgrade that stops
between the two resumes from that migration record when run again, and until then opening the estate
warns `upgrade.incomplete`. A legacy release pin with no migration record is refused; a fresh `init --release` pin has its exact dependency observation instead.

An estate running 0.7.3 at format 2 applies `record-format-3`. The plan names that step, the
published target and its artifact hashes. Applying it appends the migration record and changes the
manifest's release pin and `recordFormat` to 3 after the steps succeed. Existing record bytes, hashes,
signatures and original format rules are preserved. Reapplying the installed target makes no change;
a failed step or refused migration record restores the previous repository files. Downgrading the
format is refused. A format-2 runtime refuses the upgraded manifest before opening a writable estate.

Stop older runtimes and update every participating writer before using format-3 records. Installing
a binary alone neither changes the estate pin nor migrates records. A clone that has not received
the new manifest still has its old view, so coordinate synchronization and restart explicitly.
Device keys and their custody do not move with the upgrade.

An estate still at format 1 first applies `record-format-2`, then `record-format-3`. The first step
lists every unsigned admission present, including superseded admissions and revocations, by id and
hash. At format 2 or 3 such an admission counts only when that signed migration anchors it. Where
anything needs anchoring, the operator or a principal holding `administer` applies the upgrade with
an admitted signing device; `--check` shows the list first. No original record is rewritten.

The migration infers no policy, duty, writing entry, placement, authority or completed work. The
operator still approves exact policy definitions and adoption bodies, grants the builder's bounded
writing authority, and authorizes the declarations. The first-party policy proposals retain unresolved
publication, grant, carrier and gate inputs; complete and approve them before authoring. Readable
register adoption follows the upgrade and policy declarations. A legacy generated lineage also needs
its independently reviewed conversion; changing the manifest alone does not convert it.
See [record format 3](record-format-v3.md), [policy adoption](policy-adoption.md) and
[the adoption proposals](policy-adoption-proposals.md).


## 7. Preparing the worker

Install the chosen coding-agent harness separately and provision its login under the operating-system user that runs the worker. `vericairn worker launchers` lists `claude-code`, `codex` and `generic` with their settings, but generic dispatch is refused. The native baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`. Other exact versions require an authorized person's `worker qualify` record under RUN-03 and matching local probes. Codex's goals feature and the complete supported inventories are checked before dispatch. Qualification establishes the launcher protocol. The seven retained live Codex runs additionally exercise the specific path described below, without qualifying provider billing or other accounts.

Before a native run or an agent goal step, set that machine's model and effort defaults:

```sh
vericairn worker configure --launcher codex --model <model> --effort max
vericairn worker configure --launcher codex
```

Replace `<model>` with your chosen model selector; Codex accepts `max` in 0.8.4. Use `--launcher claude-code` with one of its supported efforts for Claude Code; model/effort compatibility is checked by the harness at launch. Setting requires both model and effort; omitting choices shows the configuration (`--json` is available). Repeated `--allow-model` and `--allow-effort` options narrow permitted choices and must include their defaults. With no model allowlist only the default model is permitted; with no effort allowlist the launcher's supported efforts are permitted. Each set replaces that launcher's defaults and allowlists. Configuration never widens the qualified inventory or adds tools, servers, environment variables or routes. The stock runtime derives its environment policy only after a baseline or exact current qualification admits the native inventory.

Configuration is per machine and is never recorded in the estate. Its owner-only files are `worker/codex.json` and `worker/claude-code.json` under the user's Vericairn home. Reads and writes are refused when that home is inside a Git repository or estate. If your home directory is a repository, set `VERICAIRN_HOME` to a machine-local directory outside all repositories and estates for the CLI and daemon before starting them. Both read the configuration for each run and agent goal step; changing its choices needs no daemon restart. Missing configuration refuses before intent and names the setup command. Installing or qualifying a harness alone supplies no model or effort default. Explicit run overrides cannot bypass local allowlists or inherited restrictions.

Harness qualification is machine-local as well: `POST /v1/harness-qualifications` refuses a paired local-network device before probes or record writes. The runtime's local socket and signed loopback retain the existing human authority checks. See section 18 of the operator guide and `software.md` for the complete setup and admission rules.

The default workspace is beneath the Vericairn home. If your home holds `.claude` or `.codex` configuration, the native scan refuses runs beneath it. FIX-12 lets you choose an absolute workspace root outside that home:

```sh
vericairn worker configure --workspaces <absolute-directory>
vericairn worker configure
```

Omit `--launcher` when setting or showing the workspace root. This separate setting applies to every launcher and lives in the owner-only `worker/workspaces.json` under the Vericairn home. Its path stays per machine and is never recorded. The root must be outside Git repositories (including bare repositories) and estates; neither it nor an ancestor may hold `.claude`, `.codex`, `.agents` or `.mcp.json`. The root itself cannot be a symbolic link or reparse point. Linked ancestors are resolved and the canonical root is stored. A new root is created owner-only. Invalid configuration, unknown fields and duplicate keys refuse, without fallback.

The CLI and daemon reread the root for every run and recheck before intent and dispatch, without a restart. New workspaces use `<root>/<estate>/<branch-slug>`, with branch slashes flattened to hyphens; the unconfigured default remains `<VericairnHome>/estates/<estate>/workspaces/<branch-slug>`. An existing independent workspace is reused only under the selected root. If it is elsewhere, preserve its work before removing that directory, or select a root containing it. A legacy linked worktree requires preservation and `git worktree remove` before retrying. Setting a root moves or deletes nothing. A root changed after intent refuses dispatch as an unstarted, uncharged failure.

Codex still separately inspects the user's own `~/.agents`. A run is refused while it supplies skills or other customization, even after moving the workspace and using a fresh `CODEX_HOME`. Use a worker account without that discovery root. The native scan permits an empty ordinary directory or recognized instruction files alone, but refuses other entries, child directories and uninspected links. Workspace-root validation itself refuses any `.agents` marker in the root or its ancestors, including an empty directory. Moving workspaces does not admit other unqualified configuration or managed policy.

The worker's agent-side MCP process currently resolves the archive command `vericairn`; keep that executable on PATH. A NuGet-only worker run is not covered by the package installation checks. The efficiency Adapter is absent until EFF-01 in 0.8.4 and enabling it is refused. Remote is not built. Context packages report exact bytes, with tokens and model fit unknown without a qualified local tokenizer.

Adopt the product repository and register its clone with `vericairn sources clone <path> --resource <repository id>`. The agent needs an assignment, an admitted signing device held here, and an active goal with a budget. The worker prepares an independent repository, borrowing the registered clone's objects read-only; it does not work in the clone's working tree. The custody guard refuses launch while an operator's or the sponsor's key can sign here without presence, unless the accountable person recorded a current, expiring custody exception for that goal. Use a separate operating-system user where keys and credentials must be isolated. See operator-guide.md sections 17 and 18 for the commands and the limits of budgets and containment.

Every run checks the work order's current holder before preparing its workspace and again before intent. An assignment displaced by hand-over is refused whether named explicitly or supplied by routing, without an intent or charge. Existing runs still need observed containment.

Settlement withdraws the run's still-open or notified process attention asks, including stall and
command-audit asks, as no longer needed. Withdrawals pin the exact settlement and preserve the question
and observations; already answered asks stay answered. Closed asks no longer produce stale-subject
warnings. The new `attempt-missing` and `scope-settlement` asks remain open for review, pinned to the
settled operation's exact revision and hash. The settlement, new asks and withdrawals are validated
together and journaled; interrupted storage still requires reconciliation. Withdrawal is not an answer
by the person asked and accepts no work.

FIX-12 keeps valid public keys and signatures from matching credential patterns only in the record fields that hold them: device `body.publicKey` and envelope `signatures[].signature`. Other fields and invalid encodings remain scanned. Machine-path checks and cryptographic signature verification are unchanged.

The brief must contain at least one interpretable relative permitted-path pattern, such as `path:NOTES.md`
or `path:docs/**`; prose alone refuses before intent. `path:` can name a literal path containing spaces.
Changes remain subject to the settlement audit; reads inside the workspace need no change permission.
Outside reads and opaque interpreter input warn; confidently identified writes outside the workspace and
launch-supplied temporary directory request a stop. See the limitations guide for classification limits.

FIX-18 admits Codex's `max` effort as well as `none`, `minimal`, `low`, `medium`, `high` and `xhigh`.
The harness checks whether the selected model supports the effort at launch; an observed refusal settles
as `known-failure`. The launcher enables `code_mode_host` for ordinary shell and MCP tools while
`code_mode` and `code_mode_only` stay disabled. It reads model and effort from the run's own rollout after
exit, never assistant prose or ambient sessions; missing or unreadable facts stay unknown, and mismatches
remain discrepancies. A run with no new attempt by the agent on its exact assignment gets the
`attempt-missing` settlement warning and review ask, without changing the meaning of `observed-success`.

Only the estate MCP server's tools are pre-approved, in both generated configuration and launch overrides.
Global approval policy stays unset; the server still checks the run principal's authority. The baseline's
MCP list omits approval settings and tool lists, so approval verification rests on the exact generated
configuration and isolation checks, not an independent report of those settings.

Seven builder live runs with codex-cli `0.159.0-alpha.12.1` exercised the selected model at `max`:

| Run and code commit | Observed outcome and its limit |
|---|---|
| First, `6bab07f` | The session reported model and max effort; 1 wall minute and 34,573 tokens were recorded. Every tool call failed with the code-mode host disabled. No attempt or result was recorded. |
| Second, `80423b5` | The first shell command ran, rollout model/effort matched admission, and settlement raised missing-attempt attention. The old audit stopped an outside-workspace read. |
| Third, `42f989d` | The workspace search and brief read ran. The old audit mistook a Python heredoc division operator for an outside write target and stopped the run. |
| Fourth, `8e272de` | The corrected audit warned for opaque Python input without stopping it. The run settled observed-success, with 2 wall minutes and 95,406 tokens; MCP approval under exec's never policy prevented an attempt/result, and no file changed. Missing-attempt attention appeared. |
| Fifth, `531d169` | The cap was recorded before launch, the admitted model and max effort were observed, and usage was 3 wall minutes and 205,767 tokens. Estate-only pre-approval allowed a signed attempt and result. Exactly the permitted `NOTES.md` changed (40 bytes). The commit failed at the sandbox boundary. |
| Sixth, `eb12a8e` | The cap, model at max, usage and signed attempt/result were recorded, and exactly `NOTES.md` changed. The independent repository alone was insufficient: Codex still protected its `.git`, so the commit failed. |
| Seventh, `36c90ba` | On macOS, Codex committed exactly the permitted `NOTES.md` with only its own `.git` added as a writable root. Settlement audited that commit and fetched its branch into the registered clone, whose `.git` remained unwritable by the agent. |

These runs qualify the reported live Codex path: inference at max, ordinary tools, rollout observation,
missing-attempt attention, brief access, the corrected audit and estate-only MCP attempt/result authoring,
and the seventh run's sandboxed commit and audited branch transfer on macOS only.
They do not qualify arbitrary commands, other versions, models, accounts or platforms, Claude Code live
sign-in, provider billing, hard spending caps, compaction/recovery, remote managed policy or acceptance of
an agent's work. Stand-in protocol coverage and the release's three-platform CI remain separate evidence.

FIX-19, retained from 0.7.3, gives each workspace its own repository. Its index, refs, logs and new objects stay
inside its own `.git`; it borrows the registered clone's objects read-only without importing remotes,
configuration or hook templates. Codex adds exactly that workspace-owned `.git` to its writable roots,
never the registered clone's Git administration. Linked or shared Git administration is refused.
The seventh live Codex run committed successfully on macOS; this live qualification is macOS-only.

After recording settlement, the worker fetches the exact audited run-branch head through a disposable
bare view of its objects. Worker Git disables hooks, ambient configuration, non-file transports and
automatic maintenance; it does not load agent-controlled repository configuration. The clone's branch
updates only if its old head still matches preparation. A concurrent move, deletion or creation,
a checked-out destination, a changed workspace head or incomplete scope audit refuses transfer.
Outside-scope commits are retained with their scope warning for review. A transfer refusal leaves the
workspace intact and adds a local `audit-unverified` warning to worker observations, without rewriting
the settlement. The clone's working tree and index stay unchanged; no push or acceptance occurs.

Uncommitted changes remain in the workspace. Reuse requires the selected root and a consistent branch;
changing the root does not relocate or delete a workspace. Preserve committed and uncommitted work before
removing an independent workspace directory. For a legacy linked worktree, preserve it and use
`git worktree remove` from the registered clone before retrying; there is no silent conversion.
Borrowed objects depend on the registered clone: preserve work before removing it or pruning objects
needed by the workspace. The scope audit does not inventory Git administration, prove attribution or
recover unreachable commits or reverted uncommitted changes. See [the software guide](software.md#coding-agents-the-worker-and-its-launchers).

### Retained command-observation rules

FIX-18 distinguishes reads, known write targets and unclassified effects. Reads inside the workspace
need no change permission; ordinary reads outside it warn with `scope-read`. Changes inside the workspace
are checked against the brief at settlement. Critical `scope` requires a confidently identified write
target outside the workspace and the temporary directory supplied at launch. The audit grants no ambient
temporary boundary or one claimed in a tool event.

FIX-20, retained from 0.7.3, recognizes reads, copies and listings of the Vericairn home's `keys/`, `connections/`,
per-clone runtime certificate and identity files, and listings of the runtime directory. It also recognizes
`gh auth token`, `gh auth status --show-token`/`-t`, `git credential fill` and Git credential-helper `get`.
These produce critical `credentials` signals and request containment. `gh auth status` without token
output retains its network warning; `git credential reject` is not a credential-output command.
The audit resolves simple captured environment substitutions, assignments and known directory changes;
it reads no credential content. An unresolved target remains uncertain unless independently recognized.

Recognized outside writes now include in-place `sed`/`perl`, `rsync`/`install` destinations, archive
extraction and output targets, `patch` targets, `dd of=`, Git init/clone/worktree destinations and literal
interpreter writes. Known wrappers do not hide the inner command. Interpreter code still warns with
`audit-unverified` alongside any independently recognized critical signal. Calls in common quoted samples
or comments are not write invocations. Output to `/dev/null`, or `NUL` and `$null` on Windows, is exempt
from outside-write classification; other devices retain their treatment. Input redirects, heredocs and
here-strings are input, with interpreter bodies inspected as code.

Warning-only cases remain: ordinary outside reads, recognizable network attempts, unresolved expansions
or command substitutions, unclassified outside-looking operands, unknown directories, uncertain physical
containment, remote `rsync` operands, opaque interpreter input (including `osascript`) and `xargs` inputs
not visible in the invocation. An independently recognized credential target or literal outside write
still produces its critical signal. Git message text and Git object selectors are not filesystem targets.
The audit does not interpret archive contents, patch bodies, arbitrary programs, computed filenames,
aliases, every option or every shell expansion. It cannot prove that an unreported effect did not occur.
Events may arrive after execution: this is a signal, not an enforcement boundary. Operating-system
isolation, credential separation and the settlement scope audit remain necessary. Stand-in tests report
commands without executing them; they establish no new live-harness or platform qualification.
[Command observations](software.md#command-observations-070) gives the complete recognized forms and limits.

### Command audit and settlement in 0.8.0

FIX-24 recognizes whole-home recursive copy, archive and sync sources, credential-helper actions
after their options, and additional outside write forms. Home-root listings, including recursive
listings, and per-clone `live.db` reads remain warning-only unless an independently recognized
credential target is present. Recognized credential access and outside writes request containment;
ordinary outside reads, network attempts and unverified effects remain warnings.

FIX-26 permits location-only home reads and matches the harness's own tools only to native identities
in its admitted inventory. This behavior belongs to the running worker: a worker still at 0.7.3
keeps its older audit and tool inventory even when it builds 0.8.0 source. Ignored build output and
untracked worker brief exports are excluded from settlement's file inventory; tracked files and
other untracked changes still count. Committed history remains separately audited. A complete
committed audit can retain the branch for review when the file inventory is incomplete. Timed-out
checks retry once; a credential check that still cannot finish requests containment.

FIX-25 removes a newly prepared, unused workspace after pre-dispatch refusal only when its complete
inventory still matches preparation. Reused, changed, concurrently used or incompletely inspected
workspaces are preserved with a warning. Windows opens use extended paths for the longer staging
name. This is guarded cleanup, not permission to delete work that cannot be proved unused.

FIX-28 publishes complete run-state and stop-request files before readers see them, coordinates
Windows readers and replacement writes, and keeps supervision alive through observation failures.
Unreadable observations remain unavailable; they never become proof of exit or absence of a stop.
A published stop request remains recorded even if its confirmation cannot be read.
The SDK also checks elapsed time when enforcing client deadlines. These corrections do not establish
new live-harness or native-platform qualification. The audit observes reported invocations, may run
after an effect, and cannot enforce credential isolation. See [software](software.md) and
[client operations](client-operations.md).

### Command audit and settlement in 0.8.2

FIX-34 removes quoting as a POSIX shell does before the workspace test, inspects the inner commands of
`dash`, `ksh`, `mksh`, `ash`, `yash`, `posh` and `eval`, and warns with `audit-unverified` for a
program the captured environment cannot resolve or a shell with another syntax, while still reading its
`-c` body. A recursive copy, archive or sync of the home's `estates` directory, of one estate's directory
or of one clone's directory is critical; copies inside a workspace under an estate's `workspaces`
directory are not. FIX-35 makes settlement exclude only what the run's starting ignore rules exclude:
the base commit's committed `.gitignore` files and the `info/exclude` captured before launch. An ignore
file the run changed or added counts as a change outside the brief, and a nested repository anywhere,
even under an excluded directory, marks the audit incomplete. These belong to the running worker: a
worker still at 0.8.0 keeps its older audit and settlement. See [software](software.md).

## 8. Enabling another machine to connect

The local-network listener is off by default. On the runtime's machine, configure its private IP address and port with `vericairn runtime configure --local-network <address:port>`, restart the daemon, and issue a code with `vericairn devices pair --principal <principal> --by <administrator>`. On the other machine, `vericairn connections add <address:port> --code <code> --name <device name>` pairs it. The code has 16 secret characters displayed in four groups of four, lasts ten minutes and admits one device. Five failed confirmations exhaust it. It also loses validity when its issuing device loses admission or the authority used to issue it is revoked or expires; restoring that authority does not revive the code. Share it only with the intended person.

An address must be reachable through the machines' network and firewall configuration; Vericairn does not discover peers or configure the firewall. A wildcard or public address requires `--allow-public`. TLS 1.2, runtime-identity pinning and device-signed requests are mandatory for the local-network profile. Synchronize clocks: a request signed before the runtime's startup cutoff or more than five seconds ahead is refused. Section 19 of the operator guide covers checks, revocation and rotation. Workbench has no paired-runtime chooser; saved connections are used by the SDK.

FIX-21, retained from 0.7.3, corrects the concurrent certificate first-use race present since 0.6.1. Loaded and
rotated certificates have their typed extension caches initialized before TLS listeners receive them,
including Server Authentication usage. When `DaemonOptions.Log` is configured, failed handshakes report
exception type and message, without stacks, inner exceptions, peer certificates or request data.
A daemon started through `runtime start` retains these diagnostics in its local log. Trust, authentication,
network and resource-limit failures can still reject a handshake. This changes no pin, permission,
protocol, rotation or replay rule. See [API transports](api-v1.md#transports).

## 9. NuGet command line and SDK

After release publication and successful NuGet publishing, install the .NET 10 SDK on Linux, macOS or Windows, then:

```sh
dotnet tool install --global dotnet-vc
dotnet vc version
dotnet vc doctor
dotnet vc --estate <estate root> status
```

Put the global tool directory on PATH if the installer asks. The tool uses the .NET and ASP.NET Core shared runtimes supplied by the SDK; unlike the archives, it is not self-contained. Use `--version 0.8.4` on the install command to pin this release, and `dotnet tool update --global dotnet-vc` to update a global installation to a published version. Installation grants no estate authority.

For a repository-local tool, create a manifest once and commit it:

```sh
dotnet new tool-manifest
dotnet tool install dotnet-vc --version 0.8.4
dotnet vc version
```

A new checkout runs `dotnet tool restore` and then `dotnet vc ...` from that repository. The NuGet tool contains no Workbench and no macOS Secure Enclave helper. Use the macOS archive when the bundled `vericairn-enclave` helper is needed for Secure Enclave custody; the tool alone cannot provide it.

In a .NET 10 console or library project, add the SDK front door:

```sh
dotnet add package Vericairn --version 0.8.4
```

`Vericairn` is a dependency-only package that brings `Vericairn.Sdk`, `Vericairn.Protocol` and `Vericairn.Records`. The namespace and assembly remain `Vericairn.Sdk`; see `api-v1.md` for client construction. The front door does not embed a runtime or daemon. Embedding hosts can add `Vericairn.Runtime` or `Vericairn.Daemon` separately, and adapters can use `Vericairn.Mcp`. These Community packages use Apache-2.0.

NuGet Trusted Publishing uploads exactly the package assets the operator publishes, verified against the release's own `SHA256SUMS`, without rebuilding them. It does not compare with the estate's accepted candidate or require its acceptance record; publishing the exact accepted draft is the operator's responsibility. Published prereleases also trigger upload. Retries use `--skip-duplicate` and cannot replace an existing package version or verify the bytes already held by nuget.org.

### MCP discovery in 0.8.2

From 0.8.2, the tool package carries both `DotnetTool` and `McpServer` package types for
[nuget.org's MCP server filter](https://www.nuget.org/packages?packagetype=McpServer&q=dotnet-vc).
Its embedded `.mcp/server.json` describes `io.github.vericairn/vericairn` for the
[official MCP Registry](https://registry.modelcontextprotocol.io/). NuGet indexing follows
package publication; the registry listing requires the operator's separate
[publication step](operator-actions.md#mcp-registry-publication). These are publication targets,
not evidence that either listing is already live.

After 0.8.4 is published, a client with the .NET 10 SDK can start the server without
installing a global tool:

```sh
dnx dotnet-vc@0.8.4 --verbosity quiet -- --estate "<estate-root>" --as "<principal-id>" mcp serve
```

Supply an existing estate root and the principal id to act for. Registry clients request both
inputs; neither has a default. The server uses stdio and the same estate authority as `dotnet vc`.
See [the MCP guide](mcp.md#discovery-and-dnx) for client configuration.

## Release check and upgrades

By default, `version`, `doctor` and `status`, and Workbench when opened, check for a newer published
Vericairn release. They share at most one request per rolling 24 hours under the same user's Vericairn
home, including failed attempts. Workbench also checks while it remains open. Other CLI commands, the
daemon, MCP and SDK API calls do not initiate this check.

The sole source is GitHub's
[latest-release endpoint](https://api.github.com/repos/Vericairn/vericairn/releases/latest).
The request is unauthenticated and contains no installed version, machine, user or estate identifier,
estate data or usage telemetry. Its only application header is the constant `User-Agent: Vericairn`
required by GitHub; cookies, credentials, tracing headers and redirects are disabled. System proxies
are ignored, so a network requiring one may leave only cached information available. Drafts and
prereleases are ignored. The check waits at most two seconds for the network and never fails a command.
Offline or on a failed response, it keeps the last successful answer without an error message.

The CLI prints the disclosure and any newer-release notice to **stderr**, including with `--json`;
stdout and canonical estate status remain unchanged. Workbench shows them in its shell even before an
estate is opened. The notice includes the version, release-notes link, NuGet command, archive download
page and estate upgrade command. Nothing installs or upgrades automatically.

Show or change the shared machine-local setting, without making a network request:

```sh
dotnet vc updates configure
dotnet vc updates configure --enabled false
dotnet vc updates configure --enabled true
dotnet vc updates configure --json
```

The archive command is `vericairn updates configure` with the same options. `false` prevents requests;
previously cached release information can still be shown. Settings live in `updates/settings.json`
under the [Vericairn home](operator-guide.md#machine-local-state). `updates/check.json` keeps the last
attempt time, last successful answer time and release tag. Neither file contains an identifier or machine
path, and neither belongs in a repository or estate. Both clients reread the setting before checking.
If settings are unreadable or invalid, or the attempt cannot be saved, no request is made.

Choose an upgrade route explicitly after reading the release notes:

```sh
dotnet tool update --global dotnet-vc --version <published-version>
# For a repository-local tool, omit --global.
```

For an archive installation or Workbench, download the appropriate archive from the linked release and
verify its hashes as that release's guide describes.

Restart any serving daemon with the installed release first: from the estate directory on its machine,
run `vericairn runtime stop`, then `vericairn runtime start`. Restart running workers after their active
work has settled. An estate upgrade changes the estate's pin; it does not restart those processes.

To move an estate's pin, use the target release's CLI. From 0.8.3 it verifies and records a published
dependency when the estate has no recorded Vericairn release:

```sh
vericairn estate upgrade --to <published-version> --check
vericairn estate upgrade --to <published-version> --by <operator-id>
```

`estate check` makes no release-check request. It warns with `manifest.release-behind` when the manifest
pins a release below the newest stable published version known from matching release records,
`vericairn-release` observations or the local cache. For source pins it compares that knowledge with
the build performing the check; it does not infer an arbitrary source commit's version. The cache
applies only to `Vericairn/vericairn`; another product's releases do not count.
This warning never makes the command fail and never changes the pin. See the
[operator guide](operator-guide.md) for the distinction between installing a tool and upgrading an estate.


## Current required attention

FIX-22 derives required attention from obligations still open in the available records. Every depth
and frequency, including `none`, includes compact items with complete source and subject references,
current state, recorded freshness, reason and an inspection command. Original records and evidence
remain complete behind those references; the timestamp is evidence freshness, not live provider health.

A later passing verification resolves only the same exact subject, method, environment and command.
A later result under the same exact assignment, acceptance of the exact result, or acceptance of its
work order's current revision resolves result attention. A later review of the same exact subject,
acceptance or supersession of that subject, its typed withdrawal, or acceptance of its current work
order resolves review attention. Retiring a work order does not withdraw the current result a review
judges. Operations resolve through their own settled revisions. Ordinary `after` planning stays in the
held list; open `blockedBy` relations and paused, retired, withdrawn or failed predecessors require attention.

These rules do not infer disposition from prose, an unrelated acceptance, a changed implementation or a
release. A changed verification environment does not match an old check. Missing records, mismatched
pins, unavailable sources, malformed projections or incomplete resolution chains keep the affected item
unknown. Quiet settings, acknowledgement or dismissal resolve nothing. Open asks retain their queue;
worker signals can remain continuity concerns after the operation settles. There is no new dismissal,
acknowledgement or expiry state. [Required attention](required-attention.md) states the exact rules.

Canonical status JSON derives attention from records alone and is identical on machines with the same
records. Progress also reads this runtime's worker observations; human status, progress and Workbench
label additional concerns `Local worker observations`. No reader can infer another machine's unrecorded
facts. The estate observation's existing status-hash contract is unchanged; these API v1 projections add
no record fields, CLI commands or permissions.

## Client interruption and source compatibility

The asynchronous SDK and Workbench support cancellation and per-operation deadlines. Interactive
reads normally have 30 seconds; longer checks, context, upgrades and release operations have
15 minutes. An interrupted effect can remain unknown and must be reconciled before retrying.
Synchronous SDK signatures keep their original timeout and exception behavior. Shared public
vocabularies are immutable and option owners copy supplied collections; source consumers should
review API compatibility [for 0.8.0](api-compatibility-0.8.0.md), [for 0.8.2](api-compatibility-0.8.2.md), [for 0.8.3](api-compatibility-0.8.3.md) and [for 0.8.4](api-compatibility-0.8.4.md) before rebuilding. The ordinary source version
retains `-dev`; an installed binary is no evidence of publication or qualification.
