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
probes and the separately admitted inventory must still match. Generic worker dispatch is refused.
Qualification checks the launcher's protocol and isolation boundaries, not live inference or sign-in.

At this head, the stock CLI/daemon has no loader for RUN-01's required run configurations. Its native
launchers provide no default, so worker launch refuses until an embedding host supplies
`WorkerOptions.Configurations`, explicit model/effort choices and the environment admission policy.
Installing a harness or qualifying its version alone does not fill this gap. The
[software guide](software.md#explicit-execution-and-environment-admission-run-01-070) describes it and the
qualification-route defect that still needs a code correction.

The efficiency Adapter is absent until the planned 0.8.0 work; enabling it is refused. Remote is not built.
Context payloads are measured in bytes; tokens and model fit remain unknown without a qualified local tokenizer.
