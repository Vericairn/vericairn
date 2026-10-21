<!-- mcp-name: io.github.vericairn/vericairn -->
# Vericairn Community

Vericairn is a local-first creation and operations platform. It helps you, alone or as an organization,
carry work from an intention to a reviewed, delivered and maintained result, with its decisions, sources
and history kept as records you own: by default in a git repository beside your work, or in a store on one
machine, with hosted options planned. People and agents do the work, and nothing is released until you
accept it. Software is its first domain: today Vericairn helps you build and ship
software with coding agents. Its catalog plans more than 3,000 entries across creative and technical work
and 27 industries, from books, courses and games to construction, medicine and finance; none of them ships
yet.

Its Community edition keeps work, decisions,
evidence and history as append-only, content-addressed, signed records. These packages contain Community
code only, under Apache-2.0, authored by Jason Furr. The source, releases and guides are at
https://github.com/Vericairn/vericairn.

## Command line

With the .NET 10 SDK installed:

```sh
dotnet tool install --global dotnet-vc
dotnet vc version
dotnet vc doctor
```

Or keep the tool in a repository's local manifest:

```sh
dotnet new tool-manifest
dotnet tool install dotnet-vc
dotnet vc version
```

The tool is framework-dependent and uses the .NET and ASP.NET Core runtimes included with the SDK.
The self-contained release archives continue to provide the `vericairn` executable for machines without
the SDK. The macOS archives also include the Secure Enclave helper; the portable NuGet tool does not.
The worker's agent-side MCP launcher still resolves `vericairn`; use the archive executable on PATH for
worker runs. The package smoke checks qualify tool invocation and SDK consumption, not a NuGet-only worker run.

## SDK

In a .NET 10 project:

```sh
dotnet add package Vericairn
```

`Vericairn` brings `Vericairn.Sdk`, `Vericairn.Protocol` and `Vericairn.Records`. The SDK's namespace remains
`Vericairn.Sdk`; `VericairnClient` implements the protocol's `IEstateApi`. The front-door package holds no
assembly of its own, so existing SDK consumers and the supporting libraries use the same assemblies.

`Vericairn.Runtime` provides the embedded runtime. `Vericairn.Daemon` hosts it and requires the ASP.NET Core
shared framework. `Vericairn.Mcp` is the Model Context Protocol adapter over the protocol. These remain
separate packages for hosts that need them; installing the SDK alone does not bring the daemon or runtime.

Packages follow the repository's version. Assembly-bearing packages have portable symbols with Source Link
from the .NET SDK; the dependency-only `Vericairn` package needs no symbol package.

See the [repository](https://github.com/Vericairn/vericairn),
[API and SDK guide](https://github.com/Vericairn/vericairn/blob/main/docs/api-v1.md), and the
[release documentation](https://github.com/Vericairn/vericairn/tree/main/docs/releases).
