# API reference, Vericairn 0.2.0

Generated from the OpenAPI document the 0.2.0 daemon serves at `/openapi/v1.json` (OpenAPI 3.0.3, API version 1) on October 13, 2026. The daemon is the one owner of an estate's live state on a machine; every surface, the command line included, is its client. How to reach it (the local socket or named pipe, the pinned loopback TLS endpoint with device-signed requests) and what the outcomes and problem codes mean are in the API document; this reference lists every operation of the release.

## `GET /v1/health`

The daemon's identity and what it serves.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/status`

Current status: human text and the canonical JSON that is identical on every machine.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/validate`

Validate every record and the manifest.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/check`

Validate and run the repository checks.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/records/{id}`

A record by id.

Parameters:
- `id` (path, required): 

Responses:
- `200`: What the runtime observed.
- `404`: Nothing has that id, key or hash.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/records`

Author a record; signed with the author's admitted device on the runtime's machine; rejected with findings when the estate would be invalid.

Request body (`application/json`): `NewRecordRequest`

Responses:
- `200`: What the runtime observed.
- `404`: The subject named does not exist (WriteOutcome with a not-found problem).
- `422`: Refused or rejected (WriteOutcome with a problem; a rejection carries findings).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/decisions`

Current decisions.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/decisions/{id}`

One decision by the id of any of its revisions.

Parameters:
- `id` (path, required): 

Responses:
- `200`: What the runtime observed.
- `404`: Nothing has that id, key or hash.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/work/{key}/brief`

The brief of a work order: everything a contributor needs, from the records alone.

Parameters:
- `key` (path, required): 

Responses:
- `200`: What the runtime observed.
- `404`: Nothing has that id, key or hash.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/work/{key}`

What an observer sees of a work order.

Parameters:
- `key` (path, required): 

Responses:
- `200`: What the runtime observed.
- `404`: Nothing has that id, key or hash.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/work/assign`

Delegate a work order to a contributor under a brief.

Request body (`application/json`): `AssignRequest`

Responses:
- `200`: What the runtime observed.
- `404`: The subject named does not exist (WriteOutcome with a not-found problem).
- `422`: Refused or rejected (WriteOutcome with a problem; a rejection carries findings).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/work/attempt`

Record the start of an attempt.

Request body (`application/json`): `AttemptRequest`

Responses:
- `200`: What the runtime observed.
- `404`: The subject named does not exist (WriteOutcome with a not-found problem).
- `422`: Refused or rejected (WriteOutcome with a problem; a rejection carries findings).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/work/result`

Submit the result of an attempt: a claim to inspect.

Request body (`application/json`): `ResultRequest`

Responses:
- `200`: What the runtime observed.
- `404`: The subject named does not exist (WriteOutcome with a not-found problem).
- `422`: Refused or rejected (WriteOutcome with a problem; a rejection carries findings).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/work/amend`

Request an amendment; it authorizes nothing.

Request body (`application/json`): `AmendRequest`

Responses:
- `200`: What the runtime observed.
- `404`: The subject named does not exist (WriteOutcome with a not-found problem).
- `422`: Refused or rejected (WriteOutcome with a problem; a rejection carries findings).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/devices`

Devices known to the estate and whether this machine holds their keys.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/devices`

Create a device on the runtime's machine.

Request body (`application/json`): `DeviceCreateRequest`

Responses:
- `200`: What the runtime observed.
- `404`: The subject named does not exist (WriteOutcome with a not-found problem).
- `422`: Refused or rejected (WriteOutcome with a problem; a rejection carries findings).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/devices/admit`

Admit a device.

Request body (`application/json`): `AdmitRequest`

Responses:
- `200`: What the runtime observed.
- `404`: The subject named does not exist (WriteOutcome with a not-found problem).
- `422`: Refused or rejected (WriteOutcome with a problem; a rejection carries findings).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/devices/revoke`

Revoke a device's admission from now on.

Request body (`application/json`): `RevokeRequest`

Responses:
- `200`: What the runtime observed.
- `404`: The subject named does not exist (WriteOutcome with a not-found problem).
- `422`: Refused or rejected (WriteOutcome with a problem; a rejection carries findings).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/releases`

Current release records.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/estate/upgrade`

Plan or apply an upgrade to a published release.

Request body (`application/json`): `UpgradeRequest`

Responses:
- `200`: What the runtime observed.
- `409`: Not carried out (Problem).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/estate/export`

Write a verified export to a path on the runtime's machine; no keys or authority travel with it.

Request body (`application/json`): `ExportRequest`

Responses:
- `200`: What the runtime observed.
- `409`: Not carried out (Problem).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/reconcile`

Reconcile tasks left by diverged copies.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/recover/inspect`

What this copy holds, for recovery.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/objects`

Objects held.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/objects`

Store bytes as an object named by their hash.

Request body (`application/octet-stream`): `ObjectBytes`

Responses:
- `200`: What the runtime observed.
- `413`: Above the object size limit (Problem).
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/objects/verify`

Verify every object against its name.

Responses:
- `200`: What the runtime observed.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `GET /v1/objects/{hash}`

An object's verified bytes.

Parameters:
- `hash` (path, required): 

Responses:
- `200`: What the runtime observed.
- `404`: Nothing has that id, key or hash.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## `POST /v1/shutdown`

Ask the daemon to stop; closing a client never stops it.

Responses:
- `200`: What the runtime observed.
- `202`: The daemon will stop.
- `401`: On loopback: the request is not signed by a device admitted now (Problem with code denied).

## Schemas

The request and response types named above, as the document defines them.

### `NewRecordRequest`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `type` | `string` | no |  |
| `by` | `string` | no |  |
| `body` | `object` | no |  |
| `subject` | `string` | no |  |
| `supersedes` | `string` | no |  |
| `at` | `string` | no |  |
| `id` | `string` | no |  |

### `AssignRequest`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `workOrder` | `string` | no |  |
| `contributor` | `string` | no |  |
| `by` | `string` | no |  |
| `brief` | `object` | no |  |
| `note` | `string` | no |  |

### `AttemptRequest`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `assignment` | `string` | no |  |
| `by` | `string` | no |  |
| `workspace` | `string` | no |  |
| `note` | `string` | no |  |
| `at` | `string` | no |  |

### `ResultRequest`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `attempt` | `string` | no |  |
| `by` | `string` | no |  |
| `body` | `object` | no |  |

### `AmendRequest`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `assignment` | `string` | no |  |
| `by` | `string` | no |  |
| `body` | `object` | no |  |

### `DeviceCreateRequest`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `principal` | `string` | no |  |
| `name` | `string` | no |  |
| `custody` | `string` | no |  |
| `profiles` | `array of ` | no |  |

### `AdmitRequest`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `device` | `string` | no |  |
| `by` | `string` | no |  |
| `note` | `string` | no |  |

### `RevokeRequest`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `device` | `string` | no |  |
| `by` | `string` | no |  |
| `reason` | `string` | no |  |

### `UpgradeRequest`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `version` | `string` | no |  |
| `by` | `string` | no |  |
| `check` | `boolean` | no |  |

### `ExportRequest`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `output` | `string` | no |  |
| `by` | `string` | no |  |

### `ObjectBytes`

### `WriteOutcome`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `written` | `object` | no |  |
| `problem` | `object` | no |  |

### `Problem`

| Property | Type | Required | Meaning |
|---|---|---|---|
| `code` | `string` | no |  |
| `message` | `string` | no |  |
| `findings` | `array of ` | no |  |
