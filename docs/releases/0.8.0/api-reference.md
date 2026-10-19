# API reference

The retained OpenAPI document supplies every field below. Field paths are JSON Pointers; absent values are not inferred.

## `GET /v1/asks`

| Recorded field | Value |
|---|---|
| &#47;operationId | getAsks |
| &#47;parameters&#47;0&#47;description | The principal asked&#44; by id&#59; every principal when omitted&#46; |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | to |
| &#47;parameters&#47;0&#47;required | false |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | What waits on a principal &#40;to&#41;&#44; or on every principal&#58; the asks still open or notified&#44; oldest first&#44; at most three delivered to each with the recommendation first and the consequence of leaving it unanswered&#44; the rest waiting in order &#40;vericairn&#46;asks&#47;1&#41;&#46; Reading it answers nothing&#58; an ask is answered by a decision of the principal it is put to that names it&#44; never by waiting&#46; |

## `POST /v1/changes/merge`

| Recorded field | Value |
|---|---|
| &#47;operationId | postChangesMerge |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangesMergeRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangeResponse |
| &#47;responses&#47;200&#47;description | ChangeResponse&#58; recorded intent&#44; observed effect and exact&#45;head evidence&#59; uncertainty never authorizes retry&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Record intent and checks&#44; refuse missing or unsuccessful checks&#44; merge pinned to the head&#44; and observe the target run&#46; A failure raises an operator ask&#46; |

## `POST /v1/changes/observe`

| Recorded field | Value |
|---|---|
| &#47;operationId | postChangesObserve |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangesObserveRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangeResponse |
| &#47;responses&#47;200&#47;description | ChangeResponse&#58; recorded intent&#44; observed effect and exact&#45;head evidence&#59; uncertainty never authorizes retry&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Record an observation under an original change&#39;s provider and head&#59; incomplete or uncertain outcomes remain uncertain&#46; |

## `POST /v1/changes/open`

| Recorded field | Value |
|---|---|
| &#47;operationId | postChangesOpen |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangesOpenRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangeResponse |
| &#47;responses&#47;200&#47;description | ChangeResponse&#58; recorded intent&#44; observed effect and exact&#45;head evidence&#59; uncertainty never authorizes retry&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Record intent and open a pull request from an exact head with a recorded successful scanned push&#46; |

## `POST /v1/changes/push`

| Recorded field | Value |
|---|---|
| &#47;operationId | postChangesPush |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangesPushRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangeResponse |
| &#47;responses&#47;200&#47;description | ChangeResponse&#58; recorded intent&#44; observed effect and exact&#45;head evidence&#59; uncertainty never authorizes retry&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Record intent&#44; scan exactly the commits&#44; then push the pinned head with a remote lease and record its observed outcome&#46; Requires repository&#45;write authority&#46; |

## `GET /v1/check`

| Recorded field | Value |
|---|---|
| &#47;operationId | getCheck |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Validate and run the repository checks&#46; |

## `GET /v1/communication`

| Recorded field | Value |
|---|---|
| &#47;operationId | getCommunication |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | recipient |
| &#47;parameters&#47;0&#47;required | false |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;parameters&#47;1&#47;in | query |
| &#47;parameters&#47;1&#47;name | assignment |
| &#47;parameters&#47;1&#47;required | false |
| &#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;parameters&#47;2&#47;in | query |
| &#47;parameters&#47;2&#47;name | progress |
| &#47;parameters&#47;2&#47;required | false |
| &#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;parameters&#47;3&#47;in | query |
| &#47;parameters&#47;3&#47;name | status |
| &#47;parameters&#47;3&#47;required | false |
| &#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;parameters&#47;4&#47;in | query |
| &#47;parameters&#47;4&#47;name | decision |
| &#47;parameters&#47;4&#47;required | false |
| &#47;parameters&#47;4&#47;schema&#47;type | string |
| &#47;parameters&#47;5&#47;in | query |
| &#47;parameters&#47;5&#47;name | result |
| &#47;parameters&#47;5&#47;required | false |
| &#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;parameters&#47;6&#47;in | query |
| &#47;parameters&#47;6&#47;name | handoff |
| &#47;parameters&#47;6&#47;required | false |
| &#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | The effective recipient profile with every contributing revision&#44; conflicts and freshness&#46; Query values override this view only&#46; No record is authored&#46; |

## `POST /v1/communication`

| Recorded field | Value |
|---|---|
| &#47;operationId | postCommunication |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CommunicationSetRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Replace or reset one scoped communication profile with its expected prior hash&#46; Only its owner or an explicit delegate may change it&#59; stale edits return conflict&#46; |

## `POST /v1/communication/delegations`

| Recorded field | Value |
|---|---|
| &#47;operationId | postCommunicationDelegations |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CommunicationDelegateRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | A person grants or revokes permission to change one scope of their communication setting&#44; with the expected prior hash&#46; |

## `GET /v1/companions`

| Recorded field | Value |
|---|---|
| &#47;operationId | getCompanions |
| &#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CompanionsResponse |
| &#47;responses&#47;200&#47;description | A versioned read result with explicit unavailable&#44; historical&#44; current or different basis&#46; Generation grants no authority or authored clearance&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Installed readable coverage&#44; exact collection snapshots and declared destinations&#59; no adoption is inferred&#46; |

## `GET /v1/companions/compare`

| Recorded field | Value |
|---|---|
| &#47;operationId | getCompanionsCompare |
| &#47;parameters&#47;0&#47;description | item or nested list at a revision&#59; current or all&#45;revisions at a snapshot&#46; Comparison selects a declaration or destination&#46; |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | selection |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;parameters&#47;1&#47;description | Exact retained source commit for a declared repository item&#59; no clone&#45;head substitution&#46; |
| &#47;parameters&#47;1&#47;in | query |
| &#47;parameters&#47;1&#47;name | commit |
| &#47;parameters&#47;1&#47;required | false |
| &#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;parameters&#47;2&#47;description | Internal by default&#46; A requested audience grants no access&#59; unsupported audiences are unavailable&#46; |
| &#47;parameters&#47;2&#47;in | query |
| &#47;parameters&#47;2&#47;name | audience |
| &#47;parameters&#47;2&#47;required | false |
| &#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CompanionComparison |
| &#47;responses&#47;200&#47;description | A versioned read result with explicit unavailable&#44; historical&#44; current or different basis&#46; Generation grants no authority or authored clearance&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Fresh byte and membership comparison without repair&#44; generated pins or authored clearance&#46; |

## `GET /v1/companions/render`

| Recorded field | Value |
|---|---|
| &#47;operationId | getCompanionsRender |
| &#47;parameters&#47;0&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | generator |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;parameters&#47;1&#47;description | item or nested list at a revision&#59; current or all&#45;revisions at a snapshot&#46; Comparison selects a declaration or destination&#46; |
| &#47;parameters&#47;1&#47;in | query |
| &#47;parameters&#47;1&#47;name | selection |
| &#47;parameters&#47;1&#47;required | true |
| &#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;parameters&#47;2&#47;description | Exact immutable record revision&#59; never follows a successor&#46; |
| &#47;parameters&#47;2&#47;in | query |
| &#47;parameters&#47;2&#47;name | revision |
| &#47;parameters&#47;2&#47;required | false |
| &#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;parameters&#47;3&#47;description | Exact record or manifest snapshot returned by coverage&#46; Refresh and select again if unavailable&#46; |
| &#47;parameters&#47;3&#47;in | query |
| &#47;parameters&#47;3&#47;name | snapshot |
| &#47;parameters&#47;3&#47;required | false |
| &#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;parameters&#47;4&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;4&#47;in | query |
| &#47;parameters&#47;4&#47;name | page |
| &#47;parameters&#47;4&#47;required | false |
| &#47;parameters&#47;4&#47;schema&#47;default | 1 |
| &#47;parameters&#47;4&#47;schema&#47;minimum | 1 |
| &#47;parameters&#47;4&#47;schema&#47;type | integer |
| &#47;parameters&#47;5&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;5&#47;in | query |
| &#47;parameters&#47;5&#47;name | field |
| &#47;parameters&#47;5&#47;required | false |
| &#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;parameters&#47;6&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;6&#47;in | query |
| &#47;parameters&#47;6&#47;name | index |
| &#47;parameters&#47;6&#47;required | false |
| &#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;parameters&#47;7&#47;description | Internal by default&#46; A requested audience grants no access&#59; unsupported audiences are unavailable&#46; |
| &#47;parameters&#47;7&#47;in | query |
| &#47;parameters&#47;7&#47;name | audience |
| &#47;parameters&#47;7&#47;required | false |
| &#47;parameters&#47;7&#47;schema&#47;type | string |
| &#47;parameters&#47;8&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;8&#47;in | query |
| &#47;parameters&#47;8&#47;name | contract |
| &#47;parameters&#47;8&#47;required | false |
| &#47;parameters&#47;8&#47;schema&#47;default | 1 |
| &#47;parameters&#47;8&#47;schema&#47;minimum | 1 |
| &#47;parameters&#47;8&#47;schema&#47;type | integer |
| &#47;parameters&#47;9&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;9&#47;in | query |
| &#47;parameters&#47;9&#47;name | declaration |
| &#47;parameters&#47;9&#47;required | false |
| &#47;parameters&#47;9&#47;schema&#47;type | string |
| &#47;parameters&#47;10&#47;description | Exact retained source commit for a declared repository item&#59; no clone&#45;head substitution&#46; |
| &#47;parameters&#47;10&#47;in | query |
| &#47;parameters&#47;10&#47;name | commit |
| &#47;parameters&#47;10&#47;required | false |
| &#47;parameters&#47;10&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CompanionResponse |
| &#47;responses&#47;200&#47;description | A versioned read result with explicit unavailable&#44; historical&#44; current or different basis&#46; Generation grants no authority or authored clearance&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Render one explicitly selected authorized item or collection&#59; currentness describes its exact basis&#44; not acceptance&#46; |

## `GET /v1/companions/sources`

| Recorded field | Value |
|---|---|
| &#47;operationId | getCompanionsSources |
| &#47;parameters&#47;0&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | generator |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;parameters&#47;1&#47;description | item or nested list at a revision&#59; current or all&#45;revisions at a snapshot&#46; Comparison selects a declaration or destination&#46; |
| &#47;parameters&#47;1&#47;in | query |
| &#47;parameters&#47;1&#47;name | selection |
| &#47;parameters&#47;1&#47;required | true |
| &#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;parameters&#47;2&#47;description | Exact immutable record revision&#59; never follows a successor&#46; |
| &#47;parameters&#47;2&#47;in | query |
| &#47;parameters&#47;2&#47;name | revision |
| &#47;parameters&#47;2&#47;required | false |
| &#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;parameters&#47;3&#47;description | Exact record or manifest snapshot returned by coverage&#46; Refresh and select again if unavailable&#46; |
| &#47;parameters&#47;3&#47;in | query |
| &#47;parameters&#47;3&#47;name | snapshot |
| &#47;parameters&#47;3&#47;required | false |
| &#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;parameters&#47;4&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;4&#47;in | query |
| &#47;parameters&#47;4&#47;name | page |
| &#47;parameters&#47;4&#47;required | false |
| &#47;parameters&#47;4&#47;schema&#47;default | 1 |
| &#47;parameters&#47;4&#47;schema&#47;minimum | 1 |
| &#47;parameters&#47;4&#47;schema&#47;type | integer |
| &#47;parameters&#47;5&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;5&#47;in | query |
| &#47;parameters&#47;5&#47;name | field |
| &#47;parameters&#47;5&#47;required | false |
| &#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;parameters&#47;6&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;6&#47;in | query |
| &#47;parameters&#47;6&#47;name | index |
| &#47;parameters&#47;6&#47;required | false |
| &#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;parameters&#47;7&#47;description | Internal by default&#46; A requested audience grants no access&#59; unsupported audiences are unavailable&#46; |
| &#47;parameters&#47;7&#47;in | query |
| &#47;parameters&#47;7&#47;name | audience |
| &#47;parameters&#47;7&#47;required | false |
| &#47;parameters&#47;7&#47;schema&#47;type | string |
| &#47;parameters&#47;8&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;8&#47;in | query |
| &#47;parameters&#47;8&#47;name | contract |
| &#47;parameters&#47;8&#47;required | false |
| &#47;parameters&#47;8&#47;schema&#47;default | 1 |
| &#47;parameters&#47;8&#47;schema&#47;minimum | 1 |
| &#47;parameters&#47;8&#47;schema&#47;type | integer |
| &#47;parameters&#47;9&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;parameters&#47;9&#47;in | query |
| &#47;parameters&#47;9&#47;name | declaration |
| &#47;parameters&#47;9&#47;required | false |
| &#47;parameters&#47;9&#47;schema&#47;type | string |
| &#47;parameters&#47;10&#47;description | Exact retained source commit for a declared repository item&#59; no clone&#45;head substitution&#46; |
| &#47;parameters&#47;10&#47;in | query |
| &#47;parameters&#47;10&#47;name | commit |
| &#47;parameters&#47;10&#47;required | false |
| &#47;parameters&#47;10&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CompanionResponse |
| &#47;responses&#47;200&#47;description | A versioned read result with explicit unavailable&#44; historical&#44; current or different basis&#46; Generation grants no authority or authored clearance&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Read the exact permitted source basis without invoking a generator&#59; local inspectors stay local&#46; |

## `GET /v1/context/original/{pin}`

| Recorded field | Value |
|---|---|
| &#47;operationId | getContextOriginalPin |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | pin |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | ContextReadResponse&#58; self&#45;contained pinned identity and exact bytes from immutable records or a matching working&#45;tree document&#46; Rechecks access and hashes&#59; writes nothing&#46; |

## `GET /v1/context/work/{key}`

| Recorded field | Value |
|---|---|
| &#47;operationId | getContextWorkKey |
| &#47;parameters&#47;0&#47;description | Optional context allocation in canonical payload bytes&#44; separate from a run spending cap&#46; Mandatory originals&#44; manifest and accounting must fit or the package is refused&#46; |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | maxBytes |
| &#47;parameters&#47;0&#47;required | false |
| &#47;parameters&#47;0&#47;schema&#47;minimum | 1 |
| &#47;parameters&#47;0&#47;schema&#47;type | integer |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | ContextResponse&#58; compute a deterministic work context package and exact originals&#59; writes nothing&#46; |

## `GET /v1/context/work/{key}/brief`

| Recorded field | Value |
|---|---|
| &#47;operationId | getContextWorkKeyBrief |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | recipient |
| &#47;parameters&#47;0&#47;required | false |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;parameters&#47;1&#47;in | query |
| &#47;parameters&#47;1&#47;name | assignment |
| &#47;parameters&#47;1&#47;required | false |
| &#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;parameters&#47;2&#47;in | query |
| &#47;parameters&#47;2&#47;name | progress |
| &#47;parameters&#47;2&#47;required | false |
| &#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;parameters&#47;3&#47;in | query |
| &#47;parameters&#47;3&#47;name | status |
| &#47;parameters&#47;3&#47;required | false |
| &#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;parameters&#47;4&#47;in | query |
| &#47;parameters&#47;4&#47;name | decision |
| &#47;parameters&#47;4&#47;required | false |
| &#47;parameters&#47;4&#47;schema&#47;type | string |
| &#47;parameters&#47;5&#47;in | query |
| &#47;parameters&#47;5&#47;name | result |
| &#47;parameters&#47;5&#47;required | false |
| &#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;parameters&#47;6&#47;in | query |
| &#47;parameters&#47;6&#47;name | handoff |
| &#47;parameters&#47;6&#47;required | false |
| &#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;parameters&#47;7&#47;description | Optional context allocation in canonical payload bytes&#44; separate from a run spending cap&#46; Mandatory originals&#44; manifest and accounting must fit or the package is refused&#46; |
| &#47;parameters&#47;7&#47;in | query |
| &#47;parameters&#47;7&#47;name | maxBytes |
| &#47;parameters&#47;7&#47;required | false |
| &#47;parameters&#47;7&#47;schema&#47;minimum | 1 |
| &#47;parameters&#47;7&#47;schema&#47;type | integer |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | BriefResponse&#58; measured agent brief&#59; mandatory originals inline&#44; named optional sources withheld unless a context allocation is given and they fit&#46; Writes nothing&#46; |

## `GET /v1/context/{manifest}/sources/{source}`

| Recorded field | Value |
|---|---|
| &#47;operationId | getContextManifestSourcesSource |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | manifest |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;parameters&#47;1&#47;in | path |
| &#47;parameters&#47;1&#47;name | source |
| &#47;parameters&#47;1&#47;required | true |
| &#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | ContextReadResponse&#58; legacy manifest handles return original&#45;unavailable after access checking&#59; prepare again for a self&#45;contained pin&#46; |

## `GET /v1/decisions`

| Recorded field | Value |
|---|---|
| &#47;operationId | getDecisions |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Current decisions&#46; |

## `GET /v1/decisions/{id}`

| Recorded field | Value |
|---|---|
| &#47;operationId | getDecisionsId |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | One decision by the id of any of its revisions&#46; |

## `GET /v1/devices`

| Recorded field | Value |
|---|---|
| &#47;operationId | getDevices |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Devices known to the estate and whether this machine holds their keys&#46; |

## `POST /v1/devices`

| Recorded field | Value |
|---|---|
| &#47;operationId | postDevices |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;DeviceCreateRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Create a device on the runtime&#39;s machine&#46; |

## `POST /v1/devices/admit`

| Recorded field | Value |
|---|---|
| &#47;operationId | postDevicesAdmit |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;AdmitRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Admit a device&#46; |

## `POST /v1/devices/recovery`

| Recorded field | Value |
|---|---|
| &#47;operationId | postDevicesRecovery |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;DeviceRecoveryRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Make the operator&#39;s recovery device&#58; its key is written once&#44; encrypted&#44; for offline keeping&#44; and an admitted device of the operator admits it&#46; |

## `POST /v1/devices/rename`

| Recorded field | Value |
|---|---|
| &#47;operationId | postDevicesRename |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;DeviceRenameRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Rename a device&#44; keeping its identity&#46; |

## `POST /v1/devices/revoke`

| Recorded field | Value |
|---|---|
| &#47;operationId | postDevicesRevoke |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;RevokeRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Revoke a device&#39;s admission from now on&#46; |

## `POST /v1/devices/rotate`

| Recorded field | Value |
|---|---|
| &#47;operationId | postDevicesRotate |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;DeviceRotateRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Give a device a new key&#44; keeping its identity&#46; |

## `GET /v1/documents`

| Recorded field | Value |
|---|---|
| &#47;operationId | getDocuments |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | The declared living documents with their derived state&#58; current&#44; due with every trigger newer than the latest clearance&#44; or unavailable with the contributions no installed pack provides&#46; |

## `POST /v1/documents/{id}/reviews`

| Recorded field | Value |
|---|---|
| &#47;operationId | postDocumentsIdReviews |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;DocumentReviewRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Review a living document by its declaration&#58; the runtime reads the content&#44; records the covered paths&#39; identity and the triggers the review answers&#44; runs the declared checks&#44; and refuses a clearance a failed check or the declaration&#39;s clearedBy forbids&#46; |

## `GET /v1/estate-policies`

| Recorded field | Value |
|---|---|
| &#47;operationId | getEstate&#45;policies |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | PolicyResponse&#58; declarations&#44; exception history and findings at recorded time&#44; with the explicit basis&#59; creates no record&#46; |

## `POST /v1/estate-policies`

| Recorded field | Value |
|---|---|
| &#47;operationId | postEstate&#45;policies |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | PolicyDeclare&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |

## `GET /v1/estate-policies/access`

| Recorded field | Value |
|---|---|
| &#47;operationId | getEstate&#45;policiesAccess |
| &#47;parameters&#47;0&#47;description | Acting principal whose current policy action admission is inspected&#59; exact bounds are rechecked for every write&#46; |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | principal |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | PolicyAccess&#58; admitted policy actions&#44; with exact target bounds rechecked at submission&#46; |

## `GET /v1/estate-policies/evaluate`

| Recorded field | Value |
|---|---|
| &#47;operationId | getEstate&#45;policiesEvaluate |
| &#47;parameters&#47;0&#47;description | Explicit canonical UTC preview time&#46; Never authorizes submission&#46; |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | asOf |
| &#47;parameters&#47;0&#47;required | false |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;parameters&#47;1&#47;description | Exact release record id selecting its product&#44; version and commit&#46; |
| &#47;parameters&#47;1&#47;in | query |
| &#47;parameters&#47;1&#47;name | release |
| &#47;parameters&#47;1&#47;required | false |
| &#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | PolicyResponse evaluated from server sources&#44; with optional canonical UTC asOf and exact release record preview&#59; never authorizes submission&#46; |

## `GET /v1/estate-policies/kinds`

| Recorded field | Value |
|---|---|
| &#47;operationId | getEstate&#45;policiesKinds |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Installed kind versions&#44; implementation hashes&#44; supported selections&#44; sources and bounds&#46; |

## `POST /v1/estate-policies/{key}/exceptions`

| Recorded field | Value |
|---|---|
| &#47;operationId | postEstate&#45;policiesKeyExceptions |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | key |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | PolicyExcept&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |

## `POST /v1/estate-policies/{key}/exceptions/{id}/revoke`

| Recorded field | Value |
|---|---|
| &#47;operationId | postEstate&#45;policiesKeyExceptionsIdRevoke |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | key |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;parameters&#47;1&#47;in | path |
| &#47;parameters&#47;1&#47;name | id |
| &#47;parameters&#47;1&#47;required | true |
| &#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | PolicyRevokeException&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |

## `POST /v1/estate-policies/{key}/retire`

| Recorded field | Value |
|---|---|
| &#47;operationId | postEstate&#45;policiesKeyRetire |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | key |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | PolicyRetire&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |

## `POST /v1/estate-policies/{key}/revisions`

| Recorded field | Value |
|---|---|
| &#47;operationId | postEstate&#45;policiesKeyRevisions |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | key |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | PolicyRevise&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |

## `GET /v1/estate-policies/{key}/view`

| Recorded field | Value |
|---|---|
| &#47;operationId | getEstate&#45;policiesKeyView |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | key |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | PolicyResponse for a key or revision id&#44; including recurring&#45;duty deadlines&#44; completion evidence and exceptions&#46; |

## `POST /v1/estate/export`

| Recorded field | Value |
|---|---|
| &#47;operationId | postEstateExport |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ExportRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;409&#47;description | Not carried out &#40;Problem&#41;&#46; |
| &#47;summary | Write a verified export to a path on the runtime&#39;s machine&#59; no keys or authority travel with it&#46; |

## `POST /v1/estate/upgrade`

| Recorded field | Value |
|---|---|
| &#47;operationId | postEstateUpgrade |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;UpgradeRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;409&#47;description | Not carried out &#40;Problem&#41;&#46; |
| &#47;summary | Plan or apply an upgrade to a published release&#46; |

## `GET /v1/goals`

| Recorded field | Value |
|---|---|
| &#47;operationId | getGoals |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | The estate&#39;s current goals&#58; each one&#39;s current revision&#44; title&#44; status and sponsor&#46; |

## `POST /v1/goals`

| Recorded field | Value |
|---|---|
| &#47;operationId | postGoals |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalProposeRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Propose a goal&#44; or a revision of one &#40;supersedes&#41;&#44; from the goal record&#39;s body&#59; a proposal authorizes and funds nothing&#44; and a revision starts again from its sponsor&#39;s activation&#46; Anyone may propose a new goal&#59; a revision is recorded only by the sponsor of the revision it supersedes when the sponsor is a person&#44; and by an operator when an agent sponsors it&#46; The body&#39;s limits&#46;deadline and limits&#46;consecutiveFailures are validated and recorded but not yet enforced&#46; |

## `GET /v1/goals/{id}`

| Recorded field | Value |
|---|---|
| &#47;operationId | getGoalsId |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | A goal by the id of any of its revisions&#58; its objective&#44; sponsor&#44; status and granted budget&#44; each condition&#39;s verdict &#40;met&#44; not&#45;met or unknown&#41; with the records it read beside its current evaluation&#39;s verdict&#44; its steps&#44; and what stands between it and its acceptance&#46; |

## `POST /v1/goals/{id}/accept`

| Recorded field | Value |
|---|---|
| &#47;operationId | postGoalsIdAccept |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalAcceptRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Accept a goal at its current revision&#58; by its sponsor when the sponsor is a person&#44; or the principal the sponsor delegated an ordinary goal&#39;s acceptance to&#44; and by an operator&#44; only an operator&#44; when an agent sponsors it&#59; never by its evaluator or a step&#39;s author&#44; and only once its current evaluation finds every condition met and every record predicate holds&#46; |

## `POST /v1/goals/{id}/activate`

| Recorded field | Value |
|---|---|
| &#47;operationId | postGoalsIdActivate |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalActionRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Activate a proposed goal&#44; which grants its budget&#58; by its sponsor&#44; a person who holds the operator role or a grant of the spending scope&#44; or by an operator when an agent sponsors it&#59; an agent never activates a goal&#46; |

## `POST /v1/goals/{id}/evaluate`

| Recorded field | Value |
|---|---|
| &#47;operationId | postGoalsIdEvaluate |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalEvaluateRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Record a goal&#39;s evaluation&#44; condition by condition&#58; the runtime&#39;s verdict for each record predicate and the evaluator&#39;s for each evidence statement&#44; with the records each read&#44; by a principal other than any step&#39;s author&#46; |

## `GET /v1/goals/{id}/next`

| Recorded field | Value |
|---|---|
| &#47;operationId | getGoalsIdNext |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | A goal&#39;s steps routed from the runtime&#39;s machine &#40;vericairn&#46;next&#47;1&#41;&#58; each step&#39;s state&#44; what holds each planned one&#44; and for each eligible one the principal its role&#39;s roster reaches &#40;an agent where a launcher for its harness&#44; its admitted key and the goal&#39;s budget allow&#44; a person where needed&#44; a reserved action only to one who holds it&#41; or why none is available&#44; with whether the budget allows it and the custody guard for an agent&#59; the goal&#39;s ledger and asks&#46; Read&#45;only&#59; it never falls back to a principal the roster does not name&#46; |

## `POST /v1/goals/{id}/pause`

| Recorded field | Value |
|---|---|
| &#47;operationId | postGoalsIdPause |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalActionRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Pause an active goal&#59; its sponsor&#39;s&#44; or an operator&#39;s for a goal an agent sponsors&#46; Prevents new runs but does not stop runs already in flight&#59; request each run&#39;s stop separately&#46; |

## `POST /v1/goals/{id}/resume`

| Recorded field | Value |
|---|---|
| &#47;operationId | postGoalsIdResume |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalActionRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Resume a paused&#44; budget&#45;limited or blocked goal&#44; which grants its budget again&#59; by whoever may activate it&#46; |

## `POST /v1/goals/{id}/step`

| Recorded field | Value |
|---|---|
| &#47;operationId | postGoalsIdStep |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalStepRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | GoalStepResponse&#58; what was recorded and started&#59; a problem inside when nothing was dispatched&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Take an active goal&#39;s next step&#44; one at a time&#44; as an operator&#44; the goal&#39;s sponsor when a person&#44; or a principal holding local&#45;engineering&#58; its first eligible step whose route acts&#44; routed from the runtime&#39;s machine&#46; An agent&#39;s step goes on with the assignment that holds it&#44; or is assigned to the agent together with its run&#39;s intent after the worker&#39;s whole preflight&#44; and its run starts through the worker&#59; every refusal before the intent&#44; custody&#44; the budget&#44; the repository&#44; the launcher or the workspace&#44; records nothing&#44; the assignment included&#46; A person&#39;s step is assigned to the person and asked of them&#44; the recommendation first&#44; the two recorded together&#59; a step no principal is available for is asked of the sponsor&#44; never given to a contributor the roster does not name&#46; It activates&#44; funds&#44; accepts and answers nothing&#46; |

## `POST /v1/goals/{id}/stop`

| Recorded field | Value |
|---|---|
| &#47;operationId | postGoalsIdStop |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalActionRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Stop a goal&#58; its sponsor&#44; or an operator for a goal an agent sponsors&#44; ends it as abandoned and prevents new runs&#46; Runs already in flight are not stopped&#59; request each run&#39;s stop separately&#46; |

## `GET /v1/handoff`

| Recorded field | Value |
|---|---|
| &#47;operationId | getHandoff |
| &#47;parameters&#47;0&#47;description | Optional context allocation in canonical payload bytes&#44; separate from a run spending cap&#46; Mandatory originals&#44; manifest and accounting must fit or the package is refused&#46; |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | maxBytes |
| &#47;parameters&#47;0&#47;required | false |
| &#47;parameters&#47;0&#47;schema&#47;minimum | 1 |
| &#47;parameters&#47;0&#47;schema&#47;type | integer |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | HandoffResponse&#58; a complete deterministic hand&#45;over and measured package&#44; or a problem naming every missing or invalid source&#46; Read&#45;only&#59; all records retained&#59; no synthesis or authority&#46; Optional maxBytes refuses overflow&#46; |

## `POST /v1/harness-qualifications`

| Recorded field | Value |
|---|---|
| &#47;operationId | postHarness&#45;qualifications |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;HarnessQualifyRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Machine&#45;local only&#58; an authorized human qualifies one exact installed native harness version from six local probe digests&#46; Paired local&#45;network devices are refused before probing&#59; local socket and signed loopback retain their authority checks&#46; No agent is launched and no raw probe output is recorded&#46; Remote is not built&#46; |

## `GET /v1/health`

| Recorded field | Value |
|---|---|
| &#47;operationId | getHealth |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | The daemon&#39;s identity and what it serves&#46; |

## `GET /v1/launchers`

| Recorded field | Value |
|---|---|
| &#47;operationId | getLaunchers |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | The coding&#45;agent launchers available to the runtime on its machine&#44; providers of vericairn&#46;launcher&#46;agent&#47;1&#44; with their settings and limitations&#59; a generic launcher is labeled as not complete agent support&#46; |

## `GET /v1/objects`

| Recorded field | Value |
|---|---|
| &#47;operationId | getObjects |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Objects held&#46; |

## `POST /v1/objects`

| Recorded field | Value |
|---|---|
| &#47;operationId | postObjects |
| &#47;requestBody&#47;content&#47;application&#126;1octet&#45;stream&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ObjectBytes |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;413&#47;description | Above the object size limit &#40;Problem&#41;&#46; |
| &#47;summary | Store bytes as an object named by their hash&#46; |

## `GET /v1/objects/verify`

| Recorded field | Value |
|---|---|
| &#47;operationId | getObjectsVerify |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Verify every object against its name&#46; |

## `GET /v1/objects/{hash}`

| Recorded field | Value |
|---|---|
| &#47;operationId | getObjectsHash |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | hash |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | An object&#39;s verified bytes&#46; |

## `GET /v1/operator/actions`

| Recorded field | Value |
|---|---|
| &#47;operationId | getOperatorActions |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | recipient |
| &#47;parameters&#47;0&#47;required | false |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;parameters&#47;1&#47;in | query |
| &#47;parameters&#47;1&#47;name | assignment |
| &#47;parameters&#47;1&#47;required | false |
| &#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;parameters&#47;2&#47;in | query |
| &#47;parameters&#47;2&#47;name | progress |
| &#47;parameters&#47;2&#47;required | false |
| &#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;parameters&#47;3&#47;in | query |
| &#47;parameters&#47;3&#47;name | status |
| &#47;parameters&#47;3&#47;required | false |
| &#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;parameters&#47;4&#47;in | query |
| &#47;parameters&#47;4&#47;name | decision |
| &#47;parameters&#47;4&#47;required | false |
| &#47;parameters&#47;4&#47;schema&#47;type | string |
| &#47;parameters&#47;5&#47;in | query |
| &#47;parameters&#47;5&#47;name | result |
| &#47;parameters&#47;5&#47;required | false |
| &#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;parameters&#47;6&#47;in | query |
| &#47;parameters&#47;6&#47;name | handoff |
| &#47;parameters&#47;6&#47;required | false |
| &#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | OperatorActionsResponse&#58; text in the selected communication settings&#44; complete canonical JSON &#40;vericairn&#46;operator&#45;actions&#47;1&#41;&#44; and hasErrors&#46; Items retain their sources&#44; reasons&#44; waitsOn&#44; inspection command and record&#45;authoring commands with argument arrays and required JSON bodies&#59; null body fields require operator input&#46; No signing or provider access occurs&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Every open operator ask&#44; pending operator acceptance and recorded waiting reserved action&#44; with reasons&#44; dependencies&#44; exact record&#45;authoring commands and required JSON input &#40;vericairn&#46;operator&#45;actions&#47;1&#41;&#46; Text follows communication settings&#44; defaulting to the estate&#39;s root operator&#59; JSON always contains the complete queue&#46; Record errors suppress authoring commands&#46; This read neither signs nor records&#44; inspects keys or calls providers&#46; Not exposed through MCP&#46; |

## `POST /v1/operator/record`

| Recorded field | Value |
|---|---|
| &#47;operationId | postOperatorRecord |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;OperatorRecordRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;OperatorRecordResponse |
| &#47;responses&#47;200&#47;description | OperatorRecordResponse&#58; the signed record&#44; retained branch and head&#44; last recorded change and exact recovery step&#46; Only ok with completed means the pinned merge and target checks passed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Sign the operator&#39;s own record on their admitted device&#44; commit that record alone on a new estate branch&#44; and scan&#44; push&#44; open&#44; wait and merge through recorded changes operations&#46; Requires a clean repository&#45;synced estate at record format 3 and declared checks&#46; Identical input resumes the original action&#59; uncertain effects are observed before retry&#46; waitSeconds bounds each check phase from 0 to 240 &#40;default 120&#41;&#46; Synchronous only&#59; not exposed through MCP&#46; |

## `POST /v1/pairing/confirm`

| Recorded field | Value |
|---|---|
| &#47;operationId | postPairingConfirm |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PairingConfirm |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | PairingCompleted&#58; the device record and the admission&#44; with the runtime&#39;s MAC over the exchange&#46; |
| &#47;responses&#47;401&#47;description | No such exchange is open&#44; the code expired or was used&#44; or the confirmation does not match the code &#40;Problem with code denied&#41;&#59; nothing is recorded&#46; |
| &#47;responses&#47;422&#47;description | The device&#39;s signature does not verify&#44; or the estate refused its records &#40;Problem&#44; with findings when rejected&#41;&#59; nothing is recorded&#46; |
| &#47;summary | Complete a pairing&#58; the device&#39;s key&#44; descriptive identity and signature of its record&#44; with its MAC with the code&#39;s device key&#59; the runtime records the device and its admission&#44; and answers with its MAC over the whole exchange&#46; |

## `POST /v1/pairing/hello`

| Recorded field | Value |
|---|---|
| &#47;operationId | postPairingHello |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PairingHello |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | PairingChallenge&#58; the runtime&#39;s identity&#44; the certificate on this connection and the device record to sign&#44; with the runtime&#39;s MAC&#59; uses one of the code&#39;s attempts&#46; |
| &#47;responses&#47;401&#47;description | The code is unknown&#44; expired&#44; used or out of attempts&#44; or the request did not come over the local network &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;422&#47;description | A hello without a handle or a nonce &#40;Problem&#41;&#46; |
| &#47;summary | Start a pairing over the local network with a code&#39;s handle and a nonce&#58; the runtime answers with its identity&#44; the certificate on this connection&#44; the device record it will write&#44; and its MAC with the code&#39;s runtime key&#46; |

## `POST /v1/pairings`

| Recorded field | Value |
|---|---|
| &#47;operationId | postPairings |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PairingIssueRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | PairingIssued&#58; the code&#44; shown once&#44; with the principal&#44; the profiles&#44; the expiry&#44; the attempts&#44; the address and the runtime identity&#39;s fingerprint&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;409&#47;description | No local&#45;network profile is served&#44; or too many codes wait &#40;Problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused&#58; an unknown principal or profile&#44; no administrator&#39;s authority&#44; or no admitted device of the administrator here to sign the admission &#40;Problem&#41;&#46; |
| &#47;summary | Issue a pairing code under an administrator&#39;s authority &#40;the operator&#44; or a principal with the administer scope whose admitted device here signs the admission&#41; for a principal and the connectivity profiles its device will be permitted&#58; 16 characters&#44; 80 secret bits&#44; for ten minutes&#44; five attempts and one device&#46; On the runtime&#39;s own machine only&#46; |

## `GET /v1/progress`

| Recorded field | Value |
|---|---|
| &#47;operationId | getProgress |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | The progress report&#58; where the estate stands&#44; the work orders by state&#44; each published release and what it contained&#44; what comes next by release&#44; the next eligible work and what waits on the operator&#59; text and canonical JSON with compact current attention references&#46; Record&#45;derived sections agree for the same records&#59; repository checks&#44; latest local agent claims and worker observations are labelled separately and can differ by runtime&#46; |

## `GET /v1/providers`

| Recorded field | Value |
|---|---|
| &#47;operationId | getProviders |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | The provider and adapter implementations available to the runtime on its machine&#44; with their declared settings&#44; destinations and served locations&#58; release providers&#44; source adapters&#44; coding&#45;agent launchers and workspace providers&#46; |

## `GET /v1/reconcile`

| Recorded field | Value |
|---|---|
| &#47;operationId | getReconcile |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Reconcile tasks left by diverged copies&#46; |

## `GET /v1/records`

| Recorded field | Value |
|---|---|
| &#47;operationId | getRecords |
| &#47;parameters&#47;0&#47;description | A record type&#44; for example work&#45;order&#46; |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | type |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | The current records of one type&#44; oldest first&#46; |

## `POST /v1/records`

| Recorded field | Value |
|---|---|
| &#47;operationId | postRecords |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;NewRecordRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Author a record&#59; signed with the author&#39;s admitted device on the runtime&#39;s machine&#59; rejected with findings when the estate would be invalid&#46; |

## `GET /v1/records/{id}`

| Recorded field | Value |
|---|---|
| &#47;operationId | getRecordsId |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | A record by id&#46; |

## `GET /v1/recover/inspect`

| Recorded field | Value |
|---|---|
| &#47;operationId | getRecoverInspect |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | What this copy holds&#44; for recovery&#46; |

## `GET /v1/release-bases/{hash}`

| Recorded field | Value |
|---|---|
| &#47;operationId | getRelease&#45;basesHash |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | hash |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | Read retained exact release&#45;admission inputs and findings&#59; historical evidence grants no new authority&#46; |

## `GET /v1/releases`

| Recorded field | Value |
|---|---|
| &#47;operationId | getReleases |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Current release records&#46; |

## `POST /v1/releases/observe`

| Recorded field | Value |
|---|---|
| &#47;operationId | postReleasesObserve |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ReleaseObserveRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | ReleaseResponse&#58; what was recorded and what the provider showed&#59; a problem inside when nothing could proceed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Observe a release&#39;s run&#44; draft and publication and record what settled &#40;the operation revision&#44; the candidate&#44; the observed publication&#41;&#59; or cancel an intent that had no effect&#46; |

## `POST /v1/releases/submit`

| Recorded field | Value |
|---|---|
| &#47;operationId | postReleasesSubmit |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ReleaseSubmitRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | ReleaseResponse&#58; what was recorded and what the provider showed&#59; a problem inside when nothing could proceed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Submit a release as intent before effect&#58; the operation record is written&#44; then the build is dispatched through the provider the request names or the product declares&#59; nothing is published&#44; and a provider with no implementation here is refused&#46; |

## `POST /v1/resume`

| Recorded field | Value |
|---|---|
| &#47;operationId | postResume |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;HandoffPackage |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | ResumeResponse&#58; every difference&#44; or an invalid&#45;handoff problem&#46; This POST is a read&#59; it authors no record&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Compare a retained hand&#45;over with current records&#44; including every addition and supersession&#44; missing or changed originals&#44; the full estate pin and observed state&#46; The world may have moved&#59; grants no authority and restarts nothing&#46; |

## `GET /v1/runs`

| Recorded field | Value |
|---|---|
| &#47;operationId | getRuns |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | The agent runs&#44; each with its cap recorded before it started&#44; its observed usage once settled&#44; what it is charged &#40;its cap for every unit not reported&#44; and while in flight&#41;&#44; a stop&#39;s facts and worker&#45;observed signals with severity and attention references&#59; and the ledger of every goal with a run or active now&#46; |

## `POST /v1/runs`

| Recorded field | Value |
|---|---|
| &#47;operationId | postRuns |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;RunStartRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | RunResponse&#58; what was recorded and launched&#59; a problem inside when nothing was launched&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Start a coding agent&#39;s run on an assignment &#40;vericairn&#46;worker&#47;1&#41;&#58; refused while the goal is not active or its budget is spent&#44; while a device of an operator or of the goal&#39;s sponsor signs without presence on the runtime&#39;s machine and the sponsor recorded no custody exception in force for the goal&#44; and before anything is recorded when the launcher cannot run&#59; otherwise the workspace is prepared&#44; the intent recorded with the cap&#44; and the harness launched&#44; then observed and settled&#46; The principal recording the run &#40;by&#41; is the operator&#44; the goal&#39;s sponsor when the sponsor is a person&#44; or a principal with the local&#45;engineering scope&#46; Nothing is accepted&#46; |

## `POST /v1/runs/{id}/stop`

| Recorded field | Value |
|---|---|
| &#47;operationId | postRunsIdStop |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;RunStopRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | RunStopResponse&#58; the stop&#39;s facts as observed&#59; a problem inside when no stop was asked&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Ask a run to stop&#58; the request&#44; its transmission to the harness&#44; the harness&#39;s acknowledgment&#44; an escalation after the grace period and the observed containment are separate facts&#59; a stop that is not observed is reported as requested&#46; |

## `POST /v1/runtime/certificate`

| Recorded field | Value |
|---|---|
| &#47;operationId | postRuntimeCertificate |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CertificateRotateRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | CertificateRotated&#58; the new certificate&#39;s fingerprint and expiry&#44; the replaced one&#39;s&#44; whether open connections were closed&#44; and the new identity&#39;s fingerprint when it was replaced&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;409&#47;description | The daemon serves no TLS profile&#44; or no identity to replace &#40;Problem&#41;&#46; |
| &#47;summary | Replace the runtime certificate now&#58; paired devices keep working&#44; since the runtime identity signs the new one&#59; for a compromise every other open connection is closed too&#44; and identity replaces the runtime identity as well&#44; after which every device pairs again&#46; On the runtime&#39;s own machine only&#46; |

## `GET /v1/runtime/identity`

| Recorded field | Value |
|---|---|
| &#47;operationId | getRuntimeIdentity |
| &#47;parameters&#47;0&#47;description | 32 random bytes&#44; base64url&#44; fresh for each connection&#46; |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | nonce |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | RuntimeIdentityProof&#58; the identity key&#39;s signature over the estate&#44; the certificate on this connection and the nonce&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The runtime has no identity&#58; it serves no local&#45;network profile &#40;Problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | No nonce of 16 to 64 bytes &#40;Problem&#41;&#46; |
| &#47;summary | The runtime identity&#39;s proof for a nonce&#58; its signature over the estate&#44; the certificate this connection presented and the nonce&#44; which a paired device checks before any other request&#46; |

## `POST /v1/shutdown`

| Recorded field | Value |
|---|---|
| &#47;operationId | postShutdown |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;202&#47;description | The daemon will stop&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Ask the daemon to stop&#59; closing a client never stops it&#46; |

## `GET /v1/status`

| Recorded field | Value |
|---|---|
| &#47;operationId | getStatus |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Current status&#58; human text and canonical JSON&#44; identical on every machine for the same records&#46; Its compact attention references are derived from records alone&#46; |

## `POST /v1/status/observations`

| Recorded field | Value |
|---|---|
| &#47;operationId | postStatusObservations |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;EstateObserveRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Record a person&#39;s explicit observation of estate status&#44; with runtime&#45;supplied timestamp and hashes of the record set&#44; pin and status&#46; Agents cannot author it&#59; ordinary reads remain read&#45;only&#46; |

## `GET /v1/validate`

| Recorded field | Value |
|---|---|
| &#47;operationId | getValidate |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Validate every record and the manifest&#46; |

## `POST /v1/work/amend`

| Recorded field | Value |
|---|---|
| &#47;operationId | postWorkAmend |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;AmendRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Request an amendment&#59; it authorizes nothing&#46; |

## `POST /v1/work/assign`

| Recorded field | Value |
|---|---|
| &#47;operationId | postWorkAssign |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;AssignRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Delegate a work order to a contributor under a brief&#46; |

## `POST /v1/work/attempt`

| Recorded field | Value |
|---|---|
| &#47;operationId | postWorkAttempt |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;AttemptRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Record the start of an attempt&#46; |

## `POST /v1/work/progress`

| Recorded field | Value |
|---|---|
| &#47;operationId | postWorkProgress |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;WorkProgressRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | WorkProgressResponse&#58; the latest agent claim&#44; a written record only for a completed phase&#44; or a problem when refused&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Publish the contributor&#39;s latest progress claim on its own attempt&#46; Active notes replace local state&#59; a completed phase is recorded once&#44; and an identical completion retry returns its record&#46; Claims never establish worker liveness&#44; scope or acceptance&#46; |

## `POST /v1/work/result`

| Recorded field | Value |
|---|---|
| &#47;operationId | postWorkResult |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ResultRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Submit a result with exact duty proof bindings&#59; a duty result requires the current attempt hash and an admitted signature&#46; |

## `POST /v1/work/review`

| Recorded field | Value |
|---|---|
| &#47;operationId | postWorkReview |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ResultReviewRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | Review the expected exact result revision with its evidence read&#44; signed as the acting principal&#59; no acceptance is recorded&#46; |

## `POST /v1/work/takeover`

| Recorded field | Value |
|---|---|
| &#47;operationId | postWorkTakeover |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;TakeoverRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | TakeoverResponse&#58; the hand&#45;over recorded and the stops asked&#59; a problem inside when nothing was recorded&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | Hand a step an agent holds over to a person&#58; a new assignment of the step to the person&#44; carrying the agent&#39;s brief with the agent&#39;s assignment among its sources&#44; which holds the step from then on&#59; every run of the agent&#39;s assignment still in flight is asked to stop&#44; each stop&#39;s facts reported as observed&#46; Recorded by a person only&#44; the one taking the step over&#44; the sponsor of its goal or an operator&#44; never by an agent&#46; |

## `GET /v1/work/{key}`

| Recorded field | Value |
|---|---|
| &#47;operationId | getWorkKey |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | key |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | What an observer sees of a work order&#46; |

## `GET /v1/work/{key}/brief`

| Recorded field | Value |
|---|---|
| &#47;operationId | getWorkKeyBrief |
| &#47;parameters&#47;0&#47;in | query |
| &#47;parameters&#47;0&#47;name | recipient |
| &#47;parameters&#47;0&#47;required | false |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;parameters&#47;1&#47;in | query |
| &#47;parameters&#47;1&#47;name | assignment |
| &#47;parameters&#47;1&#47;required | false |
| &#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;parameters&#47;2&#47;in | query |
| &#47;parameters&#47;2&#47;name | progress |
| &#47;parameters&#47;2&#47;required | false |
| &#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;parameters&#47;3&#47;in | query |
| &#47;parameters&#47;3&#47;name | status |
| &#47;parameters&#47;3&#47;required | false |
| &#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;parameters&#47;4&#47;in | query |
| &#47;parameters&#47;4&#47;name | decision |
| &#47;parameters&#47;4&#47;required | false |
| &#47;parameters&#47;4&#47;schema&#47;type | string |
| &#47;parameters&#47;5&#47;in | query |
| &#47;parameters&#47;5&#47;name | result |
| &#47;parameters&#47;5&#47;required | false |
| &#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;parameters&#47;6&#47;in | query |
| &#47;parameters&#47;6&#47;name | handoff |
| &#47;parameters&#47;6&#47;required | false |
| &#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | The brief of a work order&#58; everything a contributor needs&#44; from the records alone&#46; |

## `POST /v1/work/{key}/placement`

| Recorded field | Value |
|---|---|
| &#47;operationId | postWorkKeyPlacement |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | key |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;WorkPlaceRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | WorkPlace&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |

## `GET /v1/writing-exceptions`

| Recorded field | Value |
|---|---|
| &#47;operationId | getWriting&#45;exceptions |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;summary | WritingExceptionsResponse&#58; registry history&#44; authors&#44; reasons&#44; scopes&#44; counts and exact warning and excepted locations with the basis&#46; |

## `POST /v1/writing-exceptions`

| Recorded field | Value |
|---|---|
| &#47;operationId | postWriting&#45;exceptions |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | WritingExceptionAdd&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |

## `GET /v1/writing-exceptions/{id}`

| Recorded field | Value |
|---|---|
| &#47;operationId | getWriting&#45;exceptionsId |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;summary | WritingExceptionsResponse for one entry identity&#44; including removal history and current matches&#59; creates no record&#46; |

## `POST /v1/writing-exceptions/{id}/remove`

| Recorded field | Value |
|---|---|
| &#47;operationId | postWriting&#45;exceptionsIdRemove |
| &#47;parameters&#47;0&#47;in | path |
| &#47;parameters&#47;0&#47;name | id |
| &#47;parameters&#47;0&#47;required | true |
| &#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;WritingExceptionRemoveRequest |
| &#47;requestBody&#47;required | true |
| &#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;summary | WritingExceptionRemove&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |

## Complete OpenAPI fields

| Recorded field | Value |
|---|---|
| &#47;components&#47;schemas&#47;AdmitRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;AdmitRequest&#47;properties&#47;device&#47;type | string |
| &#47;components&#47;schemas&#47;AdmitRequest&#47;properties&#47;note&#47;type | string |
| &#47;components&#47;schemas&#47;AdmitRequest&#47;properties&#47;passphrase&#47;type | string |
| &#47;components&#47;schemas&#47;AdmitRequest&#47;properties&#47;recovery&#47;type | string |
| &#47;components&#47;schemas&#47;AdmitRequest&#47;type | object |
| &#47;components&#47;schemas&#47;AmendRequest&#47;properties&#47;assignment&#47;type | string |
| &#47;components&#47;schemas&#47;AmendRequest&#47;properties&#47;body&#47;type | object |
| &#47;components&#47;schemas&#47;AmendRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;AmendRequest&#47;type | object |
| &#47;components&#47;schemas&#47;AssignRequest&#47;properties&#47;brief&#47;type | object |
| &#47;components&#47;schemas&#47;AssignRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;AssignRequest&#47;properties&#47;contributor&#47;type | string |
| &#47;components&#47;schemas&#47;AssignRequest&#47;properties&#47;note&#47;type | string |
| &#47;components&#47;schemas&#47;AssignRequest&#47;properties&#47;workOrder&#47;type | string |
| &#47;components&#47;schemas&#47;AssignRequest&#47;type | object |
| &#47;components&#47;schemas&#47;AttemptRequest&#47;properties&#47;assignment&#47;type | string |
| &#47;components&#47;schemas&#47;AttemptRequest&#47;properties&#47;at&#47;type | string |
| &#47;components&#47;schemas&#47;AttemptRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;AttemptRequest&#47;properties&#47;note&#47;type | string |
| &#47;components&#47;schemas&#47;AttemptRequest&#47;properties&#47;workspace&#47;type | string |
| &#47;components&#47;schemas&#47;AttemptRequest&#47;type | object |
| &#47;components&#47;schemas&#47;CertificateRotateRequest&#47;properties&#47;compromised&#47;type | boolean |
| &#47;components&#47;schemas&#47;CertificateRotateRequest&#47;properties&#47;identity&#47;type | boolean |
| &#47;components&#47;schemas&#47;CertificateRotateRequest&#47;properties&#47;reason&#47;type | string |
| &#47;components&#47;schemas&#47;CertificateRotateRequest&#47;type | object |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;ask&#47;type | object |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;checkStatus&#47;type | string |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;head&#47;type | string |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;identity&#47;type | string |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;mergeCommit&#47;type | string |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;message&#47;type | string |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;observation&#47;type | object |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;ok&#47;type | boolean |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;operation&#47;type | object |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;problem&#47;type | object |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;pullRequest&#47;type | string |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;properties&#47;status&#47;type | string |
| &#47;components&#47;schemas&#47;ChangeResponse&#47;type | object |
| &#47;components&#47;schemas&#47;ChangesMergeRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesMergeRequest&#47;properties&#47;head&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesMergeRequest&#47;properties&#47;product&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesMergeRequest&#47;properties&#47;pullRequest&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesMergeRequest&#47;properties&#47;reconciliation&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesMergeRequest&#47;type | object |
| &#47;components&#47;schemas&#47;ChangesObserveRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesObserveRequest&#47;properties&#47;cancel&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesObserveRequest&#47;properties&#47;operation&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesObserveRequest&#47;type | object |
| &#47;components&#47;schemas&#47;ChangesOpenRequest&#47;properties&#47;body&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesOpenRequest&#47;properties&#47;branch&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesOpenRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesOpenRequest&#47;properties&#47;head&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesOpenRequest&#47;properties&#47;product&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesOpenRequest&#47;properties&#47;reconciliation&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesOpenRequest&#47;properties&#47;title&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesOpenRequest&#47;type | object |
| &#47;components&#47;schemas&#47;ChangesPushRequest&#47;properties&#47;branch&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesPushRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesPushRequest&#47;properties&#47;head&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesPushRequest&#47;properties&#47;product&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesPushRequest&#47;properties&#47;reconciliation&#47;type | string |
| &#47;components&#47;schemas&#47;ChangesPushRequest&#47;type | object |
| &#47;components&#47;schemas&#47;CommunicationDelegateRequest&#47;properties&#47;assignment&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationDelegateRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationDelegateRequest&#47;properties&#47;expected&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationDelegateRequest&#47;properties&#47;expires&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationDelegateRequest&#47;properties&#47;grantee&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationDelegateRequest&#47;properties&#47;recipient&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationDelegateRequest&#47;properties&#47;scope&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationDelegateRequest&#47;properties&#47;status&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationDelegateRequest&#47;type | object |
| &#47;components&#47;schemas&#47;CommunicationProfile&#47;properties&#47;assignment&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationProfile&#47;properties&#47;conflicts&#47;type | array |
| &#47;components&#47;schemas&#47;CommunicationProfile&#47;properties&#47;fields&#47;type | object |
| &#47;components&#47;schemas&#47;CommunicationProfile&#47;properties&#47;observedAt&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationProfile&#47;properties&#47;recipient&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationProfile&#47;properties&#47;revisionHash&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationProfile&#47;properties&#47;revisions&#47;type | array |
| &#47;components&#47;schemas&#47;CommunicationProfile&#47;properties&#47;unavailable&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationProfile&#47;type | object |
| &#47;components&#47;schemas&#47;CommunicationSetRequest&#47;properties&#47;assignment&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationSetRequest&#47;properties&#47;authority&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationSetRequest&#47;properties&#47;basis&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationSetRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationSetRequest&#47;properties&#47;expected&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationSetRequest&#47;properties&#47;recipient&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationSetRequest&#47;properties&#47;scope&#47;type | string |
| &#47;components&#47;schemas&#47;CommunicationSetRequest&#47;properties&#47;values&#47;type | object |
| &#47;components&#47;schemas&#47;CommunicationSetRequest&#47;type | object |
| &#47;components&#47;schemas&#47;CompanionCompareRequest&#47;properties&#47;audience&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionCompareRequest&#47;properties&#47;commit&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionCompareRequest&#47;properties&#47;selection&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionCompareRequest&#47;type | object |
| &#47;components&#47;schemas&#47;CompanionComparison&#47;properties&#47;commit&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionComparison&#47;properties&#47;currentness&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionComparison&#47;properties&#47;differences&#47;type | array |
| &#47;components&#47;schemas&#47;CompanionComparison&#47;properties&#47;problems&#47;type | array |
| &#47;components&#47;schemas&#47;CompanionComparison&#47;properties&#47;snapshot&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionComparison&#47;properties&#47;status&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionComparison&#47;properties&#47;version&#47;type | integer |
| &#47;components&#47;schemas&#47;CompanionComparison&#47;type | object |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;audience&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;commit&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;contract&#47;type | integer |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;declaration&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;field&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;generator&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;index&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;page&#47;type | integer |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;revision&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;selection&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;properties&#47;snapshot&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionRequest&#47;type | object |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;audience&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;commit&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;completeness&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;contentHash&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;currentness&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;generator&#47;type | object |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;inputDigest&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;reason&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;snapshot&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;sources&#47;type | array |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;status&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;text&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;properties&#47;version&#47;type | integer |
| &#47;components&#47;schemas&#47;CompanionResponse&#47;type | object |
| &#47;components&#47;schemas&#47;CompanionSource&#47;properties&#47;at&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionSource&#47;properties&#47;bytes&#47;type | integer |
| &#47;components&#47;schemas&#47;CompanionSource&#47;properties&#47;current&#47;type | boolean |
| &#47;components&#47;schemas&#47;CompanionSource&#47;properties&#47;hash&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionSource&#47;properties&#47;identity&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionSource&#47;properties&#47;kind&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionSource&#47;properties&#47;path&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionSource&#47;properties&#47;recordType&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionSource&#47;properties&#47;repository&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionSource&#47;type | object |
| &#47;components&#47;schemas&#47;CompanionsResponse&#47;properties&#47;coverage&#47;type | array |
| &#47;components&#47;schemas&#47;CompanionsResponse&#47;properties&#47;destinations&#47;type | array |
| &#47;components&#47;schemas&#47;CompanionsResponse&#47;properties&#47;generators&#47;type | array |
| &#47;components&#47;schemas&#47;CompanionsResponse&#47;properties&#47;manifestSnapshot&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionsResponse&#47;properties&#47;reason&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionsResponse&#47;properties&#47;snapshot&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionsResponse&#47;properties&#47;status&#47;type | string |
| &#47;components&#47;schemas&#47;CompanionsResponse&#47;properties&#47;version&#47;type | integer |
| &#47;components&#47;schemas&#47;CompanionsResponse&#47;type | object |
| &#47;components&#47;schemas&#47;DeviceCreateRequest&#47;properties&#47;custody&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceCreateRequest&#47;properties&#47;name&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceCreateRequest&#47;properties&#47;presence&#47;type | boolean |
| &#47;components&#47;schemas&#47;DeviceCreateRequest&#47;properties&#47;principal&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceCreateRequest&#47;properties&#47;profiles&#47;type | array |
| &#47;components&#47;schemas&#47;DeviceCreateRequest&#47;type | object |
| &#47;components&#47;schemas&#47;DeviceRecoveryRequest&#47;properties&#47;name&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceRecoveryRequest&#47;properties&#47;output&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceRecoveryRequest&#47;properties&#47;passphrase&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceRecoveryRequest&#47;properties&#47;principal&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceRecoveryRequest&#47;type | object |
| &#47;components&#47;schemas&#47;DeviceRenameRequest&#47;properties&#47;device&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceRenameRequest&#47;properties&#47;name&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceRenameRequest&#47;type | object |
| &#47;components&#47;schemas&#47;DeviceRotateRequest&#47;properties&#47;custody&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceRotateRequest&#47;properties&#47;device&#47;type | string |
| &#47;components&#47;schemas&#47;DeviceRotateRequest&#47;properties&#47;presence&#47;type | boolean |
| &#47;components&#47;schemas&#47;DeviceRotateRequest&#47;type | object |
| &#47;components&#47;schemas&#47;DocumentReviewRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;DocumentReviewRequest&#47;properties&#47;commit&#47;type | string |
| &#47;components&#47;schemas&#47;DocumentReviewRequest&#47;properties&#47;findings&#47;type | array |
| &#47;components&#47;schemas&#47;DocumentReviewRequest&#47;properties&#47;independence&#47;type | string |
| &#47;components&#47;schemas&#47;DocumentReviewRequest&#47;properties&#47;outcome&#47;type | string |
| &#47;components&#47;schemas&#47;DocumentReviewRequest&#47;properties&#47;summary&#47;type | string |
| &#47;components&#47;schemas&#47;DocumentReviewRequest&#47;type | object |
| &#47;components&#47;schemas&#47;EstateObserveRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;EstateObserveRequest&#47;type | object |
| &#47;components&#47;schemas&#47;ExportRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;ExportRequest&#47;properties&#47;output&#47;type | string |
| &#47;components&#47;schemas&#47;ExportRequest&#47;type | object |
| &#47;components&#47;schemas&#47;GoalAcceptRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;GoalAcceptRequest&#47;properties&#47;statement&#47;type | string |
| &#47;components&#47;schemas&#47;GoalAcceptRequest&#47;type | object |
| &#47;components&#47;schemas&#47;GoalActionRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;GoalActionRequest&#47;properties&#47;note&#47;type | string |
| &#47;components&#47;schemas&#47;GoalActionRequest&#47;type | object |
| &#47;components&#47;schemas&#47;GoalEvaluateRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;GoalEvaluateRequest&#47;properties&#47;summary&#47;type | string |
| &#47;components&#47;schemas&#47;GoalEvaluateRequest&#47;properties&#47;verdicts&#47;type | array |
| &#47;components&#47;schemas&#47;GoalEvaluateRequest&#47;type | object |
| &#47;components&#47;schemas&#47;GoalProposeRequest&#47;properties&#47;body&#47;type | object |
| &#47;components&#47;schemas&#47;GoalProposeRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;GoalProposeRequest&#47;properties&#47;supersedes&#47;type | string |
| &#47;components&#47;schemas&#47;GoalProposeRequest&#47;type | object |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;properties&#47;allocation&#47;type | object |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;properties&#47;base&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;properties&#47;branch&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;properties&#47;configuration&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;properties&#47;contextPackage&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;properties&#47;execution&#47;type | object |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;properties&#47;repository&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;properties&#47;settings&#47;type | object |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;properties&#47;wallMinutes&#47;type | integer |
| &#47;components&#47;schemas&#47;GoalStepRequest&#47;type | object |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;ask&#47;type | object |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;assignment&#47;type | object |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;goal&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;message&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;ok&#47;type | boolean |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;outcome&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;principal&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;problem&#47;type | object |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;route&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;run&#47;type | object |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;properties&#47;step&#47;type | string |
| &#47;components&#47;schemas&#47;GoalStepResponse&#47;type | object |
| &#47;components&#47;schemas&#47;GoalVerdict&#47;properties&#47;condition&#47;type | integer |
| &#47;components&#47;schemas&#47;GoalVerdict&#47;properties&#47;read&#47;type | array |
| &#47;components&#47;schemas&#47;GoalVerdict&#47;properties&#47;reason&#47;type | string |
| &#47;components&#47;schemas&#47;GoalVerdict&#47;properties&#47;verdict&#47;type | string |
| &#47;components&#47;schemas&#47;GoalVerdict&#47;type | object |
| &#47;components&#47;schemas&#47;HandoffPackage&#47;properties&#47;bytes&#47;type | integer |
| &#47;components&#47;schemas&#47;HandoffPackage&#47;properties&#47;hash&#47;type | string |
| &#47;components&#47;schemas&#47;HandoffPackage&#47;properties&#47;payload&#47;type | object |
| &#47;components&#47;schemas&#47;HandoffPackage&#47;type | object |
| &#47;components&#47;schemas&#47;HarnessQualifyRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;HarnessQualifyRequest&#47;properties&#47;launcher&#47;type | string |
| &#47;components&#47;schemas&#47;HarnessQualifyRequest&#47;properties&#47;settings&#47;type | object |
| &#47;components&#47;schemas&#47;HarnessQualifyRequest&#47;type | object |
| &#47;components&#47;schemas&#47;NewRecordRequest&#47;properties&#47;at&#47;type | string |
| &#47;components&#47;schemas&#47;NewRecordRequest&#47;properties&#47;body&#47;type | object |
| &#47;components&#47;schemas&#47;NewRecordRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;NewRecordRequest&#47;properties&#47;expected&#47;type | string |
| &#47;components&#47;schemas&#47;NewRecordRequest&#47;properties&#47;id&#47;type | string |
| &#47;components&#47;schemas&#47;NewRecordRequest&#47;properties&#47;subject&#47;type | string |
| &#47;components&#47;schemas&#47;NewRecordRequest&#47;properties&#47;supersedes&#47;type | string |
| &#47;components&#47;schemas&#47;NewRecordRequest&#47;properties&#47;type&#47;type | string |
| &#47;components&#47;schemas&#47;NewRecordRequest&#47;type | object |
| &#47;components&#47;schemas&#47;ObjectBytes&#47;format | binary |
| &#47;components&#47;schemas&#47;ObjectBytes&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;body&#47;type | object |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;product&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;reconciliation&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;subject&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;supersedes&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;type&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;waitSeconds&#47;default | 120 |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;waitSeconds&#47;maximum | 240 |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;waitSeconds&#47;minimum | 0 |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;properties&#47;waitSeconds&#47;type | integer |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;required&#47;0 | type |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;required&#47;1 | by |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;required&#47;2 | body |
| &#47;components&#47;schemas&#47;OperatorRecordRequest&#47;type | object |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;properties&#47;branch&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;properties&#47;change&#47;type | object |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;properties&#47;head&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;properties&#47;identity&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;properties&#47;message&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;properties&#47;nextStep&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;properties&#47;ok&#47;type | boolean |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;properties&#47;problem&#47;type | object |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;properties&#47;record&#47;type | object |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;properties&#47;status&#47;type | string |
| &#47;components&#47;schemas&#47;OperatorRecordResponse&#47;type | object |
| &#47;components&#47;schemas&#47;PairingConfirm&#47;properties&#47;device&#47;type | object |
| &#47;components&#47;schemas&#47;PairingConfirm&#47;properties&#47;handle&#47;type | string |
| &#47;components&#47;schemas&#47;PairingConfirm&#47;properties&#47;mac&#47;type | string |
| &#47;components&#47;schemas&#47;PairingConfirm&#47;properties&#47;runtimeNonce&#47;type | string |
| &#47;components&#47;schemas&#47;PairingConfirm&#47;properties&#47;signature&#47;type | string |
| &#47;components&#47;schemas&#47;PairingConfirm&#47;type | object |
| &#47;components&#47;schemas&#47;PairingHello&#47;properties&#47;handle&#47;type | string |
| &#47;components&#47;schemas&#47;PairingHello&#47;properties&#47;nonce&#47;type | string |
| &#47;components&#47;schemas&#47;PairingHello&#47;type | object |
| &#47;components&#47;schemas&#47;PairingIssueRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;PairingIssueRequest&#47;properties&#47;principal&#47;type | string |
| &#47;components&#47;schemas&#47;PairingIssueRequest&#47;properties&#47;profiles&#47;type | array |
| &#47;components&#47;schemas&#47;PairingIssueRequest&#47;type | object |
| &#47;components&#47;schemas&#47;PolicyChangeRequest&#47;properties&#47;body&#47;type | object |
| &#47;components&#47;schemas&#47;PolicyChangeRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;PolicyChangeRequest&#47;properties&#47;expected&#47;type | string |
| &#47;components&#47;schemas&#47;PolicyChangeRequest&#47;type | object |
| &#47;components&#47;schemas&#47;Problem&#47;properties&#47;code&#47;type | string |
| &#47;components&#47;schemas&#47;Problem&#47;properties&#47;findings&#47;type | array |
| &#47;components&#47;schemas&#47;Problem&#47;properties&#47;message&#47;type | string |
| &#47;components&#47;schemas&#47;Problem&#47;type | object |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;contract&#47;type | string |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;destination&#47;type | string |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;execution&#47;type | object |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;implementation&#47;type | string |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;kind&#47;type | string |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;limitations&#47;type | array |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;locations&#47;type | array |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;operations&#47;type | array |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;provider&#47;type | string |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;settings&#47;type | array |
| &#47;components&#47;schemas&#47;ProviderRow&#47;properties&#47;status&#47;type | string |
| &#47;components&#47;schemas&#47;ProviderRow&#47;type | object |
| &#47;components&#47;schemas&#47;ReleaseObserveRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseObserveRequest&#47;properties&#47;cancel&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseObserveRequest&#47;properties&#47;limitations&#47;type | array |
| &#47;components&#47;schemas&#47;ReleaseObserveRequest&#47;properties&#47;product&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseObserveRequest&#47;properties&#47;provider&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseObserveRequest&#47;properties&#47;qualified&#47;type | array |
| &#47;components&#47;schemas&#47;ReleaseObserveRequest&#47;properties&#47;repository&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseObserveRequest&#47;properties&#47;signing&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseObserveRequest&#47;properties&#47;version&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseObserveRequest&#47;type | object |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;basisEvidence&#47;type | array |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;candidate&#47;type | object |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;identity&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;message&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;ok&#47;type | boolean |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;operation&#47;type | object |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;pending&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;policies&#47;type | object |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;problem&#47;type | object |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;publish&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;publishedAt&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;release&#47;type | object |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;releaseId&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;run&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;status&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;properties&#47;url&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseResponse&#47;type | object |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;commit&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;expected&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;product&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;provider&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;reconciliation&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;ref&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;repository&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;settings&#47;type | object |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;version&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;properties&#47;workflow&#47;type | string |
| &#47;components&#47;schemas&#47;ReleaseSubmitRequest&#47;type | object |
| &#47;components&#47;schemas&#47;ResultRequest&#47;properties&#47;attempt&#47;type | string |
| &#47;components&#47;schemas&#47;ResultRequest&#47;properties&#47;body&#47;type | object |
| &#47;components&#47;schemas&#47;ResultRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;ResultRequest&#47;properties&#47;expected&#47;type | string |
| &#47;components&#47;schemas&#47;ResultRequest&#47;type | object |
| &#47;components&#47;schemas&#47;ResultReviewRequest&#47;properties&#47;body&#47;type | object |
| &#47;components&#47;schemas&#47;ResultReviewRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;ResultReviewRequest&#47;properties&#47;expected&#47;type | string |
| &#47;components&#47;schemas&#47;ResultReviewRequest&#47;properties&#47;result&#47;type | string |
| &#47;components&#47;schemas&#47;ResultReviewRequest&#47;type | object |
| &#47;components&#47;schemas&#47;RevokeRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;RevokeRequest&#47;properties&#47;device&#47;type | string |
| &#47;components&#47;schemas&#47;RevokeRequest&#47;properties&#47;passphrase&#47;type | string |
| &#47;components&#47;schemas&#47;RevokeRequest&#47;properties&#47;reason&#47;type | string |
| &#47;components&#47;schemas&#47;RevokeRequest&#47;properties&#47;recovery&#47;type | string |
| &#47;components&#47;schemas&#47;RevokeRequest&#47;type | object |
| &#47;components&#47;schemas&#47;RunResponse&#47;properties&#47;cap&#47;type | object |
| &#47;components&#47;schemas&#47;RunResponse&#47;properties&#47;message&#47;type | string |
| &#47;components&#47;schemas&#47;RunResponse&#47;properties&#47;ok&#47;type | boolean |
| &#47;components&#47;schemas&#47;RunResponse&#47;properties&#47;operation&#47;type | object |
| &#47;components&#47;schemas&#47;RunResponse&#47;properties&#47;problem&#47;type | object |
| &#47;components&#47;schemas&#47;RunResponse&#47;properties&#47;run&#47;type | string |
| &#47;components&#47;schemas&#47;RunResponse&#47;properties&#47;status&#47;type | string |
| &#47;components&#47;schemas&#47;RunResponse&#47;properties&#47;workspace&#47;type | string |
| &#47;components&#47;schemas&#47;RunResponse&#47;type | object |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;agent&#47;type | string |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;assignment&#47;type | string |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;cap&#47;type | object |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;charged&#47;type | object |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;execution&#47;type | object |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;executionObserved&#47;type | object |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;generic&#47;type | boolean |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;goal&#47;type | string |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;heartbeat&#47;type | string |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;identity&#47;type | string |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;launcher&#47;type | string |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;liveness&#47;type | object |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;observed&#47;type | boolean |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;run&#47;type | string |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;scope&#47;type | object |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;signals&#47;type | array |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;status&#47;type | string |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;stop&#47;type | object |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;usage&#47;type | object |
| &#47;components&#47;schemas&#47;RunRow&#47;properties&#47;workspace&#47;type | string |
| &#47;components&#47;schemas&#47;RunRow&#47;type | object |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;agent&#47;type | string |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;allocation&#47;type | object |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;assignment&#47;type | string |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;base&#47;type | string |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;branch&#47;type | string |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;configuration&#47;type | string |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;contextPackage&#47;type | string |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;execution&#47;type | object |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;goal&#47;type | string |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;launcher&#47;type | string |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;repository&#47;type | string |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;settings&#47;type | object |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;properties&#47;wallMinutes&#47;type | integer |
| &#47;components&#47;schemas&#47;RunStartRequest&#47;type | object |
| &#47;components&#47;schemas&#47;RunStopRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;RunStopRequest&#47;properties&#47;graceSeconds&#47;type | integer |
| &#47;components&#47;schemas&#47;RunStopRequest&#47;properties&#47;reason&#47;type | string |
| &#47;components&#47;schemas&#47;RunStopRequest&#47;type | object |
| &#47;components&#47;schemas&#47;RunStopResponse&#47;properties&#47;message&#47;type | string |
| &#47;components&#47;schemas&#47;RunStopResponse&#47;properties&#47;ok&#47;type | boolean |
| &#47;components&#47;schemas&#47;RunStopResponse&#47;properties&#47;problem&#47;type | object |
| &#47;components&#47;schemas&#47;RunStopResponse&#47;properties&#47;run&#47;type | string |
| &#47;components&#47;schemas&#47;RunStopResponse&#47;properties&#47;state&#47;type | string |
| &#47;components&#47;schemas&#47;RunStopResponse&#47;properties&#47;stop&#47;type | object |
| &#47;components&#47;schemas&#47;RunStopResponse&#47;type | object |
| &#47;components&#47;schemas&#47;RunsResponse&#47;properties&#47;goals&#47;type | array |
| &#47;components&#47;schemas&#47;RunsResponse&#47;properties&#47;runs&#47;type | array |
| &#47;components&#47;schemas&#47;RunsResponse&#47;type | object |
| &#47;components&#47;schemas&#47;TakeoverRequest&#47;properties&#47;assignment&#47;type | string |
| &#47;components&#47;schemas&#47;TakeoverRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;TakeoverRequest&#47;properties&#47;contributor&#47;type | string |
| &#47;components&#47;schemas&#47;TakeoverRequest&#47;properties&#47;note&#47;type | string |
| &#47;components&#47;schemas&#47;TakeoverRequest&#47;type | object |
| &#47;components&#47;schemas&#47;TakeoverResponse&#47;properties&#47;assignment&#47;type | object |
| &#47;components&#47;schemas&#47;TakeoverResponse&#47;properties&#47;message&#47;type | string |
| &#47;components&#47;schemas&#47;TakeoverResponse&#47;properties&#47;ok&#47;type | boolean |
| &#47;components&#47;schemas&#47;TakeoverResponse&#47;properties&#47;problem&#47;type | object |
| &#47;components&#47;schemas&#47;TakeoverResponse&#47;properties&#47;stops&#47;type | array |
| &#47;components&#47;schemas&#47;TakeoverResponse&#47;type | object |
| &#47;components&#47;schemas&#47;UpgradeRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;UpgradeRequest&#47;properties&#47;check&#47;type | boolean |
| &#47;components&#47;schemas&#47;UpgradeRequest&#47;properties&#47;version&#47;type | string |
| &#47;components&#47;schemas&#47;UpgradeRequest&#47;type | object |
| &#47;components&#47;schemas&#47;WorkPlaceRequest&#47;properties&#47;backlog&#47;type | string |
| &#47;components&#47;schemas&#47;WorkPlaceRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;WorkPlaceRequest&#47;properties&#47;expected&#47;type | string |
| &#47;components&#47;schemas&#47;WorkPlaceRequest&#47;properties&#47;expectedSelection&#47;type | string |
| &#47;components&#47;schemas&#47;WorkPlaceRequest&#47;properties&#47;releasePlan&#47;type | string |
| &#47;components&#47;schemas&#47;WorkPlaceRequest&#47;type | object |
| &#47;components&#47;schemas&#47;WorkProgressRequest&#47;properties&#47;attempt&#47;type | string |
| &#47;components&#47;schemas&#47;WorkProgressRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;WorkProgressRequest&#47;properties&#47;completed&#47;type | boolean |
| &#47;components&#47;schemas&#47;WorkProgressRequest&#47;properties&#47;note&#47;type | string |
| &#47;components&#47;schemas&#47;WorkProgressRequest&#47;properties&#47;phase&#47;type | string |
| &#47;components&#47;schemas&#47;WorkProgressRequest&#47;type | object |
| &#47;components&#47;schemas&#47;WriteOutcome&#47;properties&#47;problem&#47;type | object |
| &#47;components&#47;schemas&#47;WriteOutcome&#47;properties&#47;written&#47;type | object |
| &#47;components&#47;schemas&#47;WriteOutcome&#47;type | object |
| &#47;components&#47;schemas&#47;WritingExceptionRemoveRequest&#47;properties&#47;basis&#47;type | object |
| &#47;components&#47;schemas&#47;WritingExceptionRemoveRequest&#47;properties&#47;by&#47;type | string |
| &#47;components&#47;schemas&#47;WritingExceptionRemoveRequest&#47;properties&#47;expected&#47;type | string |
| &#47;components&#47;schemas&#47;WritingExceptionRemoveRequest&#47;properties&#47;reason&#47;type | string |
| &#47;components&#47;schemas&#47;WritingExceptionRemoveRequest&#47;type | object |
| &#47;components&#47;securitySchemes&#47;deviceSignature&#47;description | Required on loopback and the local network&#58; an ECDSA P&#45;256 signature by an admitted device over the request &#40;scheme vericairn&#45;request&#47;1&#41;&#44; with Vericairn&#45;Device and Vericairn&#45;Signed&#45;At&#46; |
| &#47;components&#47;securitySchemes&#47;deviceSignature&#47;in | header |
| &#47;components&#47;securitySchemes&#47;deviceSignature&#47;name | Vericairn&#45;Signature |
| &#47;components&#47;securitySchemes&#47;deviceSignature&#47;type | apiKey |
| &#47;info&#47;description | One versioned API that every client uses&#46; Authority is evaluated by the runtime behind it&#59; a client presents&#44; requests and observes&#46; Every response is what the runtime observed&#44; never what a client assumed&#46; Problems carry a stable code &#40;refused&#44; rejected&#44; not&#45;found&#44; invalid&#44; denied&#44; provider&#44; conflict&#41;&#44; a message with the permitted next action and&#44; for rejected records&#44; the findings&#46; Transports&#58; a local socket &#40;Unix domain socket&#44; or a named pipe on Windows&#41; authenticated by operating&#45;system identity&#44; and loopback TLS pinned to the runtime certificate where every request is signed by an admitted device &#40;headers Vericairn&#45;Device&#44; Vericairn&#45;Signed&#45;At&#44; Vericairn&#45;Signature&#59; scheme vericairn&#45;request&#47;1&#41;&#46; From 0&#46;6&#46;0&#44; when the operator configures an address&#44; the local network&#58; mutual TLS&#44; where a paired device presents a certificate its device key issued&#44; has the runtime identity it pinned at pairing sign the runtime certificate&#39;s fingerprint for each connection &#40;GET &#47;v1&#47;runtime&#47;identity&#44; scheme vericairn&#45;runtime&#45;identity&#47;1&#41;&#44; and signs every request&#59; the device must be admitted now and permitted the local&#45;network profile&#44; and acts for its own principal&#46; A device pairs with a code an administrator issues &#40;POST &#47;v1&#47;pairings&#41;&#44; over POST &#47;v1&#47;pairing&#47;hello and POST &#47;v1&#47;pairing&#47;confirm &#40;scheme vericairn&#45;pairing&#47;1&#41;&#46; |
| &#47;info&#47;title | Vericairn API |
| &#47;info&#47;version | 1 |
| &#47;openapi | 3&#46;0&#46;3 |
| &#47;paths&#47;&#126;1v1&#126;1asks&#47;get&#47;operationId | getAsks |
| &#47;paths&#47;&#126;1v1&#126;1asks&#47;get&#47;parameters&#47;0&#47;description | The principal asked&#44; by id&#59; every principal when omitted&#46; |
| &#47;paths&#47;&#126;1v1&#126;1asks&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1asks&#47;get&#47;parameters&#47;0&#47;name | to |
| &#47;paths&#47;&#126;1v1&#126;1asks&#47;get&#47;parameters&#47;0&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1asks&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1asks&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1asks&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1asks&#47;get&#47;summary | What waits on a principal &#40;to&#41;&#44; or on every principal&#58; the asks still open or notified&#44; oldest first&#44; at most three delivered to each with the recommendation first and the consequence of leaving it unanswered&#44; the rest waiting in order &#40;vericairn&#46;asks&#47;1&#41;&#46; Reading it answers nothing&#58; an ask is answered by a decision of the principal it is put to that names it&#44; never by waiting&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1merge&#47;post&#47;operationId | postChangesMerge |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1merge&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangesMergeRequest |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1merge&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1merge&#47;post&#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangeResponse |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1merge&#47;post&#47;responses&#47;200&#47;description | ChangeResponse&#58; recorded intent&#44; observed effect and exact&#45;head evidence&#59; uncertainty never authorizes retry&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1merge&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1merge&#47;post&#47;summary | Record intent and checks&#44; refuse missing or unsuccessful checks&#44; merge pinned to the head&#44; and observe the target run&#46; A failure raises an operator ask&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1observe&#47;post&#47;operationId | postChangesObserve |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1observe&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangesObserveRequest |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1observe&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1observe&#47;post&#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangeResponse |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1observe&#47;post&#47;responses&#47;200&#47;description | ChangeResponse&#58; recorded intent&#44; observed effect and exact&#45;head evidence&#59; uncertainty never authorizes retry&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1observe&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1observe&#47;post&#47;summary | Record an observation under an original change&#39;s provider and head&#59; incomplete or uncertain outcomes remain uncertain&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1open&#47;post&#47;operationId | postChangesOpen |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1open&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangesOpenRequest |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1open&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1open&#47;post&#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangeResponse |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1open&#47;post&#47;responses&#47;200&#47;description | ChangeResponse&#58; recorded intent&#44; observed effect and exact&#45;head evidence&#59; uncertainty never authorizes retry&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1open&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1open&#47;post&#47;summary | Record intent and open a pull request from an exact head with a recorded successful scanned push&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1push&#47;post&#47;operationId | postChangesPush |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1push&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangesPushRequest |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1push&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1push&#47;post&#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ChangeResponse |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1push&#47;post&#47;responses&#47;200&#47;description | ChangeResponse&#58; recorded intent&#44; observed effect and exact&#45;head evidence&#59; uncertainty never authorizes retry&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1push&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1changes&#126;1push&#47;post&#47;summary | Record intent&#44; scan exactly the commits&#44; then push the pinned head with a remote lease and record its observed outcome&#46; Requires repository&#45;write authority&#46; |
| &#47;paths&#47;&#126;1v1&#126;1check&#47;get&#47;operationId | getCheck |
| &#47;paths&#47;&#126;1v1&#126;1check&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1check&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1check&#47;get&#47;summary | Validate and run the repository checks&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;operationId | getCommunication |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;0&#47;name | recipient |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;0&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;1&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;1&#47;name | assignment |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;1&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;2&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;2&#47;name | progress |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;2&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;3&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;3&#47;name | status |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;3&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;4&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;4&#47;name | decision |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;4&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;4&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;5&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;5&#47;name | result |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;5&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;6&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;6&#47;name | handoff |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;6&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;get&#47;summary | The effective recipient profile with every contributing revision&#44; conflicts and freshness&#46; Query values override this view only&#46; No record is authored&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;post&#47;operationId | postCommunication |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CommunicationSetRequest |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#47;post&#47;summary | Replace or reset one scoped communication profile with its expected prior hash&#46; Only its owner or an explicit delegate may change it&#59; stale edits return conflict&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#126;1delegations&#47;post&#47;operationId | postCommunicationDelegations |
| &#47;paths&#47;&#126;1v1&#126;1communication&#126;1delegations&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CommunicationDelegateRequest |
| &#47;paths&#47;&#126;1v1&#126;1communication&#126;1delegations&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1communication&#126;1delegations&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#126;1delegations&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#126;1delegations&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#126;1delegations&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1communication&#126;1delegations&#47;post&#47;summary | A person grants or revokes permission to change one scope of their communication setting&#44; with the expected prior hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#47;get&#47;operationId | getCompanions |
| &#47;paths&#47;&#126;1v1&#126;1companions&#47;get&#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CompanionsResponse |
| &#47;paths&#47;&#126;1v1&#126;1companions&#47;get&#47;responses&#47;200&#47;description | A versioned read result with explicit unavailable&#44; historical&#44; current or different basis&#46; Generation grants no authority or authored clearance&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#47;get&#47;summary | Installed readable coverage&#44; exact collection snapshots and declared destinations&#59; no adoption is inferred&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;operationId | getCompanionsCompare |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;0&#47;description | item or nested list at a revision&#59; current or all&#45;revisions at a snapshot&#46; Comparison selects a declaration or destination&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;0&#47;name | selection |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;1&#47;description | Exact retained source commit for a declared repository item&#59; no clone&#45;head substitution&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;1&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;1&#47;name | commit |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;1&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;2&#47;description | Internal by default&#46; A requested audience grants no access&#59; unsupported audiences are unavailable&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;2&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;2&#47;name | audience |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;2&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CompanionComparison |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;responses&#47;200&#47;description | A versioned read result with explicit unavailable&#44; historical&#44; current or different basis&#46; Generation grants no authority or authored clearance&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1compare&#47;get&#47;summary | Fresh byte and membership comparison without repair&#44; generated pins or authored clearance&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;operationId | getCompanionsRender |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;0&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;0&#47;name | generator |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;1&#47;description | item or nested list at a revision&#59; current or all&#45;revisions at a snapshot&#46; Comparison selects a declaration or destination&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;1&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;1&#47;name | selection |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;1&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;2&#47;description | Exact immutable record revision&#59; never follows a successor&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;2&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;2&#47;name | revision |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;2&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;3&#47;description | Exact record or manifest snapshot returned by coverage&#46; Refresh and select again if unavailable&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;3&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;3&#47;name | snapshot |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;3&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;4&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;4&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;4&#47;name | page |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;4&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;4&#47;schema&#47;default | 1 |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;4&#47;schema&#47;minimum | 1 |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;4&#47;schema&#47;type | integer |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;5&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;5&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;5&#47;name | field |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;5&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;6&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;6&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;6&#47;name | index |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;6&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;7&#47;description | Internal by default&#46; A requested audience grants no access&#59; unsupported audiences are unavailable&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;7&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;7&#47;name | audience |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;7&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;7&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;8&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;8&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;8&#47;name | contract |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;8&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;8&#47;schema&#47;default | 1 |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;8&#47;schema&#47;minimum | 1 |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;8&#47;schema&#47;type | integer |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;9&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;9&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;9&#47;name | declaration |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;9&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;9&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;10&#47;description | Exact retained source commit for a declared repository item&#59; no clone&#45;head substitution&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;10&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;10&#47;name | commit |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;10&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;parameters&#47;10&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CompanionResponse |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;responses&#47;200&#47;description | A versioned read result with explicit unavailable&#44; historical&#44; current or different basis&#46; Generation grants no authority or authored clearance&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1render&#47;get&#47;summary | Render one explicitly selected authorized item or collection&#59; currentness describes its exact basis&#44; not acceptance&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;operationId | getCompanionsSources |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;0&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;0&#47;name | generator |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;1&#47;description | item or nested list at a revision&#59; current or all&#45;revisions at a snapshot&#46; Comparison selects a declaration or destination&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;1&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;1&#47;name | selection |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;1&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;2&#47;description | Exact immutable record revision&#59; never follows a successor&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;2&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;2&#47;name | revision |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;2&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;3&#47;description | Exact record or manifest snapshot returned by coverage&#46; Refresh and select again if unavailable&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;3&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;3&#47;name | snapshot |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;3&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;4&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;4&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;4&#47;name | page |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;4&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;4&#47;schema&#47;default | 1 |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;4&#47;schema&#47;minimum | 1 |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;4&#47;schema&#47;type | integer |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;5&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;5&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;5&#47;name | field |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;5&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;6&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;6&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;6&#47;name | index |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;6&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;7&#47;description | Internal by default&#46; A requested audience grants no access&#59; unsupported audiences are unavailable&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;7&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;7&#47;name | audience |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;7&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;7&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;8&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;8&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;8&#47;name | contract |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;8&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;8&#47;schema&#47;default | 1 |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;8&#47;schema&#47;minimum | 1 |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;8&#47;schema&#47;type | integer |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;9&#47;description | Explicit reader input&#59; inspect the installed contract through coverage&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;9&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;9&#47;name | declaration |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;9&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;9&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;10&#47;description | Exact retained source commit for a declared repository item&#59; no clone&#45;head substitution&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;10&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;10&#47;name | commit |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;10&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;parameters&#47;10&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CompanionResponse |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;responses&#47;200&#47;description | A versioned read result with explicit unavailable&#44; historical&#44; current or different basis&#46; Generation grants no authority or authored clearance&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1companions&#126;1sources&#47;get&#47;summary | Read the exact permitted source basis without invoking a generator&#59; local inspectors stay local&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1original&#126;1&#123;pin&#125;&#47;get&#47;operationId | getContextOriginalPin |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1original&#126;1&#123;pin&#125;&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1original&#126;1&#123;pin&#125;&#47;get&#47;parameters&#47;0&#47;name | pin |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1original&#126;1&#123;pin&#125;&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1original&#126;1&#123;pin&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1original&#126;1&#123;pin&#125;&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1original&#126;1&#123;pin&#125;&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1original&#126;1&#123;pin&#125;&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1original&#126;1&#123;pin&#125;&#47;get&#47;summary | ContextReadResponse&#58; self&#45;contained pinned identity and exact bytes from immutable records or a matching working&#45;tree document&#46; Rechecks access and hashes&#59; writes nothing&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;operationId | getContextWorkKey |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;parameters&#47;0&#47;description | Optional context allocation in canonical payload bytes&#44; separate from a run spending cap&#46; Mandatory originals&#44; manifest and accounting must fit or the package is refused&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;parameters&#47;0&#47;name | maxBytes |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;parameters&#47;0&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;minimum | 1 |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;type | integer |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#47;get&#47;summary | ContextResponse&#58; compute a deterministic work context package and exact originals&#59; writes nothing&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;operationId | getContextWorkKeyBrief |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;0&#47;name | recipient |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;0&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;1&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;1&#47;name | assignment |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;1&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;2&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;2&#47;name | progress |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;2&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;3&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;3&#47;name | status |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;3&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;4&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;4&#47;name | decision |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;4&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;4&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;5&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;5&#47;name | result |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;5&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;6&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;6&#47;name | handoff |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;6&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;7&#47;description | Optional context allocation in canonical payload bytes&#44; separate from a run spending cap&#46; Mandatory originals&#44; manifest and accounting must fit or the package is refused&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;7&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;7&#47;name | maxBytes |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;7&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;7&#47;schema&#47;minimum | 1 |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;7&#47;schema&#47;type | integer |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;summary | BriefResponse&#58; measured agent brief&#59; mandatory originals inline&#44; named optional sources withheld unless a context allocation is given and they fit&#46; Writes nothing&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;operationId | getContextManifestSourcesSource |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;parameters&#47;0&#47;name | manifest |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;parameters&#47;1&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;parameters&#47;1&#47;name | source |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;parameters&#47;1&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1context&#126;1&#123;manifest&#125;&#126;1sources&#126;1&#123;source&#125;&#47;get&#47;summary | ContextReadResponse&#58; legacy manifest handles return original&#45;unavailable after access checking&#59; prepare again for a self&#45;contained pin&#46; |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#47;get&#47;operationId | getDecisions |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#47;get&#47;summary | Current decisions&#46; |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#126;1&#123;id&#125;&#47;get&#47;operationId | getDecisionsId |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#126;1&#123;id&#125;&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#126;1&#123;id&#125;&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#126;1&#123;id&#125;&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1decisions&#126;1&#123;id&#125;&#47;get&#47;summary | One decision by the id of any of its revisions&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;get&#47;operationId | getDevices |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;get&#47;summary | Devices known to the estate and whether this machine holds their keys&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;post&#47;operationId | postDevices |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;DeviceCreateRequest |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#47;post&#47;summary | Create a device on the runtime&#39;s machine&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1admit&#47;post&#47;operationId | postDevicesAdmit |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1admit&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;AdmitRequest |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1admit&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1admit&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1admit&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1admit&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1admit&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1admit&#47;post&#47;summary | Admit a device&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1recovery&#47;post&#47;operationId | postDevicesRecovery |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1recovery&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;DeviceRecoveryRequest |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1recovery&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1recovery&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1recovery&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1recovery&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1recovery&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1recovery&#47;post&#47;summary | Make the operator&#39;s recovery device&#58; its key is written once&#44; encrypted&#44; for offline keeping&#44; and an admitted device of the operator admits it&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rename&#47;post&#47;operationId | postDevicesRename |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rename&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;DeviceRenameRequest |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rename&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rename&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rename&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rename&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rename&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rename&#47;post&#47;summary | Rename a device&#44; keeping its identity&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1revoke&#47;post&#47;operationId | postDevicesRevoke |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1revoke&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;RevokeRequest |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1revoke&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1revoke&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1revoke&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1revoke&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1revoke&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1revoke&#47;post&#47;summary | Revoke a device&#39;s admission from now on&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rotate&#47;post&#47;operationId | postDevicesRotate |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rotate&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;DeviceRotateRequest |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rotate&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rotate&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rotate&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rotate&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rotate&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1devices&#126;1rotate&#47;post&#47;summary | Give a device a new key&#44; keeping its identity&#46; |
| &#47;paths&#47;&#126;1v1&#126;1documents&#47;get&#47;operationId | getDocuments |
| &#47;paths&#47;&#126;1v1&#126;1documents&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1documents&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1documents&#47;get&#47;summary | The declared living documents with their derived state&#58; current&#44; due with every trigger newer than the latest clearance&#44; or unavailable with the contributions no installed pack provides&#46; |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;operationId | postDocumentsIdReviews |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;DocumentReviewRequest |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1documents&#126;1&#123;id&#125;&#126;1reviews&#47;post&#47;summary | Review a living document by its declaration&#58; the runtime reads the content&#44; records the covered paths&#39; identity and the triggers the review answers&#44; runs the declared checks&#44; and refuses a clearance a failed check or the declaration&#39;s clearedBy forbids&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;get&#47;operationId | getEstate&#45;policies |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;get&#47;summary | PolicyResponse&#58; declarations&#44; exception history and findings at recorded time&#44; with the explicit basis&#59; creates no record&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;post&#47;operationId | postEstate&#45;policies |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#47;post&#47;summary | PolicyDeclare&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1access&#47;get&#47;operationId | getEstate&#45;policiesAccess |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1access&#47;get&#47;parameters&#47;0&#47;description | Acting principal whose current policy action admission is inspected&#59; exact bounds are rechecked for every write&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1access&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1access&#47;get&#47;parameters&#47;0&#47;name | principal |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1access&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1access&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1access&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1access&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1access&#47;get&#47;summary | PolicyAccess&#58; admitted policy actions&#44; with exact target bounds rechecked at submission&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;operationId | getEstate&#45;policiesEvaluate |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;parameters&#47;0&#47;description | Explicit canonical UTC preview time&#46; Never authorizes submission&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;parameters&#47;0&#47;name | asOf |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;parameters&#47;0&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;parameters&#47;1&#47;description | Exact release record id selecting its product&#44; version and commit&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;parameters&#47;1&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;parameters&#47;1&#47;name | release |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;parameters&#47;1&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1evaluate&#47;get&#47;summary | PolicyResponse evaluated from server sources&#44; with optional canonical UTC asOf and exact release record preview&#59; never authorizes submission&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1kinds&#47;get&#47;operationId | getEstate&#45;policiesKinds |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1kinds&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1kinds&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1kinds&#47;get&#47;summary | Installed kind versions&#44; implementation hashes&#44; supported selections&#44; sources and bounds&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;operationId | postEstate&#45;policiesKeyExceptions |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;parameters&#47;0&#47;name | key |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#47;post&#47;summary | PolicyExcept&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;operationId | postEstate&#45;policiesKeyExceptionsIdRevoke |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;parameters&#47;0&#47;name | key |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;parameters&#47;1&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;parameters&#47;1&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;parameters&#47;1&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1exceptions&#126;1&#123;id&#125;&#126;1revoke&#47;post&#47;summary | PolicyRevokeException&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;operationId | postEstate&#45;policiesKeyRetire |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;parameters&#47;0&#47;name | key |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1retire&#47;post&#47;summary | PolicyRetire&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;operationId | postEstate&#45;policiesKeyRevisions |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;parameters&#47;0&#47;name | key |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1revisions&#47;post&#47;summary | PolicyRevise&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1view&#47;get&#47;operationId | getEstate&#45;policiesKeyView |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1view&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1view&#47;get&#47;parameters&#47;0&#47;name | key |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1view&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1view&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1view&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1view&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1view&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#45;policies&#126;1&#123;key&#125;&#126;1view&#47;get&#47;summary | PolicyResponse for a key or revision id&#44; including recurring&#45;duty deadlines&#44; completion evidence and exceptions&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1export&#47;post&#47;operationId | postEstateExport |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1export&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ExportRequest |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1export&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1export&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1export&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1export&#47;post&#47;responses&#47;409&#47;description | Not carried out &#40;Problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1export&#47;post&#47;summary | Write a verified export to a path on the runtime&#39;s machine&#59; no keys or authority travel with it&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1upgrade&#47;post&#47;operationId | postEstateUpgrade |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1upgrade&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;UpgradeRequest |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1upgrade&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1upgrade&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1upgrade&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1upgrade&#47;post&#47;responses&#47;409&#47;description | Not carried out &#40;Problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1estate&#126;1upgrade&#47;post&#47;summary | Plan or apply an upgrade to a published release&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;get&#47;operationId | getGoals |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;get&#47;summary | The estate&#39;s current goals&#58; each one&#39;s current revision&#44; title&#44; status and sponsor&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;post&#47;operationId | postGoals |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalProposeRequest |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#47;post&#47;summary | Propose a goal&#44; or a revision of one &#40;supersedes&#41;&#44; from the goal record&#39;s body&#59; a proposal authorizes and funds nothing&#44; and a revision starts again from its sponsor&#39;s activation&#46; Anyone may propose a new goal&#59; a revision is recorded only by the sponsor of the revision it supersedes when the sponsor is a person&#44; and by an operator when an agent sponsors it&#46; The body&#39;s limits&#46;deadline and limits&#46;consecutiveFailures are validated and recorded but not yet enforced&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#47;get&#47;operationId | getGoalsId |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#47;get&#47;summary | A goal by the id of any of its revisions&#58; its objective&#44; sponsor&#44; status and granted budget&#44; each condition&#39;s verdict &#40;met&#44; not&#45;met or unknown&#41; with the records it read beside its current evaluation&#39;s verdict&#44; its steps&#44; and what stands between it and its acceptance&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;operationId | postGoalsIdAccept |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalAcceptRequest |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1accept&#47;post&#47;summary | Accept a goal at its current revision&#58; by its sponsor when the sponsor is a person&#44; or the principal the sponsor delegated an ordinary goal&#39;s acceptance to&#44; and by an operator&#44; only an operator&#44; when an agent sponsors it&#59; never by its evaluator or a step&#39;s author&#44; and only once its current evaluation finds every condition met and every record predicate holds&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;operationId | postGoalsIdActivate |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalActionRequest |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1activate&#47;post&#47;summary | Activate a proposed goal&#44; which grants its budget&#58; by its sponsor&#44; a person who holds the operator role or a grant of the spending scope&#44; or by an operator when an agent sponsors it&#59; an agent never activates a goal&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;operationId | postGoalsIdEvaluate |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalEvaluateRequest |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1evaluate&#47;post&#47;summary | Record a goal&#39;s evaluation&#44; condition by condition&#58; the runtime&#39;s verdict for each record predicate and the evaluator&#39;s for each evidence statement&#44; with the records each read&#44; by a principal other than any step&#39;s author&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1next&#47;get&#47;operationId | getGoalsIdNext |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1next&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1next&#47;get&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1next&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1next&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1next&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1next&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1next&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1next&#47;get&#47;summary | A goal&#39;s steps routed from the runtime&#39;s machine &#40;vericairn&#46;next&#47;1&#41;&#58; each step&#39;s state&#44; what holds each planned one&#44; and for each eligible one the principal its role&#39;s roster reaches &#40;an agent where a launcher for its harness&#44; its admitted key and the goal&#39;s budget allow&#44; a person where needed&#44; a reserved action only to one who holds it&#41; or why none is available&#44; with whether the budget allows it and the custody guard for an agent&#59; the goal&#39;s ledger and asks&#46; Read&#45;only&#59; it never falls back to a principal the roster does not name&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;operationId | postGoalsIdPause |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalActionRequest |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1pause&#47;post&#47;summary | Pause an active goal&#59; its sponsor&#39;s&#44; or an operator&#39;s for a goal an agent sponsors&#46; Prevents new runs but does not stop runs already in flight&#59; request each run&#39;s stop separately&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;operationId | postGoalsIdResume |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalActionRequest |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1resume&#47;post&#47;summary | Resume a paused&#44; budget&#45;limited or blocked goal&#44; which grants its budget again&#59; by whoever may activate it&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1step&#47;post&#47;operationId | postGoalsIdStep |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1step&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1step&#47;post&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1step&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1step&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1step&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalStepRequest |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1step&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1step&#47;post&#47;responses&#47;200&#47;description | GoalStepResponse&#58; what was recorded and started&#59; a problem inside when nothing was dispatched&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1step&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1step&#47;post&#47;summary | Take an active goal&#39;s next step&#44; one at a time&#44; as an operator&#44; the goal&#39;s sponsor when a person&#44; or a principal holding local&#45;engineering&#58; its first eligible step whose route acts&#44; routed from the runtime&#39;s machine&#46; An agent&#39;s step goes on with the assignment that holds it&#44; or is assigned to the agent together with its run&#39;s intent after the worker&#39;s whole preflight&#44; and its run starts through the worker&#59; every refusal before the intent&#44; custody&#44; the budget&#44; the repository&#44; the launcher or the workspace&#44; records nothing&#44; the assignment included&#46; A person&#39;s step is assigned to the person and asked of them&#44; the recommendation first&#44; the two recorded together&#59; a step no principal is available for is asked of the sponsor&#44; never given to a contributor the roster does not name&#46; It activates&#44; funds&#44; accepts and answers nothing&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;operationId | postGoalsIdStop |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;GoalActionRequest |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1goals&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;summary | Stop a goal&#58; its sponsor&#44; or an operator for a goal an agent sponsors&#44; ends it as abandoned and prevents new runs&#46; Runs already in flight are not stopped&#59; request each run&#39;s stop separately&#46; |
| &#47;paths&#47;&#126;1v1&#126;1handoff&#47;get&#47;operationId | getHandoff |
| &#47;paths&#47;&#126;1v1&#126;1handoff&#47;get&#47;parameters&#47;0&#47;description | Optional context allocation in canonical payload bytes&#44; separate from a run spending cap&#46; Mandatory originals&#44; manifest and accounting must fit or the package is refused&#46; |
| &#47;paths&#47;&#126;1v1&#126;1handoff&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1handoff&#47;get&#47;parameters&#47;0&#47;name | maxBytes |
| &#47;paths&#47;&#126;1v1&#126;1handoff&#47;get&#47;parameters&#47;0&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1handoff&#47;get&#47;parameters&#47;0&#47;schema&#47;minimum | 1 |
| &#47;paths&#47;&#126;1v1&#126;1handoff&#47;get&#47;parameters&#47;0&#47;schema&#47;type | integer |
| &#47;paths&#47;&#126;1v1&#126;1handoff&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1handoff&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1handoff&#47;get&#47;summary | HandoffResponse&#58; a complete deterministic hand&#45;over and measured package&#44; or a problem naming every missing or invalid source&#46; Read&#45;only&#59; all records retained&#59; no synthesis or authority&#46; Optional maxBytes refuses overflow&#46; |
| &#47;paths&#47;&#126;1v1&#126;1harness&#45;qualifications&#47;post&#47;operationId | postHarness&#45;qualifications |
| &#47;paths&#47;&#126;1v1&#126;1harness&#45;qualifications&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;HarnessQualifyRequest |
| &#47;paths&#47;&#126;1v1&#126;1harness&#45;qualifications&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1harness&#45;qualifications&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1harness&#45;qualifications&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1harness&#45;qualifications&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1harness&#45;qualifications&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1harness&#45;qualifications&#47;post&#47;summary | Machine&#45;local only&#58; an authorized human qualifies one exact installed native harness version from six local probe digests&#46; Paired local&#45;network devices are refused before probing&#59; local socket and signed loopback retain their authority checks&#46; No agent is launched and no raw probe output is recorded&#46; Remote is not built&#46; |
| &#47;paths&#47;&#126;1v1&#126;1health&#47;get&#47;operationId | getHealth |
| &#47;paths&#47;&#126;1v1&#126;1health&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1health&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1health&#47;get&#47;summary | The daemon&#39;s identity and what it serves&#46; |
| &#47;paths&#47;&#126;1v1&#126;1launchers&#47;get&#47;operationId | getLaunchers |
| &#47;paths&#47;&#126;1v1&#126;1launchers&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1launchers&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1launchers&#47;get&#47;summary | The coding&#45;agent launchers available to the runtime on its machine&#44; providers of vericairn&#46;launcher&#46;agent&#47;1&#44; with their settings and limitations&#59; a generic launcher is labeled as not complete agent support&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;get&#47;operationId | getObjects |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;get&#47;summary | Objects held&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;post&#47;operationId | postObjects |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;post&#47;requestBody&#47;content&#47;application&#126;1octet&#45;stream&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ObjectBytes |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;post&#47;responses&#47;413&#47;description | Above the object size limit &#40;Problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#47;post&#47;summary | Store bytes as an object named by their hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1verify&#47;get&#47;operationId | getObjectsVerify |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1verify&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1verify&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1verify&#47;get&#47;summary | Verify every object against its name&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1&#123;hash&#125;&#47;get&#47;operationId | getObjectsHash |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1&#123;hash&#125;&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1&#123;hash&#125;&#47;get&#47;parameters&#47;0&#47;name | hash |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1&#123;hash&#125;&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1&#123;hash&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1&#123;hash&#125;&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1&#123;hash&#125;&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1&#123;hash&#125;&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1objects&#126;1&#123;hash&#125;&#47;get&#47;summary | An object&#39;s verified bytes&#46; |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;operationId | getOperatorActions |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;0&#47;name | recipient |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;0&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;1&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;1&#47;name | assignment |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;1&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;2&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;2&#47;name | progress |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;2&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;3&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;3&#47;name | status |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;3&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;4&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;4&#47;name | decision |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;4&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;4&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;5&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;5&#47;name | result |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;5&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;6&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;6&#47;name | handoff |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;6&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;responses&#47;200&#47;description | OperatorActionsResponse&#58; text in the selected communication settings&#44; complete canonical JSON &#40;vericairn&#46;operator&#45;actions&#47;1&#41;&#44; and hasErrors&#46; Items retain their sources&#44; reasons&#44; waitsOn&#44; inspection command and record&#45;authoring commands with argument arrays and required JSON bodies&#59; null body fields require operator input&#46; No signing or provider access occurs&#46; |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1actions&#47;get&#47;summary | Every open operator ask&#44; pending operator acceptance and recorded waiting reserved action&#44; with reasons&#44; dependencies&#44; exact record&#45;authoring commands and required JSON input &#40;vericairn&#46;operator&#45;actions&#47;1&#41;&#46; Text follows communication settings&#44; defaulting to the estate&#39;s root operator&#59; JSON always contains the complete queue&#46; Record errors suppress authoring commands&#46; This read neither signs nor records&#44; inspects keys or calls providers&#46; Not exposed through MCP&#46; |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1record&#47;post&#47;operationId | postOperatorRecord |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1record&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;OperatorRecordRequest |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1record&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1record&#47;post&#47;responses&#47;200&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;OperatorRecordResponse |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1record&#47;post&#47;responses&#47;200&#47;description | OperatorRecordResponse&#58; the signed record&#44; retained branch and head&#44; last recorded change and exact recovery step&#46; Only ok with completed means the pinned merge and target checks passed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1record&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1operator&#126;1record&#47;post&#47;summary | Sign the operator&#39;s own record on their admitted device&#44; commit that record alone on a new estate branch&#44; and scan&#44; push&#44; open&#44; wait and merge through recorded changes operations&#46; Requires a clean repository&#45;synced estate at record format 3 and declared checks&#46; Identical input resumes the original action&#59; uncertain effects are observed before retry&#46; waitSeconds bounds each check phase from 0 to 240 &#40;default 120&#41;&#46; Synchronous only&#59; not exposed through MCP&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1confirm&#47;post&#47;operationId | postPairingConfirm |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1confirm&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PairingConfirm |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1confirm&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1confirm&#47;post&#47;responses&#47;200&#47;description | PairingCompleted&#58; the device record and the admission&#44; with the runtime&#39;s MAC over the exchange&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1confirm&#47;post&#47;responses&#47;401&#47;description | No such exchange is open&#44; the code expired or was used&#44; or the confirmation does not match the code &#40;Problem with code denied&#41;&#59; nothing is recorded&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1confirm&#47;post&#47;responses&#47;422&#47;description | The device&#39;s signature does not verify&#44; or the estate refused its records &#40;Problem&#44; with findings when rejected&#41;&#59; nothing is recorded&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1confirm&#47;post&#47;summary | Complete a pairing&#58; the device&#39;s key&#44; descriptive identity and signature of its record&#44; with its MAC with the code&#39;s device key&#59; the runtime records the device and its admission&#44; and answers with its MAC over the whole exchange&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1hello&#47;post&#47;operationId | postPairingHello |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1hello&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PairingHello |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1hello&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1hello&#47;post&#47;responses&#47;200&#47;description | PairingChallenge&#58; the runtime&#39;s identity&#44; the certificate on this connection and the device record to sign&#44; with the runtime&#39;s MAC&#59; uses one of the code&#39;s attempts&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1hello&#47;post&#47;responses&#47;401&#47;description | The code is unknown&#44; expired&#44; used or out of attempts&#44; or the request did not come over the local network &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1hello&#47;post&#47;responses&#47;422&#47;description | A hello without a handle or a nonce &#40;Problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairing&#126;1hello&#47;post&#47;summary | Start a pairing over the local network with a code&#39;s handle and a nonce&#58; the runtime answers with its identity&#44; the certificate on this connection&#44; the device record it will write&#44; and its MAC with the code&#39;s runtime key&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairings&#47;post&#47;operationId | postPairings |
| &#47;paths&#47;&#126;1v1&#126;1pairings&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PairingIssueRequest |
| &#47;paths&#47;&#126;1v1&#126;1pairings&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1pairings&#47;post&#47;responses&#47;200&#47;description | PairingIssued&#58; the code&#44; shown once&#44; with the principal&#44; the profiles&#44; the expiry&#44; the attempts&#44; the address and the runtime identity&#39;s fingerprint&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairings&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairings&#47;post&#47;responses&#47;409&#47;description | No local&#45;network profile is served&#44; or too many codes wait &#40;Problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairings&#47;post&#47;responses&#47;422&#47;description | Refused&#58; an unknown principal or profile&#44; no administrator&#39;s authority&#44; or no admitted device of the administrator here to sign the admission &#40;Problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1pairings&#47;post&#47;summary | Issue a pairing code under an administrator&#39;s authority &#40;the operator&#44; or a principal with the administer scope whose admitted device here signs the admission&#41; for a principal and the connectivity profiles its device will be permitted&#58; 16 characters&#44; 80 secret bits&#44; for ten minutes&#44; five attempts and one device&#46; On the runtime&#39;s own machine only&#46; |
| &#47;paths&#47;&#126;1v1&#126;1progress&#47;get&#47;operationId | getProgress |
| &#47;paths&#47;&#126;1v1&#126;1progress&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1progress&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1progress&#47;get&#47;summary | The progress report&#58; where the estate stands&#44; the work orders by state&#44; each published release and what it contained&#44; what comes next by release&#44; the next eligible work and what waits on the operator&#59; text and canonical JSON with compact current attention references&#46; Record&#45;derived sections agree for the same records&#59; repository checks&#44; latest local agent claims and worker observations are labelled separately and can differ by runtime&#46; |
| &#47;paths&#47;&#126;1v1&#126;1providers&#47;get&#47;operationId | getProviders |
| &#47;paths&#47;&#126;1v1&#126;1providers&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1providers&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1providers&#47;get&#47;summary | The provider and adapter implementations available to the runtime on its machine&#44; with their declared settings&#44; destinations and served locations&#58; release providers&#44; source adapters&#44; coding&#45;agent launchers and workspace providers&#46; |
| &#47;paths&#47;&#126;1v1&#126;1reconcile&#47;get&#47;operationId | getReconcile |
| &#47;paths&#47;&#126;1v1&#126;1reconcile&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1reconcile&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1reconcile&#47;get&#47;summary | Reconcile tasks left by diverged copies&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;get&#47;operationId | getRecords |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;get&#47;parameters&#47;0&#47;description | A record type&#44; for example work&#45;order&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;get&#47;parameters&#47;0&#47;name | type |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;get&#47;summary | The current records of one type&#44; oldest first&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;post&#47;operationId | postRecords |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;NewRecordRequest |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#47;post&#47;summary | Author a record&#59; signed with the author&#39;s admitted device on the runtime&#39;s machine&#59; rejected with findings when the estate would be invalid&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#126;1&#123;id&#125;&#47;get&#47;operationId | getRecordsId |
| &#47;paths&#47;&#126;1v1&#126;1records&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1records&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1records&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1records&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1records&#126;1&#123;id&#125;&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#126;1&#123;id&#125;&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#126;1&#123;id&#125;&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1records&#126;1&#123;id&#125;&#47;get&#47;summary | A record by id&#46; |
| &#47;paths&#47;&#126;1v1&#126;1recover&#126;1inspect&#47;get&#47;operationId | getRecoverInspect |
| &#47;paths&#47;&#126;1v1&#126;1recover&#126;1inspect&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1recover&#126;1inspect&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1recover&#126;1inspect&#47;get&#47;summary | What this copy holds&#44; for recovery&#46; |
| &#47;paths&#47;&#126;1v1&#126;1release&#45;bases&#126;1&#123;hash&#125;&#47;get&#47;operationId | getRelease&#45;basesHash |
| &#47;paths&#47;&#126;1v1&#126;1release&#45;bases&#126;1&#123;hash&#125;&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1release&#45;bases&#126;1&#123;hash&#125;&#47;get&#47;parameters&#47;0&#47;name | hash |
| &#47;paths&#47;&#126;1v1&#126;1release&#45;bases&#126;1&#123;hash&#125;&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1release&#45;bases&#126;1&#123;hash&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1release&#45;bases&#126;1&#123;hash&#125;&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1release&#45;bases&#126;1&#123;hash&#125;&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1release&#45;bases&#126;1&#123;hash&#125;&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1release&#45;bases&#126;1&#123;hash&#125;&#47;get&#47;summary | Read retained exact release&#45;admission inputs and findings&#59; historical evidence grants no new authority&#46; |
| &#47;paths&#47;&#126;1v1&#126;1releases&#47;get&#47;operationId | getReleases |
| &#47;paths&#47;&#126;1v1&#126;1releases&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1releases&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1releases&#47;get&#47;summary | Current release records&#46; |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1observe&#47;post&#47;operationId | postReleasesObserve |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1observe&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ReleaseObserveRequest |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1observe&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1observe&#47;post&#47;responses&#47;200&#47;description | ReleaseResponse&#58; what was recorded and what the provider showed&#59; a problem inside when nothing could proceed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1observe&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1observe&#47;post&#47;summary | Observe a release&#39;s run&#44; draft and publication and record what settled &#40;the operation revision&#44; the candidate&#44; the observed publication&#41;&#59; or cancel an intent that had no effect&#46; |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1submit&#47;post&#47;operationId | postReleasesSubmit |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1submit&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ReleaseSubmitRequest |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1submit&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1submit&#47;post&#47;responses&#47;200&#47;description | ReleaseResponse&#58; what was recorded and what the provider showed&#59; a problem inside when nothing could proceed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1submit&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1releases&#126;1submit&#47;post&#47;summary | Submit a release as intent before effect&#58; the operation record is written&#44; then the build is dispatched through the provider the request names or the product declares&#59; nothing is published&#44; and a provider with no implementation here is refused&#46; |
| &#47;paths&#47;&#126;1v1&#126;1resume&#47;post&#47;operationId | postResume |
| &#47;paths&#47;&#126;1v1&#126;1resume&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;HandoffPackage |
| &#47;paths&#47;&#126;1v1&#126;1resume&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1resume&#47;post&#47;responses&#47;200&#47;description | ResumeResponse&#58; every difference&#44; or an invalid&#45;handoff problem&#46; This POST is a read&#59; it authors no record&#46; |
| &#47;paths&#47;&#126;1v1&#126;1resume&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1resume&#47;post&#47;summary | Compare a retained hand&#45;over with current records&#44; including every addition and supersession&#44; missing or changed originals&#44; the full estate pin and observed state&#46; The world may have moved&#59; grants no authority and restarts nothing&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runs&#47;get&#47;operationId | getRuns |
| &#47;paths&#47;&#126;1v1&#126;1runs&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runs&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runs&#47;get&#47;summary | The agent runs&#44; each with its cap recorded before it started&#44; its observed usage once settled&#44; what it is charged &#40;its cap for every unit not reported&#44; and while in flight&#41;&#44; a stop&#39;s facts and worker&#45;observed signals with severity and attention references&#59; and the ledger of every goal with a run or active now&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runs&#47;post&#47;operationId | postRuns |
| &#47;paths&#47;&#126;1v1&#126;1runs&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;RunStartRequest |
| &#47;paths&#47;&#126;1v1&#126;1runs&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1runs&#47;post&#47;responses&#47;200&#47;description | RunResponse&#58; what was recorded and launched&#59; a problem inside when nothing was launched&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runs&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runs&#47;post&#47;summary | Start a coding agent&#39;s run on an assignment &#40;vericairn&#46;worker&#47;1&#41;&#58; refused while the goal is not active or its budget is spent&#44; while a device of an operator or of the goal&#39;s sponsor signs without presence on the runtime&#39;s machine and the sponsor recorded no custody exception in force for the goal&#44; and before anything is recorded when the launcher cannot run&#59; otherwise the workspace is prepared&#44; the intent recorded with the cap&#44; and the harness launched&#44; then observed and settled&#46; The principal recording the run &#40;by&#41; is the operator&#44; the goal&#39;s sponsor when the sponsor is a person&#44; or a principal with the local&#45;engineering scope&#46; Nothing is accepted&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runs&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;operationId | postRunsIdStop |
| &#47;paths&#47;&#126;1v1&#126;1runs&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1runs&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1runs&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1runs&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1runs&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;RunStopRequest |
| &#47;paths&#47;&#126;1v1&#126;1runs&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1runs&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;responses&#47;200&#47;description | RunStopResponse&#58; the stop&#39;s facts as observed&#59; a problem inside when no stop was asked&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runs&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runs&#126;1&#123;id&#125;&#126;1stop&#47;post&#47;summary | Ask a run to stop&#58; the request&#44; its transmission to the harness&#44; the harness&#39;s acknowledgment&#44; an escalation after the grace period and the observed containment are separate facts&#59; a stop that is not observed is reported as requested&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1certificate&#47;post&#47;operationId | postRuntimeCertificate |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1certificate&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;CertificateRotateRequest |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1certificate&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1certificate&#47;post&#47;responses&#47;200&#47;description | CertificateRotated&#58; the new certificate&#39;s fingerprint and expiry&#44; the replaced one&#39;s&#44; whether open connections were closed&#44; and the new identity&#39;s fingerprint when it was replaced&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1certificate&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1certificate&#47;post&#47;responses&#47;409&#47;description | The daemon serves no TLS profile&#44; or no identity to replace &#40;Problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1certificate&#47;post&#47;summary | Replace the runtime certificate now&#58; paired devices keep working&#44; since the runtime identity signs the new one&#59; for a compromise every other open connection is closed too&#44; and identity replaces the runtime identity as well&#44; after which every device pairs again&#46; On the runtime&#39;s own machine only&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;operationId | getRuntimeIdentity |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;parameters&#47;0&#47;description | 32 random bytes&#44; base64url&#44; fresh for each connection&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;parameters&#47;0&#47;name | nonce |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;responses&#47;200&#47;description | RuntimeIdentityProof&#58; the identity key&#39;s signature over the estate&#44; the certificate on this connection and the nonce&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;responses&#47;404&#47;description | The runtime has no identity&#58; it serves no local&#45;network profile &#40;Problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;responses&#47;422&#47;description | No nonce of 16 to 64 bytes &#40;Problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1runtime&#126;1identity&#47;get&#47;summary | The runtime identity&#39;s proof for a nonce&#58; its signature over the estate&#44; the certificate this connection presented and the nonce&#44; which a paired device checks before any other request&#46; |
| &#47;paths&#47;&#126;1v1&#126;1shutdown&#47;post&#47;operationId | postShutdown |
| &#47;paths&#47;&#126;1v1&#126;1shutdown&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1shutdown&#47;post&#47;responses&#47;202&#47;description | The daemon will stop&#46; |
| &#47;paths&#47;&#126;1v1&#126;1shutdown&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1shutdown&#47;post&#47;summary | Ask the daemon to stop&#59; closing a client never stops it&#46; |
| &#47;paths&#47;&#126;1v1&#126;1status&#47;get&#47;operationId | getStatus |
| &#47;paths&#47;&#126;1v1&#126;1status&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1status&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1status&#47;get&#47;summary | Current status&#58; human text and canonical JSON&#44; identical on every machine for the same records&#46; Its compact attention references are derived from records alone&#46; |
| &#47;paths&#47;&#126;1v1&#126;1status&#126;1observations&#47;post&#47;operationId | postStatusObservations |
| &#47;paths&#47;&#126;1v1&#126;1status&#126;1observations&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;EstateObserveRequest |
| &#47;paths&#47;&#126;1v1&#126;1status&#126;1observations&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1status&#126;1observations&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1status&#126;1observations&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1status&#126;1observations&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1status&#126;1observations&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1status&#126;1observations&#47;post&#47;summary | Record a person&#39;s explicit observation of estate status&#44; with runtime&#45;supplied timestamp and hashes of the record set&#44; pin and status&#46; Agents cannot author it&#59; ordinary reads remain read&#45;only&#46; |
| &#47;paths&#47;&#126;1v1&#126;1validate&#47;get&#47;operationId | getValidate |
| &#47;paths&#47;&#126;1v1&#126;1validate&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1validate&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1validate&#47;get&#47;summary | Validate every record and the manifest&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1amend&#47;post&#47;operationId | postWorkAmend |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1amend&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;AmendRequest |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1amend&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1amend&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1amend&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1amend&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1amend&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1amend&#47;post&#47;summary | Request an amendment&#59; it authorizes nothing&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1assign&#47;post&#47;operationId | postWorkAssign |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1assign&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;AssignRequest |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1assign&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1assign&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1assign&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1assign&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1assign&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1assign&#47;post&#47;summary | Delegate a work order to a contributor under a brief&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1attempt&#47;post&#47;operationId | postWorkAttempt |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1attempt&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;AttemptRequest |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1attempt&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1attempt&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1attempt&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1attempt&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1attempt&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1attempt&#47;post&#47;summary | Record the start of an attempt&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1progress&#47;post&#47;operationId | postWorkProgress |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1progress&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;WorkProgressRequest |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1progress&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1progress&#47;post&#47;responses&#47;200&#47;description | WorkProgressResponse&#58; the latest agent claim&#44; a written record only for a completed phase&#44; or a problem when refused&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1progress&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1progress&#47;post&#47;summary | Publish the contributor&#39;s latest progress claim on its own attempt&#46; Active notes replace local state&#59; a completed phase is recorded once&#44; and an identical completion retry returns its record&#46; Claims never establish worker liveness&#44; scope or acceptance&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1result&#47;post&#47;operationId | postWorkResult |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1result&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ResultRequest |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1result&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1result&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1result&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1result&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1result&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1result&#47;post&#47;summary | Submit a result with exact duty proof bindings&#59; a duty result requires the current attempt hash and an admitted signature&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1review&#47;post&#47;operationId | postWorkReview |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1review&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;ResultReviewRequest |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1review&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1review&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1review&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1review&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1review&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1review&#47;post&#47;summary | Review the expected exact result revision with its evidence read&#44; signed as the acting principal&#59; no acceptance is recorded&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1takeover&#47;post&#47;operationId | postWorkTakeover |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1takeover&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;TakeoverRequest |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1takeover&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1takeover&#47;post&#47;responses&#47;200&#47;description | TakeoverResponse&#58; the hand&#45;over recorded and the stops asked&#59; a problem inside when nothing was recorded&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1takeover&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1takeover&#47;post&#47;summary | Hand a step an agent holds over to a person&#58; a new assignment of the step to the person&#44; carrying the agent&#39;s brief with the agent&#39;s assignment among its sources&#44; which holds the step from then on&#59; every run of the agent&#39;s assignment still in flight is asked to stop&#44; each stop&#39;s facts reported as observed&#46; Recorded by a person only&#44; the one taking the step over&#44; the sponsor of its goal or an operator&#44; never by an agent&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#47;get&#47;operationId | getWorkKey |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#47;get&#47;parameters&#47;0&#47;name | key |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#47;get&#47;summary | What an observer sees of a work order&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;operationId | getWorkKeyBrief |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;0&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;0&#47;name | recipient |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;0&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;1&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;1&#47;name | assignment |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;1&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;1&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;2&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;2&#47;name | progress |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;2&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;2&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;3&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;3&#47;name | status |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;3&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;3&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;4&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;4&#47;name | decision |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;4&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;4&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;5&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;5&#47;name | result |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;5&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;5&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;6&#47;in | query |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;6&#47;name | handoff |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;6&#47;required | false |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;parameters&#47;6&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1brief&#47;get&#47;summary | The brief of a work order&#58; everything a contributor needs&#44; from the records alone&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;operationId | postWorkKeyPlacement |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;parameters&#47;0&#47;name | key |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;WorkPlaceRequest |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1work&#126;1&#123;key&#125;&#126;1placement&#47;post&#47;summary | WorkPlace&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;get&#47;operationId | getWriting&#45;exceptions |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;get&#47;summary | WritingExceptionsResponse&#58; registry history&#44; authors&#44; reasons&#44; scopes&#44; counts and exact warning and excepted locations with the basis&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;post&#47;operationId | postWriting&#45;exceptions |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;PolicyChangeRequest |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#47;post&#47;summary | WritingExceptionAdd&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#47;get&#47;operationId | getWriting&#45;exceptionsId |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#47;get&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#47;get&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#47;get&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#47;get&#47;responses&#47;404&#47;description | Nothing has that id&#44; key or hash&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#47;get&#47;summary | WritingExceptionsResponse for one entry identity&#44; including removal history and current matches&#59; creates no record&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;operationId | postWriting&#45;exceptionsIdRemove |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;parameters&#47;0&#47;in | path |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;parameters&#47;0&#47;name | id |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;parameters&#47;0&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;parameters&#47;0&#47;schema&#47;type | string |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;requestBody&#47;content&#47;application&#126;1json&#47;schema&#47;&#36;ref | &#35;&#47;components&#47;schemas&#47;WritingExceptionRemoveRequest |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;requestBody&#47;required | true |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;responses&#47;200&#47;description | What the runtime observed&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;responses&#47;401&#47;description | On loopback&#58; the request is not signed by a device admitted now&#46; On the local network&#58; also when the device is not permitted that profile&#44; the connection&#39;s client certificate is not one its key issued&#44; the request names another principal&#44; or it acts on the runtime&#39;s own machine &#40;Problem with code denied&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;responses&#47;404&#47;description | The subject named does not exist &#40;WriteOutcome with a not&#45;found problem&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;responses&#47;422&#47;description | Refused or rejected &#40;WriteOutcome with a problem&#59; a rejection carries findings&#41;&#46; |
| &#47;paths&#47;&#126;1v1&#126;1writing&#45;exceptions&#126;1&#123;id&#125;&#126;1remove&#47;post&#47;summary | WritingExceptionRemove&#58; validate the expected hash and all source evidence&#44; then sign as the acting principal&#46; |

## Generation basis

Build commit: `9393a977a6b7d7f2b09a93cf5c087260e22dd784`.

Generator: `vericairn.software/api-reference/1`; implementation: `vericairn.software.companions/1`; source digest: `sha256:c3997f1a7bfe9d21f850d7887766cc3159a696ef78174357f71746e10a71f55e`.

Input digest: `sha256:8086593d5c1fce2727213fc12eda5b0b1dc2de459f7647e243849135230dc4a3`.

| Input | SHA-256 | Bytes |
|---|---|---|
| inputs&#47;openapi&#46;json | `sha256:2318b79d214476db5d456c463b1decb52a1045088ce4623abfe4510ac21a36b0` | 161389 |
