# Record reference

## `acceptance`

Acceptance of an exact subject revision&#58; a work order&#44; or a result toward one&#59; a goal&#59; or another record&#44; such as a release candidate&#44; which only the operator accepts&#46; Milestones and their results need the operator&#59; ordinary work may be self&#45;accepted only under an explicit authority scope&#46; A goal is accepted by its sponsor when the sponsor is a person and by an operator when an agent sponsors it&#44; after its sponsor activated it&#44; relying on its current evaluation&#44; which finds every condition met&#44; and never by that evaluation&#39;s author or by the author of one of its steps&#46;

Subject type: null; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| statement | string | true | 1 | What is accepted and on what basis&#46; |
| basis | ulid&#91;&#93; | true | 1 | Review records of the same subject that the acceptance relies on&#59; may be empty for operator acceptance&#46; A goal&#39;s acceptance names the goal&#39;s current evaluation&#46; |
| self | boolean | true | 1 | True when the accepting principal authored the subject&#59; must match the facts&#46; |

## `admission`

Admits a principal&#39;s device to author records in this estate &#40;status admitted&#41;&#44; or revokes it &#40;a superseding record with status revoked and revokedAt&#41;&#46; Authored by the operator or under the &#39;administer&#39; scope&#59; obtaining a clone grants nothing&#46;

Subject type: device; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| principal | ulid | true | 1 | The device&#39;s principal&#44; repeated for clarity&#59; must match the device record&#46; |
| status | admitted&#124;revoked | true | 1 | admitted&#44; or revoked by a superseding record&#46; |
| note | string | true | 1 | Why&#46; |
| revokedAt | timestamp | false | 1 | When the admission ended&#59; required when revoked&#59; records signed by the device at or after this time are invalid&#46; |

## `amendment`

A request&#44; raised during work&#44; to change an assignment&#39;s outcome&#44; scope&#44; target&#44; budget or permitted effects&#46; It authorizes nothing&#58; a new work&#45;order revision&#44; a new assignment or a decision resolves it&#46;

Subject type: assignment; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| kind | scope&#124;effect&#124;budget&#124;target&#124;other | true | 1 | What the request changes&#46; |
| rationale | string | true | 1 | Why the change is needed&#46; |
| proposedChanges | string&#91;&#93; | true | 1 | The concrete changes proposed&#46; |

## `ask`

A question put to a principal&#44; kept apart from any notification of it&#58; what is asked&#44; the recommendation first&#44; what happens while it stays unanswered&#44; and its state &#40;open&#44; notified or withdrawn&#41;&#46; A decision that names it in answers answers it&#59; waiting never does&#46;

Subject type: null; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| to | ulid | true | 2 | The principal asked&#46; |
| question | string | true | 2 | What is asked&#46; |
| recommendation | string | true | 2 | The asker&#39;s recommendation&#44; given first&#46; |
| consequence | string | true | 2 | What happens while the ask stays unanswered&#46; |
| options | string&#91;&#93; | false | 2 | The choices offered&#44; when there are some&#46; |
| goal | ulid | false | 2 | The goal the ask belongs to&#44; when it does&#46; |
| state | open&#124;notified&#124;withdrawn | true | 2 | open when asked&#59; notified when a notification went out&#44; whose delivery stays unknown&#59; withdrawn when the asker takes it back&#46; |
| assignment | ulid | false | 2 | The assignment routing stored together with this ask&#44; of the principal asked&#44; which holds its step only with it&#46; |

## `assignment`

Delegates a work order to a contributor under a brief &#40;outcome&#44; sources&#44; permitted changes&#44; prohibited effects&#44; workspace&#44; limits&#44; expected outputs&#44; review criteria&#44; recovery&#41;&#44; pinned to the exact work&#45;order revision&#46;

Subject type: work&#45;order; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| execution | object | false | 2 | Explicit execution choices and cumulative restrictions&#59; launcher&#44; roster&#44; goal&#44; assignment&#44; then run precedence&#46; Role restrictions remain binding&#46; |
| contributor | ulid | true | 1 | The principal that does the work&#58; a human&#44; a deterministic tool&#44; a native agent or a qualified external agent&#46; Admitted before the work is assigned&#46; |
| brief | object | true | 1 | outcome&#44; sources &#40;references &#123;type&#44; id&#44; hash&#63;&#125;&#41;&#44; permittedChanges&#44; prohibitedEffects&#44; workspace &#40;allowed trees&#44; named without machine paths&#41;&#44; limits&#44; expectedOutputs&#44; reviewCriteria&#44; recovery&#46; |
| note | string | false | 1 | Free text&#46; |
| routedWith | run&#124;ask | false | 2 | Set by routing on an assignment it stores together with the record that carries it&#58; run&#44; the intent of the agent run on it &#40;an agent&#45;run operation whose subject is this assignment&#41;&#44; or ask&#44; the ask that names it&#46; Until that record exists the assignment holds nothing of its step&#44; so an interrupted store never holds a step&#46; |

## `attempt`

One attributable contribution toward an assignment&#44; authored by the assignment&#39;s contributor and pinned to the exact brief&#46; A result record closes it&#59; several attempts can exist without erasing one another&#46;

Subject type: assignment; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| startedAt | timestamp | true | 1 | When the attempt began&#46; |
| workspace | string | true | 1 | Where the work happens&#44; named without machine&#45;specific paths&#58; a repository and branch&#44; a clone&#44; a tool&#46; |
| note | string | true | 1 | What the contributor intends to do&#46; |

## `authority`

A standing&#44; scoped grant of permission to a principal&#44; with reserved actions and conditions&#44; based on a decision&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| grantee | ulid | true | 1 | The principal receiving the grant&#46; |
| scope | string&#91;&#93; | true | 1 | Granted scopes&#59; known names&#58; local&#45;engineering&#44; repository&#45;write&#44; self&#45;accept&#45;ordinary&#45;work&#44; accept&#45;work&#44; administer&#44; spending&#44; qualify&#44; manage&#45;estate&#45;policies&#44; grant&#45;policy&#45;exceptions&#44; manage&#45;writing&#45;exceptions&#46; Others are free text&#46; |
| reserved | string&#91;&#93; | true | 1 | Actions explicitly never granted &#40;publication&#44; spending&#44; credentials&#44; &#46;&#46;&#46;&#41;&#46; |
| conditions | string&#91;&#93; | true | 1 | Conditions under which the grant applies&#46; |
| basis | ulid | true | 1 | The decision record that authorizes this grant&#46; |
| expires | timestamp | false | 2 | When the grant stops being in force&#59; a record authored from then on cannot rely on it&#46; |
| bounds | object&#91;&#93; | false | 3 | Required for policy scopes&#58; one typed bound per scope&#44; with actions&#44; subject selection and scope&#45;specific limits&#46; Policy grants require expiry and an operator signature&#46; |

## `communication-delegation`

A person delegates changes to their own communication setting at one scope&#59; no other authority is granted&#46;

Subject type: null; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| recipient | ulid | true | 1 | The person granting authority&#59; must be the record&#39;s author&#46; |
| grantee | ulid | true | 1 | The principal allowed to change this scope&#46; |
| scope | person&#124;assignment | true | 1 | The personal scope delegated&#46; |
| assignment | ulid | false | 1 | Required only for the assignment scope&#46; |
| status | active&#124;revoked | true | 1 | Revocation supersedes the grant&#59; earlier authorized changes remain valid&#46; |
| expires | timestamp | false | 1 | No change at or after this time is authorized&#46; |

## `communication-setting`

Estate defaults or a person&#39;s estate or assignment communication values&#44; with an append&#45;only revision history&#46;

Subject type: null; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| scope | estate&#124;person&#124;assignment | true | 1 | The scope being revised&#46; |
| recipient | ulid | false | 1 | The person whose choices these are&#59; required outside estate defaults&#46; |
| assignment | ulid | false | 1 | Required only for an assignment override&#46; |
| values | object | true | 1 | Optional progress &#40;none&#124;completion&#45;and&#45;need&#124;milestones&#124;verbose&#41;&#44; status&#44; decision&#44; result and handoff &#40;short&#124;standard&#124;comprehensive&#41;&#46; Omission inherits&#46; |
| authority | ulid | false | 1 | Explicit communication&#45;delegation for a change by another principal&#46; |
| authorityHash | hash | false | 1 | Exact delegation revision used&#46; |
| basis | ulid | false | 1 | Existing decision explicitly adopted&#59; never inferred from conversation&#46; |
| basisHash | hash | false | 1 | Exact decision revision adopted&#46; |

## `credential-reference`

A credential named by reference and never by its value&#58; what it is for&#44; who holds it&#44; where it is kept&#44; what it permits&#44; how its presence is checked without using it&#44; and when it is rotated&#46; Records&#44; briefs and logs carry references&#44; never secrets&#46;

Subject type: null; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| name | slug | true | 2 | The stable name it is referred to by&#59; unique among current references&#46; |
| purpose | string | true | 2 | What it is for&#46; |
| kind | api&#45;token&#124;oauth&#45;client&#124;signing&#45;certificate&#124;ssh&#45;key&#124;password&#124;device&#45;key&#124;other | true | 2 | What kind of credential it is&#46; |
| holder | ulid | true | 2 | The principal responsible for it&#46; |
| store | string | true | 2 | Where it is kept&#44; described without its value or a machine path&#44; for example a named keychain item or an Actions secret of a repository&#46; |
| scopes | string&#91;&#93; | true | 2 | What it permits&#59; may be empty&#46; |
| check | string | false | 2 | How its presence is checked without using it&#59; a presence check is not authentication&#46; |
| status | active&#124;rotated&#124;revoked&#124;retired | true | 2 | active&#44; rotated &#40;a successor reference replaces it&#41;&#44; revoked or retired&#46; |
| rotatedAt | timestamp | false | 2 | When it was last issued or rotated&#46; |
| rotateBy | timestamp | false | 2 | When it is due for rotation&#46; |

## `custody-exception`

The sponsor&#39;s exception to the custody precondition for one goal revision&#58; why unattended runs of the goal may launch on a machine where a key of an operator or of the sponsor signs without its owner&#39;s presence&#44; and when the exception expires&#46; Recorded only by the goal&#39;s sponsor&#44; or by an operator when an agent sponsors it&#59; a revised goal needs a fresh one&#46;

Subject type: goal; subject hash required: true; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| reason | string | true | 2 | Why the sponsor accepts unattended runs of the goal while such a key signs where they run&#46; |
| expires | timestamp | true | 2 | When the exception stops being in force&#59; later than the record&#46; A revision by its author can bring it forward&#46; |

## `decision`

An operator decision&#58; the verbatim statement&#44; a summary&#44; its effects and the work orders it affects&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| title | string | true | 1 | Short title&#46; |
| statement | string | true | 1 | The operator&#39;s words&#44; verbatim&#46; |
| summary | string | true | 1 | What was decided&#44; in one or two sentences&#44; for lists&#46; |
| synthesis | string | false | 1 | A clean&#44; thorough restatement for display&#58; context&#44; what was decided&#44; why&#44; consequences and limits&#46; Expected on every decision&#59; the recorder writes it and it never alters the verbatim statement&#46; |
| effects | string&#91;&#93; | true | 1 | Concrete effects on the estate&#46; |
| affects | ulid&#91;&#93; | true | 1 | Work orders this decision changes&#59; may be empty&#46; |
| decidedBy | ulid | true | 1 | The operator principal who decided&#46; |
| decidedAt | timestamp | true | 1 | When the operator decided&#46; |
| stake | low&#124;medium&#124;high&#124;critical | true | 2 | How much rides on the decision&#59; at medium and above the recorder asks with a recommendation first and at most three questions at a time&#46; |
| notAuthorized | string&#91;&#93; | true | 2 | What this answer does not authorize&#44; so no one reads more into it&#59; may be empty&#46; |
| answers | ulid&#91;&#93; | false | 2 | The asks this decision answers&#59; an ask is answered by the principal it was put to&#44; and never by waiting&#46; |
| obligations | object&#91;&#93; | false | 2 | What the answer leaves owed&#58; key&#44; summary&#44; owner &#40;ulid&#41;&#44; resolution &#40;action&#124;evidence&#124;review&#124;decision&#41;&#44; discharge &#40;what discharges it&#41; and optionally gates &#40;the operation or work it holds&#41;&#59; each stays open until a discharge record names it&#46; |
| documents | string&#91;&#93; | false | 2 | The living documents this decision changes&#44; by path or by declaration id&#59; each becomes due until it is cleared&#46; |
| policies | object&#91;&#93; | false | 3 | Keyed declare&#47;revise&#47;retire requirements&#58; policy key or identity&#44; priorHash for changes&#44; change and optional exact definitionHash&#46; A requirement does not grant authority&#46; |

## `device`

A device holding a signing key for a principal&#58; platform&#44; custody strength&#44; public key and permitted connectivity profiles&#46; Signed by its own key to prove possession&#59; admitted separately&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| principal | ulid | true | 1 | The principal the device acts for&#46; |
| name | string | true | 1 | Descriptive name&#44; never a machine path&#46; |
| platform | windows&#124;linux&#124;macos&#124;ios&#124;android&#124;other | true | 1 | Operating system family&#46; |
| custody | windows&#45;cng&#124;macos&#45;keychain&#124;linux&#45;secret&#45;service&#124;file&#45;owner&#45;only&#124;macos&#45;secure&#45;enclave&#124;offline | true | 1 | How the private key is held&#59; recorded strength&#44; never assumed&#46; From record format 2&#44; macos&#45;secure&#45;enclave&#44; and offline for a recovery device&#39;s exported key&#46; |
| algorithm | ecdsa&#45;p256&#45;sha256 | true | 1 | Signature algorithm&#46; |
| publicKey | string | true | 1 | Base64url SubjectPublicKeyInfo of the P&#45;256 public key&#46; |
| profiles | string&#91;&#93; | true | 1 | Permitted connectivity profiles&#58; embedded&#44; local&#45;socket&#44; loopback&#44; local&#45;network&#44; relay&#46; |
| presence | required&#124;none | true | 2 | Whether each signature needs the owner&#39;s presence&#59; required only where the custody enforces it&#44; and never for an agent&#39;s or a service&#39;s device&#46; |
| purpose | signing&#124;recovery | true | 2 | signing for ordinary work&#59; recovery for the operator&#39;s offline recovery device&#46; |

## `discharge`

The discharge of one obligation a decision left&#58; which obligation&#44; by its key&#44; and the evidence that discharges it&#59; recorded by the obligation&#39;s owner or the operator&#46; An obligation stays open until a discharge names it&#46;

Subject type: decision; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| obligation | slug | true | 2 | The key of the obligation in the decision&#39;s obligations&#46; |
| summary | string | true | 2 | How it was discharged&#46; |
| evidence | object&#91;&#93; | true | 2 | summary&#44; optional hash and location&#58; what shows it is discharged&#59; at least one&#46; |

## `document-review`

Clears a due living document&#44; or finds it stale&#58; the reviewed content by hash &#40;and commit for another repository&#41;&#44; the covered paths&#39; identity&#44; the triggers it answers&#44; the checks that ran&#44; and the reviewer&#39;s independence of the content&#46;

Subject type: living&#45;document; subject hash required: true; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| outcome | current&#124;revised&#124;needs&#45;change | true | 2 | current when the content holds&#59; revised when it was changed for the trigger&#59; needs&#45;change when it is stale and was not changed&#44; so it stays due&#46; |
| content | object | true | 2 | The reviewed bytes&#58; hash &#40;sha256&#41; and bytes&#44; and commit for a document of another repository&#46; |
| covered | hash | false | 2 | The covered paths&#39; content identity when reviewed&#44; for a document with a covers trigger&#59; required to clear one&#46; |
| answers | object&#91;&#93; | true | 2 | The triggers this review answers&#58; on&#44; and the record that fired it for release&#44; upgrade&#44; decision and accepted&#59; may be empty&#46; |
| independence | self&#124;independent&#45;agent&#124;operator | true | 2 | self when the reviewer revised the content&#44; or&#44; for a document of another repository&#44; states that it wrote it&#59; independent&#45;agent for another agent&#59; operator for the operator&#46; |
| summary | string | true | 2 | What was checked&#46; |
| findings | object&#91;&#93; | true | 2 | severity &#40;error&#124;warning&#124;info&#41;&#44; summary&#44; optional location&#59; may be empty&#46; |
| checks | object&#91;&#93; | true | 2 | The declared checks that ran&#58; name&#44; outcome &#40;passed&#124;failed&#124;skipped&#124;not&#45;performed&#41;&#44; summary&#59; a failed check refuses the clearance&#59; may be empty&#46; |

## `document-revision`

Pins a design or docs file at an exact content hash and revision number&#59; editing the file requires a new revision record&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| path | path | true | 1 | Repository&#45;relative path under design&#47; or docs&#47;&#46; |
| title | string | true | 1 | Document title&#46; |
| hash | hash | true | 1 | sha256 of the file bytes&#46; |
| bytes | integer | true | 1 | File size in bytes&#46; |
| mediaType | string | true | 1 | For example text&#47;markdown&#46; |
| revision | integer | true | 1 | 1 for the first revision&#59; later revisions supersede the previous record&#46; |
| summary | string | true | 1 | What this revision is or changes&#46; |

## `estate`

The estate&#39;s charter&#58; name&#44; purpose&#44; root operator principal and policies&#46; Exactly one is current&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| name | string | true | 1 | Display name of the estate&#46; |
| slug | slug | true | 1 | Lower&#45;case kebab&#45;case identifier used in paths and tooling&#46; |
| purpose | string | true | 1 | What the estate exists to do&#44; in one or two sentences&#46; |
| operator | ulid | true | 1 | The root operator principal&#59; the only principal allowed to author itself&#46; |
| recordFormat | integer | true | 1 | The record format the estate was chartered in&#44; at most the format of the charter record itself&#59; the manifest states the format new records are written in&#46; |
| policies | object | true | 1 | signatures&#58; optional&#124;required&#59; releaseAcceptance&#58; roles that may accept a release&#46; |
| organization | object | false | 1 | name and optional github organization login&#46; |

## `estate-policy`

An owned&#44; signed policy with explicit selection&#44; versioned rules&#44; level&#44; activation and bounded exceptions&#59; behavioral changes need exact operator approval&#46;

Subject type: null; subject hash required: false; introduced in record format: 3.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| key | slug | true | 3 | Stable estate&#45;unique identity key&#44; retained after retirement&#46; |
| title | string | true | 3 | Human title&#46; |
| owner | ulid | true | 3 | Responsible principal&#59; ownership grants no authority&#46; |
| reason | string | true | 3 | The declaration or change reason&#46; |
| basis | object&#91;&#93; | true | 3 | Exact decision references&#44; optional requirement keys and the exact authority grant used by a delegate&#46; |
| state | active&#124;retired | true | 3 | Retirement is terminal for this identity&#46; |
| appliesTo | object | true | 3 | Tagged estate&#44; product&#44; work&#45;orders or repository selection&#46; |
| rules | object&#91;&#93; | true | 3 | Unique stable key&#44; namespaced kind&#44; positive version and parameters for each rule&#46; |
| level | remind&#124;warn&#124;refuse | true | 3 | Level of every rule&#46; |
| startsAt | timestamp | true | 3 | Activation no earlier than the first declaration&#46; |
| maxExceptionDays | integer | true | 3 | Positive maximum duration of any grant from its authoring time&#46; |
| predecessor | reference | false | 3 | Exact retired policy succeeded by a new identity and distinct key&#46; |
| retirement | object | false | 3 | Required only when retired&#58; reason&#44; findings disposition and optional successor identity&#46; |
| dutyDispositions | object&#91;&#93; | false | 3 | Exact already&#45;due occurrence inventory ended&#45;unmet by this approved revision&#44; with reasons&#46; |

## `goal`

An objective the estate carries out with agents and people&#58; what done looks like&#44; its constraints&#44; budget&#44; roles and sponsor&#46; Anyone may propose one&#59; its sponsor activates and revises it&#44; or an operator when an agent sponsors it&#44; and it is done only when the records and an independent evaluation show it and the sponsor accepts it&#46;

Subject type: null; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| execution | object | false | 2 | Explicit execution choices and cumulative restrictions&#59; launcher&#44; roster&#44; goal&#44; assignment&#44; then run precedence&#46; Role restrictions remain binding&#46; |
| title | string | true | 2 | Short title&#46; |
| objective | string | true | 2 | What the sponsor wants&#44; in the sponsor&#39;s words&#46; |
| completion | object&#91;&#93; | true | 2 | The conditions that make it done&#44; at least one&#58; a record predicate &#123;record&#58; &#123;type&#44; subject&#63;&#44; product&#63;&#44; fields&#63;&#44; state&#63;&#44; min&#63;&#44; max&#63;&#125;&#125;&#44; checked from current records&#44; counts the current records of the type about the subject &#40;any revision of it&#41;&#44; of the product &#40;living documents&#41;&#44; with each body field equal to its string and in the state status derives &#40;ask&#44; living&#45;document&#44; work&#45;order&#41;&#44; and holds when the count is within min and max &#40;at least one when neither is given&#41;&#59; an evidence statement &#123;evidence&#44; check&#125; is judged by an evaluator who did none of the work&#46; |
| constraints | string&#91;&#93; | true | 2 | What must not change or happen&#59; may be empty&#46; |
| kind | finite&#124;stewardship | true | 2 | finite ends when done&#59; stewardship holds a standing condition&#46; |
| sponsor | ulid | true | 2 | The accountable principal&#46; A person who sponsors a goal activates&#44; revises&#44; stops and accepts it&#59; a goal an agent sponsors is activated&#44; revised and accepted by an operator&#44; and only an operator&#46; A revision is recorded by who decides for the revision it supersedes&#59; anyone else proposes the change as an ask&#46; |
| delegate | ulid | false | 2 | The principal the sponsor&#44; a person&#44; delegates the acceptance of an ordinary goal to&#59; it accepts while it holds the &#39;accept&#45;work&#39; scope&#46; Never the sponsor&#44; never for a goal an agent sponsors&#44; and never for a goal that includes a reserved action&#58; a condition about a release or a milestone&#44; or a milestone among its work&#46; |
| roster | ulid | false | 2 | The roster record whose roles it uses&#59; the estate&#39;s current roster otherwise&#46; |
| roles | object&#91;&#93; | false | 2 | Role overrides for this goal&#58; role&#44; principals in order of preference&#44; optional note&#46; |
| budget | object | true | 2 | Caps by unit&#44; granted by the sponsor&#39;s activation&#59; wallMinutes and attemptsPerStep are required&#46; Units&#58; wallMinutes&#44; attemptsPerStep&#44; tokens and concurrency &#40;positive integers&#41; and currency &#40;&#123;amount in minor units&#44; unit&#58; three upper&#45;case letters&#125;&#41;&#59; an absent unit is not an unlimited one&#46; |
| plan | object | true | 2 | policy &#40;proposed&#45;by&#45;planner&#124;given&#41;&#44; approval &#40;sponsor&#45;approves&#124;self&#45;approves&#45;ordinary&#41;&#44; and workOrders &#40;ulid&#91;&#93;&#41; for a given plan&#46; |
| limits | object | false | 2 | consecutiveFailures &#40;a positive integer&#41; and deadline &#40;a timestamp&#41; are validated and recorded but not yet enforced&#46; stallSeconds &#40;event&#45;stream silence limit&#44; 1–86400 seconds&#41; and watch &#40;bounded rate and loop thresholds&#41; override the roster and launcher defaults&#44; enforced by the worker&#46; |
| inputs | reference&#91;&#93; | true | 2 | Decisions&#44; work orders and designs it builds on&#58; &#123;type&#44; id&#44; hash&#63;&#125;&#59; may be empty&#46; |

## `goal-status`

A change in a goal&#39;s lifecycle&#58; active &#40;activated or resumed by its sponsor&#44; a person with the authority to spend&#44; or by an operator when an agent sponsors it&#44; which grants its budget&#41;&#44; paused&#44; budget&#45;limited&#44; blocked&#44; impossible &#40;the evaluator&#39;s verdict&#44; confirmed by the sponsor&#41; or abandoned&#46; Achievement is an acceptance of the goal&#44; never a status&#46;

Subject type: goal; subject hash required: true; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| status | active&#124;paused&#124;budget&#45;limited&#124;blocked&#124;impossible&#124;abandoned | true | 2 | The new status of the exact goal revision&#59; a revised goal starts again from its activation&#46; Active is recorded by the sponsor&#44; a person who holds the operator role or a grant of the &#39;spending&#39; scope in force&#44; or by an operator when an agent sponsors the goal&#59; impossible and abandoned by the sponsor or&#44; for a goal an agent sponsors&#44; an operator&#46; |
| note | string | true | 2 | Why&#44; and what remains&#46; |

## `harness-qualification`

An authorized person&#39;s qualification of one exact harness version from six probe digests&#59; a superseding revocation withdraws it&#46; It establishes neither live inference nor work acceptance&#46;

Subject type: null; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| launcher | codex&#124;claude&#45;code | true | 2 | The native launcher whose probes were run&#46; |
| version | string | true | 2 | The exact observed version&#44; never a range&#46; |
| status | qualified&#124;revoked | true | 2 | Qualified initially&#59; revoked only by a superseding record preserving launcher&#44; version and probes&#46; |
| probes | object | true | 2 | SHA&#45;256 digests named version&#44; flags&#44; tools&#44; configuration&#44; login and mcp&#46; No raw probe output&#46; |

## `living-document`

Declares a living document&#58; its path and repository&#44; whether it is authored or generated&#44; the triggers that make it due and at which level &#40;remind&#44; warn or refuse&#41;&#44; its generated regions&#44; its checks and who may clear it&#46;

Subject type: null; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| path | path | false | 2 | The document&#39;s repository&#45;relative path&#59; required unless format 3 supplies pathPattern&#46; |
| pathPattern | string | false | 3 | For generated documents only&#58; a literal directory followed by &#123;month&#125;&#47;&#123;date&#125;&#45;&#123;slug&#125;&#45;&#123;id&#125;&#46;md&#46; Exclusive with path&#59; members come from exact source revisions&#46; |
| repository | ulid | false | 2 | The resource record of the repository that holds it&#59; absent for the estate&#39;s own repository&#46; |
| title | string | true | 2 | The document&#39;s title&#46; |
| kind | authored&#124;generated | true | 2 | authored by a person or an agent&#44; or generated from its inputs&#46; |
| generator | object | false | 2 | For a generated document&#44; required&#58; id &#40;such as vericairn&#46;mcp&#47;orientation&#41; and inputs &#40;string&#91;&#93;&#41;&#46; |
| source | string | true | 2 | How its content is read at a revision&#58; estate&#45;file&#44; or a pack&#39;s source such as vericairn&#46;software&#47;git&#45;file&#46; |
| triggers | object&#91;&#93; | true | 2 | Nonempty for authored and legacy declarations&#59; strict format&#45;3 whole generated declarations allow an empty list&#46; Each trigger has on&#44; level &#40;remind&#124;warn&#124;refuse&#41;&#44; and its declared inputs&#46; |
| regions | object&#91;&#93; | false | 2 | Generated regions&#58; id&#44; generator and optionally its inputs &#40;string&#91;&#93;&#41;&#59; marked &lt;&#33;&#45;&#45; vericairn&#58;generated &lt;id&gt; &#45;&#45;&gt; and &lt;&#33;&#45;&#45; vericairn&#58;end &lt;id&gt; &#45;&#45;&gt; in the document&#46; |
| clearedBy | any&#124;independent&#124;operator | false | 2 | Who may clear it&#58; any admitted principal &#40;the default&#41;&#44; independent &#40;not the one that revised or wrote its content&#41;&#44; or operator&#46; |
| checks | string&#91;&#93; | false | 2 | Checks run at each clearance&#58; names&#45;version&#44; matches&#45;generator&#44; links&#45;resolve&#44; or a pack&#39;s named namespace&#47;name&#46; |
| summary | string | true | 2 | Why it is living&#58; the facts it states&#46; |

## `migration`

An upgrade of the estate applied by &#39;vericairn estate upgrade&#39;&#58; the release pinned before and after&#44; the record format before and after&#44; the migrations applied and what they changed&#46; Forward&#45;only&#59; rollback is a reverted commit&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| fromPin | string | true | 1 | The pin before&#58; &#39;source &lt;repository&gt;&#64;&lt;commit&gt;&#39; or &#39;release &lt;version&gt;&#39;&#46; |
| toPin | string | true | 1 | The pin after&#46; |
| fromFormat | integer | true | 1 | Record format before&#46; |
| toFormat | integer | true | 1 | Record format after&#46; |
| migrations | string&#91;&#93; | true | 1 | Names of the record&#45;format migrations applied&#59; may be empty&#46; |
| changes | string&#91;&#93; | true | 1 | What changed in the repository&#59; may be empty&#46; |
| release | ulid | false | 1 | The release record upgraded to&#44; when pinning a release&#46; |
| anchored | object&#91;&#93; | false | 2 | Every admission recorded without a signature when the estate moved to record format 2&#44; each &#123;id&#44; hash&#125;&#59; signed by the estate&#39;s root operator or a principal with the &#39;administer&#39; scope&#44; this list is what keeps them counting after the move&#46; |

## `observation`

Something reported or measured about an exact subject with an as&#45;of time&#58; a repository&#44; release or workflow run&#59; from record format 2&#44; a person&#39;s explicit observation of an estate&#46; Facts&#44; never permission or health&#46;

Subject type: null; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| kind | repository&#124;release&#124;workflow&#45;run&#124;estate | true | 1 | What was observed&#59; estate requires record format 2&#44; a human author and an exact estate subject&#46; |
| asOf | timestamp | true | 1 | When the observation was made&#46; |
| summary | string | true | 1 | The observation in a sentence&#46; |
| facts | object | true | 1 | Flat facts&#58; strings&#44; numbers&#44; booleans or arrays of strings&#59; never machine paths&#46; An estate observation requires records &#40;count&#41;&#44; recordSetHash&#44; pinHash and statusHash&#46; |
| limitations | string&#91;&#93; | true | 1 | What the observation does not establish&#59; may be empty&#46; |

## `operation`

An intended external effect with an original identity&#58; recorded as not&#45;started before any dispatch&#44; then dispatched&#44; observed&#45;success&#44; known&#45;failure&#44; canceled or uncertain&#44; each as a superseding revision&#46; One current operation per identity&#59; a retry after an uncertain or failed outcome names its reconciliation&#46; The transport response is not the effect&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| provider | string | true | 1 | The provider the operation goes through&#44; for example github&#45;releases&#46; |
| action | string | true | 1 | The declared operation&#44; for example dispatch&#45;release&#45;workflow&#46; |
| identity | string | true | 1 | The original identity the effect is reconciled under&#59; unique among current operations&#46; |
| intent | string | true | 1 | The intended effect&#44; in a sentence&#46; |
| target | object | true | 1 | Where the effect lands&#58; repository&#44; workflow&#44; ref&#44; version&#44; product&#59; for an agent run&#44; its assignment&#44; its principal&#44; its workspace &#40;a repository and branch&#44; never a machine path&#41;&#44; the goal it is charged to&#44; the worker contract and&#44; for a run launched under it&#44; the sponsor&#39;s custody exception &#40;custodyException&#41;&#59; string values only&#46; |
| status | not&#45;started&#124;dispatched&#124;observed&#45;success&#124;known&#45;failure&#124;canceled&#124;uncertain | true | 1 | not&#45;started&#44; dispatched&#44; observed&#45;success&#44; known&#45;failure&#44; canceled or uncertain&#46; |
| dispatchedAt | timestamp | false | 1 | When the dispatch was made&#59; required once dispatched&#46; |
| observed | object | false | 1 | summary&#44; at&#44; optional url and reference&#58; what was observed&#59; required for observed&#45;success and known&#45;failure&#46; |
| reconciliation | string | false | 1 | How an uncertain or failed outcome was settled before a retry&#59; required when a retry is dispatched&#46; |
| evidence | object&#91;&#93; | true | 1 | summary&#44; optional hash and location&#59; may be empty&#46; |
| checks | object&#91;&#93; | false | 3 | For repository changes&#58; name&#44; head&#44; status and optional conclusion for every reported check&#46; A merge admits only successful checks of its exact head&#46; |
| requiredChecks | string&#91;&#93; | false | 3 | The exact check names from the pinned product declaration&#59; none guessed&#46; |
| scan | object | false | 3 | For a push&#58; tool&#44; version&#44; range&#44; verdict and optional findingCount&#46; Only aggregate scan facts&#59; never matched text&#44; findings or process output&#46; |
| cap | object | false | 2 | For an agent&#45;run&#58; what the run may spend&#44; recorded before it starts and kept in every revision&#59; wallMinutes is required&#46; A run that names its goal starts only while the goal is active&#44; with the units its budget grants&#44; within what remains of them&#44; its concurrency and its attempts per step&#46; Units&#58; wallMinutes&#44; attemptsPerStep&#44; tokens and concurrency &#40;positive integers&#41; and currency &#40;&#123;amount in minor units&#44; unit&#58; three upper&#45;case letters&#125;&#41;&#59; an absent unit is not an unlimited one&#46; |
| usage | object | false | 2 | For an agent&#45;run once settled&#58; what it spent as the harness reported it&#44; and the units its cap states that were not reported &#40;unreported&#41;&#44; which are charged at the cap&#46; |
| scope | object | false | 2 | Worker scope audit at settlement&#58; changed count&#44; outsideCount&#44; repository&#45;relative outside paths &#40;at most 1000&#41;&#44; and optional problem when incomplete&#46; Outside changes need attention&#44; never acceptance&#46; |
| liveness | object | false | 2 | Worker&#45;observed event&#45;stream liveness&#58; stallSeconds&#44; lastEventAt&#44; and optional attentionAt&#44; attention &#40;ask id&#41;&#44; stoppedAt&#46; Attention at one silence limit&#59; stop at two&#46; |
| signals | object&#91;&#93; | false | 2 | Worker event&#45;stream and settlement observations&#58; code&#44; severity &#40;warning&#124;critical&#41;&#44; at&#44; summary&#44; optional fingerprint&#44; count and attention &#40;ask id&#41;&#46; Raw invocation inputs never enter a record&#46; |
| execution | object | false | 2 | Immutable admitted model&#44; effort&#44; provenance&#44; disabled Adapter&#44; environment inventory and context allocation&#44; recorded before dispatch&#46; |
| executionObserved | object | false | 2 | Harness reports at settlement&#44; with discrepancies and facts still unknown&#59; never inferred from the request&#46; |
| stop | object | false | 2 | For an agent&#45;run that was stopped&#58; requestedAt and requestedBy &#40;a principal&#44; or worker for a cap the run reached&#41;&#44; optional reason&#44; then each fact once observed&#58; transmittedAt with transmission&#44; acknowledgedAt with acknowledgment&#44; escalatedAt with escalation&#44; and containedAt&#44; only in a settled revision&#46; A stop not observed stays requested&#46; |

## `policy-exception`

An attributable grant for exact finite targets of one policy rule&#44; with exclusive expiry and signed revocation&#59; it neither accepts work nor discharges debt&#46;

Subject type: estate&#45;policy; subject hash required: true; introduced in record format: 3.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| rule | slug | true | 3 | The rule in the exact subject policy&#46; |
| targets | object&#91;&#93; | true | 3 | Nonempty finite record&#44; publication&#44; file or occurrence targets&#59; optional exact release submission&#46; |
| reason | string | true | 3 | Why this grant is needed&#46; |
| expiresAt | timestamp | true | 3 | Exclusive expiry&#44; bounded from the initial grant time&#46; |
| basis | object | true | 3 | Exact decision and&#44; for a delegate&#44; authority reference&#46; |
| state | granted&#124;revoked | true | 3 | A revocation is an immediate signed successor&#59; renewal is a new identity&#46; |

## `possibility`

An idea captured with its disposition&#44; explicitly not an approval&#58; what it is&#44; where it came from&#44; and whether it is open&#44; parked&#44; adopted &#40;with the work order&#44; decision or goal it became&#41; or declined&#44; and why&#46;

Subject type: null; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| title | string | true | 2 | Short title&#46; |
| idea | string | true | 2 | The idea&#44; as it was raised&#46; |
| origin | string | true | 2 | Who raised it and where&#44; without machine paths&#46; |
| disposition | open&#124;parked&#124;adopted&#124;declined | true | 2 | What has become of it&#59; a revision records a new disposition&#46; |
| reason | string | false | 2 | Why it was parked&#44; adopted or declined&#59; required unless it is open&#46; |
| adoptedAs | reference | false | 2 | The work order&#44; decision or goal it became&#58; &#123;type&#44; id&#44; hash&#63;&#125;&#59; required when adopted&#46; |

## `principal`

A human&#44; agent or service that authors records&#46; Only the root operator may author its own principal record&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| kind | human&#124;agent&#124;service | true | 1 | What the principal is&#46; |
| name | string | true | 1 | Display name&#46; |
| roles | string&#91;&#93; | true | 1 | operator&#44; builder&#44; reviewer&#44; contributor&#44; service&#46; |
| agent | object | false | 1 | For agents&#58; vendor&#44; product and optional notes&#46; Never a model name as proof of anything&#46; |
| notes | string | false | 1 | Free text&#46; |

## `product`

A product the estate owns&#58; name&#44; summary&#44; editions and surfaces&#44; and optionally how its releases are made &#40;the destination and the artifacts a complete release carries&#41;&#46; Details live in design documents&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| name | string | true | 1 | Product name&#46; |
| slug | slug | true | 1 | Identifier&#46; |
| summary | string | true | 1 | The product in a sentence&#46; |
| editions | object&#91;&#93; | true | 1 | name&#44; slug&#44; summary&#44; optional license &#40;SPDX expression&#41;&#46; |
| surfaces | object&#91;&#93; | true | 1 | name&#44; slug&#44; summary&#58; runtime&#44; Workbench&#44; Remote&#44; CLI&#44; SDK&#44; websites&#46; |
| changes | object | false | 3 | Checked repository changes&#58; provider&#44; repository resource id&#44; target branch and requiredChecks &#40;exact host check names&#44; including each required platform&#41;&#46; An empty or absent declaration authorizes no merge&#46; |
| release | object | false | 1 | How the product&#39;s releases are made&#58; destination &#40;provider id and its settings&#44; string values&#41;&#44; checksums &#40;the asset that lists the others&#39; hashes&#41; and artifacts &#40;name with &#123;version&#125; and optionally &#123;platform&#125;&#44; role distributable&#124;sbom&#124;notices&#124;documentation&#124;other&#44; platforms&#41;&#46; Read by release 0&#46;4&#46;2&#44; the first published release to read it&#44; and later &#40;0&#46;4&#46;0&#44; which introduced it&#44; was withdrawn&#41;&#46; |

## `qualification`

A platform profile&#39;s qualification for a release&#44; derived from its observations and never asserted beside them&#58; qualified&#44; measured in part&#44; not qualified&#44; or a non&#45;claim&#44; with the verifications&#44; reviews and observations it rests on and what they do not establish&#46;

Subject type: release; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| profile | string | true | 2 | The platform profile&#44; as design&#47;platforms&#46;md names it&#44; for example linux&#45;x64 or Workbench on macOS&#46; |
| outcome | qualified&#124;measured&#45;in&#45;part&#124;not&#45;qualified&#124;non&#45;claim | true | 2 | What the evidence establishes&#59; qualified needs evidence&#44; a non&#45;claim needs none&#46; |
| evidence | reference&#91;&#93; | true | 2 | The verifications&#44; reviews and observations it rests on&#58; &#123;type&#44; id&#44; hash&#63;&#125;&#46; |
| summary | string | true | 2 | What was observed&#44; in a sentence or two&#46; |
| limitations | string&#91;&#93; | true | 2 | What the evidence does not establish&#59; may be empty&#46; |

## `reconciliation`

Settles divergent revisions of one record by keeping one successor and withdrawing the others&#46; Communication scopes also permit their owner to settle independent first profiles at that exact scope&#46; Nothing is deleted&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| kept | ulid | true | 1 | The successor of the subject that remains current&#44; or the chosen first communication profile at that scope&#46; |
| withdrawn | ulid&#91;&#93; | true | 1 | The other successors of the subject &#40;or first communication profiles at the same scope&#41;&#44; withdrawn in favor of the kept one&#46; |
| note | string | true | 1 | Why this one is kept and what&#44; if anything&#44; from the others must be carried into a later revision&#46; |
| policyAuthority | reference | false | 3 | Exact bounded grant used to reconcile policy or exception history&#59; no generic engineering grant substitutes&#46; |

## `release`

An identified release of a product&#58; version&#44; exact commit&#44; artifacts by hash and platform&#44; software bill of materials&#44; notices&#44; evidence&#44; record&#45;format compatibility&#44; signing state&#44; limitations and status&#46; A candidate precedes publication&#59; a published revision supersedes it after the operator&#39;s acceptance and the observed publication&#46;

Subject type: product; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| version | semver | true | 1 | Product version&#46; |
| repository | owner&#47;name | true | 1 | Source repository&#46; |
| commit | hex | true | 1 | The exact commit built &#40;40 hex digits&#41;&#46; |
| status | candidate&#124;published&#124;withdrawn | true | 1 | candidate&#44; published &#40;needs the operator&#39;s acceptance of the candidate&#41; or withdrawn&#46; |
| artifacts | object&#91;&#93; | true | 1 | name&#44; hash&#44; bytes&#44; platform&#59; the distributable files&#46; May be empty only for a candidate&#46; |
| recordFormat | object | true | 1 | reads &#40;integer&#91;&#93;&#41; and writes &#40;integer&#41;&#58; which record formats this release handles&#46; |
| sbom | hash | false | 1 | Hash of the software bill of materials artifact&#46; |
| notices | hash | false | 1 | Hash of the third&#45;party notices artifact&#46; |
| evidence | object&#91;&#93; | true | 1 | summary&#44; optional hash and location&#58; test runs&#44; qualification&#44; cold starts&#46; |
| qualified | string&#91;&#93; | true | 1 | Platforms whose qualification is recorded for this release&#59; may be empty&#46; |
| signing | string | true | 1 | For example &#39;unsigned developer build&#39; until signing material exists&#46; |
| limitations | string&#91;&#93; | true | 1 | Known limitations&#59; may be empty&#46; |
| publishedAt | timestamp | false | 1 | When the publication was observed &#40;published only&#41;&#46; |
| url | string | false | 1 | Where the published release is observed &#40;published only&#41;&#46; |

## `resource`

A repository&#44; service&#44; storage location or account that the estate uses&#44; with its role and visibility&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| kind | repository&#124;service&#124;storage&#124;account | true | 1 | What the resource is&#46; |
| name | string | true | 1 | Display name&#46; |
| role | string | true | 1 | What the estate uses it for&#46; |
| location | object | true | 1 | github&#58; owner&#47;name&#44; url&#44; and&#47;or rootCommit &#40;the first commit of a repository with no remote&#41;&#59; never a machine path&#46; |
| visibility | public&#124;private&#124;n&#47;a | true | 1 | Who can see it&#46; |
| notes | string | false | 1 | Free text&#46; |

## `result`

What an attempt submits&#58; outcome&#44; summary&#44; outputs by hash&#44; evidence&#44; checks performed or not&#44; limitations&#44; unresolved external effects and the next permitted step&#46; A claim to inspect&#59; acceptance is a separate record pinned to this exact result&#46;

Subject type: attempt; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| outcome | completed&#124;partial&#124;blocked&#124;abandoned | true | 1 | completed when the brief&#39;s outcome was produced&#59; partial when usable work exists but the outcome was not reached&#59; blocked or abandoned otherwise&#46; |
| summary | string | true | 1 | What was done and what changed&#46; |
| outputs | object&#91;&#93; | true | 1 | name&#44; hash &#40;sha256 of the bytes&#41;&#44; optional bytes&#44; mediaType and location &#40;repository&#45;relative&#59; verified when the file is present&#41;&#59; may be empty&#46; |
| evidence | object&#91;&#93; | true | 1 | summary&#44; optional hash and location&#59; may be empty&#46; |
| checks | object&#91;&#93; | true | 1 | name&#44; outcome &#40;passed&#124;failed&#124;skipped&#124;not&#45;performed&#41;&#44; summary&#59; may be empty&#46; |
| limitations | string&#91;&#93; | true | 1 | What the result does not establish&#59; may be empty&#46; |
| unresolvedEffects | string&#91;&#93; | true | 1 | External effects whose outcome is not known and must be reconciled before any retry&#59; may be empty&#46; |
| nextStep | string | true | 1 | The next permitted step for whoever continues&#44; or &#39;none&#39;&#46; |
| duties | object&#91;&#93; | false | 3 | Exact originating policy&#44; rule&#44; tagged occurrence&#44; examined subject&#44; scope&#44; coverage and supporting proof bindings&#46; This is a signed claim&#44; not acceptance&#46; |

## `retirement`

Registers a temporary artifact &#40;path&#41; with the condition for its removal&#59; once retiredAt is set the path must be gone&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| path | path | true | 1 | Repository&#45;relative path of the temporary artifact&#46; |
| reason | string | true | 1 | Why it exists&#46; |
| condition | string | true | 1 | When it must be removed&#46; |
| retiredAt | timestamp | false | 1 | Set by the superseding record once the artifact is removed&#46; |

## `review`

A review of an exact subject revision&#46; Independence is checked against who authored the subject&#58; self&#44; independent&#45;agent or operator&#46; A review of a goal is its evaluation&#58; a verdict on each completion condition with the records it read&#44; by a principal who authored none of the goal&#39;s steps&#46;

Subject type: null; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| independence | self&#124;independent&#45;agent&#124;operator | true | 1 | self when the reviewer authored the subject&#59; independent&#45;agent for a different agent principal&#59; operator for the operator&#46; |
| outcome | pass&#124;findings&#124;fail | true | 1 | The verdict&#46; |
| summary | string | true | 1 | What was reviewed and how&#46; |
| findings | object&#91;&#93; | true | 1 | severity &#40;error&#124;warning&#124;info&#41;&#44; summary&#44; optional location&#59; may be empty&#46; |
| conditions | object&#91;&#93; | false | 2 | For a review of a goal&#44; its evaluation&#44; and only then&#58; one entry per completion condition in the goal&#39;s order&#44; each a verdict &#40;met&#124;not&#45;yet&#124;impossible&#41;&#44; the reason and the records it read &#40;&#123;type&#44; id&#44; hash&#125;&#41;&#59; the outcome is pass when every verdict is met&#44; fail when one is impossible&#44; and findings otherwise&#46; |
| read | reference&#91;&#93; | false | 3 | Unique exact evidence references for an ordinary result review&#59; duty reviews require an admitted&#45;device signature&#46; |

## `roster`

Who fills each role of the estate&#39;s work&#44; in order of preference&#58; planner&#44; builder&#44; reviewer&#44; evaluator&#44; release manager&#44; publisher and decider&#44; or a role the estate names&#46; Agents where available&#44; people where needed&#59; a role with no available principal becomes an ask&#44; never a silent fallback&#46;

Subject type: null; subject hash required: false; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| execution | object | false | 2 | Explicit execution choices and cumulative restrictions&#59; launcher&#44; roster&#44; goal&#44; assignment&#44; then run precedence&#46; Role restrictions remain binding&#46; |
| roles | object&#91;&#93; | true | 2 | Each role &#40;a lower&#45;case name such as builder or decider&#41;&#44; its principals &#40;ulid&#91;&#93;&#41; in order of preference&#44; and an optional note&#59; the decider role is held by operators&#46; |
| limits | object | false | 2 | stallSeconds &#40;event&#45;stream silence limit&#44; 1–86400 seconds&#41; and watch &#40;bounded rate and loop thresholds&#41;&#44; inherited by goals using this roster&#46; Goal fields override these&#59; omitted fields keep the launcher defaults&#46; |

## `verification`

A check of an exact subject &#40;a repository resource&#44; a result or a release&#41; at an exact commit&#58; the method&#44; the command&#44; the environment&#44; who performed it&#44; the observed result&#44; evidence and limitations&#46; Evidence&#44; never permission&#59; historical once the subject changes&#46;

Subject type: null; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| method | string | true | 1 | How the check was performed&#58; command&#44; github&#45;actions&#44; manual&#44; or a named tool&#46; |
| commit | hex | true | 1 | The exact commit checked &#40;40 hex digits&#41;&#46; |
| command | string | false | 1 | The command run&#44; when the method is a command&#46; |
| environment | object | true | 1 | Where it ran&#58; platform&#44; tool versions&#44; runner&#59; string values only&#44; never machine paths&#46; |
| role | self&#124;independent&#45;agent&#124;operator&#124;automation | true | 1 | self&#44; independent&#45;agent&#44; operator or automation&#46; |
| outcome | passed&#124;failed&#124;unknown | true | 1 | passed&#44; failed or unknown&#46; |
| summary | string | true | 1 | What was observed&#46; |
| exitCode | integer | false | 1 | Process exit code&#44; when a command ran&#46; |
| durationMs | integer | false | 1 | How long the check took&#46; |
| startedAt | timestamp | false | 1 | When it started&#46; |
| finishedAt | timestamp | false | 1 | When it finished&#46; |
| evidence | object&#91;&#93; | true | 1 | summary&#44; optional hash and location&#59; may be empty&#46; |
| limitations | string&#91;&#93; | true | 1 | What the check does not establish&#59; may be empty&#46; |
| evidenceClass | observation&#124;report&#124;attestation&#124;inference | false | 2 | How the evidence was obtained&#58; observed where it was recorded&#44; reported by a host or provider&#44; attested by a principal&#44; or inferred from other evidence&#46; |
| observer | person&#124;agent&#124;automation&#124;provider | false | 2 | What kind of observer made the claim&#46; |
| expires | timestamp | false | 2 | When the claim stops counting as current and needs observing again&#46; |

## `work-order`

A bounded unit of work&#58; intent&#44; scope&#44; inputs&#44; deliverables and the one question whose answer closes it&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| key | KEY | true | 1 | Short upper&#45;case key such as M0 or DESIGN&#45;01&#59; unique among current work orders&#46; |
| kind | milestone&#124;ordinary | true | 1 | Milestones need operator acceptance&#59; ordinary work may be self&#45;accepted under authority&#46; |
| title | string | true | 1 | Short title&#46; |
| intent | string | true | 1 | Why this work exists and what outcome it serves&#46; |
| scope | string&#91;&#93; | true | 1 | What is in scope&#46; |
| exitQuestion | string | true | 1 | The question whose honest &#39;yes&#39; closes the work&#46; |
| inputs | reference&#91;&#93; | true | 1 | Records this work builds on&#58; &#123;type&#44; id&#125;&#59; may be empty&#46; |
| deliverables | string&#91;&#93; | true | 1 | What will exist when the work is done&#46; |
| after | ulid&#91;&#93; | false | 2 | Work orders this one starts after&#44; so the next eligible work can be derived&#59; never itself&#44; never a cycle&#46; |
| blockedBy | reference&#91;&#93; | false | 2 | What holds it&#58; asks&#44; decisions&#44; operations or work orders&#44; as &#123;type&#44; id&#44; hash&#63;&#125;&#46; |
| role | string | false | 2 | The role of the roster whose principals carry it&#58; planner&#44; builder&#44; reviewer&#44; evaluator&#44; release&#45;manager&#44; publisher&#44; decider&#44; or a role the estate names&#59; builder when omitted&#46; A step whose role is publisher or decider is a reserved action&#58; it reaches only a person who holds it&#44; or an agent a grant in force covers&#44; and otherwise becomes an ask&#46; |
| placement | object | false | 3 | Tagged release &#40;carrier identity&#44; product identity&#44; version&#41; or backlog &#40;key&#41;&#46; Placement grants no authority&#46; |
| releasePlan | object | false | 3 | The product identity and exact version this work order carries&#59; it may place itself in its own plan&#46; |

## `work-progress`

The contributor&#39;s claim about one completed phase of an attempt&#46; At most one record per phase&#59; interim notes stay in live state&#46; Neither liveness nor acceptance&#46;

Subject type: attempt; subject hash required: true; introduced in record format: 2.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| phase | string | true | 2 | A stable lower&#45;case phase name&#44; reused by its interim notes&#46; |
| note | string | true | 2 | The contributor&#39;s claim about the phase&#44; never proof of liveness or scope&#46; |
| completedAt | timestamp | true | 2 | When the contributor claimed the phase completed&#46; |

## `work-status`

A status change of a work order&#58; planned&#44; active&#44; paused&#44; submitted or retired&#46; Acceptance is a separate record&#46;

Subject type: work&#45;order; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| status | planned&#124;active&#124;paused&#124;submitted&#124;retired | true | 1 | The new status&#46; |
| note | string | true | 1 | Why&#44; and what remains&#46; |

## `writing-exception`

One exact writing&#46;spacing token&#44; its reason&#44; optional coverage&#44; signed author and signed removal&#59; never authority for another action&#46;

Subject type: null; subject hash required: false; introduced in record format: 3.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| token | string | true | 3 | One case&#45;sensitive whole spacing token&#46; |
| code | writing&#46;spacing | true | 3 | Only the spacing warning&#46; |
| reason | string | true | 3 | Why the spelling is intentional&#46; |
| scope | object | false | 3 | Union of repository path selectors and exact record&#47;field selections&#59; omitted means the whole scan&#46; |
| state | active&#124;removed | true | 3 | Removal is a signed successor and terminal for this identity&#46; |
| basis | object | true | 3 | kind&#58; operator&#44; or kind&#58; authority with an exact grant reference&#46; |
| removalReason | string | false | 3 | Required only for removal&#46; |

Reserved for later releases: &#91;&#34;object&#34;&#93;.

Build version: 0&#46;8&#46;0; provenance instant: 2026&#45;10&#45;19T14&#58;18&#58;24Z.

## Generation basis

Build commit: `0d9cc5404308f4e73909074069629b05be5510f1`.

Generator: `vericairn.software/record-reference/1`; implementation: `vericairn.software.companions/1`; source digest: `sha256:c3997f1a7bfe9d21f850d7887766cc3159a696ef78174357f71746e10a71f55e`.

Input digest: `sha256:787b7b63c8803031a86dc065b9ac01978140d9992f8b09fb5997012741e9509c`.

| Input | SHA-256 | Bytes |
|---|---|---|
| inputs&#47;record&#45;types&#46;json | `sha256:f679041af46fe112bc83f39f569f8f2c44c0ca2d77d45a8c7160be9c592a7a5a` | 65696 |
