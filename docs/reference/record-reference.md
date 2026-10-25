# Record reference

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
| sections | object&#91;&#93; | false | 3 | Exact section reviews&#58; id&#44; hash&#44; sourceDigest&#44; outcome&#44; fidelity &#40;sketch&#124;draft&#124;final&#41;&#44; sources &#40;id&#44; kind&#44; identity&#44; hash&#44; bytes&#44; status and exact records&#41;&#44; checks and per&#45;rule cohesion verdicts&#46; Evaluator judgments name their exact evidence hashes and require independence&#46; A section review clears no other section&#46; |

## `document-revision`

Pins a design or docs file at an exact content hash and revision number&#59; editing the file requires a new revision record&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| path | path | true | 1 | Repository&#45;relative path under design&#47; or docs&#47;&#59; format 3 also admits README&#46;md for the estate&#39;s template instance&#46; |
| title | string | true | 1 | Document title&#46; |
| hash | hash | true | 1 | sha256 of the file bytes&#46; |
| bytes | integer | true | 1 | File size in bytes&#46; |
| mediaType | string | true | 1 | For example text&#47;markdown&#46; |
| revision | integer | true | 1 | 1 for the first revision&#59; later revisions supersede the previous record&#46; |
| summary | string | true | 1 | What this revision is or changes&#46; |

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
| template | object | false | 3 | An exact template instance&#58; id&#44; version&#44; hash&#44; owner&#44; optional scope &#40;domain&#44; product&#44; project&#41;&#44; all effective options and overrides &#40;section&#44; action&#44; reason&#44; and heading&#44; definition or exact decision as applicable&#41;&#46; Templates grant nothing&#46; |
| templateUpgrade | object | false | 3 | An owner&#39;s explicit version choice&#58; offered &#40;id&#44; version&#44; hash&#41;&#44; outcome &#40;accepted&#124;declined&#41;&#44; reason&#46; Acceptance revises the pin&#59; decline preserves it&#46; |

## `observation`

Something reported or measured about an exact subject with an as&#45;of time&#58; a repository&#44; release or workflow run&#59; from record format 2&#44; a person&#39;s explicit observation of an estate&#59; from format 3&#44; package registry state with response evidence&#46; Facts&#44; never permission or health&#46;

Subject type: null; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| kind | repository&#124;release&#124;workflow&#45;run&#124;estate&#124;package&#124;vericairn&#45;release&#124;operator&#45;request | true | 1 | What was observed&#59; estate requires record format 2&#44; a human author and an exact estate subject&#46; |
| asOf | timestamp | true | 1 | When the observation was made&#46; |
| summary | string | true | 1 | The observation in a sentence&#46; |
| facts | object | true | 1 | Flat facts&#58; strings&#44; numbers&#44; booleans or arrays of strings&#59; never machine paths&#46; An estate observation requires records &#40;count&#41;&#44; recordSetHash&#44; pinHash and statusHash&#46; An operator&#45;request observation requires outcome &#40;done&#41;&#44; destination&#44; location and evidenceHash&#44; and its signed exact request subject&#46; Format&#45;3 package facts pin provider&#44; product&#44; productHash&#44; package and version&#44; with either release and releaseHash or reserved &#40;the same version declared reserved in that exact product&#44; with no release pins&#41;&#46; The subject is the exact release&#44; reserved product declaration or original account&#45;holder intent&#46; |
| limitations | string&#91;&#93; | true | 1 | What the observation does not establish&#59; may be empty&#46; |
| evidence | object&#91;&#93; | false | 3 | For package observations&#58; exact public registry url&#44; response&#45;body hash&#44; HTTP status&#44; byte count and content encoding for each response&#59; hashes cover bytes before gzip decoding&#46; |

## `operation`

An intended external effect with an original identity&#58; recorded as not&#45;started before any dispatch&#44; then dispatched&#44; observed&#45;success&#44; known&#45;failure&#44; canceled or uncertain&#44; each as a superseding revision&#46; One current operation per identity&#59; a retry after an uncertain or failed outcome names its reconciliation&#46; The transport response is not the effect&#46;

Subject type: null; subject hash required: false; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| promptUse | object | false | 3 | Exact prompt source&#44; key&#44; revision&#47;version&#44; hash&#44; definition&#44; placeholder values and rendered comment&#46; Authority is unchanged&#46; Kept in run and decision revisions&#46; |
| provider | string | true | 1 | The provider the operation goes through&#44; for example github&#45;releases&#46; |
| action | string | true | 1 | The declared operation&#44; for example dispatch&#45;release&#45;workflow&#46; |
| identity | string | true | 1 | The original identity the effect is reconciled under&#59; unique among current operations&#46; |
| intent | string | true | 1 | The intended effect&#44; in a sentence&#46; |
| target | object | true | 1 | Where the effect lands&#58; repository&#44; workflow&#44; ref&#44; version&#44; product&#59; for an agent run&#44; its assignment&#44; its principal&#44; its workspace &#40;a repository and branch&#44; never a machine path&#41;&#44; the goal it is charged to&#44; the worker contract and&#44; for a run launched under it&#44; the sponsor&#39;s custody exception &#40;custodyException&#41;&#59; string values only&#46; Format&#45;3 package actions pin provider&#44; product&#44; productHash&#44; package and version&#44; with either release and releaseHash or reserved &#40;the same version declared reserved in that exact product&#44; with no release pins&#41;&#59; the subject pins that release or product declaration&#46; |
| status | not&#45;started&#124;dispatched&#124;observed&#45;success&#124;known&#45;failure&#124;canceled&#124;uncertain | true | 1 | not&#45;started&#44; dispatched&#44; observed&#45;success&#44; known&#45;failure&#44; canceled or uncertain&#46; |
| dispatchedAt | timestamp | false | 1 | When the dispatch was made&#59; required once dispatched&#46; Account&#45;holder package actions never claim a dispatch and omit this field&#46; |
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
| packages | object | false | 3 | Registry observations&#58; provider &#40;nuget&#45;org&#41;&#44; ids &#40;one to sixteen distinct NuGet package ids&#41;&#44; and optional reserved &#40;one to eight distinct NuGet placeholder versions&#44; none recorded as a release of this product&#41;&#46; Reserved versions pin this declaration without a release&#46; Account holders perform registry changes&#46; |
| release | object | false | 1 | How the product&#39;s releases are made&#58; destination &#40;provider id and its settings&#44; string values&#41;&#44; checksums &#40;the asset that lists the others&#39; hashes&#41; and artifacts &#40;name with &#123;version&#125; and optionally &#123;platform&#125;&#44; role distributable&#124;sbom&#124;notices&#124;documentation&#124;other&#44; platforms&#41;&#46; Read by release 0&#46;4&#46;2&#44; the first published release to read it&#44; and later &#40;0&#46;4&#46;0&#44; which introduced it&#44; was withdrawn&#41;&#46; |

## `result`

What an attempt submits&#58; outcome&#44; summary&#44; outputs by hash&#44; evidence&#44; checks performed or not&#44; limitations&#44; unresolved external effects and the next permitted step&#46; A claim to inspect&#59; acceptance is a separate record pinned to this exact result&#46;

Subject type: attempt; subject hash required: true; introduced in record format: 1.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| outcome | completed&#124;partial&#124;blocked&#124;abandoned | true | 1 | completed when the brief&#39;s outcome was produced&#59; partial when usable work exists but the outcome was not reached&#59; blocked or abandoned otherwise&#46; |
| summary | string | true | 1 | What was done and what changed&#46; |
| outputs | object&#91;&#93; | true | 1 | name&#44; hash &#40;sha256 of the bytes&#41;&#44; optional bytes&#44; mediaType and location &#40;repository&#45;relative&#59; verified when the file is present&#41;&#59; may be empty&#46; From format 3 an optional section contains id and document &#40;an exact living&#45;document reference&#41;&#59; hash and bytes then identify that section&#39;s payload&#46; |
| evidence | object&#91;&#93; | true | 1 | summary&#44; optional hash and location&#59; may be empty&#46; |
| checks | object&#91;&#93; | true | 1 | name&#44; outcome &#40;passed&#124;failed&#124;skipped&#124;not&#45;performed&#41;&#44; summary&#59; may be empty&#46; |
| limitations | string&#91;&#93; | true | 1 | What the result does not establish&#59; may be empty&#46; |
| unresolvedEffects | string&#91;&#93; | true | 1 | External effects whose outcome is not known and must be reconciled before any retry&#59; may be empty&#46; |
| nextStep | string | true | 1 | The next permitted step for whoever continues&#44; or &#39;none&#39;&#46; |
| duties | object&#91;&#93; | false | 3 | Exact originating policy&#44; rule&#44; tagged occurrence&#44; examined subject&#44; scope&#44; coverage and supporting proof bindings&#46; This is a signed claim&#44; not acceptance&#46; |

## `template-defaults`

Option defaults for an exact template version at estate&#44; domain&#44; product or project scope&#46; Narrower scopes win when an instance is created&#59; existing instances keep their recorded choices&#46;

Subject type: null; subject hash required: false; introduced in record format: 3.

| Field | Kind | Required | Introduced in format | Meaning |
|---|---|---|---|---|
| template | object | true | 3 | The exact content pin&#58; id &#40;namespace&#47;name&#41;&#44; version &#40;semantic version&#41; and hash &#40;canonical SHA&#45;256&#41;&#46; |
| scope | object | true | 3 | kind &#40;estate&#124;domain&#124;product&#124;project&#41;&#44; and target for every kind except estate&#46; One current declaration per template version and scope&#46; |
| options | object | true | 3 | Allowed option choices&#58; template option ids&#44; or section&#45;id&#46;option&#45;id and field&#45;id&#46;option&#45;id&#44; mapped to string values&#46; Unspecified values inherit wider defaults&#46; |
| reason | string | true | 3 | Why this adoption scope uses these defaults&#46; Recording them assigns no role and grants no authority&#46; |

Reserved for later releases: &#91;&#34;object&#34;&#93;.

Build version: 0&#46;8&#46;3; provenance instant: 2026&#45;10&#45;25T22&#58;09&#58;27Z.

## Generation basis

Build commit: `0acf44cbd90710d0f2a83e1b30836303f6ac07af`.

Generator: `vericairn.software/record-reference/1`; implementation: `vericairn.software.companions/1`; source digest: `sha256:6203cd471571e986e7257e7d8bf033aef714b677f2980a7d9f26d465517bebf6`.

Input digest: `sha256:2714bc1f7ad8136b7949d512bfd70cae2ecdbb42d9afd452a3ad350ac5bbdb55`.

| Input | SHA-256 | Bytes |
|---|---|---|
| inputs&#47;record&#45;types&#46;json | `sha256:39b9def84852a9f3721daf86ec4579b2ae6bbdda4645079b1e3ba1f116a132a9` | 20242 |
