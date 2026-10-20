# CLI reference additions

Revision 1, October 20, 2026. Document policy definition hashing in the development build.

The build generates the complete CLI reference from its command tree, including every option. Published references under `docs/releases/` describe their frozen builds. This page documents the policy preparation command added after 0.8.0.

## `vericairn policies hash`

Print the definition hash of a prepared policy or recurring-duty body:

```sh
vericairn policies hash --body policy.json
vericairn policies hash --body duty.json --json
```

| Option | Meaning |
|---|---|
| `--body <file>` | Required. Read the prepared JSON body from this file. Use `-` to read standard input. Supply the body without a record envelope. |
| `--json` | Return `{"definitionHash":"sha256:...","problem":null}`. Without this option, print only the hash and a newline. |
| `--help`, `-h` | Show command help. |
| `--estate <root>`, `-e <root>` | Inherited root option; hashing does not use it or open an estate. |

The value is SHA-256 over the record format's RFC 8785 canonical body, with only the top-level `basis` member omitted. This is the `definitionHash` an operator approves and `policies declare` checks. `basis` may be absent while preparing approval; adding or changing it leaves the hash unchanged. Every other member contributes, including a nested member named `basis`.

The command reads only its input and writes no files, records, keys or live state. It needs neither an estate nor write authority. Exit 0 means a hash was printed. An unreadable file, invalid JSON, a value that cannot be canonicalized, or a value without the policy body structure exits 2 with a diagnostic and no hash.

Hashing checks the body structure and canonical JSON representation. Declaration still validates references, rule parameters, approval and authority. See the [adoption guide](policy-adoption.md) for using the value in an exact approval, and [Estate policies](policies.md) for the API and SDK equivalents.
