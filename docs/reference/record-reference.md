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

Build version: 0&#46;8&#46;3; provenance instant: 2026&#45;10&#45;25T06&#58;16&#58;52Z.

## Generation basis

Build commit: `d882d002a391418a02d7cb35409702d3fe917f61`.

Generator: `vericairn.software/record-reference/1`; implementation: `vericairn.software.companions/1`; source digest: `sha256:6203cd471571e986e7257e7d8bf033aef714b677f2980a7d9f26d465517bebf6`.

Input digest: `sha256:b26aeb47a93cb9eda0dab78c4decb27735ebbe55b0409769098ea9412bf1c8f4`.

| Input | SHA-256 | Bytes |
|---|---|---|
| inputs&#47;record&#45;types&#46;json | `sha256:3ad3dbc5d4df0969ff98062495a61f40436e6864038f5e3c54eaebb35bac8eae` | 9033 |
