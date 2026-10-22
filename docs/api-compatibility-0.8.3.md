# API compatibility for 0.8.3: work-order creation

This page describes CODE-07 in the 0.8.3 development build. It is an interface inventory,
not a claim of publication or a completed package compatibility check. The API remains
version 1, and the work-order record format is unchanged.

## Members added to `IEstateApi`

`Vericairn.Protocol.IEstateApi` gains two members. Code that calls existing members is
unaffected. A type outside Vericairn that implements the interface must implement
`WorkNew` to compile against 0.8.3; an implementation compiled against 0.8.2 must be
updated and rebuilt. `WorkNewAsync` has a default implementation that calls `WorkNew`
with the existing cancellation and operation-lifetime behavior.

| Added member | Work order |
| --- | --- |
| `WriteOutcome WorkNew(WorkNewRequest request)` | CODE-07 |
| `Task<WriteOutcome> WorkNewAsync(WorkNewRequest request, CancellationToken cancellationToken = default)` | CODE-07 |

`WorkNewRequest` carries `By`, `Key`, `Title`, `Intent`, `Scope`, `ExitQuestion`,
`Deliverables`, `Kind`, `After` and `Inputs`. `Kind` defaults to `ordinary`, `Inputs`
defaults to an empty list and omitted `After` adds no dependencies. Input references
are `type:id` strings; dependencies are work-order record ids. Required body fields
are checked by the existing work-order validator, with the same refusal messages.

`VericairnClient` and `CancellationBoundApi` expose both members. The SDK calls
`POST /v1/work/new`, which returns the existing `WriteOutcome` and signs as the
author named by `By`, like `POST /v1/records`. [API v1](api-v1.md#work-order-creation-083-development)
describes the request and the command it matches. Earlier interface changes remain
documented in [API compatibility for 0.8.2](api-compatibility-0.8.2.md).
