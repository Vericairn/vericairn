# CLI reference additions

Revision 2, October 20, 2026. Document exact-byte document pinning and policy definition hashing in the development build.

The build generates the complete CLI reference from its command tree, including every option. Published references under `docs/releases/` describe their frozen builds. This page documents commands added after 0.8.0 for 0.8.1.

## `vericairn docs pin`

Record a document's next revision from the exact bytes in the estate repository:

```sh
vericairn docs pin docs/guide.md --by <principal id> --summary "Describe the revised workflow."
vericairn docs pin docs/guide.md --by <principal id> --summary "Update the examples." --expected <current pin id> --json
```

| Argument or option | Meaning |
|---|---|
| `<path>` | Required. A normalized repository-relative file path under `design/` or `docs/`, using forward slashes. |
| `--by <id>` | Required. The authoring principal; the runtime uses its admitted device under the estate's signing rules. |
| `--summary <text>` | Required. What this revision is or changes. |
| `--title <text>` | Override the title. Otherwise carry the current pin's title; a first pin uses the file name without its extension. |
| `--expected <id>` | Require this exact record id to be the current pin for the path. An old, unrelated or unknown id refuses the write. |
| `--json` | Print the record receipt as `{"path":"records/document-revision/...","id":"...","hash":"sha256:..."}`. The hash identifies the record; its body holds the document's byte hash. |
| `--estate <root>`, `-e <root>` | Select the estate; the runtime reads the file in that estate's repository. |
| `--help`, `-h`, `-?`, `/?`, `/h` | Show command help. |

The first pin is revision 1. Each later pin increments the current revision and supersedes that record. The runtime computes SHA-256 and byte count from one byte read, preserving line endings and any byte-order mark. Media type follows the file extension: Markdown, plain text, JSON, YAML, HTML, XML, CSV, PDF, SVG, PNG and JPEG are recognized; other extensions use `application/octet-stream`.

Unchanged bytes refuse even if the title or summary differs. Paths outside the document directories, unavailable files, symbolic links, case aliases, whole generated documents and generated register members refuse. Authored documents may contain generated regions. A stale expected id or multiple current pins also refuses. These refusals record nothing. Without `--expected`, the runtime selects the current pin when the operation begins; clients that read a pin earlier should supply its id.

If two branches pin the same predecessor, their merged records form a fork. Both `estate check` and `docs pin` identify the conflicting pins and predecessor. Record a `reconciliation` that keeps one successor and withdraws the others, settle the document text, then pin the combined bytes with the kept id as `--expected`. Neither branch wins automatically, and all revision records remain.

Text output prints the document revision, record id and record hash. Exit 0 means the pin was recorded; a refusal exits 2 with a diagnostic or validation findings. A receipt accompanied by a companion-recovery problem also exits 2: reconcile the stored operation before retrying. The command does not edit the document or its hand-written revision line. See [Living documents](operator-guide.md#16-living-documents).

The matching API is `POST /v1/documents/pin`, with JSON fields `path`, `by`, `summary`, optional `title` and optional `expected`. It returns `DocumentPinResponse`: `written` (the record receipt), `revision`, `problem` and `ok`. A refusal returns HTTP 422 with no `written` or `revision`; a stored receipt returns HTTP 200, including when a companion problem needs reconciliation. SDK clients use `DocumentPin(DocumentPinRequest)` or `DocumentPinAsync(DocumentPinRequest, CancellationToken)`:

```csharp
var result = await client.DocumentPinAsync(
    new DocumentPinRequest("docs/guide.md", principal, "Update the examples.", Expected: currentPinId),
    cancellationToken);
```

The runtime reads its estate file for every client. Callers supply neither document bytes nor a claimed hash or revision number.

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
| `--help`, `-h`, `-?`, `/?`, `/h` | Show command help. |
| `--estate <root>`, `-e <root>` | Inherited root option; hashing does not use it or open an estate. |
| `--recipient <id>` | Inherited presentation option; hashing does not use it. |
| `--for-assignment <id>` | Inherited presentation option; hashing does not use it. |
| `--depth <depth>` | Inherited presentation option; hashing always prints the same hash or JSON response. |

The value is SHA-256 over the record format's RFC 8785 canonical body, with only the top-level `basis` member omitted. This is the `definitionHash` an operator approves and `policies declare` checks. `basis` may be absent while preparing approval; adding or changing it leaves the hash unchanged. Every other member contributes, including a nested member named `basis`.

The command reads only its input and writes no files, records, keys or live state. It needs neither an estate nor write authority. Exit 0 means a hash was printed. An unreadable file, invalid JSON, a value that cannot be canonicalized, or a value without the policy body structure exits 2 with a diagnostic and no hash.

Hashing checks the body structure and canonical JSON representation. Declaration still validates references, rule parameters, approval and authority. See the [adoption guide](policy-adoption.md) for using the value in an exact approval, and [Estate policies](policies.md) for the API and SDK equivalents.
