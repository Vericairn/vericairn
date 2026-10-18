# Installing Vericairn

This guide describes 0.7.3: independent worker repositories (FIX-19), release notices (UPDATE-01),
command-audit corrections (FIX-20), concurrent TLS certificate preparation and diagnostics (FIX-21),
and current required attention (FIX-22). Earlier sets stay frozen, including the withdrawn 0.7.0 candidate carried in 0.7.1 with FIX-12.
NuGet distribution becomes available after the operator publishes a release and its publication workflow succeeds.
The `0.0.1-reserved` packages reserve names; they are not the product. For the currently published build, use its frozen guide in the [release index](releases/README.md).
The NuGet workflow verifies the exact package assets the operator publishes against that release's own
`SHA256SUMS`; it does not compare them with the estate's accepted candidate. Publication of the accepted
draft is the operator's responsibility. No package is rebuilt during upload, and duplicate versions are skipped.

## Command line from NuGet

Install the .NET 10 SDK on Linux, macOS or Windows, then:

```sh
dotnet tool install --global dotnet-vc
dotnet vc version
dotnet vc doctor
```

Put the global tool directory on PATH if the installer asks. `dotnet-vc` is the tool's package and command
name; `dotnet vc` is its canonical invocation. It uses the .NET and ASP.NET Core shared runtimes included
with the SDK. `doctor` reports the runtime, git, SDK and available device-key custody. Git is needed for
repository operations.

To pin the tool in a repository instead, run these commands at its root (create the manifest only once):

```sh
dotnet new tool-manifest
dotnet tool install dotnet-vc --version <published-version>
dotnet vc version
dotnet vc doctor
```

Commit the generated tool manifest. A new checkout runs `dotnet tool restore`, then uses the same
`dotnet vc ...` commands from that repository. Use `--version <version>` when installing a particular
published release, and `dotnet tool update --global dotnet-vc` to update a global installation.

For example, inspect the record types or read an existing estate:

```sh
dotnet vc records describe
dotnet vc --estate <estate root> status
```

Installation grants no estate authority; writes still require the acting principal's admitted device.
The worker's agent-side MCP launcher currently resolves the archive command `vericairn`; keep that
executable on PATH for worker runs. A NuGet-only worker run is not covered by the package installation checks.

## Release check and upgrades (0.7.3)

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
verify its hashes as that release's guide describes. To move an estate's pin, first ensure its target
published release is recorded in that estate, then use the target release's CLI:

```sh
vericairn estate upgrade --to <published-version> --check
vericairn estate upgrade --to <published-version> --by <operator-id>
```

`estate check` makes no release-check request. It warns with `manifest.release-behind` when the manifest
pins a release below the newest stable published version known from matching release records or the
local cache. The cache applies only to `Vericairn/vericairn`; another product's releases do not count.
This warning never makes the command fail and never changes the pin. See the
[operator guide](operator-guide.md) for the distinction between installing a tool and upgrading an estate.

## SDK

In a .NET 10 console or library project:

```sh
dotnet add package Vericairn
```

This dependency-only front door brings `Vericairn.Sdk` and its `Vericairn.Protocol` and `Vericairn.Records`
dependencies. The existing `Vericairn.Sdk` namespace and assembly stay the SDK's identity. It does not bring
the embedded runtime or the daemon; hosts can add `Vericairn.Runtime` or `Vericairn.Daemon` separately, and
MCP adapters can use `Vericairn.Mcp`. All are Community packages under Apache-2.0. See the
[API and SDK guide](api-v1.md#the-sdk) for connecting to an estate.

## Self-contained archives and Workbench

The release archives still contain `bin/vericairn` (`bin/vericairn.exe` on Windows), self-contained for each
platform. Follow the published release's installation guide to verify the archive against the estate's
recorded hashes before running it. Archive commands written as `vericairn ...` use the same arguments as
`dotnet vc ...`.

The macOS archives include `vericairn-enclave` beside the executable. The portable NuGet tool does not
include that native helper; use the macOS archive when you need the bundled helper for Secure Enclave custody.
Workbench continues to use its own archive, with the `vericairn` executable beside it; the tool package installs the command
line only. See the [Workbench draft](workbench.md).

On POSIX, the repository adapter and release provider's bounded Git and gh runner requires the runtime to run under an init process, not as PID 1,
without ignored `SIGCHLD`, automatic child reaping or an independent child reaper. Unsupported hosts refuse
before launching a command. Each adapter Git command has 30 seconds, each provider gh command two minutes, and each output
stream has a 4,194,304-character limit; [software.md](software.md#release-providers-and-the-products-release-profile)
describes containment and uncertain dispatch.

Workbench's first screen reuses status and progress observations and avoids repeated full-history scans.
Unchanged bytes reuse parsing and hashes; opening still validates every record once. Large estates can
still take time, and headless checks establish no fixed native startup-time bound.

## Worker prerequisites and holding limits

The native inventory baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`.
An authorized human may record a qualification for another exact version using `worker qualify`; all local
probes and the admitted inventory must still match. Generic worker dispatch is refused.
Qualification checks the launcher's protocol and isolation boundaries, not live inference or sign-in.
Qualification is machine-local: its API route refuses paired local-network devices before probes or writes;
local socket and signed loopback requests still require human qualification authority.

Before a native run, set model and effort defaults on the worker's machine. Replace the model and effort
values with your chosen model selector and an effort supported by that launcher:

```sh
dotnet vc worker configure --launcher codex --model <model> --effort <effort>
dotnet vc worker configure --launcher codex
```

Use `--launcher claude-code` for Claude Code. Both choices are required when setting; omitting them shows
the configuration (`--json` is also available). Repeat `--allow-model` or `--allow-effort` to narrow allowed
choices, including each default. With no model allowlist only the default model is allowed; with no effort
allowlist the launcher's supported efforts are allowed. Setting replaces that launcher's defaults and
allowlists. It never widens the qualified inventory or adds tools, servers, variables or routes.

Codex accepts `none`, `minimal`, `low`, `medium`, `high`, `xhigh` and `max` in 0.7.3. For example, `dotnet vc worker configure --launcher codex --model gpt-6-astra --effort max` stores
that selection. The inventory probes do not validate a model's effort support; the harness validates it at launch.
An observed nonzero exit, including an effort refusal, is recorded as `known-failure`. See the
[execution guide](software.md#explicit-execution-and-environment-admission-run-01-070) for the admission and usage limits.

Configuration is per machine and is never recorded in the estate. The owner-only files live at
`worker/codex.json` and `worker/claude-code.json` under the user's Vericairn home. Reads and writes refuse
when that home is inside a Git repository or an estate. If your home directory is a repository, set
`VERICAIRN_HOME` to a machine-local directory outside every repository and estate for the CLI and daemon
before starting them. The CLI and daemon reread configuration for each run and agent goal step; changing
its choices needs no daemon restart. Missing or invalid configuration refuses before intent and never
falls back to ambient harness choices. See the
[software guide](software.md#explicit-execution-and-environment-admission-run-01-070) for precedence and admission.

A home holding `.claude/settings.json` or `.codex/config.toml` is an ancestor of the default workspace,
so the native configuration scan refuses runs there. Choose an absolute directory outside that home:

```sh
dotnet vc worker configure --workspaces <absolute-directory>
dotnet vc worker configure
```

Omit `--launcher` for this setting. It applies to every launcher on the machine and is stored in the
owner-only `worker/workspaces.json` under the Vericairn home. The root must be outside Git repositories
(including bare repositories) and estates, and cannot itself be a symbolic link or reparse point. Linked
ancestors are resolved and the canonical root is stored. The canonical root and its ancestors cannot hold
`.claude`, `.codex`, `.agents` or `.mcp.json`; refusal names what it found.
A new root is created owner-only. Unknown fields and duplicate keys refuse; an invalid file never falls
back to the default. The CLI and daemon reread it for each run, without a restart, and recheck before dispatch.

New workspaces go under `<root>/<estate>/<branch-slug>`; slashes in the branch become hyphens.
With no root configured, the location remains `<VericairnHome>/estates/<estate>/workspaces/<branch-slug>`.
Each new workspace is an independent repository with its own `.git`, borrowing the registered clone's
objects read-only. An existing independent workspace for the branch is reused only under the selected
root. A workspace elsewhere is refused: preserve its work before removing that directory, or select a
root containing it. Changing the root does not move it. Legacy linked worktrees are refused; preserve
their committed and uncommitted work, then use `git worktree remove` from the registered clone before retrying.
Workspace paths stay local and never enter records. Moving workspaces changes their ancestors; separately
discovered user skills and managed configuration still face the same native checks.
In particular, a Codex run is refused while the user's own `~/.agents` contains skills or other customization,
even with a workspace outside that home and a fresh `CODEX_HOME`. Use a worker account without this discovery
root. The native scan allows an empty ordinary directory or one containing only recognized instruction files;
it refuses other entries, child directories and uninspected links. Workspace-root validation itself refuses any `.agents`
marker in the root or its ancestors, including an empty one.

FIX-12 checks the current holder of the work order for every run, before preparing its workspace and again
before recording intent. A run on an assignment displaced by hand-over is refused, whether the assignment
was named explicitly or routed, without an intent or charge. Existing runs still need observed containment.

The Codex launcher enables the ordinary code-mode tool host and pre-approves only the estate MCP server's
tools; the server still enforces the acting principal's authority. Global approval policy is unset.
The seventh live run committed the permitted file on macOS. FIX-19 adds exactly the independent workspace's
own `.git` to Codex's writable roots; the registered clone's `.git` stays protected. Settlement audits the
run branch and fetches the audited head into the clone with a guarded reference update. An incomplete audit,
changed head, checked-out destination or concurrent branch move refuses transfer and leaves a local warning;
preserve the workspace for review. Uncommitted work stays there. Worker Git ignores ambient configuration
and disables hooks. Borrowed objects depend on the registered clone: preserve work before pruning its objects
or removing it. This live qualification is macOS-only and does not qualify other accounts, versions or billing.
The brief must contain at least one interpretable permitted path pattern; prose alone refuses before intent.

FIX-20 requests a stop for recognized credential access and outside writes, including token-output commands;
null-device output does not trigger an outside-write stop. Ordinary outside reads, network attempts and
unverified effects still warn. Reports can arrive after execution; the audit does not isolate credentials
or prove that unreported activity did not occur. See [command observations](software.md#command-observations-070).

FIX-21 prepares loaded and rotated runtime certificates before concurrent TLS use, correcting the race
present since 0.6.1. Handshake diagnostics retain exception type and message when daemon logging is configured;
trust, authentication and transport failures remain possible. FIX-22 shows current required attention on
every surface, with unknown resolution visible. A fix in prose or unavailable evidence cannot close an item;
see [required attention](required-attention.md).

The efficiency Adapter is absent until the planned 0.8.0 work; enabling it is refused. Remote is not built.
Context payloads are measured in bytes; tokens and model fit remain unknown without a qualified local tokenizer.
