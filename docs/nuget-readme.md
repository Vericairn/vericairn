# Vericairn Community

Vericairn is a local-first creation and operations platform. Its Community edition keeps work, decisions,
evidence and history as append-only, content-addressed, signed records. These packages contain Community
code only, under Apache-2.0, authored by Jason Furr.

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
