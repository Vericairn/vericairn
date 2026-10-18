# Generating the client outcome reference

Run from the repository root after an offline or otherwise permitted restore:

```sh
dotnet run --project docs/cli-reference-generator --no-restore -- artifacts/reference docs/releases/documents.json docs/cli-client-outcomes.md
```

This build helper delegates command, API and record generation to the existing documentation tool. It adds only the interrupted-request section to the newly generated CLI reference, using the codes and messages from `ClientFailure`, and writes the same section to `docs/cli-client-outcomes.md`. Command names, options, help text and JSON schemas are unchanged. The full generated set stays in ignored build output until a release's documentation is prepared.

The helper creates its own temporary home under the output directory and refuses output under `docs/releases/`. Frozen release documents and their checksums remain historical. Its only intended CLI reference difference, apart from generation metadata, is the section headed “Interrupted client requests”.
