# Installing Vericairn

This is the installation draft for 0.7.0's NuGet distribution, available after the operator publishes that
release and its publication workflow succeeds. The `0.0.1-reserved` packages reserve names; they are not
the product. For the currently published build, use its frozen guide in the [release index](releases/README.md).

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
dotnet tool install dotnet-vc --version 0.7.0
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

## Worker prerequisites and holding limits

The native inventory baselines are codex-cli `0.159.0-alpha.12.1` and Claude Code `2.1.296`.
An authorized human may record a qualification for another exact version using `worker qualify`; all local
probes and the admitted inventory must still match. Generic worker dispatch is refused.
Qualification checks the launcher's protocol and isolation boundaries, not live inference or sign-in.
Qualification is machine-local: its API route refuses paired local-network devices before probes or writes;
local socket and signed loopback requests still require human qualification authority.

Before a native run, set model and effort defaults on the worker's machine. Replace the model and effort
placeholders with your chosen model selector and an effort supported by that launcher:

```sh
dotnet vc worker configure --launcher codex --model <model> --effort <effort>
dotnet vc worker configure --launcher codex
```

Use `--launcher claude-code` for Claude Code. Both choices are required when setting; omitting them shows
the configuration (`--json` is also available). Repeat `--allow-model` or `--allow-effort` to narrow allowed
choices, including each default. With no model allowlist only the default model is allowed; with no effort
allowlist the launcher's supported efforts are allowed. Setting replaces that launcher's defaults and
allowlists. It never widens the qualified inventory or adds tools, servers, variables or routes.

Configuration is per machine and is never recorded in the estate. The owner-only files live at
`worker/codex.json` and `worker/claude-code.json` under the user's Vericairn home. Reads and writes refuse
when that home is inside a Git repository or an estate. If your home directory is a repository, set
`VERICAIRN_HOME` to a machine-local directory outside every repository and estate for the CLI and daemon
before starting them. The CLI and daemon reread configuration for each run and agent goal step; changing
its choices needs no daemon restart. Missing or invalid configuration refuses before intent and never
falls back to ambient harness choices. See the
[software guide](software.md#explicit-execution-and-environment-admission-run-01-070) for precedence and admission.

The efficiency Adapter is absent until the planned 0.8.0 work; enabling it is refused. Remote is not built.
Context payloads are measured in bytes; tokens and model fit remain unknown without a qualified local tokenizer.
