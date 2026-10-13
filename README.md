# Vericairn

Vericairn is a local-first creation and operations platform: it helps a person or organization turn an
intention into an understood, authorized, produced, reviewed, delivered and maintained result, with its
decisions, sources, relationships and history intact.

This repository holds the Community edition (Apache-2.0): the record format, the runtime, the `vericairn`
command-line interface, the SDK, and the Workbench and Remote clients as they are built.

## Status

Release 0.2.0 is published and the Vericairn estate runs on it; 0.3.0 is in development on this branch. Each release
carries its own documentation set under [docs/releases](docs/releases/README.md): the 0.2.0 set is at
[docs/releases/0.2.0](docs/releases/0.2.0/README.md) (overview, installation, the operator's guide, the generated
command-line, API and record references, release notes and known limitations). The documents at `docs/` are the drafts
for the next release. What exists today:

- `Vericairn.Records`: record format 1 (canonical JSON, ULIDs, content hashes, signed records, devices and
  admission, estate loading, validation, cleanliness checks, status), documented in
  [docs/record-format-v1.md](docs/record-format-v1.md).
- `Vericairn.Runtime`: the live store, objects, storage profiles, export and restore, recovery, upgrades, and
  the one implementation of the estate operations.
- `Vericairn.Protocol`, `Vericairn.Daemon`, `Vericairn.Sdk`: the versioned API contract, the daemon that owns
  an estate's live state and serves it over a local socket and pinned loopback TLS, and the typed client,
  documented in [docs/api-v1.md](docs/api-v1.md).
- The software journey: the git adapter, verification records from commands and workflow runs, and the
  GitHub Releases provider with intent recorded before effect and publication left to the operator,
  documented in [docs/software.md](docs/software.md).
- `vericairn` CLI: `records`, `estate`, `work`, `devices`, `release`, `sources`, `check`, `runtime`,
  `recover`, `objects`, `docs`, `status`, `decisions`, `doctor`, `version`; a client of the daemon when one serves
  the estate, the embedded runtime otherwise. `vericairn docs` generates a release's references from the build and
  its notes from an estate's records, and refuses an incomplete documentation set; the release workflow runs that
  check before it builds a draft.

Nothing here is a release until a release record in the Vericairn estate says so.

## Developing on Windows, Linux or macOS

Development is supported on all three. Requires the .NET SDK pinned in `global.json` and git on the PATH.
Files are LF in every working tree (`.gitattributes`), console output is UTF-8 with LF line endings on
every platform, and continuous integration builds and tests on all three operating systems. Building the
iOS client and signing for Apple platforms will require macOS, which is Apple's constraint, not ours;
everything else, including the Android client, builds anywhere.

## Build

```sh
dotnet build
dotnet test
dotnet run --project src/Vericairn.Cli -- records describe
```

Build output goes to `artifacts/`, which is ignored.

## License

Apache License 2.0; see [LICENSE](LICENSE) and [NOTICE](NOTICE).
