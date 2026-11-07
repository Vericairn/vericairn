# API compatibility in 0.9.1

## IEstateApi.RunPrepare (FIX-86)

`IEstateApi` adds `RunPrepare(RunStartRequest)` and its cancellable counterpart
`RunPrepareAsync(RunStartRequest, CancellationToken)`, both with default
implementations. The synchronous default returns a refused `RunResponse` with status
`not-started` and an `unsupported` problem; the asynchronous one bridges to it like every
other operation. An existing implementer therefore compiles and keeps its behavior, and
answers that preparing a workspace alone is unavailable. `EstateService`, `VericairnClient`
and `CancellationBoundApi` implement both. No interface member is added without a default
implementation.

`POST /v1/runs/prepare` takes the existing `RunStartRequest` and answers with the existing
`RunResponse`, adding no contract type or field. A prepared workspace answers with status
`prepared`, `ok` true and its location on the runtime's machine in `workspace`, with no
operation and no cap; a refusal answers as a refused run start does. The route acts on the
runtime's own machine, so it is refused over the local network, as `POST /v1/runs` is;
on loopback its `by` is the signing device's principal. `ClientOperation` gives
`RunPrepare` the fifteen-minute deadline of `RunStart`. Earlier servers have no such route,
and a client calling one receives its not-found answer.

## Workspace setup before dispatch (FIX-86)

No contract, route, response field or interface member changes. A workspace setup command
configured on the runtime's machine runs before a run's intent, so `POST /v1/runs` and an
agent's `POST /v1/goals/{id}/step` can refuse for it before anything is recorded: when it
fails, times out or cannot be observed to its end, or when it leaves a new link or a change
outside the assignment's permitted changes. A run's intent then holds one more `evidence`
entry, with the command's status, exit code and duration and never its command,
environment or output. Readers of the intent's evidence treat it as any other summary.
`WorkerConfigurationStore` adds `ReadSetup`, `ListSetup`, `WriteSetup`, `ClearSetup` and
`SetupCommand`, with the `WorkspaceSetupConfiguration` and `WorkspaceSetupEntry` records;
they are additions to a class, not an interface. Unchanged runs record exactly what they did.

## Clearer refusal of an unregistered or altered workspace (FIX-86)

No contract changes. A directory at a run's workspace location that the worker did not
prepare and register, or a registered workspace that was altered, is refused with its path
and the reason, where 0.9.0 could answer that a scope entry was not a regular file. The
refusal's code and its place, before anything is recorded, are unchanged; only its message
differs.
