# Installing Vericairn 0.7.2

Vericairn 0.7.2 ships as self-contained distributables per platform: no separately installed .NET runtime is required. The operating system's native libraries are still required, and Workbench needs a desktop session. There are two distributables per platform: `vericairn-0.7.2-<platform>` holds the command line (`bin/vericairn`, and on macOS also `bin/vericairn-enclave`, the helper the command line calls for Secure Enclave custody), and `vericairn-workbench-0.7.2-<platform>` holds the desktop (`bin/vericairn-workbench`) with a copy of the command line beside it, because Workbench drives the daemon through that command line. `git` is needed for the commands that read or adopt repositories and for a repository-synced estate; the GitHub CLI `gh`, logged in as you, is needed only for the release provider and for observing workflow runs, because Vericairn never holds a token of its own. On Linux, secret-service custody needs `secret-tool` (from libsecret) and a running secret service such as GNOME Keyring; without them device keys are kept in owner-only files.

The archives install `vericairn`. Introduced in published 0.7.1, the NuGet tool installs `dotnet-vc`, canonically invoked as `dotnet vc`; the two command lines take the same estate arguments. Workbench is installed by direct download only (section 3). The SDK is a separate NuGet package (section 9).

Use the 0.7.2 archives after the operator accepts and publishes the release. NuGet installation goes live when that publication triggers the Trusted Publishing workflow and it succeeds; package availability follows that workflow, not installation of a local build. The `0.0.1-reserved` packages reserve names and are not the product. Withdrawn candidates 0.5.0, 0.5.1, 0.6.0 and 0.7.0 are not installation targets.

0.7.2 follows published 0.7.1 with five fixes: FIX-13's identifiers, exact-commit release gate, grouped notes,
settled attention, workflow checks and Workbench first screen; FIX-14's inventory-bound export restore
(F-01); FIX-15's claimed release dispatch and bounded Git/gh runner; FIX-16's validation before record
publication and visible unreadable journals; and FIX-18's corrected Codex worker at max effort.
The withdrawn 0.7.0 candidate's work shipped in 0.7.1 with FIX-12. All earlier sets stay frozen and indexed.

Git and gh share one bounded runner: 30 seconds per Git command, two minutes per gh command, and
4,194,304 characters per stdout or stderr stream. The deadline includes launch, concurrent output reads
and exit. Overflow fails instead of accepting partial output. Timeout or cancellation terminates the
owned POSIX process group or Windows Job Object, including descendants holding output pipes. Cleanup
waits at most one additional second; unconfirmed exit retains native ownership for later cleanup.
On POSIX the runtime must run under an init process, not as PID 1, without ignored `SIGCHLD`,
`SA_NOCLDWAIT` or a separate child reaper. Unsupported hosts refuse before launch. Process-group escape
and the existing containment limits still apply.

## 1. Route A: as part of an estate, through its bootstrap

Once the estate has upgraded to 0.7.2, its repository pins that release in `estate/manifest.json`, by version and by the SHA-256 of every distributable. Its `bootstrap` script fetches that release, verifies it and runs it. This is the cold start. The bootstrap belongs to the estate; the commands below assume it provides the first-party bootstrap interface.

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
2. Fetches `vericairn-0.7.2-<platform>.tar.gz` (or `.zip` on Windows) from the release page, unless `VERICAIRN_RELEASE_BASE` names another directory or URL to fetch from.
3. Computes the archive's SHA-256 and refuses to continue when it differs from the manifest. The hash comes from the manifest, never from the tag name alone.
4. Unpacks the archive under `.vericairn/toolchain/release/0.7.2/<platform>/` inside the clone, a directory git ignores, and remembers the verified hash beside it so later runs skip the fetch.
5. Runs `bin/vericairn --estate <the clone> <your arguments>`; with no arguments it runs `estate check`.

Expected observation: `estate check` ends with a line like `N records: 0 errors, 0 warnings`, and `status` renders the estate: its work orders, releases, assignments, decisions, documents and the living documents that are due. The bootstrap fetches the version its manifest pins, which is 0.7.2 only after that upgrade has been recorded.

If the manifest pins a source commit instead of a release (an estate younger than its first release), the bootstrap builds Vericairn from that commit and needs the .NET SDK named by the source's `global.json`; set `VERICAIRN_SOURCE` to a local clone of the Vericairn source to build from it instead of fetching.

## 2. Route B: direct download of the command line

1. After publication, from https://github.com/Vericairn/vericairn/releases/tag/v0.7.2 download the distributable for your platform and `SHA256SUMS`.
2. Verify the download. Linux: `sha256sum -c SHA256SUMS --ignore-missing`. macOS: `shasum -a 256 -c SHA256SUMS --ignore-missing`. Windows PowerShell: `(Get-FileHash .\vericairn-0.7.2-win-x64.zip).Hash` and compare with the line in `SHA256SUMS`. The expected hashes are in the estate's published release record, which is the authority: a tag can be moved, a record cannot.
3. Unpack: `tar -xzf vericairn-0.7.2-<platform>.tar.gz -C <directory>` on Linux and macOS; `Expand-Archive` on Windows. The archive holds `bin/vericairn` (with `bin/vericairn-enclave` beside it on macOS), `LICENSE`, `NOTICE`, the third-party notices and `licenses/` with the license and notice texts of every component the command line bundles.
4. Run `<directory>/bin/vericairn version` (`bin\vericairn.exe` on Windows). It prints `0.7.2+<commit>`.
5. Put `bin` on your `PATH` if you want to type `vericairn` alone. Every command takes `--estate <path>`; without it the current directory is the estate. Keep `vericairn-enclave` beside `vericairn`: the command line looks for the helper next to itself, and without it Secure Enclave custody is unavailable.

Platform notes, stated honestly because the build is unsigned:

- macOS may refuse to run a binary that a browser downloaded, because it carries the quarantine attribute and is not notarized. Downloading with `curl` avoids the attribute; otherwise remove it with `xattr -dr com.apple.quarantine <directory>` after verifying the checksum, which covers the helper too. The estate bootstrap fetches with `curl`, so Route A does not hit this. The helper uses CryptoKit's Secure Enclave keys, which need no entitlement and no developer signature; when the Mac does not expose a usable Secure Enclave, `vericairn doctor` reports it unavailable and new keys use file custody. The helper is built for macOS 13.0 or later; availability is probed at runtime, including in virtual machines.
- Windows SmartScreen may warn about an unsigned program the first time. PowerShell may require `-ExecutionPolicy Bypass` to run `bootstrap.ps1` when the policy is restricted.
- Linux: `tar` preserves the executable bit. Workbench uses X11 or XWayland; a headless machine can use the command line. Secret-service custody needs the service and `secret-tool` described above.

## 3. Workbench, the desktop

1. From the same release page download `vericairn-workbench-0.7.2-<platform>.tar.gz` (`.zip` on Windows) and verify it against `SHA256SUMS` as above.
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

## 5. After installing

`vericairn doctor` reports the version, the runtime, `git` and `dotnet` if present, the estate it sees, the key custody a new device gets by default in the estate it sees, whether Secure Enclave custody is available on a Mac and why not when it is not, the record formats the build reads (1 and 2) and the newest it writes (2) and whether a daemon serves the estate. Per-user state (device keys or, for enclave keys, their enclave-wrapped blobs, each estate's live store and daemon endpoint, logs, Workbench's layout preference) lives in the per-user application-support directory `doctor` prints; set `VERICAIRN_HOME` to use another. A device key created in an estate still at record format 1 is kept in the strongest custody format 1 can name (the platform key store on Windows, the secret service on Linux where one runs, otherwise an owner-only file); the Secure Enclave and presence need the estate at record format 2 (section 6), after which `vericairn devices rotate <device> --custody secure-enclave` moves a key there.

Nothing runs in the background until you ask: commands run the embedded runtime unless a daemon serves the estate (`vericairn runtime status` says which). A daemon started by `vericairn runtime start` (or by Workbench, through that command) writes its output to `daemon.log` beside the estate's live store, never to the terminal or the program that started it. When the daemon stops before it serves, `runtime start` exits with an error that quotes what the daemon wrote to its log and says where the log is. And `vericairn runtime install --plan` shows what installing the daemon as a per-user service would do before `runtime install` does it; Workbench's runtime workspace shows the same plan before it installs or uninstalls. The operator guide covers both.

## 6. Upgrading an estate to 0.7.2

Install the 0.7.2 binary by Route B, then run these commands in the estate after it holds the published release record:

```sh
vericairn estate upgrade --to 0.7.2 --check
vericairn estate upgrade --to 0.7.2 --by <your principal id>
```

The plan names the target release and artifact hashes. Applying it updates the manifest's pin and writes a migration record; commit that change so each clone's bootstrap fetches the same release. An estate already running 0.7.1 at record format 2 stays at format 2: 0.7.2 reads formats 1 and 2 and writes an estate's own format, at most 2. The retained progress, communication, execution and harness-qualification additions and new worker signal values still require readers that know them; format compatibility alone does not make older binaries understand them. Update every participating writer before using those additions.

An estate still at format 1 also applies `record-format-2`. That step rewrites no existing record. It lists every unsigned admission present, including superseded admissions and revocations, by id and hash, and an unsigned admission counts at format 2 only when that signed list names it. Where there is anything to anchor, the root operator or a principal holding `administer` applies the upgrade with an admitted device whose admission rests on signatures. `--check` names the admissions first. The format documents and operator guide describe this rule.

A format-2 decision needs `stake` and `notAuthorized`; scripts that author decisions must supply them. Admissions and revocations need a signature. Upgrading moves no device key: rotate a key on the machine that holds it when its custody should change. The withdrawn candidates 0.5.0, 0.5.1, 0.6.0 and 0.7.0 are not upgrade targets. Existing settlement and accounting rules still apply without a new format number.

## 7. Preparing the worker

Install the chosen coding-agent harness separately and provision its login under the operating-system user that runs the worker. `vericairn worker launchers` lists `claude-code`, `codex` and `generic` with their settings, but generic dispatch is refused. The native baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`. Other exact versions require an authorized person's `worker qualify` record under RUN-03 and matching local probes. Codex's goals feature and the complete supported inventories are checked before dispatch. Qualification establishes the launcher protocol. FIX-18's five live Codex runs additionally exercise the specific path described below, without qualifying provider billing or other accounts.

Before a native run or an agent goal step, set that machine's model and effort defaults:

```sh
vericairn worker configure --launcher codex --model <model> --effort max
vericairn worker configure --launcher codex
```

Replace `<model>` with your chosen model selector; Codex accepts `max` in 0.7.2. Use `--launcher claude-code` with one of its supported efforts for Claude Code; model/effort compatibility is checked by the harness at launch. Setting requires both model and effort; omitting choices shows the configuration (`--json` is available). Repeated `--allow-model` and `--allow-effort` options narrow permitted choices and must include their defaults. With no model allowlist only the default model is permitted; with no effort allowlist the launcher's supported efforts are permitted. Each set replaces that launcher's defaults and allowlists. Configuration never widens the qualified inventory or adds tools, servers, environment variables or routes. The stock runtime derives its environment policy only after a baseline or exact current qualification admits the native inventory.

Configuration is per machine and is never recorded in the estate. Its owner-only files are `worker/codex.json` and `worker/claude-code.json` under the user's Vericairn home. Reads and writes are refused when that home is inside a Git repository or estate. If your home directory is a repository, set `VERICAIRN_HOME` to a machine-local directory outside all repositories and estates for the CLI and daemon before starting them. Both read the configuration for each run and agent goal step; changing its choices needs no daemon restart. Missing configuration refuses before intent and names the setup command. Installing or qualifying a harness alone supplies no model or effort default. Explicit run overrides cannot bypass local allowlists or inherited restrictions.

Harness qualification is machine-local as well: `POST /v1/harness-qualifications` refuses a paired local-network device before probes or record writes. The runtime's local socket and signed loopback retain the existing human authority checks. See section 18 of the operator guide and `software.md` for the complete setup and admission rules.

The default workspace is beneath the Vericairn home. If your home holds `.claude` or `.codex` configuration, the native scan refuses runs beneath it. FIX-12 lets you choose an absolute workspace root outside that home:

```sh
vericairn worker configure --workspaces <absolute-directory>
vericairn worker configure
```

Omit `--launcher` when setting or showing the workspace root. This separate setting applies to every launcher and lives in the owner-only `worker/workspaces.json` under the Vericairn home. Its path stays per machine and is never recorded. The root must be outside Git repositories (including bare repositories) and estates; neither it nor an ancestor may hold `.claude`, `.codex`, `.agents` or `.mcp.json`. The root itself cannot be a symbolic link or reparse point. Linked ancestors are resolved and the canonical root is stored. A new root is created owner-only. Invalid configuration, unknown fields and duplicate keys refuse, without fallback.

The CLI and daemon reread the root for every run and recheck before intent and dispatch, without a restart. New workspaces use `<root>/<estate>/<branch-slug>`, with branch slashes flattened to hyphens; the unconfigured default remains `<VericairnHome>/estates/<estate>/workspaces/<branch-slug>`. An existing branch worktree is reused only under the selected root. If it is elsewhere, preserve its work and follow the refusal's `git worktree remove` guidance from the registered clone, or select a root containing it. Setting a root does not move or delete a worktree. A root changed after intent refuses dispatch as an unstarted, uncharged failure.

Codex still separately inspects the user's own `~/.agents`. A run is refused while it supplies skills or other customization, even after moving the workspace and using a fresh `CODEX_HOME`. Use a worker account without that discovery root. The native scan permits an empty ordinary directory or recognized instruction files alone, but refuses other entries, child directories and uninspected links. Workspace-root validation itself refuses any `.agents` marker in the root or its ancestors, including an empty directory. Moving workspaces does not admit other unqualified configuration or managed policy.

The worker's agent-side MCP process currently resolves the archive command `vericairn`; keep that executable on PATH. A NuGet-only worker run is not covered by the package installation checks. The efficiency Adapter is absent until the planned 0.8.0 work and enabling it is refused. Remote is not built. Context packages report exact bytes, with tokens and model fit unknown without a qualified local tokenizer.

Adopt the product repository and register its clone with `vericairn sources clone <path> --resource <repository id>`. The agent needs an assignment, an admitted signing device held here, and an active goal with a budget. The worker creates a git worktree; it does not work in the registered clone's working tree. The custody guard refuses launch while an operator's or the sponsor's key can sign here without presence, unless the accountable person recorded a current, expiring custody exception for that goal. Use a separate operating-system user where keys and credentials must be isolated. See operator-guide.md sections 17 and 18 for the commands and the limits of budgets and containment.

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

Five builder live runs with codex-cli `0.159.0-alpha.12.1` exercised `gpt-6-astra` at `max`:

| Run and code commit | Observed outcome and its limit |
|---|---|
| First, `6bab07f` | The session reported model and max effort; 1 wall minute and 34,573 tokens were recorded. Every tool call failed with the code-mode host disabled. No attempt or result was recorded. |
| Second, `80423b5` | The first shell command ran, rollout model/effort matched admission, and settlement raised missing-attempt attention. The old audit stopped an outside-workspace read. |
| Third, `42f989d` | The workspace search and brief read ran. The old audit mistook a Python heredoc division operator for an outside write target and stopped the run. |
| Fourth, `8e272de` | The corrected audit warned for opaque Python input without stopping it. The run settled observed-success, with 2 wall minutes and 95,406 tokens; MCP approval under exec's never policy prevented an attempt/result, and no file changed. Missing-attempt attention appeared. |
| Fifth, `531d169` | The cap was recorded before launch, the admitted model and max effort were observed, and usage was 3 wall minutes and 205,767 tokens. Estate-only pre-approval allowed a signed attempt and result. Exactly the permitted `NOTES.md` changed (40 bytes). The commit failed at the sandbox boundary. |

These runs qualify the reported live Codex path: inference at max, ordinary tools, rollout observation,
missing-attempt attention, brief access, the corrected audit and estate-only MCP attempt/result authoring.
They do not qualify arbitrary commands, other versions, models, accounts or platforms, Claude Code live
sign-in, provider billing, hard spending caps, compaction/recovery, remote managed policy or acceptance of
an agent's work. Stand-in protocol coverage and the release's three-platform CI remain separate evidence.

A worker-launched Codex run cannot yet commit: the workspace is a git worktree whose Git metadata lies
outside the sandbox. Its changes stay uncommitted in the worktree for review. FIX-19 makes the workspace
a repository of its own; that change is not in 0.7.2.

## 8. Enabling another machine to connect

The local-network listener is off by default. On the runtime's machine, configure its private IP address and port with `vericairn runtime configure --local-network <address:port>`, restart the daemon, and issue a code with `vericairn devices pair --principal <principal> --by <administrator>`. On the other machine, `vericairn connections add <address:port> --code <code> --name <device name>` pairs it. The code has 16 secret characters displayed in four groups of four, lasts ten minutes and admits one device. Five failed confirmations exhaust it. It also loses validity when its issuing device loses admission or the authority used to issue it is revoked or expires; restoring that authority does not revive the code. Share it only with the intended person.

An address must be reachable through the machines' network and firewall configuration; Vericairn does not discover peers or configure the firewall. A wildcard or public address requires `--allow-public`. TLS 1.2, runtime-identity pinning and device-signed requests are mandatory for the local-network profile. Synchronize clocks: a request signed before the runtime's startup cutoff or more than five seconds ahead is refused. Section 19 of the operator guide covers checks, revocation and rotation. Workbench has no paired-runtime chooser; saved connections are used by the SDK.

## 9. NuGet command line and SDK

After release publication and successful NuGet publishing, install the .NET 10 SDK on Linux, macOS or Windows, then:

```sh
dotnet tool install --global dotnet-vc
dotnet vc version
dotnet vc doctor
dotnet vc --estate <estate root> status
```

Put the global tool directory on PATH if the installer asks. The tool uses the .NET and ASP.NET Core shared runtimes supplied by the SDK; unlike the archives, it is not self-contained. Use `--version 0.7.2` on the install command to pin this release, and `dotnet tool update --global dotnet-vc` to update a global installation to a published version. Installation grants no estate authority.

For a repository-local tool, create a manifest once and commit it:

```sh
dotnet new tool-manifest
dotnet tool install dotnet-vc --version 0.7.2
dotnet vc version
```

A new checkout runs `dotnet tool restore` and then `dotnet vc ...` from that repository. The NuGet tool contains no Workbench and no macOS Secure Enclave helper. Use the macOS archive when the bundled `vericairn-enclave` helper is needed for Secure Enclave custody; the tool alone cannot provide it.

In a .NET 10 console or library project, add the SDK front door:

```sh
dotnet add package Vericairn --version 0.7.2
```

`Vericairn` is a dependency-only package that brings `Vericairn.Sdk`, `Vericairn.Protocol` and `Vericairn.Records`. The namespace and assembly remain `Vericairn.Sdk`; see `api.md` for client construction. The front door does not embed a runtime or daemon. Embedding hosts can add `Vericairn.Runtime` or `Vericairn.Daemon` separately, and adapters can use `Vericairn.Mcp`. These Community packages use Apache-2.0.

NuGet Trusted Publishing uploads exactly the package assets the operator publishes, verified against the release's own `SHA256SUMS`, without rebuilding them. It does not compare with the estate's accepted candidate or require its acceptance record; publishing the exact accepted draft is the operator's responsibility. Published prereleases also trigger upload. Retries use `--skip-duplicate` and cannot replace an existing package version or verify the bytes already held by nuget.org.
