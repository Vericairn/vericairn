# API compatibility for 0.8.2: two interfaces

0.8.1's candidate, withdrawn before publication, first carried these changes, so 0.8.2 is the first
published release with them. Each 0.8.2 library package was compared with its published 0.8.0 package by the .NET
package compatibility check (`apicompat package` from Microsoft.DotNet.ApiCompat.Tool
10.0.303). `Vericairn`, `Vericairn.Records`, `Vericairn.Sdk`, `Vericairn.Mcp` and
`Vericairn.Daemon` report no breaking change. `Vericairn.Protocol` and
`Vericairn.Runtime` report the interface changes below and no other. The check reports
removed or changed members, not additions or behavior. These are source and binary
changes to in-process interfaces. They change no record bytes, and the API v1 routes 0.8.2 adds are listed
in [API v1](api-v1.md). The shared-value ownership changes of 0.8.0 remain as described in
[API compatibility for 0.8.0](api-compatibility-0.8.0.md).

## Members added to `IEstateApi`

`Vericairn.Protocol.IEstateApi` gains four members. Code that calls the interface is
unaffected. A type outside Vericairn that implements it must implement these members
to compile against 0.8.2, and an implementation compiled against 0.8.0 fails to load
with 0.8.2.

| Added member | Work order |
| --- | --- |
| `DocumentPinResponse DocumentPin(DocumentPinRequest request)` | DOCS-07 |
| `PackagesResponse PackagesObserve(PackagesObserveRequest request)` | DIST-02 |
| `PackagesResponse PackagesUnlist(PackagesUnlistRequest request)` | DIST-02 |
| `PackagesResponse PackagesDeprecate(PackagesDeprecateRequest request)` | DIST-02 |

## Changed members of `IRecordCommitAdapter`

FIX-44 commits an operator record together with its generated register members. The
three members of `Vericairn.Runtime.Providers.IRecordCommitAdapter` that named one
record path and its byte hash now take `files`, an `IReadOnlyDictionary<string, string>`
from each repository-relative path to its expected SHA-256 byte hash. It holds exactly
one canonical record path under `records/`, and every other entry is a generated
document path. The old signatures are removed, so both callers and implementations
must change; there is no overload that accepts the old arguments.

| 0.8.0 member | 0.8.2 member |
| --- | --- |
| `void Branch(string directory, RecordCommitBasis basis, string branch)` | `void Branch(string directory, RecordCommitBasis basis, string branch, IReadOnlyDictionary<string, string> files)` |
| `string Commit(string directory, string branch, string basis, string path, string byteHash, IReadOnlySet<string> receipts)` | `string Commit(string directory, string branch, string basis, IReadOnlyDictionary<string, string> files, IReadOnlySet<string> receipts)` |
| `void Verify(string directory, string head, string basis, string path, string byteHash)` | `void Verify(string directory, string head, string basis, IReadOnlyDictionary<string, string> files)` |

`Prepare` and `Check` and `RecordCommitBasis` are unchanged. A single record without
generated members is the map `{ [path] = byteHash }`.
