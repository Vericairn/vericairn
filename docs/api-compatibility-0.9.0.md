# API compatibility in 0.9.0

## Settling a run whose observation is marked settled (FIX-76)

`RunSettleRequest` adds an init-only `bool RecordsChecked` property, default false.
Its positional constructor and generated deconstructor are unchanged, and no route,
response contract or interface member changes. OpenAPI declares optional JSON
`recordsChecked` as a boolean on the `RunSettleRequest` schema.

With `recordsChecked: true`, a settle request records the settlement of an exited
run whose retained observation is already marked settled while the estate's records
hold none, from the observation's facts, and the settlement's evidence says so.
Omitted or false keeps the earlier behavior: that settlement stays `unresolved`, and
its message says how to check the estate's branches and copies. Startup recovery
never sets it. The confirmation changes nothing else: an already recorded settlement
is still refused as `already-settled`, and an observation that lacks a fact is still
`unresolved`. The command is `vericairn worker settle <run> --by <principal>
--records-checked`. Earlier servers ignore the field and keep refusing.

## Release lines and channels (CODE-12)

`ReleaseSubmitRequest` adds nullable init-only properties `Line`, `Channel`,
`EndOfSupport` and `BackportOf`. Its positional constructor and deconstructor
stay unchanged. `ReleaseBackportReference(Id, Hash)` carries the exact source
release. The existing API routes, SDK methods and async bridges carry these
properties. OpenAPI and the read-only MCP `release_submit_check` tool declare
the corresponding optional JSON fields. Omitted fields retain existing behavior.

`IReleaseProvider.ManualPublicationCommand(string destination, string tag,
string channel)` is a new overload with a default implementation. For `stable`
it delegates to the original two-argument member; other channels throw
`NotSupportedException` until the provider implements them. The runtime reports
unsupported publication instead of offering a stable command for another channel.
No interface member is added without a default implementation.

The GitHub provider retains its two-argument stable command and implements all
four channels through the overload. The provider prints commands and never runs
publication. Submission preserves the new fields as strings in its existing
operation target; observation carries them into the candidate and its published
revision. The target keys `releaseLine`, `releaseChannel`, `releaseEndOfSupport`,
`releaseBackport` and `releaseBackportHash` are reserved for these fields and
refused as provider settings.

Record format remains 3. Product `lines` and `cadence` and release `line`,
`channel`, `endOfSupport` and `backportOf` are additive fields. A format-2 record
carrying any of them is refused. An older reader that cannot interpret these
fields must be upgraded before they are used. Existing records keep their bytes,
hashes and signatures. `ReleaseLines.Line` and `ReleaseLines.Channel` read the
defaults without changing a body; `ReleaseBackports.Notes` renders recorded
backports from either line.

The release companion implementation advances to `vericairn.records/releases/2`
and the generic record reader to `vericairn.records/readers/4` for the new fields.
Their hashes change. Declarations pinned to earlier implementations require the
ordinary explicit generator update before regeneration; earlier golden fixtures
remain retained.


