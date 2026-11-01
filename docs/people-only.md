# Run an estate with people only

Use this arrangement when people author, review and accept the work. The current
command line supports the record workflow without an agent account, launcher or
model provider. There is no shipped people-mode switch or worker refusal: keep
agent principals and launchers out of this arrangement yourself.

This guide uses the current build's existing commands. The
[people and agents guide](people-and-agents.md) and
[agents-only guide](agents-only.md) cover the other arrangements; their planned
mode features are not prerequisites here.

| Responsibility | In this walkthrough |
| --- | --- |
| Charter, identities, assignments and reserved acts | Human operator |
| Write the file and report its result | Human contributor |
| Inspect and accept the exact result | Human operator, distinct from the contributor |

Two people give this example a separate author and accepter. A solo operator can
prepare work and label a review of their own result `self`; that is not independent
review. The current runtime also permits operator self-acceptance of ordinary work
and an explicit `self-accept-ordinary-work` grant. Those exceptions do not provide
the separation required here: bring in another authorized person to review and
accept the result. Do not label a non-operator human reviewer `independent-agent`;
the current review vocabulary has no independent-human label. This walkthrough
uses the human operator as reviewer.

## Set up a practice estate

Install the [command line](install.md) first. These examples require Bash, Git and
Python 3, with `vericairn` on the command search path. Git Bash supplies Bash on
Windows; the blocks are not PowerShell syntax. With the NuGet tool, replace the
executable `vericairn` with `dotnet vc` throughout. Run the blocks in order in one
Bash session, starting in an empty practice directory. `field` reads one field
from a JSON receipt, so the examples use the identities your estate actually creates.

The walkthrough is offline. Its first Vericairn command disables the machine's
optional release checks; that setting persists until you enable it again through
the [release notice settings](software.md#newer-release-notices-073).
File custody here is for disposable practice identities. For a real estate, use
each person's own device and the strongest available custody, with presence where
supported; follow [devices and admission](operator-guide.md#3-principals-devices-and-admission).
Keep signing keys outside the repository and never give an agent a person's key.

```bash
set -euo pipefail
field() {
  python3 -c 'import json,sys; print(json.load(sys.stdin)[sys.argv[1]])' "$1"
}
mkdir people-only-estate
cd people-only-estate
git init --quiet
git config core.autocrlf false
mkdir -p .vericairn/guide docs
printf '.vericairn/\n' > .gitignore
vericairn updates configure --enabled false
vericairn --json estate init --name "People only practice" --slug people-only-practice \
  --purpose "Practice a reviewed local documentation task." \
  --operator-name "Practice Operator" --organization "Practice" \
  > .vericairn/guide/init.json
operator=$(field operator < .vericairn/guide/init.json)
operator_device=$(vericairn --json devices create --principal "$operator" \
  --name "Operator practice device" --custody file | field id)
vericairn devices admit "$operator_device" --by "$operator" \
  --note "Admit the practice operator."
```

Initialization pins the running build's source commit and creates the human
operator, charter, manifest and bootstrap scripts. It does not select an operating
mode. Use an installed build that reports its source commit; a build without one
needs the explicit commit option described in the
[initialization guide](operator-guide.md#2-create-an-estate).

The operator adds a human contributor and admits that person's own device.

```bash
vericairn --json records new principal --by "$operator" --body - \
  > .vericairn/guide/contributor.json <<'JSON'
{"kind":"human","name":"Practice Contributor","roles":["contributor"]}
JSON
contributor=$(field id < .vericairn/guide/contributor.json)
contributor_device=$(vericairn --json devices create --principal "$contributor" \
  --name "contributor practice device" --custody file | field id)
vericairn devices admit "$contributor_device" --by "$operator" \
  --note "Admit the practice contributor."
```


```bash
vericairn devices list
vericairn estate check
```

Every listed practice device should be admitted, and the check should report zero
errors and warnings. Receipts under `.vericairn/guide` are local scratch; the
records themselves are authored only by Vericairn.

## Work a task through review and acceptance

The deliverable is one local file containing `Ready for review.` and a newline.
The operator creates an ordinary work order; the operator assigns it to the contributor.
The brief names the scope, evidence and prohibited effects before work begins.

```bash
vericairn --json work new --by "$operator" --key GUIDE-1 \
  --title "Write a readiness note" \
  --intent "Practice producing and accepting an exact local output." \
  --scope "docs/readiness.txt" --deliverable "docs/readiness.txt" \
  --exit-question "Does the reviewed file contain the required sentence and newline?" \
  > .vericairn/guide/order.json
order=$(field id < .vericairn/guide/order.json)
vericairn --json work assign GUIDE-1 --contributor "$contributor" --by "$operator" \
  --brief - > .vericairn/guide/assignment.json <<JSON
{
  "outcome":"A reviewed readiness note.",
  "sources":[{"type":"work-order","id":"$order"}],
  "permittedChanges":["docs/readiness.txt"],
  "prohibitedEffects":["no publication","no spending","no credential use"],
  "workspace":["practice estate, local working tree"],
  "limits":["one local documentation task"],
  "expectedOutputs":["docs/readiness.txt"],
  "reviewCriteria":["The file is exactly Ready for review. followed by a newline.","The output hash and byte count match."],
  "recovery":"Read this brief and the latest result before continuing."
}
JSON
assignment=$(field id < .vericairn/guide/assignment.json)
vericairn work brief GUIDE-1
attempt=$(vericairn --json work attempt "$assignment" --by "$contributor" \
  --workspace "practice estate, local working tree" \
  --note "Write and check the readiness note." | field id)
printf 'Ready for review.\n' > docs/readiness.txt
python3 - <<'PY' > .vericairn/guide/result-body.json
import hashlib, json
from pathlib import Path
data = Path('docs/readiness.txt').read_bytes()
assert data == b'Ready for review.\n'
print(json.dumps({
    'outcome': 'completed',
    'summary': 'Wrote and checked the readiness note.',
    'outputs': [{'name': 'readiness note', 'location': 'docs/readiness.txt',
                 'hash': 'sha256:' + hashlib.sha256(data).hexdigest(), 'bytes': len(data)}],
    'evidence': [{'summary': 'Compared the file bytes with the required sentence and newline.'}],
    'checks': [{'name': 'exact content', 'outcome': 'passed', 'summary': 'The bytes match.'}],
    'limitations': ['A local documentation exercise; no software release was built.'],
    'unresolvedEffects': [],
    'nextStep': 'A different principal reviews and accepts the exact result.'
}))
PY
vericairn --json work result "$attempt" --by "$contributor" \
  --body .vericairn/guide/result-body.json > .vericairn/guide/result.json
result=$(field id < .vericairn/guide/result.json)
result_hash=$(field hash < .vericairn/guide/result.json)
```

The result is now a claim awaiting review. The operator reads the stored result and
the actual output, then records a review of that exact result hash. The expected
hash also guards against the result changing before the review is recorded. In real work,
the reviewer performs these checks using their own admitted device; changing the
name passed to a command does not make a person's inspection independent.

```bash
vericairn --json records show "$result" > .vericairn/guide/result-record.json
cat docs/readiness.txt
python3 - <<'PY'
import hashlib, json
from pathlib import Path
record = json.loads(Path('.vericairn/guide/result-record.json').read_text())
output = record['body']['outputs'][0]
data = Path(output['location']).read_bytes()
assert data == b'Ready for review.\n'
assert output['bytes'] == len(data)
assert output['hash'] == 'sha256:' + hashlib.sha256(data).hexdigest()
print('Content, byte count and hash match the recorded output.')
PY
vericairn --json records new review --by "$operator" \
  --subject "result:$result:$result_hash" --expected "$result_hash" --body - \
  > .vericairn/guide/review.json <<JSON
{
  "independence":"operator",
  "outcome":"pass",
  "summary":"Read the result and verified the exact file content, byte count and hash against the brief.",
  "findings":[],
  "read":[{"type":"result","id":"$result","hash":"$result_hash"}]
}
JSON
review=$(field id < .vericairn/guide/review.json)
vericairn records new acceptance --by "$operator" \
  --subject "result:$result:$result_hash" --body - <<JSON
{
  "statement":"Accept the exact readiness-note result from its passing review.",
  "basis":["$review"],
  "self":false
}
JSON
vericairn work observe GUIDE-1
vericairn status
vericairn estate check
```

The observation should show a completed, accepted result with its review and
acceptance; the estate check should still have zero errors and warnings. This
accepts the result, not a release, and it does not close the work order: `GUIDE-1`
stays planned until a review of the work order and an acceptance of it close it, as
[review and acceptance](operator-guide.md#6-review-and-acceptance) describes. A review
used as an acceptance basis must judge the same subject: do not reuse this result
review to accept a work-order record.
Changed output needs a new result and a fresh review and acceptance of its hash.
A failed check calls for a `partial` or `blocked` result with limitations and a
next step; a review with findings calls for another attempt, not a passing label.

Commit the estate's records and the deliverable when satisfied. Local receipts and
keys are not part of that commit. Git history carries the signed records between
clones; it does not confer signing authority.

## Run the daily loop

```bash
vericairn status
vericairn progress
vericairn ask --to "$operator"
vericairn policies list
vericairn policies check
vericairn work observe GUIDE-1
vericairn estate check
```

People choose the next work order, agree its brief, assign it to a person,
record an attempt and result, and have the operator review and accept the exact
result. No worker or launcher is needed. Read unanswered asks and make decisions;
reading or waiting does not answer them.

Read policy findings together with their basis. The default evaluation uses the
latest recorded timestamp, so it is not a live deadline clock. An unavailable
source remains unavailable, and a refuse-level finding holds release submission
even when the estate check reports it as a warning. An empty policy list in this
practice estate does not clear a real estate's obligations. See
[estate policies](policies.md) for adoption, exact approvals and release gates.

Keep a result and its next step before handing work over. Reconcile an uncertain
external effect under its existing identity before retrying it; an effect needs
recorded intent before dispatch and observation afterwards. A successful process
exit, a green check or an agent's progress claim does not accept work.

## What stays with a person

The estate has a human operator at its root. In this guide, people administer
principals and devices, admit and revoke devices, change the charter, and grant,
withdraw or renew authority. Agents use only their own admitted signing devices.
Keep the `operator` role and `administer` scope with people. The planned universal
refusal of agent administration is not implemented; these arrangements must be
maintained by the operator with the current role and grant system.

The human operator accepts every release and milestone, authorizes publication
and spending, and keeps credentials in human custody. Ordinary-work grants do not
confer those permissions. For a product release, follow the
[release journey](operator-guide.md#8-releases): inspect the candidate's evidence
and applicable gates, accept it, and authorize publication separately.
Legal commitments (contracts, terms and filings), transfers of estate, product,
domain or account ownership, and destructive operations on the estate's records
or repositories stay with people in every mode, including the planned agents mode.
Granting, widening or renewing a planned delegated reserved act also stays human;
even a planned credential-use grant would leave the credential material in human custody.

The operator retains the ability to withdraw authority, pause goals and request
run stops. Pausing a goal prevents further dispatch; it does not stop a run already
in flight. A stop request is distinct from observed containment and settlement;
check those facts before assuming a run ended. Nothing an agent writes removes
the operator's authority. Follow the
[worker's stop and custody rules](software.md#coding-agents-the-worker-and-its-launchers)
when agents are running.

## Planned, not available

The accepted operating-mode design separates the intended arrangement from today's
implementation. These parts are planned and have no release placement yet:

- `ESTATE-02`: charter fields `operatingMode` and `checkpointDays`, mode changes and
  contradiction warnings, people-mode refusal of agent authorship, assignments and
  runs, and the universal refusal of never-delegable acts by agents.
- `ID-04`: checked, bounded, expiring grants for `accept-release`, `publish`, `spend`
  and `use-credential`, plus refusal and asks outside their bounds. Today an unknown
  authority scope can be stored as free text; these four names grant no such power.
- `OPS-06`: human checkpoints and the agents-only attention view, including grant
  bounds, expiry, delegated acts and renewal asks. The accepted design calls for
  grants to return to a person within at most 30 days; no automatic checkpoint or
  renewal mechanism exists today.

Do not add those charter fields or assume those refusals or delegations exist.
Changing today's arrangement means a person deliberately changes principals,
assignments and current grants and handles any running work; there is no shipped
atomic mode-switch operation. The current attention views still show ordinary
asks, policies, work and worker observations.
