# Installing Vericairn 0.7.0

Vericairn 0.7.0 ships as self-contained distributables per platform: no separately installed .NET runtime is required. The operating system's native libraries are still required, and Workbench needs a desktop session. There are two distributables per platform: `vericairn-0.7.0-<platform>` holds the command line (`bin/vericairn`, and on macOS also `bin/vericairn-enclave`, the helper the command line calls for Secure Enclave custody), and `vericairn-workbench-0.7.0-<platform>` holds the desktop (`bin/vericairn-workbench`) with a copy of the command line beside it, because Workbench drives the daemon through that command line. `git` is needed for the commands that read or adopt repositories and for a repository-synced estate; the GitHub CLI `gh`, logged in as you, is needed only for the release provider and for observing workflow runs, because Vericairn never holds a token of its own. On Linux, secret-service custody needs `secret-tool` (from libsecret) and a running secret service such as GNOME Keyring; without them device keys are kept in owner-only files.

The archives install `vericairn`. New in 0.7.0, the NuGet tool installs `dotnet-vc`, canonically invoked as `dotnet vc`; the two command lines take the same estate arguments. Workbench is installed by direct download only (section 3). The SDK is a separate NuGet package (section 9).

This guide is prepared before publication. Use the 0.7.0 archives only after the operator accepts and publishes the release. NuGet installation goes live when that publication triggers the Trusted Publishing workflow and it succeeds; this guide does not claim that the product packages are already available on nuget.org. The last published release is 0.6.1. The `0.0.1-reserved` packages reserve names and are not the product; withdrawn candidates 0.5.0, 0.5.1 and 0.6.0 are not installation targets.

## 1. Route A: as part of an estate, through its bootstrap

Once the estate has upgraded to 0.7.0, its repository pins that release in `estate/manifest.json`, by version and by the SHA-256 of every distributable. Its `bootstrap` script fetches that release, verifies it and runs it. This is the cold start. The bootstrap belongs to the estate; the commands below assume it provides the first-party bootstrap interface.

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
2. Fetches `vericairn-0.7.0-<platform>.tar.gz` (or `.zip` on Windows) from the release page, unless `VERICAIRN_RELEASE_BASE` names another directory or URL to fetch from.
3. Computes the archive's SHA-256 and refuses to continue when it differs from the manifest. The hash comes from the manifest, never from the tag name alone.
4. Unpacks the archive under `.vericairn/toolchain/release/0.7.0/<platform>/` inside the clone, a directory git ignores, and remembers the verified hash beside it so later runs skip the fetch.
5. Runs `bin/vericairn --estate <the clone> <your arguments>`; with no arguments it runs `estate check`.

Expected observation: `estate check` ends with a line like `N records: 0 errors, 0 warnings`, and `status` renders the estate: its work orders, releases, assignments, decisions, documents and the living documents that are due. The bootstrap fetches the version its manifest pins, which is 0.7.0 only after that upgrade has been recorded.

If the manifest pins a source commit instead of a release (an estate younger than its first release), the bootstrap builds Vericairn from that commit and needs the .NET SDK named by the source's `global.json`; set `VERICAIRN_SOURCE` to a local clone of the Vericairn source to build from it instead of fetching.

## 2. Route B: direct download of the command line

1. After publication, from https://github.com/Vericairn/vericairn/releases/tag/v0.7.0 download the distributable for your platform and `SHA256SUMS`.
2. Verify the download. Linux: `sha256sum -c SHA256SUMS --ignore-missing`. macOS: `shasum -a 256 -c SHA256SUMS --ignore-missing`. Windows PowerShell: `(Get-FileHash .\vericairn-0.7.0-win-x64.zip).Hash` and compare with the line in `SHA256SUMS`. The expected hashes are in the estate's published release record, which is the authority: a tag can be moved, a record cannot.
3. Unpack: `tar -xzf vericairn-0.7.0-<platform>.tar.gz -C <directory>` on Linux and macOS; `Expand-Archive` on Windows. The archive holds `bin/vericairn` (with `bin/vericairn-enclave` beside it on macOS), `LICENSE`, `NOTICE`, the third-party notices and `licenses/` with the license and notice texts of every component the command line bundles.
4. Run `<directory>/bin/vericairn version` (`bin\vericairn.exe` on Windows). It prints `0.7.0+<commit>`.
5. Put `bin` on your `PATH` if you want to type `vericairn` alone. Every command takes `--estate <path>`; without it the current directory is the estate. Keep `vericairn-enclave` beside `vericairn`: the command line looks for the helper next to itself, and without it Secure Enclave custody is unavailable.

Platform notes, stated honestly because the build is unsigned:

- macOS may refuse to run a binary that a browser downloaded, because it carries the quarantine attribute and is not notarized. Downloading with `curl` avoids the attribute; otherwise remove it with `xattr -dr com.apple.quarantine <directory>` after verifying the checksum, which covers the helper too. The estate bootstrap fetches with `curl`, so Route A does not hit this. The helper uses CryptoKit's Secure Enclave keys, which need no entitlement and no developer signature; when the Mac does not expose a usable Secure Enclave, `vericairn doctor` reports it unavailable and new keys use file custody. The helper is built for macOS 13.0 or later; availability is probed at runtime, including in virtual machines.
- Windows SmartScreen may warn about an unsigned program the first time. PowerShell may require `-ExecutionPolicy Bypass` to run `bootstrap.ps1` when the policy is restricted.
- Linux: `tar` preserves the executable bit. Workbench uses X11 or XWayland; a headless machine can use the command line. Secret-service custody needs the service and `secret-tool` described above.

## 3. Workbench, the desktop

1. From the same release page download `vericairn-workbench-0.7.0-<platform>.tar.gz` (`.zip` on Windows) and verify it against `SHA256SUMS` as above.
2. Unpack it to a directory of its own. It holds `bin/vericairn-workbench` and `bin/vericairn` (with `.exe` on Windows, and with `bin/vericairn-enclave` on macOS), `LICENSE`, `NOTICE`, the third-party notices and `licenses/` with the license and notice texts of every bundled component, the fonts' among them.
3. Run it:

```sh
<directory>/bin/vericairn-workbench --estate <estate root>
```

Workbench opens the estate's root (the directory with `estate/manifest.json`), connects to the daemon that serves it, and starts one through the command line beside it when none answers; nothing runs in the daemon's place inside Workbench. Without `--estate` it shows the first-run page, where the root is typed or chosen from the recent estates. `--as <principal id>` names the principal Workbench acts for: the selector in the target bar then shows that principal's admitted device held on this machine, the one the runtime signs with, and choosing another device in the selector changes the acting principal. When the named principal has no device held here, the target bar says whether its writes are refused (the estate requires signatures) or written unsigned. Without `--as`, Workbench chooses among the admitted devices held here and says when it is read-only. `VERICAIRN_CLI` names another command-line executable to drive; by default the one beside Workbench is used, then one on the `PATH`. A device whose key requires presence asks for Touch ID or the login password when the runtime signs with it. Automated tests do not establish the interactive presence experience; consult the estate's platform qualifications (`limitations-and-platforms.md`).

Workbench is an unsigned developer build with no application bundle, installer or signature. The same checksum and platform checks apply as for the command line. Headless tests cover its work loop, releases, documents, Goals workspace and progress report; native desktop and screen-reader qualification needs separate observations for each platform. See `limitations-and-platforms.md` and `workbench.md`.

## 4. The MCP server for coding agents

A coding agent reaches an estate through `vericairn mcp serve`, a server over standard input and output for compatible MCP harnesses. Start the daemon first when several clients will share the estate. The server uses it when available, or holds an embedded runtime for its session otherwise; the adapter holds no key of its own, and its write tools exist only for a principal whose admitted device key this machine holds:

```sh
vericairn --estate <estate root> mcp serve --as <principal id>
```

Claude Code: `claude mcp add vericairn -- vericairn --estate <estate root> mcp serve --as <principal id>`. `vericairn mcp orientation --out <directory>` writes the orientation file an agent reads first (`AGENTS.md`, generated from the estate's identity and the tool registry, with `CLAUDE.md` and `GEMINI.md` as pointers), and `--check` says whether existing copies are current; `mcp.md` in this set has the protocol subset, the tools and the limitations. An agent's device never needs its owner's presence, so an agent signs without a prompt.

## 5. After installing

`vericairn doctor` reports the version, the runtime, `git` and `dotnet` if present, the estate it sees, the key custody a new device gets by default in the estate it sees, whether Secure Enclave custody is available on a Mac and why not when it is not, the record formats the build reads (1 and 2) and the newest it writes (2) and whether a daemon serves the estate. Per-user state (device keys or, for enclave keys, their enclave-wrapped blobs, each estate's live store and daemon endpoint, logs, Workbench's layout preference) lives in the per-user application-support directory `doctor` prints; set `VERICAIRN_HOME` to use another. A device key created in an estate still at record format 1 is kept in the strongest custody format 1 can name (the platform key store on Windows, the secret service on Linux where one runs, otherwise an owner-only file); the Secure Enclave and presence need the estate at record format 2 (section 6), after which `vericairn devices rotate <device> --custody secure-enclave` moves a key there.

Nothing runs in the background until you ask: commands run the embedded runtime unless a daemon serves the estate (`vericairn runtime status` says which). A daemon started by `vericairn runtime start` (or by Workbench, through that command) writes its output to `daemon.log` beside the estate's live store, never to the terminal or the program that started it. When the daemon stops before it serves, `runtime start` exits with an error that quotes what the daemon wrote to its log and says where the log is. And `vericairn runtime install --plan` shows what installing the daemon as a per-user service would do before `runtime install` does it; Workbench's runtime workspace shows the same plan before it installs or uninstalls. The operator guide covers both.

## 6. Upgrading an estate to 0.7.0

Install the 0.7.0 binary by Route B, then run these commands in the estate after it holds the published release record:

```sh
vericairn estate upgrade --to 0.7.0 --check
vericairn estate upgrade --to 0.7.0 --by <your principal id>
```

The plan names the target release and artifact hashes. Applying it updates the manifest's pin and writes a migration record; commit that change so each clone's bootstrap fetches the same release. An estate already running 0.6.1 at record format 2 stays at format 2: 0.7.0 reads formats 1 and 2 and writes an estate's own format, at most 2. New progress, communication, execution and harness-qualification records and fields still require readers that know them; format compatibility alone does not make older binaries understand them. Update every participating writer before using those additions.

An estate still at format 1 also applies `record-format-2`. That step rewrites no existing record. It lists every unsigned admission present, including superseded admissions and revocations, by id and hash, and an unsigned admission counts at format 2 only when that signed list names it. Where there is anything to anchor, the root operator or a principal holding `administer` applies the upgrade with an admitted device whose admission rests on signatures. `--check` names the admissions first. The format documents and operator guide describe this rule.

A format-2 decision needs `stake` and `notAuthorized`; scripts that author decisions must supply them. Admissions and revocations need a signature. Upgrading moves no device key: rotate a key on the machine that holds it when its custody should change. The withdrawn candidates 0.5.0, 0.5.1 and 0.6.0 are not upgrade targets. Existing settlement and accounting rules still apply without a new format number.

## 7. Preparing the worker

Install the chosen coding-agent harness separately and provision its login under the operating-system user that runs the worker. `vericairn worker launchers` lists `claude-code`, `codex` and `generic` with their settings, but generic dispatch is refused. The native baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`. Other exact versions require an authorized person's `worker qualify` record under RUN-03 and matching local probes. Codex's goals feature and the complete supported inventories are checked before dispatch. Qualification establishes the launcher protocol, not live inference, sign-in or provider billing.

**Configuration gap in this build:** the stock CLI and daemon supply no RUN-01 configuration loader, and the native launchers supply no default configuration. They refuse worker launch until an embedding host supplies `WorkerOptions.Configurations` with explicit model, effort and environment policy. The `--configuration` option selects such a supplied configuration; it does not create one. Installing or requalifying a harness alone does not resolve this gap. See the operator guide and `software.md` for the intended configured-host journey and the qualification-route defect.

The worker's agent-side MCP process currently resolves the archive command `vericairn`; keep that executable on PATH. A NuGet-only worker run is not covered by the package installation checks. The efficiency Adapter is absent until the planned 0.8.0 work and enabling it is refused. Remote is not built. Context packages report exact bytes, with tokens and model fit unknown without a qualified local tokenizer.

Adopt the product repository and register its clone with `vericairn sources clone <path> --resource <repository id>`. The agent needs an assignment, an admitted signing device held here, and an active goal with a budget. The worker creates a git worktree; it does not work in the registered clone's working tree. The custody guard refuses launch while an operator's or the sponsor's key can sign here without presence, unless the accountable person recorded a current, expiring custody exception for that goal. Use a separate operating-system user where keys and credentials must be isolated. See operator-guide.md sections 17 and 18 for the commands and the limits of budgets and containment.

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

Put the global tool directory on PATH if the installer asks. The tool uses the .NET and ASP.NET Core shared runtimes supplied by the SDK; unlike the archives, it is not self-contained. Use `--version 0.7.0` on the install command to pin this release, and `dotnet tool update --global dotnet-vc` to update a global installation to a published version. Installation grants no estate authority.

For a repository-local tool, create a manifest once and commit it:

```sh
dotnet new tool-manifest
dotnet tool install dotnet-vc --version 0.7.0
dotnet vc version
```

A new checkout runs `dotnet tool restore` and then `dotnet vc ...` from that repository. The NuGet tool contains no Workbench and no macOS Secure Enclave helper. Use the macOS archive when the bundled `vericairn-enclave` helper is needed for Secure Enclave custody; the tool alone cannot provide it.

In a .NET 10 console or library project, add the SDK front door:

```sh
dotnet add package Vericairn --version 0.7.0
```

`Vericairn` is a dependency-only package that brings `Vericairn.Sdk`, `Vericairn.Protocol` and `Vericairn.Records`. The namespace and assembly remain `Vericairn.Sdk`; see `api.md` for client construction. The front door does not embed a runtime or daemon. Embedding hosts can add `Vericairn.Runtime` or `Vericairn.Daemon` separately, and adapters can use `Vericairn.Mcp`. These Community packages use Apache-2.0. Publication reuses the verified packages from the release build through NuGet Trusted Publishing; retries use `--skip-duplicate` and cannot replace an existing package version.
