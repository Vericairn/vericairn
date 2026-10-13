# Vericairn

Vericairn is a local-first creation and operations platform: it helps a person or organization turn an
intention into an understood, authorized, produced, reviewed, delivered and maintained result, with its
decisions, sources, relationships and history intact.

This repository holds the Community edition (Apache-2.0): the record format, the runtime, the `vericairn`
command-line interface, the SDK, and the Workbench and Remote clients as they are built.

## Status

Early. Release 0.1.0 is being built. What exists today:

- `Vericairn.Records`: record format 1 (canonical JSON, ULIDs, content hashes, twelve record types, estate
  loading, validation, cleanliness checks, status), documented in [docs/record-format-v1.md](docs/record-format-v1.md).
- `vericairn` CLI: `records validate|new|describe|format|hash`, `estate check`, `status`, `doctor`, `version`.

Nothing here is a release until a release record in the Vericairn estate says so.

## Build

Requires the .NET SDK pinned in `global.json`.

```sh
dotnet build
dotnet test
dotnet run --project src/Vericairn.Cli -- records describe
```

Build output goes to `artifacts/`, which is ignored.

## License

Apache License 2.0; see [LICENSE](LICENSE) and [NOTICE](NOTICE).
