# Command-line reference

## Interrupted client requests

An interrupted client request writes its diagnostic to standard error&#46; It does not emit a replacement JSON result on standard output&#46; Existing command names&#44; options&#44; help and successful JSON responses are unchanged&#46;

| Outcome | Exit code | Diagnostic |
|---|---|---|
| `timed-out` | `124` | `error: The request timed out.` |
| `cancelled` | `130` | `error: The request was cancelled.` |
| `unavailable` | `2` | `error: The runtime is unavailable.` |

When an effect may have happened, the diagnostic also says: `The outcome is unknown; reconcile the original operation before retrying.`

A definite refusal keeps its diagnostic and exit code &#96;2&#96;&#59; it is never called unavailable or of unknown outcome&#46; Existing structured refusal responses keep their representation&#46;

Caller cancellation &#40;including Ctrl&#43;C for ordinary requests&#41; and a per&#45;operation deadline are separate outcomes&#46; Cancellation does not undo an effect&#44; and these clients do not retry it&#46; Foreground worker following still interprets Ctrl&#43;C as a request to stop the original run&#59; runtime serving still interprets it as a request to stop the service&#46; Their requests remain subject to deadlines&#46;

## Root options

- `-?, -h, /?, /h, --help`: Show help and usage information&#46; Applies to subcommands&#46;
- `--version`: Show version information
- `-e, --estate <estate>`: Path to the estate repository root&#46; Applies to subcommands&#46; Default&#58; the current directory&#46;
- `--json`: Machine&#45;readable output&#46; Applies to subcommands&#46;
- `--recipient <recipient>`: The person addressed by human presentation &#40;ULID&#41;&#59; omitted means estate defaults&#44; never an inferred person&#46; Applies to subcommands&#46;
- `--for-assignment <for-assignment>`: Exact assignment for the recipient&#39;s communication override&#46; Applies to subcommands&#46;
- `--depth <depth>`: One&#45;view depth&#58; short&#44; standard or comprehensive&#46; Canonical &#45;&#45;json is independent of depth&#46; Applies to subcommands&#46;

## `vericairn ask`

Show what waits on a principal&#44; or on every principal&#58; the asks still open or notified&#44; at most three in one delivery with the recommendation first and the consequence of leaving each unanswered&#44; the rest waiting in order&#46; Reading them answers nothing&#59; an ask is answered by a decision of the principal it is put to that names it in its answers&#46;

Usage: `vericairn ask [options]`

Options:

- `--to <to>`: The principal asked &#40;ULID&#41;&#59; every principal when omitted&#46;

## `vericairn changes`

Record checked pushes&#44; pull requests&#44; pinned merges and their observations&#46;

Usage: `vericairn changes [command] [options]`

Verbs: `push`, `open`, `merge`, `observe`

### `vericairn changes merge`

Observe every declared check at the exact head&#44; merge pinned to that head&#44; then observe the target run&#46;

Usage: `vericairn changes merge [options]`

Options:

- `--product <product>`: Product record id declaring the repository&#44; provider&#44; target branch and required checks&#46; Required&#46;
- `--pull-request <pull-request>`: Pull request number at the declared destination&#46; Required&#46;
- `--head <head>`: Exact commit expected at the branch or pull request head&#46; Required&#46;
- `--by <by>`: Principal id with repository&#45;write authority in force&#46; Required&#46;
- `--reconciliation <reconciliation>`: How the previous known failure under this identity was settled&#59; required for a retry&#46;

### `vericairn changes observe`

Record an observation of the original change&#59; uncertain effects remain uncertain until confirmed&#46;

Usage: `vericairn changes observe [options]`

Options:

- `--operation <operation>`: Recorded push&#44; opening or merge operation id to observe under its original pins&#46; Required&#46;
- `--by <by>`: Principal id with repository&#45;write authority in force&#46; Required&#46;
- `--cancel <cancel>`: Operator&#39;s explicit reconciliation that the original intent had no effect&#59; cancels only the recorded intent&#46;

### `vericairn changes open`

Open a pull request from an exact head with a recorded successful scanned push&#46;

Usage: `vericairn changes open [options]`

Options:

- `--product <product>`: Product record id declaring the repository&#44; provider&#44; target branch and required checks&#46; Required&#46;
- `--branch <branch>`: The branch to push or open&#44; in the declared repository&#46; Required&#46;
- `--head <head>`: Exact commit expected at the branch or pull request head&#46; Required&#46;
- `--title <title>`: Title of the pull request&#46; Required&#46;
- `--body <body>`: Body of the pull request&#46; Required&#46;
- `--by <by>`: Principal id with repository&#45;write authority in force&#46; Required&#46;
- `--reconciliation <reconciliation>`: How the previous known failure under this identity was settled&#59; required for a retry&#46;

### `vericairn changes push`

Record intent&#44; scan exactly the commits&#44; then push the pinned head and record its observed outcome&#46;

Usage: `vericairn changes push [options]`

Options:

- `--product <product>`: Product record id declaring the repository&#44; provider&#44; target branch and required checks&#46; Required&#46;
- `--branch <branch>`: The branch to push or open&#44; in the declared repository&#46; Required&#46;
- `--head <head>`: Exact commit expected at the branch or pull request head&#46; Required&#46;
- `--by <by>`: Principal id with repository&#45;write authority in force&#46; Required&#46;
- `--reconciliation <reconciliation>`: How the previous known failure under this identity was settled&#59; required for a retry&#46;

## `vericairn check`

Run a check and record its evidence as a verification of the exact commit&#44; or observe a workflow run&#46;

Usage: `vericairn check [command] [options]`

Verbs: `run`, `observe`

### `vericairn check observe`

Observe a workflow run at its host and record it as automation&#39;s verification of the commit it built&#46;

Usage: `vericairn check observe [options]`

Options:

- `--resource <resource>`: Resource record id of the repository checked&#46; Required&#46;
- `--repository <repository>`: Where the run is &#40;for GitHub&#44; owner&#47;name&#41;&#59; the resource&#39;s recorded location when omitted&#46;
- `--provider <provider>`: The provider that observes the run&#59; the one serving the resource&#39;s recorded location when omitted&#46;
- `--run <run>`: Workflow run id&#46; Required&#46;
- `--workflow <workflow>`: Workflow file the run belongs to&#46; Default&#58; ci&#46;yml&#46;
- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;

### `vericairn check run`

Run a command in a repository and record a verification&#58; commit&#44; command&#44; environment&#44; exit code&#44; duration and output as evidence&#46;

Usage: `vericairn check run <command>... [options]`

Arguments:

- `<command>...`: The command and its arguments&#59; put &#45;&#45; before it&#46;

Options:

- `--resource <resource>`: Resource record id of the repository checked&#46; Required&#46;
- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--in <in>`: Working tree the command runs in&#46; Default&#58; the current directory&#46;
- `--role <role>`: self&#44; independent&#45;agent&#44; operator or automation&#46; Default&#58; self&#46;
- `--summary <summary>`: What the check establishes&#44; in your words&#59; the observed exit and duration are recorded regardless&#46;
- `--timeout <timeout>`: Seconds after which the command is stopped and its result recorded as unknown&#46; Default&#58; 3600&#46;

## `vericairn communication`

Inspect or change attributable personal communication settings&#46; Remote is not built&#46;

Usage: `vericairn communication [command] [options]`

Verbs: `show`, `set`, `reset`, `delegate`

### `vericairn communication delegate`

Explicitly grant or revoke changes to one&#39;s own communication setting&#59; grants no other permission&#46;

Usage: `vericairn communication delegate [options]`

Options:

- `--as <as>`: The recipient granting consent&#46; Required&#46;
- `--to <to>`: The delegated principal&#46; Required&#46;
- `--revoke`: Revoke the current delegation&#46;
- `--expected <expected>`: Expected prior delegation hash&#59; required when revising a grant&#46;
- `--expires <expires>`: Expiry as a UTC record timestamp&#46;

### `vericairn communication reset`

Reset this complete scope to inheritance as a new record&#46;

Usage: `vericairn communication reset [options]`

Options:

- `--as <as>`: Actual author&#59; defaults to their own setting unless &#45;&#45;recipient names another person&#46; Required&#46;
- `--scope <scope>`: person&#44; estate&#44; or assignment &#40;also selected by &#45;&#45;for&#45;assignment&#41;&#46; Default&#58; person&#46;
- `--expected <expected>`: Expected prior revision hash&#44; or absent to require no existing profile&#46; Without it the command reads the current revision before applying the named fields&#46;
- `--authority <authority>`: Explicit communication&#45;delegation record for a delegated change&#46;
- `--basis <basis>`: Exact decision being explicitly adopted&#59; does not grant delegated authority&#46;
- `--progress <progress>`: none&#44; completion&#45;and&#45;need&#44; milestones&#44; verbose&#44; or inherit&#46;
- `--status <status>`: short&#44; standard&#44; comprehensive&#44; or inherit&#46;
- `--decisions <decisions>`: Decision lead depth&#44; or inherit&#46;
- `--results <results>`: Result explanation depth&#44; or inherit&#46;
- `--handoffs <handoffs>`: Handoff introduction depth&#44; or inherit&#46;

### `vericairn communication set`

Change the named fields&#44; preserving the others&#59; inherit resets a field&#46;

Usage: `vericairn communication set [options]`

Options:

- `--as <as>`: Actual author&#59; defaults to their own setting unless &#45;&#45;recipient names another person&#46; Required&#46;
- `--scope <scope>`: person&#44; estate&#44; or assignment &#40;also selected by &#45;&#45;for&#45;assignment&#41;&#46; Default&#58; person&#46;
- `--expected <expected>`: Expected prior revision hash&#44; or absent to require no existing profile&#46; Without it the command reads the current revision before applying the named fields&#46;
- `--authority <authority>`: Explicit communication&#45;delegation record for a delegated change&#46;
- `--basis <basis>`: Exact decision being explicitly adopted&#59; does not grant delegated authority&#46;
- `--progress <progress>`: none&#44; completion&#45;and&#45;need&#44; milestones&#44; verbose&#44; or inherit&#46;
- `--status <status>`: short&#44; standard&#44; comprehensive&#44; or inherit&#46;
- `--decisions <decisions>`: Decision lead depth&#44; or inherit&#46;
- `--results <results>`: Result explanation depth&#44; or inherit&#46;
- `--handoffs <handoffs>`: Handoff introduction depth&#44; or inherit&#46;

### `vericairn communication show`

Show effective values&#44; contributing revisions&#44; conflicts and freshness&#46;

Usage: `vericairn communication show [options]`

Options:

- `--preview`: Show the shared status&#44; decision&#44; result and handoff examples&#46;

## `vericairn connections`

The runtimes this machine reaches over the local network&#58; add one by pairing with a code&#44; list them with their state&#44; and remove one&#46; Kept for this user on this machine&#44; never in a repository&#46;

Usage: `vericairn connections [command] [options]`

Verbs: `add`, `list`, `remove`

### `vericairn connections add`

Pair this machine with the runtime at an address using a code an administrator issued&#58; the runtime proves it knows the code and who it is&#44; this machine makes its device key and presents it&#44; and the runtime records and admits the device&#46; The connection pins the runtime&#39;s identity&#46;

Usage: `vericairn connections add <address> [options]`

Arguments:

- `<address>`: Where the runtime listens&#58; its address and port&#44; as the pairing code&#39;s administrator gave them&#44; for example 192&#46;168&#46;1&#46;20&#58;7443&#46;

Options:

- `--code <code>`: The pairing code&#44; as shown&#58; four groups of four characters &#40;16 in all&#41;&#46; Required&#46;
- `--name <name>`: This device&#39;s descriptive name&#44; never a machine path&#46; Required&#46;
- `--custody <custody>`: Key custody&#58; default&#44; the strongest this machine offers&#44; or file&#44; cng&#44; secure&#45;enclave or secret&#45;service&#46;
- `--presence <presence>`: Every signature needs its owner&#39;s presence &#40;Secure Enclave custody only&#44; never for an agent&#41;&#46; Default&#58; a person&#39;s device needs it wherever its custody enforces it&#59; &#39;&#45;&#45;presence false&#39; declines&#46;

### `vericairn connections list`

Check every connection now and show its state&#58; current&#44; partial&#44; denied&#44; cached &#40;with when it was last current&#41; or unavailable&#46;

Usage: `vericairn connections list [options]`

### `vericairn connections remove`

Forget a connection on this machine&#46; The device stays admitted until an administrator revokes it&#44; since disconnection is never revocation&#44; and its key stays in this machine&#39;s custody&#46;

Usage: `vericairn connections remove <id> [options]`

Arguments:

- `<id>`: The connection&#39;s id&#44; as listed&#46;

## `vericairn context`

Prepare measured context and retrieve pinned originals under current permission&#46;

Usage: `vericairn context [command] [options]`

Verbs: `catalogue`, `prepare`, `read`

### `vericairn context catalogue`

List the current document revision catalogue without selecting or reading its bodies&#46;

Usage: `vericairn context catalogue [options]`

### `vericairn context prepare`

Compute the context package for a work order without writing&#46;

Usage: `vericairn context prepare <work-order> [options]`

Arguments:

- `<work-order>`:

Options:

- `--max-bytes <max-bytes>`: Remaining context input allocation in bytes&#44; separate from the run spending cap&#46; Mandatory overflow is refused&#46;

### `vericairn context read`

Read and verify a pinned original&#44; never silently its latest revision&#46;

Usage: `vericairn context read <pin> [options]`

Arguments:

- `<pin>`: Self&#45;contained pinned identity from the package&#46;

Options:

- `--source <source>`: Legacy manifest handles are unavailable&#59; prepare context again for a pin&#46;
- `--original`: Return only the pinned original&#46; Required&#46;
- `--out <out>`: Write exact bytes to a new file&#59; never overwrite&#46;

## `vericairn decisions`

List the estate&#39;s current decisions&#44; or show one cleanly&#46;

Usage: `vericairn decisions [<id>] [options]`

Arguments:

- `<id>`: Decision record id &#40;any revision&#41;&#59; omit to list&#46;

## `vericairn devices`

Device keys&#58; create a device for a principal on this machine&#44; pair one on another machine&#44; list&#44; admit&#44; revoke&#44; rename and rotate&#44; and the operator&#39;s recovery device&#46;

Usage: `vericairn devices [command] [options]`

Verbs: `create`, `list`, `admit`, `revoke`, `recovery`, `pair`, `writes`, `rename`, `rotate`

### `vericairn devices admit`

Admit a device to author records in this estate &#40;operator or &#39;administer&#39; scope&#41;&#46;

Usage: `vericairn devices admit <device> [options]`

Arguments:

- `<device>`: Device record id&#46;

Options:

- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--note <note>`: Why the device is admitted&#46; Required&#46;
- `--recovery <recovery>`: Sign with the operator&#39;s recovery key in this file&#44; when no admitted device of the operator is left&#59; asks for its passphrase&#46; Every use stays in &#39;status&#39; until the operator reviews it&#46;
- `--passphrase-stdin`: Read the recovery key&#39;s passphrase from the first line of standard input instead of the terminal&#46;

### `vericairn devices create`

Generate a key on this machine and record the device&#44; signed by its own key&#46;

Usage: `vericairn devices create [options]`

Options:

- `--principal <principal>`: The principal this device acts for&#46; Required&#46;
- `--name <name>`: Descriptive device name&#44; never a machine path&#46; Required&#46;
- `--custody <custody>`: Key custody&#58; default&#44; the strongest this machine offers &#40;CNG on Windows&#59; the Secure Enclave on a Mac that has one&#59; the secret service on Linux where one runs&#59; else an owner&#45;only file&#41;&#44; or file&#44; cng&#44; secure&#45;enclave or secret&#45;service&#46;
- `--presence <presence>`: Every signature needs its owner&#39;s presence &#40;Touch ID or the login password&#59; Secure Enclave custody only&#44; never for an agent&#41;&#46; Default&#58; a person&#39;s device needs it wherever its custody enforces it&#59; &#39;&#45;&#45;presence false&#39; declines&#46;
- `--profile <profile>`: Permitted connectivity profile&#59; repeatable &#40;default&#58; embedded&#41;&#46;

### `vericairn devices list`

List device records with their admission state and whether their key is held here&#46;

Usage: `vericairn devices list [options]`

### `vericairn devices pair`

Issue a pairing code for a new device on another machine&#58; 16 characters&#44; valid ten minutes&#44; for one device and five attempts&#46; Entered there with &#39;vericairn connections add&#39;&#44; it records the device with the key it presents and admits it under your authority &#40;needs the runtime serving the local&#45;network profile&#41;&#46;

Usage: `vericairn devices pair [options]`

Options:

- `--principal <principal>`: The principal the new device will act for&#46; Required&#46;
- `--by <by>`: The administrator&#58; the operator&#44; or a principal holding the &#39;administer&#39; scope&#44; whose admitted device on this machine signs the admission&#46; Required&#46;
- `--profile <profile>`: A connectivity profile the device will be permitted&#59; repeatable &#40;default&#58; local&#45;network&#41;&#46;

### `vericairn devices recovery`

Make the operator&#39;s recovery device for emergency access&#58; its key is written once to a file&#44; encrypted with a passphrase you type&#44; and never held by this machine&#59; an admitted device of the operator held here admits it &#40;record format 2&#41;&#46;

Usage: `vericairn devices recovery [options]`

Options:

- `--principal <principal>`: The operator the recovery device acts for&#46; Required&#46;
- `--name <name>`: Descriptive name&#44; for example where the key is kept&#59; never a machine path&#46; Required&#46;
- `--output <output>`: New file to write the encrypted key to&#44; outside the estate&#39;s repository&#59; move it off this machine at once&#46; Required&#46;
- `--passphrase-stdin`: Read the recovery key&#39;s passphrase from the first line of standard input instead of the terminal&#46;

### `vericairn devices rename`

Rename a device whose key is held here&#59; it keeps its identity&#44; key and admission &#40;record format 2&#41;&#46;

Usage: `vericairn devices rename <device> [options]`

Arguments:

- `<device>`: Device record id&#46;

Options:

- `--name <name>`: The device&#39;s new descriptive name&#44; never a machine path&#46; Required&#46;

### `vericairn devices revoke`

Revoke a device&#39;s admission from now on&#59; its earlier records stay attributable&#46;

Usage: `vericairn devices revoke <device> [options]`

Arguments:

- `<device>`: Device record id&#46;

Options:

- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--reason <reason>`: Why the admission ends&#46; Required&#46;
- `--recovery <recovery>`: Sign with the operator&#39;s recovery key in this file&#44; when no admitted device of the operator is left&#59; asks for its passphrase&#46; Every use stays in &#39;status&#39; until the operator reviews it&#46;
- `--passphrase-stdin`: Read the recovery key&#39;s passphrase from the first line of standard input instead of the terminal&#46;

### `vericairn devices rotate`

Replace a device&#39;s key with a new one&#44; signed by both keys&#59; the device keeps its identity and admission&#44; and its earlier records stay valid &#40;record format 2&#41;&#46;

Usage: `vericairn devices rotate <device> [options]`

Arguments:

- `<device>`: Device record id&#46;

Options:

- `--custody <custody>`: Custody of the new key&#58; file&#44; cng&#44; secure&#45;enclave&#44; secret&#45;service&#44; or default for the strongest this machine offers &#40;when omitted&#58; the custody the device has&#41;&#46;
- `--presence <presence>`: Every signature with the new key needs its owner&#39;s presence &#40;Secure Enclave custody only&#44; never for an agent&#41;&#59; &#39;&#45;&#45;presence false&#39; removes it&#46; Default&#58; the device&#39;s presence&#44; where the new custody enforces it&#46;

### `vericairn devices writes`

List what paired devices wrote over the local network through this machine&#39;s runtime&#58; when&#44; which device&#44; for which principal&#44; the operation and the record it produced&#46; A record a paired device asks for is signed by a device of the same principal held here&#44; so this is where the paired device behind it is kept&#46;

Usage: `vericairn devices writes [<device>] [options]`

Arguments:

- `<device>`: A paired device&#39;s id&#44; to list its writes only&#46;

## `vericairn docs`

Documentation&#58; a release&#39;s set &#40;its notes from the estate&#39;s records&#44; and the check that a set holds every document its manifest lists&#59; the references are generated by the product&#39;s own build tooling&#41;&#44; and the living documents that stay true as the estate changes&#46;

Usage: `vericairn docs [command] [options]`

Verbs: `notes`, `check`, `living`, `review`, `declare`, `regenerate`, `companions`, `render`

### `vericairn docs check`

Refuse a documentation set that lacks a document&#44; or a heading for a command&#44; an API operation or a record type its manifest lists&#59; every gap is listed at once&#46;

Usage: `vericairn docs check <directory> [options]`

Arguments:

- `<directory>`: The documentation set to check&#46;

Options:

- `--documents <documents>`: The product&#39;s declaration of the documents every set holds &#40;a JSON array of file names&#41;&#59; the set&#39;s manifest is checked against it&#46;

### `vericairn docs companions`

List installed readable coverage&#44; generator contracts&#44; declared destinations and the exact collection snapshot&#46;

Usage: `vericairn docs companions [options]`

### `vericairn docs declare`

Record or revise the declaration of a living document&#58; its triggers and levels&#44; generated regions&#44; checks and who may clear it&#46;

Usage: `vericairn docs declare [options]`

Options:

- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--body <body>`: Path to a JSON file holding the living&#45;document declaration&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;
- `--supersedes <supersedes>`: The declaration this one revises&#46;

### `vericairn docs living`

List the declared living documents&#58; each one current&#44; due with every trigger newer than its latest clearance&#44; or unavailable&#46;

Usage: `vericairn docs living [options]`

### `vericairn docs notes`

Write release notes from the estate&#39;s records since the previous release&#39;s notes ended&#44; and record their window in the set&#39;s manifest&#46;

Usage: `vericairn docs notes [options]`

Options:

- `--from <from>`: The earlier published release&#39;s version&#59; the notes begin where the notes of its documentation set&#44; the directory it names beside &#45;&#45;out&#44; ended &#40;at its publication when there is no such set&#41;&#46; Required&#46;
- `--to <to>`: The release&#39;s version&#59; its publication ends the notes once it is recorded&#44; the second before they are generated until then&#46; Required&#46;
- `--out <out>`: Directory of the documentation set the notes belong to&#46; Required&#46;
- `--since <since>`: The time the notes begin&#44; for example 2026&#45;10&#45;16T04&#58;29&#58;43Z&#44; no later than the previous set&#39;s end&#59; widens the window to include records the previous set missed and records the chosen start in the manifest&#46;

### `vericairn docs regenerate`

Rewrite an estate document&#39;s generated regions&#44; or a generated document&#44; from their generators&#59; nothing outside the markers changes&#46;

Usage: `vericairn docs regenerate <path> [options]`

Arguments:

- `<path>`: The document&#39;s repository&#45;relative path&#46;

Options:

- `--compare`: Compare fresh bytes and complete membership without writing output&#44; pins or review&#46;
- `--commit <commit>`: Exact external source commit for &#45;&#45;compare&#59; repair remains local&#46;

### `vericairn docs render`

Read a registered item or collection at an explicit revision or snapshot&#59; no output files or records are written&#46;

Usage: `vericairn docs render <generator> [options]`

Arguments:

- `<generator>`: The installed generator identity from docs companions&#46;

Options:

- `--selection <selection>`: item or nested list at &#45;&#45;revision&#59; current or all&#45;revisions at &#45;&#45;snapshot&#46; Required&#46;
- `--revision <revision>`: The exact immutable source record id&#59; never resolved to its successor&#46;
- `--snapshot <snapshot>`: The exact authorized snapshot from docs companions&#46;
- `--page <page>`: One&#45;based page of the same exact selection&#46; Default&#58; 1&#46;
- `--field <field>`: body or a registered nested collection field&#46; Default&#58; body&#46;
- `--index <index>`: all&#44; or the one&#45;based nested member index&#46; Default&#58; all&#46;
- `--audience <audience>`: Requested audience&#59; grants no source access&#46; Default&#58; Internal&#46;
- `--contract <contract>`: Exact output contract version&#46; Default&#58; 1&#46;
- `--declaration <declaration>`: Exact current declaration of a retained source item&#46;
- `--commit <commit>`: Exact repository commit for that declared source&#59; clone head is never substituted&#46;
- `--sources`: Inspect the selected source basis without invoking the generator&#46;

### `vericairn docs review`

Record a review of a living document&#58; the runtime reads its content&#44; records the covered paths&#39; identity and the triggers it answers&#44; and runs its declared checks&#59; a failed check refuses the clearance&#46;

Usage: `vericairn docs review <path> [options]`

Arguments:

- `<path>`: The document&#39;s repository&#45;relative path&#46;

Options:

- `--by <by>`: The reviewing principal &#40;ULID&#41;&#46; Required&#46;
- `--outcome <outcome>`: current &#40;it holds&#41;&#44; revised &#40;it was changed for the trigger&#41; or needs&#45;change &#40;it is stale and stays due&#41;&#46; Required&#46;
- `--summary <summary>`: What was checked&#46; Required&#46;
- `--finding <finding>`: A finding&#44; severity&#58;summary with severity error&#44; warning or info&#59; repeatable&#46;
- `--commit <commit>`: For a document of another repository&#44; the commit reviewed&#46;
- `--independence <independence>`: For a document of another repository&#44; whose author the estate&#39;s records do not show&#44; the reviewer&#39;s independence of its content&#58; self &#40;the reviewer wrote or revised it&#41;&#44; independent&#45;agent or operator&#46; Default&#58; self for a principal that holds the builder role&#44; else judged from the estate&#39;s records&#46; An estate file&#39;s revisions decide its reviewer&#39;s independence&#44; and a statement that differs is refused&#46;

## `vericairn doctor`

Check this machine&#39;s prerequisites&#46; Discloses and checks GitHub for a newer published release at most once per 24 hours&#44; shared with Workbench&#59; no identifiers or usage telemetry&#46; Disclosure and upgrade notices go to stderr&#44; leaving stdout and JSON unchanged&#46; Disable with vericairn updates configure &#45;&#45;enabled false&#46; Offline failures keep the cached answer and never fail this command&#46;

Usage: `vericairn doctor [options]`

## `vericairn estate`

Create and check estates&#46;

Usage: `vericairn estate [command] [options]`

Verbs: `init`, `writing-exceptions`, `check`, `export`, `restore`, `upgrade`, `reconcile`

### `vericairn estate check`

Validate records&#44; run repository checks and display policy&#44; duty and writing&#45;exception findings with their recorded&#45;time basis&#46; Policy holds are warnings here&#46; Warn when the manifest pins a release below a newer published release known from this estate&#39;s release records or the machine&#45;local cache&#59; use vericairn estate upgrade to review and apply an upgrade&#46; This command makes no release&#45;check request&#44; and a newer&#45;release warning never changes its exit code to an error&#46;

Usage: `vericairn estate check [options]`

### `vericairn estate export`

Write a verified export&#58; records&#44; objects&#44; manifest and documents with an inventory&#59; no keys or authority&#46;

Usage: `vericairn estate export [options]`

Options:

- `--out <out>`: Path of the export file to create &#40;never overwritten&#41;&#46; Required&#46;
- `--by <by>`: Principal id recorded as the exporter&#46;

### `vericairn estate init`

Create a new estate&#58; operator principal&#44; charter and manifest&#44; validated together&#46;

Usage: `vericairn estate init [options]`

Options:

- `--profile <profile>`: Storage profile&#58; repository&#45;synced &#40;records as files in a git repository&#41; or local&#45;only &#40;records in this machine&#39;s live store&#41;&#46; Default&#58; repository&#45;synced&#46;
- `--name <name>`: Estate display name&#46; Required&#46;
- `--slug <slug>`: Estate slug &#40;lower&#45;case kebab&#45;case&#41;&#46; Required&#46;
- `--purpose <purpose>`: Why the estate exists&#46; Required&#46;
- `--operator-name <operator-name>`: Display name of the root operator &#40;a human&#41;&#46; Required&#46;
- `--organization <organization>`: Organization display name&#46; Required&#46;
- `--github <github>`: GitHub organization login&#44; if any&#46;
- `--repository <repository>`: GitHub owner&#47;name of the Vericairn source to pin&#46; Default&#58; Vericairn&#47;vericairn&#46;
- `--commit <commit>`: Commit of the Vericairn source to pin &#40;40 hex digits&#41;&#46; Required&#46;

### `vericairn estate reconcile`

List what two diverged copies left to settle&#58; divergent revisions&#44; duplicate keys&#44; duplicate results&#46;

Usage: `vericairn estate reconcile [options]`

### `vericairn estate restore`

Verify an export against its inventory and restore it into an empty directory as a repository&#45;synced estate&#59; authority is not restored&#46;

Usage: `vericairn estate restore [options]`

Options:

- `--from <from>`: Export file&#46; Required&#46;
- `--to <to>`: Empty destination directory&#46; Required&#46;

### `vericairn estate upgrade`

Pin the estate to a published release and apply record&#45;format migrations as one reviewable change with a migration record&#46;

Usage: `vericairn estate upgrade [options]`

Options:

- `--to <to>`: Version of a published release recorded in this estate&#46; Required&#46;
- `--by <by>`: Principal id applying the upgrade &#40;required unless &#45;&#45;check&#41;&#46;
- `--check`: Show the plan without changing anything&#46;

### `vericairn estate writing-exceptions`

Inspect the signed writing registry and every current match&#46;

Usage: `vericairn estate writing-exceptions [command] [options]`

Verbs: `add`, `remove`, `list`, `show`

#### `vericairn estate writing-exceptions add`

Validate and sign one exact writing token entry&#46;

Usage: `vericairn estate writing-exceptions add [options]`

Options:

- `--by <by>`: Acting principal id&#46; Required&#46;
- `--body <body>`: Prepared JSON body file&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;

#### `vericairn estate writing-exceptions list`

List writing entries&#44; removal history&#44; counts and locations&#46;

Usage: `vericairn estate writing-exceptions list [options]`

#### `vericairn estate writing-exceptions remove`

Sign a terminal removal against the expected entry hash&#46;

Usage: `vericairn estate writing-exceptions remove <id> [options]`

Arguments:

- `<id>`:

Options:

- `--by <by>`: Acting principal id&#46; Required&#46;
- `--expected <expected>`: Expected current entry hash&#46; Required&#46;
- `--reason <reason>`: Why the entry is removed&#46; Required&#46;
- `--basis <basis>`: Optional JSON authority basis file&#44; or &#39;&#45;&#39; for standard input&#46;

#### `vericairn estate writing-exceptions show`

Show one writing entry and its history by record id&#46;

Usage: `vericairn estate writing-exceptions show <id> [options]`

Arguments:

- `<id>`:

## `vericairn goal`

Goals&#58; objectives the estate carries out&#44; done when their record predicates hold&#44; an evaluator who did none of the work finds every condition met&#44; and they are accepted&#46; Without a subcommand&#44; the current goal condition by condition&#58; the one goal neither achieved nor ended&#44; or with none or several&#44; the list&#46;

Usage: `vericairn goal [command] [options]`

Verbs: `show`, `next`, `propose`, `activate`, `pause`, `resume`, `stop`, `custody-exception`, `step`, `evaluate`, `accept`

### `vericairn goal accept`

Accept a goal at its current revision&#44; relying on its current evaluation&#58; by its sponsor when the sponsor is a person&#44; or the principal the sponsor delegated an ordinary goal&#39;s acceptance to&#44; and by an operator&#44; only an operator&#44; when an agent sponsors it&#59; never by its evaluator or a step&#39;s author&#44; and only once every condition is found met and every record predicate holds&#46;

Usage: `vericairn goal accept <goal> [options]`

Arguments:

- `<goal>`: Goal record id &#40;any revision&#41;&#46;

Options:

- `--by <by>`: Acting principal id &#40;ULID&#41;&#46; Required&#46;
- `--statement <statement>`: What is accepted and on what basis&#46; Required&#46;

### `vericairn goal activate`

Activate a proposed goal&#44; which grants its budget&#58; by its sponsor&#44; a person who holds the operator role or a grant of the spending scope&#44; or by an operator when an agent sponsors it&#46; An agent never activates a goal&#46;

Usage: `vericairn goal activate <goal> [options]`

Arguments:

- `<goal>`: Goal record id &#40;any revision&#41;&#46;

Options:

- `--by <by>`: Acting principal id &#40;ULID&#41;&#46; Required&#46;
- `--note <note>`: Why&#44; and what remains&#59; a sentence naming the action when omitted&#46;

### `vericairn goal custody-exception`

Record the sponsor&#39;s exception to the custody precondition for a goal&#39;s current revision&#58; until it expires&#44; a coding agent&#39;s run of the goal may launch unattended on a machine where a key of an operator or of the sponsor signs without its owner&#39;s presence&#44; and the run names the exception&#46; Recorded only by the goal&#39;s sponsor&#44; or by an operator when an agent sponsors it&#59; a revised goal needs a fresh one&#46;

Usage: `vericairn goal custody-exception <goal> [options]`

Arguments:

- `<goal>`: Goal record id &#40;any revision&#41;&#46;

Options:

- `--by <by>`: Acting principal id &#40;ULID&#41;&#46; Required&#46;
- `--reason <reason>`: Why unattended runs of the goal may launch while such a key signs where they run&#46; Required&#46;
- `--expires <expires>`: When the exception stops being in force&#44; for example 2026&#45;10&#45;20T00&#58;00&#58;00Z&#46; Required&#46;

### `vericairn goal evaluate`

Record the evaluation of a goal&#39;s current revision&#44; condition by condition&#44; with the records each verdict read&#59; by a principal other than any step&#39;s author&#46;

Usage: `vericairn goal evaluate <goal> [options]`

Arguments:

- `<goal>`: Goal record id &#40;any revision&#41;&#46;

Options:

- `--by <by>`: Acting principal id &#40;ULID&#41;&#46; Required&#46;
- `--summary <summary>`: What was reviewed and how&#46; Required&#46;
- `--verdict <verdict>`: A condition&#39;s verdict&#44; n&#58;verdict&#58;reason with n from 1 and the verdict met&#44; not&#45;yet or impossible&#59; repeatable&#46; Every evidence statement needs one&#59; a record predicate gets the runtime&#39;s&#44; unless the evaluator judges one the runtime does not find met impossible&#46;
- `--read <read>`: A record the evaluator read for a condition&#44; n&#58;record&#45;id&#59; repeatable&#46; An evidence statement found met names at least one&#46;

### `vericairn goal next`

Show a goal&#39;s next steps routed from this machine&#58; each eligible step with the principal its role&#39;s roster reaches &#40;an agent where a launcher for its harness&#44; its admitted key and the goal&#39;s budget allow&#44; a person where needed&#44; a reserved action only to one who holds it&#41; or why none is available&#44; whether the goal&#39;s budget allows it&#44; and what holds the others&#46; It records and starts nothing&#59; vericairn goal step takes the next one&#46;

Usage: `vericairn goal next [<goal>] [options]`

Arguments:

- `<goal>`: Goal record id &#40;any revision&#41;&#59; omit for the current goal&#46;

### `vericairn goal pause`

Pause an active goal&#59; its sponsor&#39;s&#44; or an operator&#39;s for a goal an agent sponsors&#46; Prevents new runs but does not stop runs already in flight&#59; use &#39;vericairn worker stop&#39; for each run that must stop&#46;

Usage: `vericairn goal pause <goal> [options]`

Arguments:

- `<goal>`: Goal record id &#40;any revision&#41;&#46;

Options:

- `--by <by>`: Acting principal id &#40;ULID&#41;&#46; Required&#46;
- `--note <note>`: Why&#44; and what remains&#59; a sentence naming the action when omitted&#46;

### `vericairn goal propose`

Propose a goal&#44; or a revision of one&#58; what done looks like&#44; its constraints&#44; its budget and its sponsor&#46; A proposal authorizes and funds nothing&#46; Anyone may propose a goal&#59; a revision is its sponsor&#39;s when the sponsor is a person&#44; and an operator&#39;s when an agent sponsors it&#44; and anyone else proposes the change as an ask&#46;

Usage: `vericairn goal propose [options]`

Options:

- `--by <by>`: Acting principal id &#40;ULID&#41;&#46; Required&#46;
- `--title <title>`: Short title&#46; Required&#46;
- `--objective <objective>`: What the sponsor wants&#44; in the sponsor&#39;s words&#46; Required&#46;
- `--done <done>`: A completion condition as JSON&#59; repeatable&#46; A record predicate&#44; &#123;&#34;type&#34;&#58; &#46;&#46;&#46;&#125; with optional subject&#44; product&#44; fields&#44; state&#44; min and max &#40;or the same inside &#123;&#34;record&#34;&#58; &#46;&#46;&#46;&#125;&#41;&#44; or an evidence statement&#44; &#123;&#34;evidence&#34;&#58; &#46;&#46;&#46;&#44; &#34;check&#34;&#58; &#46;&#46;&#46;&#125;&#46; Required&#46;
- `--sponsor <sponsor>`: The accountable principal&#59; the proposer when omitted&#46; A person who sponsors a goal activates and accepts it&#59; a goal an agent sponsors is activated and accepted by an operator&#44; and only an operator&#46;
- `--delegate <delegate>`: A principal the sponsor&#44; a person&#44; delegates the acceptance of an ordinary goal to&#59; it accepts while it holds the &#39;accept&#45;work&#39; scope&#44; and never a goal that includes a reserved action&#46;
- `--constraint <constraint>`: What must not change or happen&#59; repeatable&#46;
- `--budget <budget>`: A cap its activation grants&#44; unit&#61;value&#59; repeatable&#58; wallMinutes and attemptsPerStep &#40;both required&#41;&#44; tokens&#44; concurrency&#44; or currency&#61;&lt;amount in minor units&gt;&#58;&lt;unit&gt; such as currency&#61;5000&#58;USD&#46; A unit not given is not granted&#46;
- `--roster <roster>`: The roster record whose roles it uses&#59; the estate&#39;s current roster when omitted&#46;
- `--kind <kind>`: finite &#40;ends when done&#41; or stewardship &#40;holds a standing condition&#41;&#46; Default&#58; finite&#46;
- `--work-order <work-order>`: A work order the sponsor gives as its plan&#59; repeatable&#46; Without one a planner proposes its work&#44; as work orders that name the goal among their inputs&#46;
- `--input <input>`: A record it builds on&#44; as type&#58;id&#59; repeatable&#46;
- `--deadline <deadline>`: The goal&#39;s deadline&#44; for example 2026&#45;12&#45;01T00&#58;00&#58;00Z&#59; validated and recorded but not yet enforced&#46;
- `--supersedes <supersedes>`: The goal this proposal revises&#44; by its current revision&#39;s id&#59; a revision needs a fresh activation&#44; and only its sponsor&#44; a person&#44; or an operator for a goal an agent sponsors records it&#46;

### `vericairn goal resume`

Resume a paused&#44; budget&#45;limited or blocked goal&#44; which grants its budget again&#59; by whoever may activate it&#46;

Usage: `vericairn goal resume <goal> [options]`

Arguments:

- `<goal>`: Goal record id &#40;any revision&#41;&#46;

Options:

- `--by <by>`: Acting principal id &#40;ULID&#41;&#46; Required&#46;
- `--note <note>`: Why&#44; and what remains&#59; a sentence naming the action when omitted&#46;

### `vericairn goal show`

Show a goal condition by condition&#58; its status&#44; the budget its activation grants&#44; each condition&#39;s verdict with the records it read beside its current evaluation&#39;s&#44; its steps&#44; and what stands between it and its acceptance&#46;

Usage: `vericairn goal show [<goal>] [options]`

Arguments:

- `<goal>`: Goal record id &#40;any revision&#41;&#59; omit for the current goal&#46;

### `vericairn goal step`

Take a goal&#39;s next step&#44; one at a time&#58; its first eligible step whose route acts&#44; routed from this machine by the goal&#39;s roster&#44; for an operator&#44; the goal&#39;s sponsor when a person&#44; or a principal holding local&#45;engineering&#46; An agent&#39;s step goes on with the assignment that holds it&#44; or is assigned to the agent with the run&#39;s intent&#44; after the worker&#39;s whole preflight&#44; and its run starts through the worker&#59; every refusal before the intent records nothing&#44; the assignment included&#44; as while a key of an operator or of the sponsor signs here without presence and no custody exception is in force&#46; A person&#39;s step is assigned to the person and asked of them&#44; the two recorded together&#59; a step no principal is available for is asked of the sponsor&#44; never given to another contributor&#46; It activates&#44; funds&#44; accepts and answers nothing&#46;

Usage: `vericairn goal step [<goal>] [options]`

Arguments:

- `<goal>`: Goal record id &#40;any revision&#41;&#59; omit for the current goal&#46;

Options:

- `--by <by>`: Acting principal id &#40;ULID&#41;&#46; Required&#46;
- `--repository <repository>`: For an agent&#39;s step&#44; the repository resource whose clone&#44; registered on this machine &#40;vericairn sources clone&#41;&#44; its workspace is made from&#46;
- `--setting <setting>`: For an agent&#39;s step&#44; a setting of the launcher routing chose&#44; as name&#61;value&#44; for example command&#61;&lt;executable or JSON array&gt;&#59; repeatable&#46;
- `--wall-minutes <wall-minutes>`: For an agent&#39;s step&#44; the run&#39;s cap in wall minutes&#59; what remains of the goal&#39;s budget when omitted&#44; and never more&#46;
- `--branch <branch>`: For an agent&#39;s step&#44; the work branch&#59; work&#47; and the work order&#39;s key when omitted&#46;
- `--base <base>`: For an agent&#39;s step&#44; what a new work branch starts from&#59; the clone&#39;s HEAD when omitted&#46;
- `--detach`: Return once an agent&#39;s run is launched&#44; leaving it to the daemon that serves the estate&#59; without it the command waits until the run settles&#44; and Ctrl&#43;C asks it to stop&#46;
- `--configuration <configuration>`: Named local launcher configuration&#59; required choices never fall back to ambient harness settings&#46;
- `--execution <execution>`: An explicit execution choice as name&#61;value&#58; model&#44; effort&#44; provider&#44; destination&#44; dataBoundary or adapter&#61;disabled&#59; repeatable&#46; Inherited restrictions still apply&#46;
- `--input-tokens <input-tokens>`: Context input allocation&#44; within the run&#39;s token reservation&#59; not measured consumption&#46;
- `--output-tokens <output-tokens>`: Output allocation&#44; within the run&#39;s token reservation&#59; not measured consumption&#46;
- `--context-bytes <context-bytes>`: Maximum canonical context payload bytes&#44; separate from token reservations&#59; mandatory originals and accounting must fit&#46;
- `--context-package <context-package>`: Optional expected hash of the context package assembled for this run&#59; a mismatch refuses dispatch&#46;

### `vericairn goal stop`

Stop a goal&#58; its sponsor&#44; or an operator for a goal an agent sponsors&#44; ends it as abandoned and prevents new runs&#46; Runs already in flight are not stopped&#59; use &#39;vericairn worker stop&#39; for each run that must stop&#46;

Usage: `vericairn goal stop <goal> [options]`

Arguments:

- `<goal>`: Goal record id &#40;any revision&#41;&#46;

Options:

- `--by <by>`: Acting principal id &#40;ULID&#41;&#46; Required&#46;
- `--note <note>`: Why&#44; and what remains&#59; a sentence naming the action when omitted&#46;

## `vericairn handoff`

Prepare a complete record&#45;derived hand&#45;over&#59; refuse missing sources or overflow&#46; Remote is not built&#46;

Usage: `vericairn handoff [options]`

Options:

- `--out <out>`: Retain the portable package in a new file&#59; never overwrite&#46;
- `--max-bytes <max-bytes>`: Optional payload byte limit&#59; no truncation&#46; No limit when omitted&#46;

## `vericairn mcp`

The Model Context Protocol server&#58; serve the estate to a coding agent over standard input and output&#44; or generate the agent orientation files&#46;

Usage: `vericairn mcp [command] [options]`

Verbs: `serve`, `orientation`

### `vericairn mcp orientation`

Write AGENTS&#46;md and the pointer files for the common agent harnesses from the estate&#39;s identity&#44; its qualifications of agent harnesses and the tool registry&#44; or check existing copies against a fresh generation&#46;

Usage: `vericairn mcp orientation [options]`

Options:

- `--out <out>`: Directory to write the files into&#44; for example a repository root&#46; Required&#46;
- `--as <as>`: Principal id the files describe as the acting principal&#44; if any&#46;
- `--check`: Write nothing&#59; compare the files in &#45;&#45;out with a fresh generation and name every one that is missing or differs&#46;

### `vericairn mcp serve`

Serve the estate&#39;s tools over standard input and output until the input ends&#59; a client of the daemon or the embedded runtime&#44; never a second owner&#46;

Usage: `vericairn mcp serve [options]`

Options:

- `--as <as>`: Principal id the agent acts for&#59; its admitted device on this machine signs the writes&#46; Without it only the read&#45;only tools are listed&#46;

## `vericairn objects`

Content&#45;addressed objects&#58; put&#44; list&#44; verify&#46;

Usage: `vericairn objects [command] [options]`

Verbs: `put`, `list`, `verify`

### `vericairn objects list`

List held objects&#46;

Usage: `vericairn objects list [options]`

### `vericairn objects put`

Store a file&#39;s bytes as an object named by their hash&#46;

Usage: `vericairn objects put <file> [options]`

Arguments:

- `<file>`: File whose bytes become an object&#46;

### `vericairn objects verify`

Read every object and check its bytes against its name&#46;

Usage: `vericairn objects verify [options]`

## `vericairn operator`

Inspect the actions waiting on the estate&#39;s operators&#46;

Usage: `vericairn operator [command] [options]`

Verbs: `list`, `record`

### `vericairn operator list`

List every open operator ask&#44; pending operator acceptance and recorded waiting reserved action&#44; with why&#44; what it waits on&#44; exact commands and required JSON input&#46; Reads record and sign nothing&#46; Communication settings change presentation&#44; never the queue&#59; &#45;&#45;json returns the complete record&#45;derived view&#46;

Usage: `vericairn operator list [options]`

### `vericairn operator record`

Sign your own operator record&#44; commit it alone on a new estate branch&#44; scan&#44; push&#44; open&#44; wait for declared checks and merge through recorded changes operations&#46; Identical input resumes the original action&#59; failures retain its branch and pull request and report the next command&#46; Requires your admitted device&#44; a clean estate repository and declared checks&#46; Not exposed through MCP&#46;

Usage: `vericairn operator record <type> [options]`

Arguments:

- `<type>`: Record type&#44; for example acceptance or decision&#46;

Options:

- `--by <by>`: Operator principal id acting on their own admitted device&#46; Required&#46;
- `--body <body>`: JSON body file&#44; or &#39;&#45;&#39; for standard input&#46; Supply the same body to resume&#46; Required&#46;
- `--subject <subject>`: Subject as type&#58;id or type&#58;id&#58;hash&#59; acceptances pin the exact hash&#46;
- `--supersedes <supersedes>`: Id of the record this one replaces&#46;
- `--product <product>`: Product declaring changes for this estate repository&#46; Defaults to its unique registered product&#46;
- `--wait-seconds <wait-seconds>`: Maximum wait for each check phase&#44; from 0 to 240 seconds&#59; 0 observes once&#46; Default&#58; 120&#46;
- `--reconciliation <reconciliation>`: How the previous known failure was settled&#59; required before retrying that effect&#46;

## `vericairn policies`

Inspect and manage estate policy declarations and their evidence&#46;

Usage: `vericairn policies [command] [options]`

Verbs: `declare`, `revise`, `retire`, `except`, `revoke-exception`, `list`, `show`, `kinds`, `check`

### `vericairn policies check`

Evaluate every policy and print its basis&#59; an explicit time or release target is a preview&#46;

Usage: `vericairn policies check [options]`

Options:

- `--as-of <as-of>`: Canonical UTC preview time&#46;
- `--release <release>`: Release record id selecting its exact product&#44; version and commit&#46;

### `vericairn policies declare`

Prepare and validate the exact policy change&#44; then sign as the acting principal&#46;

Usage: `vericairn policies declare [options]`

Options:

- `--by <by>`: Acting principal id&#46; Required&#46;
- `--body <body>`: Prepared JSON body file&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;

### `vericairn policies except`

Prepare and validate the exact policy change&#44; then sign as the acting principal&#46;

Usage: `vericairn policies except <key> [options]`

Arguments:

- `<key>`:

Options:

- `--by <by>`: Acting principal id&#46; Required&#46;
- `--body <body>`: Prepared JSON body file&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;
- `--expected <expected>`: Expected current target hash&#46; Required&#46;

### `vericairn policies kinds`

List installed policy kind versions&#44; sources and bounds&#46;

Usage: `vericairn policies kinds [options]`

### `vericairn policies list`

List declarations&#44; exceptions and findings at recorded time&#46;

Usage: `vericairn policies list [options]`

### `vericairn policies retire`

Prepare and validate the exact policy change&#44; then sign as the acting principal&#46;

Usage: `vericairn policies retire <key> [options]`

Arguments:

- `<key>`:

Options:

- `--by <by>`: Acting principal id&#46; Required&#46;
- `--body <body>`: Prepared JSON body file&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;
- `--expected <expected>`: Expected current target hash&#46; Required&#46;

### `vericairn policies revise`

Prepare and validate the exact policy change&#44; then sign as the acting principal&#46;

Usage: `vericairn policies revise <key> [options]`

Arguments:

- `<key>`:

Options:

- `--by <by>`: Acting principal id&#46; Required&#46;
- `--body <body>`: Prepared JSON body file&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;
- `--expected <expected>`: Expected current target hash&#46; Required&#46;

### `vericairn policies revoke-exception`

Prepare and validate the exact policy change&#44; then sign as the acting principal&#46;

Usage: `vericairn policies revoke-exception <key> <id> [options]`

Arguments:

- `<key>`:
- `<id>`:

Options:

- `--by <by>`: Acting principal id&#46; Required&#46;
- `--body <body>`: Prepared JSON body file&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;
- `--expected <expected>`: Expected current target hash&#46; Required&#46;

### `vericairn policies show`

Show a policy by key or record id with exact evidence&#46;

Usage: `vericairn policies show <key> [options]`

Arguments:

- `<key>`:

## `vericairn progress`

Show the estate&#39;s recorded progress with latest local agent claims and worker observations labelled separately&#58; where the estate stands&#44; the work orders by state&#44; each published release and what it contained&#44; what comes next by release&#44; the next eligible work and what waits on the operator&#46; Required attention names only open obligations with exact references and inspection commands&#59; ordinary after ordering is planning&#46; &#45;&#45;json includes complete attention references&#46;

Usage: `vericairn progress [options]`

## `vericairn records`

Create&#44; validate&#44; describe and format records&#46;

Usage: `vericairn records [command] [options]`

Verbs: `validate`, `new`, `show`, `describe`, `format`, `hash`

### `vericairn records describe`

Explain record types and their fields&#46;

Usage: `vericairn records describe [<type>] [options]`

Arguments:

- `<type>`: Record type to describe&#59; omit to list all types&#46;

### `vericairn records format`

Rewrite record files in canonical pretty form without changing their content or hash&#46;

Usage: `vericairn records format <files>... [options]`

Arguments:

- `<files>...`: Record files to rewrite in canonical pretty form&#46;

### `vericairn records hash`

Print the computed content hash of a record file&#46;

Usage: `vericairn records hash <file> [options]`

Arguments:

- `<file>`: Record file&#46;

### `vericairn records new`

Create a record in canonical form&#44; validate the estate with it&#44; and write it&#46;

Usage: `vericairn records new <type> [options]`

Arguments:

- `<type>`: Record type&#44; for example work&#45;order&#46;

Options:

- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--body <body>`: Path to a JSON file holding the body&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;
- `--subject <subject>`: Subject as type&#58;id or type&#58;id&#58;hash&#46;
- `--supersedes <supersedes>`: Id of the record this one replaces&#46;
- `--at <at>`: Authoring time&#44; for example 2026&#45;10&#45;13T14&#58;00&#58;00Z &#40;default&#58; now&#41;&#46;
- `--id <id>`: Explicit record id&#59; only the root operator principal needs this&#46;
- `--expected <expected>`: Required for policy or placement authoring&#58; expected predecessor or policy hash&#44; or the canonical body hash for a creation&#46;

### `vericairn records show`

Read one complete original record by id&#59; presentation depth never changes it&#46;

Usage: `vericairn records show <id> [options]`

Arguments:

- `<id>`: Exact record id&#46;

### `vericairn records validate`

Parse and validate every record and the manifest of an estate&#46;

Usage: `vericairn records validate [options]`

## `vericairn recover`

After an interruption or a move&#58; inspect what this copy holds and compare it with another&#46;

Usage: `vericairn recover [command] [options]`

Verbs: `inspect`, `compare`

### `vericairn recover compare`

Records only in this copy&#44; only in the other&#44; or &#40;never expected&#41; differing in content&#46;

Usage: `vericairn recover compare [options]`

Options:

- `--with <with>`: Root of the other copy&#46; Required&#46;

### `vericairn recover inspect`

What this copy knows&#44; which devices can author from here&#44; work in progress&#44; unresolved effects and reconcile tasks&#46;

Usage: `vericairn recover inspect [options]`

## `vericairn release`

Releases&#58; list the records and the providers&#44; submit a release through the provider its product declares or the request names as intent before effect&#44; observe the run&#44; the draft and the publication&#46;

Usage: `vericairn release [command] [options]`

Verbs: `list`, `providers`, `submit`, `basis`, `observe`

### `vericairn release basis`

Read a retained release basis by its evidence hash&#59; grants no new submission authority&#46;

Usage: `vericairn release basis <hash> [options]`

Arguments:

- `<hash>`: Retained release&#45;basis object hash from operation evidence or a release response&#46;

### `vericairn release list`

List current release records&#58; version&#44; status&#44; commit&#44; id&#44; signing&#46;

Usage: `vericairn release list [options]`

### `vericairn release observe`

Observe the run and the draft through the provider the operation records&#59; record the outcome&#44; the candidate release&#44; and later the operator&#39;s manual publication&#46;

Usage: `vericairn release observe [options]`

Options:

- `--version <version>`: The version to release&#44; for example 0&#46;4&#46;0&#46; Required&#46;
- `--by <by>`: Principal id recording the operation &#40;ULID&#41;&#46; Required&#46;
- `--product <product>`: Product record id&#44; to find the operation by product and version&#46;
- `--provider <provider>`: The provider id&#44; for example github&#45;releases&#59; the product&#39;s declared provider when omitted&#46;
- `--repository <repository>`: The setting repository &#40;for GitHub&#44; owner&#47;name of the repository whose workflow builds the release&#41;&#46;
- `--qualified <qualified>`: Platform whose qualification is recorded for the candidate&#59; repeatable&#46;
- `--limitation <limitation>`: A known limitation of the candidate&#59; repeatable&#46;
- `--signing <signing>`: Signing state recorded on the candidate&#46; Default&#58; unsigned developer build&#46;
- `--cancel <cancel>`: Settle an intent that was never dispatched&#44; or an outcome reconciled as having had no effect&#44; with this reason&#46;

### `vericairn release providers`

List the provider and adapter implementations available to the runtime&#44; with the settings each declares and its defaults&#46;

Usage: `vericairn release providers [options]`

### `vericairn release submit`

Record the intent&#44; then dispatch the build through the provider&#59; the run id and outcome are recorded as they are observed&#46; Never publishes&#46;

Usage: `vericairn release submit [options]`

Options:

- `--product <product>`: Product record id the release belongs to&#59; its declared release destination supplies the provider and settings the request does not name&#46; Required&#46;
- `--version <version>`: The version to release&#44; for example 0&#46;4&#46;0&#46; Required&#46;
- `--by <by>`: Principal id recording the operation &#40;ULID&#41;&#46; Required&#46;
- `--provider <provider>`: The provider id&#44; for example github&#45;releases&#59; the product&#39;s declared provider when omitted&#46;
- `--repository <repository>`: The setting repository &#40;for GitHub&#44; owner&#47;name of the repository whose workflow builds the release&#41;&#46;
- `--ref <ref>`: The setting ref &#40;the branch or tag to build from&#41;&#59; the provider&#39;s default when omitted&#46;
- `--commit <commit>`: Prepared exact commit for every source check&#59; required in format 3 or for release&#45;repository document checks&#46;
- `--expected <expected>`: Expected current product hash&#59; required in format 3&#46;
- `--workflow <workflow>`: The setting workflow &#40;the release workflow file&#41;&#59; the provider&#39;s default when omitted&#46;
- `--setting <setting>`: Any declared setting as name&#61;value&#59; repeatable&#46;
- `--reconciliation <reconciliation>`: How an earlier failed&#44; canceled or uncertain attempt under the same identity was settled&#59; required for a retry&#46;

## `vericairn resume`

Show every record&#44; pin and observed&#45;state change since a retained hand&#45;over&#46; The world may have moved&#59; this restarts nothing&#46;

Usage: `vericairn resume <package> [options]`

Arguments:

- `<package>`: The retained hand&#45;over JSON file&#44; or &#45; for standard input&#46;

## `vericairn runtime`

The daemon that owns an estate&#39;s live state on this machine&#58; serve&#44; start&#44; stop&#44; status&#44; install&#44; uninstall&#44; configure &#40;the local&#45;network profile&#41; and rotate &#40;its certificate&#41;&#46;

Usage: `vericairn runtime [command] [options]`

Verbs: `serve`, `start`, `stop`, `status`, `install`, `uninstall`, `configure`, `rotate`

### `vericairn runtime configure`

Configure the runtime of this estate on this machine&#59; without options&#44; show the configuration&#46; Nothing listens beyond the local socket and loopback unless an address is set&#46;

Usage: `vericairn runtime configure [options]`

Options:

- `--local-network <local-network>`: The address and port the local&#45;network profile listens on from the runtime&#39;s next start&#44; an IP address of this machine such as 192&#46;168&#46;1&#46;20&#58;7443&#59; &#39;off&#39; turns it off&#46; Off unless set&#46;
- `--allow-public`: With &#45;&#45;local&#45;network&#44; allow a wildcard address &#40;every interface&#41; or one other networks can reach&#59; such an address is refused without it&#46;

### `vericairn runtime install`

Install the daemon as a per&#45;user service that starts at login &#40;launchd agent&#44; systemd user unit or a logon task&#41;&#46;

Usage: `vericairn runtime install [options]`

Options:

- `--plan`: Show the unit and the commands without installing anything&#46;

### `vericairn runtime rotate`

Replace the runtime certificate now &#40;it is also replaced on a schedule before it expires&#41;&#46; Paired devices keep working&#44; since they pin the runtime identity&#44; which signs the certificate in use&#59; loopback clients read the new one from the endpoint file&#46;

Usage: `vericairn runtime rotate [options]`

Options:

- `--compromised`: The certificate&#39;s key may be known to someone else&#58; also close every open connection&#44; so nothing continues under it&#46;
- `--identity`: The runtime identity&#39;s key may be known to someone else&#58; replace the identity too&#59; every paired device then pairs again&#46;
- `--reason <reason>`: Why&#44; for the runtime&#39;s log&#46;

### `vericairn runtime serve`

Serve the estate in this process until stopped &#40;Ctrl&#43;C or &#39;runtime stop&#39;&#41;&#59; refuses when a daemon already serves it&#46;

Usage: `vericairn runtime serve [options]`

Options:

- `--no-loopback`: Disable the pinned loopback TLS endpoint&#46; The local socket and any configured local&#45;network listener remain available&#46;
- `--detached`: Write the daemon&#39;s output to its log beside the estate&#39;s live store instead of the standard streams&#59; &#39;runtime start&#39; serves this way&#46;

### `vericairn runtime start`

Start a daemon for the estate in the background and wait until it answers&#46;

Usage: `vericairn runtime start [options]`

### `vericairn runtime status`

Whether a daemon serves the estate and where&#44; or whether commands run the embedded runtime&#46;

Usage: `vericairn runtime status [options]`

### `vericairn runtime stop`

Ask the daemon serving the estate to stop&#59; closing a client never stops it&#46;

Usage: `vericairn runtime stop [options]`

### `vericairn runtime uninstall`

Remove the per&#45;user service installed by &#39;runtime install&#39;&#46;

Usage: `vericairn runtime uninstall [options]`

## `vericairn sources`

Repositories the estate uses&#58; inspect read&#45;only&#44; adopt as a resource&#44; observe again&#44; and register this machine&#39;s clone&#46;

Usage: `vericairn sources [command] [options]`

Verbs: `inspect`, `adopt`, `observe`, `clone`

### `vericairn sources adopt`

Adopt a repository as a resource by observation&#58; a resource record and a first observation&#59; nothing in the repository changes&#46;

Usage: `vericairn sources adopt <path> [options]`

Arguments:

- `<path>`: Path of a git working tree on this machine&#59; never recorded&#46;

Options:

- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--name <name>`: Display name of the resource&#46; Required&#46;
- `--role <role>`: What the estate uses the repository for&#46; Required&#46;
- `--visibility <visibility>`: public&#44; private or n&#47;a&#46; Default&#58; private&#46;
- `--notes <notes>`: Free text&#46;

### `vericairn sources clone`

Register this machine&#39;s clone of an adopted repository&#44; so that living documents in it can be read at a commit&#59; the path is machine configuration&#44; never a record&#46;

Usage: `vericairn sources clone <path> [options]`

Arguments:

- `<path>`: Path of a git working tree on this machine&#59; never recorded&#46;

Options:

- `--resource <resource>`: Resource record id of the adopted repository&#46; Required&#46;

### `vericairn sources inspect`

Observe a repository read&#45;only&#58; commit&#44; branch&#44; remote&#44; working&#45;tree state&#44; tags&#44; recent history&#46;

Usage: `vericairn sources inspect <path> [options]`

Arguments:

- `<path>`: Path of a git working tree on this machine&#59; never recorded&#46;

### `vericairn sources observe`

Record a fresh observation of an adopted repository&#46;

Usage: `vericairn sources observe <path> [options]`

Arguments:

- `<path>`: Path of a git working tree on this machine&#59; never recorded&#46;

Options:

- `--resource <resource>`: Resource record id of the adopted repository&#46; Required&#46;
- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;

## `vericairn status`

Show the estate&#39;s current state derived from its records&#44; with compact required attention resolved from current evidence&#46; Each item names its exact source and inspection command&#46; &#45;&#45;json is identical on every machine for the same records and includes complete record&#45;derived attention references&#46; Human status also shows labelled local worker concerns&#46; Discloses and checks GitHub for a newer published release at most once per 24 hours&#44; shared with Workbench&#59; no identifiers or usage telemetry&#46; Disclosure and upgrade notices go to stderr&#44; leaving stdout and JSON unchanged&#46; Disable with vericairn updates configure &#45;&#45;enabled false&#46; Offline failures keep the cached answer and never fail this command&#46;

Usage: `vericairn status [options]`

Options:

- `--observe`: Explicitly record that a person observed status&#59; requires &#45;&#45;by&#46; Ordinary reads record nothing&#46;
- `--by <by>`: The observing human principal&#59; used only with &#45;&#45;observe&#46;

## `vericairn updates`

Machine&#45;local release check settings shared by the CLI and Workbench&#59; nothing installs or upgrades automatically&#46;

Usage: `vericairn updates [command] [options]`

Verbs: `configure`

### `vericairn updates configure`

Show this machine&#39;s release check setting&#44; or replace it with &#45;&#45;enabled true or false&#46; Enabled by default&#59; the check is unauthenticated and carries no identifiers or usage telemetry&#46; Settings and the timestamped cache stay under VericairnHome&#47;updates&#44; outside repositories and estates&#46; Disabling makes no request and retains the cached answer&#46; This command itself never checks the network&#46;

Usage: `vericairn updates configure [options]`

Options:

- `--enabled <enabled>`: Whether to check for published releases at most once per 24 hours&#44; including failed attempts&#46; False prevents requests&#59; true enables future checks&#46; Omit to show the setting&#46;

## `vericairn version`

Print the version&#46; Discloses and checks GitHub for a newer published release at most once per 24 hours&#44; shared with Workbench&#59; no identifiers or usage telemetry&#46; Disclosure and upgrade notices go to stderr&#44; leaving stdout and JSON unchanged&#46; Disable with vericairn updates configure &#45;&#45;enabled false&#46; Offline failures keep the cached answer and never fail this command&#46;

Usage: `vericairn version [options]`

## `vericairn work`

Briefs&#44; assignments&#44; attempts&#44; results and amendments of work orders&#46;

Usage: `vericairn work [command] [options]`

Verbs: `place`, `brief`, `assign`, `attempt`, `result`, `review`, `amend`, `takeover`, `observe`

### `vericairn work amend`

Request a change to an assignment&#39;s outcome&#44; scope&#44; target&#44; budget or permitted effects&#59; it authorizes nothing&#46;

Usage: `vericairn work amend <assignment> [options]`

Arguments:

- `<assignment>`: Assignment record id&#46;

Options:

- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--body <body>`: Path to a JSON file holding the body&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;

### `vericairn work assign`

Delegate a work order to a contributor under a brief&#44; pinned to the exact work&#45;order revision&#46;

Usage: `vericairn work assign <work-order> [options]`

Arguments:

- `<work-order>`: Work&#45;order key &#40;for example M1&#45;WORK&#41; or record id&#46;

Options:

- `--contributor <contributor>`: Principal id of the contributor the work is delegated to&#46; Required&#46;
- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--brief <brief>`: Path to a JSON file holding the brief &#40;outcome&#44; sources&#44; permittedChanges&#44; prohibitedEffects&#44; workspace&#44; limits&#44; expectedOutputs&#44; reviewCriteria&#44; recovery&#41;&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;
- `--note <note>`: Free text&#46;

### `vericairn work attempt`

Record the start of an attempt by the assignment&#39;s contributor&#46;

Usage: `vericairn work attempt <assignment> [options]`

Arguments:

- `<assignment>`: Assignment record id&#46;

Options:

- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--workspace <workspace>`: Where the work happens&#44; named without machine paths &#40;a repository and branch&#44; a clone&#44; a tool&#41;&#46; Required&#46;
- `--note <note>`: What the contributor intends to do&#46; Required&#46;
- `--at <at>`: Start time&#44; for example 2026&#45;10&#45;13T14&#58;00&#58;00Z &#40;default&#58; now&#41;&#46;

### `vericairn work brief`

Export the brief of a work order&#58; everything a contributor needs&#44; from the records alone&#46;

Usage: `vericairn work brief <work-order> [options]`

Arguments:

- `<work-order>`: Work&#45;order key &#40;for example M1&#45;WORK&#41; or record id&#46;

### `vericairn work observe`

Show a work order&#39;s assignments&#44; attempts&#44; results&#44; amendments&#44; reviews and acceptances&#46;

Usage: `vericairn work observe <work-order> [options]`

Arguments:

- `<work-order>`: Work&#45;order key &#40;for example M1&#45;WORK&#41; or record id&#46;

### `vericairn work place`

Prepare and sign a work revision in one current release plan or named backlog&#46;

Usage: `vericairn work place <key> [options]`

Arguments:

- `<key>`:

Options:

- `--by <by>`: Acting principal id&#46; Required&#46;
- `--expected <expected>`: Expected current work&#45;order hash&#46; Required&#46;
- `--expected-selection <expected-selection>`: Expected current carrier or placement&#45;policy hash&#46; Required&#46;
- `--release-plan <release-plan>`: Carrier work&#45;order key or id&#46;
- `--backlog <backlog>`: Named backlog in the placement registry&#46;

### `vericairn work result`

Submit the result of an attempt&#58; outcome&#44; summary&#44; outputs by hash&#44; evidence&#44; checks&#44; limitations&#44; unresolved effects&#44; next step&#46;

Usage: `vericairn work result <attempt> [options]`

Arguments:

- `<attempt>`: Attempt record id&#46;

Options:

- `--expected <expected>`: Expected current attempt hash&#59; required when the result carries duties&#46;
- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--body <body>`: Path to a JSON file holding the body&#44; or &#39;&#45;&#39; for standard input&#46; Required&#46;

### `vericairn work review`

Review an exact result with its evidence read&#44; signed as the acting principal&#59; records no acceptance&#46;

Usage: `vericairn work review <result> [options]`

Arguments:

- `<result>`: Exact current result id&#46;

Options:

- `--by <by>`: Reviewing principal id&#46; Required&#46;
- `--expected <expected>`: Expected current result hash&#46; Required&#46;
- `--body <body>`: Review JSON with independence&#44; outcome&#44; summary&#44; findings and exact read references&#59; &#39;&#45;&#39; reads standard input&#46; Required&#46;

### `vericairn work takeover`

Take over a step an agent holds&#58; the hand&#45;over is a new assignment of the step to a person&#44; carrying the agent&#39;s brief and naming the agent&#39;s assignment&#44; which holds the step from then on&#59; every run of the agent&#39;s assignment still in flight is asked to stop&#46; A person records it&#44; the one taking the step over&#44; the sponsor of its goal or an operator&#44; never an agent&#46;

Usage: `vericairn work takeover <assignment> [options]`

Arguments:

- `<assignment>`: Assignment record id&#46;

Options:

- `--by <by>`: Authoring principal id &#40;ULID&#41;&#46; Required&#46;
- `--contributor <contributor>`: The person who takes the step over&#59; the recording principal when omitted&#46;
- `--note <note>`: Why&#59; a sentence naming the hand&#45;over when omitted&#46;

## `vericairn worker`

The worker &#40;vericairn&#46;worker&#47;1&#41;&#58; a coding agent carries one assignment toward its exit question through a launcher&#44; in a workspace on a work branch&#44; with its cap from its goal&#39;s budget recorded before it starts and its observed usage after&#46; It accepts nothing&#59; the agent&#39;s result is a claim another principal reviews&#46;

Usage: `vericairn worker [command] [options]`

Verbs: `launchers`, `configure`, `qualify`, `run`, `stop`, `runs`

### `vericairn worker configure`

Show or replace this machine&#39;s workspace root&#44; or a native launcher&#39;s configuration with &#45;&#45;launcher&#46; &#45;&#45;workspaces sets an absolute root for every launcher&#46; With no choices&#44; show it&#59; setting requires both model and effort and replaces the allowlists&#46; Stored owner&#45;only under VericairnHome&#44; outside repositories&#59; no secret values&#44; commands or inventory additions&#46; The CLI and daemon read it for each run&#59; this command calls no API and runs no probes&#46;

Usage: `vericairn worker configure [options]`

Options:

- `--launcher <launcher>`: The native launcher&#58; codex or claude&#45;code&#59; omitted to show or set the workspace root&#46;
- `--workspaces <workspaces>`: Absolute machine&#45;local workspace root for every launcher&#44; outside repositories&#44; estates and ancestors holding harness configuration&#46; New roots are owner&#45;only&#46; Omit &#45;&#45;launcher&#46;
- `--model <model>`: Default portable model selector&#59; required with &#45;&#45;effort when setting&#46;
- `--effort <effort>`: Default effort in the launcher&#39;s native vocabulary&#59; required with &#45;&#45;model when setting&#46;
- `--allow-model <allow-model>`: Permitted model selector&#59; repeatable&#44; including the default&#46; Omitted&#58; only the default model&#46;
- `--allow-effort <allow-effort>`: Permitted native effort&#59; repeatable&#44; including the default&#46; Omitted&#58; the launcher&#39;s supported efforts&#46; Never expands its capabilities&#46;

### `vericairn worker launchers`

List the coding&#45;agent launchers available to the runtime&#44; providers of vericairn&#46;launcher&#46;agent&#47;1&#44; with their settings and limitations&#46;

Usage: `vericairn worker launchers [options]`

### `vericairn worker qualify`

Record an authorized human&#39;s qualification of one exact installed harness version from local probe digests&#58; version&#44; help&#45;confirmed flags&#44; closed tools&#44; isolated configuration&#44; login boundary and effective MCP&#46; It launches no agent&#44; establishes no live inference or work acceptance&#44; and grants no version range&#46; Admission allows the shipped baseline or a current qualification whose repeated local probes have identical digests&#59; a superseding record revokes the qualification&#46; The existing inventory policy still applies&#46; Remote is not built&#46;

Usage: `vericairn worker qualify [options]`

Options:

- `--launcher <launcher>`: The native launcher&#58; codex or claude&#45;code&#46; Required&#46;
- `--by <by>`: A human operator or human explicitly granted the qualify scope&#59; never an agent&#46; Required&#46;
- `--setting <setting>`: Local launcher setting as name&#61;value&#44; including command&#61;&lt;executable or JSON array&gt;&#59; repeatable&#46; Values are never recorded&#46;

### `vericairn worker run`

Run a coding agent on an assignment&#58; refused while the goal is not active or its budget is spent&#44; while a device of an operator or the goal&#39;s sponsor signs here without presence and the sponsor recorded no custody exception in force for the goal&#44; or when the launcher cannot run&#59; otherwise the workspace is prepared&#44; the intent recorded with the cap&#44; the harness launched with the brief and the MCP server&#44; observed&#44; and its usage recorded once it exits&#46;

Usage: `vericairn worker run <assignment> [options]`

Arguments:

- `<assignment>`: The assignment the agent carries&#44; whose contributor it is&#46;

Options:

- `--launcher <launcher>`: The launcher&#58; claude&#45;code&#44; codex or generic &#40;generic process launch is not complete agent support&#41;&#46; Required&#46;
- `--agent <agent>`: The agent principal the harness acts for&#58; the assignment&#39;s contributor&#44; with an admitted device whose key this machine holds&#46; Required&#46;
- `--by <by>`: The principal recording the run&#58; the operator&#44; the goal&#39;s sponsor when the sponsor is a person&#44; or a principal with the local&#45;engineering scope&#46; Required&#46;
- `--repository <repository>`: The repository resource whose clone&#44; registered on this machine &#40;vericairn sources clone&#41;&#44; the workspace is made from&#46; Required&#46;
- `--goal <goal>`: The goal the run is charged to&#44; when the work belongs to more than one active goal&#46;
- `--branch <branch>`: The work branch&#59; work&#47; and the work order&#39;s key when omitted&#46; A later run of the branch continues in its workspace&#46;
- `--base <base>`: What a new work branch starts from&#59; the clone&#39;s HEAD when omitted&#46;
- `--wall-minutes <wall-minutes>`: The run&#39;s cap in wall minutes&#59; what remains of the goal&#39;s budget when omitted&#44; and never more&#46;
- `--setting <setting>`: A launcher setting as name&#61;value&#44; for example command&#61;&lt;executable or JSON array&gt;&#59; repeatable&#46;
- `--detach`: Return once the harness is launched&#44; leaving the run to the daemon that serves the estate&#59; without it the command waits until the run settles&#44; and Ctrl&#43;C asks it to stop&#46;
- `--configuration <configuration>`: Named local launcher configuration&#59; required choices never fall back to ambient harness settings&#46;
- `--execution <execution>`: An explicit execution choice as name&#61;value&#58; model&#44; effort&#44; provider&#44; destination&#44; dataBoundary or adapter&#61;disabled&#59; repeatable&#46; Inherited restrictions still apply&#46;
- `--input-tokens <input-tokens>`: Context input allocation&#44; within the run&#39;s token reservation&#59; not measured consumption&#46;
- `--output-tokens <output-tokens>`: Output allocation&#44; within the run&#39;s token reservation&#59; not measured consumption&#46;
- `--context-bytes <context-bytes>`: Maximum canonical context payload bytes&#44; separate from token reservations&#59; mandatory originals and accounting must fit&#46;
- `--context-package <context-package>`: Optional expected hash of the context package assembled for this run&#59; a mismatch refuses dispatch&#46;

### `vericairn worker runs`

List the agent runs with their caps&#44; their observed usage&#44; what each is charged &#40;its cap for what was not reported&#44; and while in flight&#41; and their stops&#44; and the ledgers of their goals&#46;

Usage: `vericairn worker runs [options]`

### `vericairn worker stop`

Ask a run to stop and report what was observed&#58; the request&#44; its transmission to the harness&#44; the harness&#39;s acknowledgment&#44; an escalation after the grace period and the containment&#44; each only once observed&#46; A stop that is not observed is reported as requested&#46;

Usage: `vericairn worker stop <run> [options]`

Arguments:

- `<run>`: The run&#58; its id&#44; the id of any of its operation&#39;s revisions&#44; or its identity&#46;

Options:

- `--by <by>`: The principal asking for the stop&#46; Required&#46;
- `--reason <reason>`: Why&#46;
- `--grace <grace>`: Seconds the harness has to stop once the stop is transmitted before its process tree is killed&#59; the worker&#39;s default&#44; ten&#44; when omitted&#46;

Build version: 0&#46;8&#46;0; provenance instant: 2026&#45;10&#45;19T14&#58;18&#58;24Z.

## Generation basis

Build commit: `9393a977a6b7d7f2b09a93cf5c087260e22dd784`.

Generator: `vericairn.software/cli-reference/1`; implementation: `vericairn.software.companions/1`; source digest: `sha256:c3997f1a7bfe9d21f850d7887766cc3159a696ef78174357f71746e10a71f55e`.

Input digest: `sha256:d2c9fc15661f5e4f0ac06245d35964822062e65fa0de17d03eeac0e42f1388a9`.

| Input | SHA-256 | Bytes |
|---|---|---|
| inputs&#47;commands&#46;json | `sha256:9e1ca0f03bbc6b356c2871e9f692833555ec97404ebdb328328ea9d6cac4fd6b` | 87830 |
