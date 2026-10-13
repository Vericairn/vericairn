# Command-line reference, Vericairn 0.2.0

Generated from the 0.2.0 binary's own command tree (`vericairn <command> --help`, walked recursively) on October 13, 2026. Every command and verb of the release is listed; nothing is edited by hand. Global options apply to every command: `--estate <path>` names the estate root (default: the current directory), `--json` switches to machine-readable output, `--version` prints the version.

## `vericairn records`

Create, validate, describe and format records.

Usage: `vericairn records [command] [options]`

Verbs: `validate`, `new`, `describe`, `format`, `hash`

### `vericairn records validate`

Parse and validate every record and the manifest of an estate.

Usage: `vericairn records validate [options]`

### `vericairn records new`

Create a record in canonical form, validate the estate with it, and write it.

Usage: `vericairn records new <type> [options]`

Arguments:
- `<type>`: Record type, for example work-order.

Options:
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).
- `--body <body> (REQUIRED)`: Path to a JSON file holding the body, or '-' for standard input.
- `--subject <subject>`: Subject as type:id or type:id:hash.
- `--supersedes <supersedes>`: Id of the record this one replaces.
- `--at <at>`: Authoring time, for example 2026-10-13T14:00:00Z (default: now).
- `--id <id>`: Explicit record id; only the root operator principal needs this.

### `vericairn records describe`

Explain record types and their fields.

Usage: `vericairn records describe [<type>] [options]`

Arguments:
- `<type>`: Record type to describe; omit to list all types.

### `vericairn records format`

Rewrite record files in canonical pretty form without changing their content or hash.

Usage: `vericairn records format <files>... [options]`

Arguments:
- `<files>`: Record files to rewrite in canonical pretty form.

### `vericairn records hash`

Print the computed content hash of a record file.

Usage: `vericairn records hash <file> [options]`

Arguments:
- `<file>`: Record file.

## `vericairn estate`

Create and check estates.

Usage: `vericairn estate [command] [options]`

Verbs: `init`, `check`, `export`, `restore`, `upgrade`, `reconcile`

### `vericairn estate init`

Create a new estate: operator principal, charter and manifest, validated together.

Usage: `vericairn estate init [options]`

Options:
- `--profile <profile>`: Storage profile: repository-synced (records as files in a git repository) or local-only (records in this machine's live store). [default: repository-synced]
- `--name <name> (REQUIRED)`: Estate display name.
- `--slug <slug> (REQUIRED)`: Estate slug (lower-case kebab-case).
- `--purpose <purpose> (REQUIRED)`: Why the estate exists.
- `--operator-name <operator-name> (REQUIRED)`: Display name of the root operator (a human).
- `--organization <organization> (REQUIRED)`: Organization display name.
- `--github <github>`: GitHub organization login, if any.
- `--repository <repository>`: GitHub owner/name of the Vericairn source to pin. [default: Vericairn/vericairn]
- `--commit <commit> (REQUIRED)`: Commit of the Vericairn source to pin (40 hex digits).

### `vericairn estate check`

Validate records and run repository cleanliness and portability checks.

Usage: `vericairn estate check [options]`

### `vericairn estate export`

Write a verified export: records, objects, manifest and documents with an inventory; no keys or authority.

Usage: `vericairn estate export [options]`

Options:
- `--out <out> (REQUIRED)`: Path of the export file to create (never overwritten).
- `--by <by>`: Principal id recorded as the exporter.

### `vericairn estate restore`

Verify an export against its inventory and restore it into an empty directory as a repository-synced estate; authority is not restored.

Usage: `vericairn estate restore [options]`

Options:
- `--from <from> (REQUIRED)`: Export file.
- `--to <to> (REQUIRED)`: Empty destination directory.

### `vericairn estate upgrade`

Pin the estate to a published release and apply record-format migrations as one reviewable change with a migration record.

Usage: `vericairn estate upgrade [options]`

Options:
- `--to <to> (REQUIRED)`: Version of a published release recorded in this estate.
- `--by <by>`: Principal id applying the upgrade (required unless --check).
- `--check`: Show the plan without changing anything.

### `vericairn estate reconcile`

List what two diverged copies left to settle: divergent revisions, duplicate keys, duplicate results.

Usage: `vericairn estate reconcile [options]`

## `vericairn work`

Briefs, assignments, attempts, results and amendments of work orders.

Usage: `vericairn work [command] [options]`

Verbs: `brief`, `assign`, `attempt`, `result`, `amend`, `observe`

### `vericairn work brief`

Export the brief of a work order: everything a contributor needs, from the records alone.

Usage: `vericairn work brief <work-order> [options]`

Arguments:
- `<work-order>`: Work-order key (for example M1-WORK) or record id.

### `vericairn work assign`

Delegate a work order to a contributor under a brief, pinned to the exact work-order revision.

Usage: `vericairn work assign <work-order> [options]`

Arguments:
- `<work-order>`: Work-order key (for example M1-WORK) or record id.

Options:
- `--contributor <contributor> (REQUIRED)`: Principal id of the contributor the work is delegated to.
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).
- `--brief <brief> (REQUIRED)`: Path to a JSON file holding the brief (outcome, sources, permittedChanges, prohibitedEffects, workspace, limits, expectedOutputs, reviewCriteria, recovery), or '-' for standard input.
- `--note <note>`: Free text.

### `vericairn work attempt`

Record the start of an attempt by the assignment's contributor.

Usage: `vericairn work attempt <assignment> [options]`

Arguments:
- `<assignment>`: Assignment record id.

Options:
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).
- `--workspace <workspace> (REQUIRED)`: Where the work happens, named without machine paths (a repository and branch, a clone, a tool).
- `--note <note> (REQUIRED)`: What the contributor intends to do.
- `--at <at>`: Start time, for example 2026-10-13T14:00:00Z (default: now).

### `vericairn work result`

Submit the result of an attempt: outcome, summary, outputs by hash, evidence, checks, limitations, unresolved effects, next step.

Usage: `vericairn work result <attempt> [options]`

Arguments:
- `<attempt>`: Attempt record id.

Options:
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).
- `--body <body> (REQUIRED)`: Path to a JSON file holding the body, or '-' for standard input.

### `vericairn work amend`

Request a change to an assignment's outcome, scope, target, budget or permitted effects; it authorizes nothing.

Usage: `vericairn work amend <assignment> [options]`

Arguments:
- `<assignment>`: Assignment record id.

Options:
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).
- `--body <body> (REQUIRED)`: Path to a JSON file holding the body, or '-' for standard input.

### `vericairn work observe`

Show a work order's assignments, attempts, results, amendments, reviews and acceptances.

Usage: `vericairn work observe <work-order> [options]`

Arguments:
- `<work-order>`: Work-order key (for example M1-WORK) or record id.

## `vericairn devices`

Device keys: create a device for a principal on this machine, list, admit and revoke.

Usage: `vericairn devices [command] [options]`

Verbs: `create`, `list`, `admit`, `revoke`

### `vericairn devices create`

Generate a key on this machine and record the device, signed by its own key.

Usage: `vericairn devices create [options]`

Options:
- `--principal <principal> (REQUIRED)`: The principal this device acts for.
- `--name <name> (REQUIRED)`: Descriptive device name, never a machine path.
- `--custody <custody>`: Key custody: default (CNG on Windows, owner-only file elsewhere), file, or cng.
- `--profile <profile>`: Permitted connectivity profile; repeatable (default: embedded).

### `vericairn devices list`

List device records with their admission state and whether their key is held here.

Usage: `vericairn devices list [options]`

### `vericairn devices admit`

Admit a device to author records in this estate (operator or 'administer' scope).

Usage: `vericairn devices admit <device> [options]`

Arguments:
- `<device>`: Device record id.

Options:
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).
- `--note <note> (REQUIRED)`: Why the device is admitted.

### `vericairn devices revoke`

Revoke a device's admission from now on; its earlier records stay attributable.

Usage: `vericairn devices revoke <device> [options]`

Arguments:
- `<device>`: Device record id.

Options:
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).
- `--reason <reason> (REQUIRED)`: Why the admission ends.

## `vericairn release`

Releases: list the records, submit a release through the GitHub Releases provider as intent before effect, observe the run, the draft and the publication.

Usage: `vericairn release [command] [options]`

Verbs: `list`, `submit`, `observe`

### `vericairn release list`

List current release records: version, status, commit, id, signing.

Usage: `vericairn release list [options]`

### `vericairn release submit`

Record the intent, then dispatch the release workflow; the run id and outcome are recorded as they are observed. Never publishes.

Usage: `vericairn release submit [options]`

Options:
- `--product <product> (REQUIRED)`: Product record id the release belongs to.
- `--version <version> (REQUIRED)`: The version to release, for example 0.2.0.
- `--repository <repository> (REQUIRED)`: GitHub owner/name of the source repository.
- `--by <by> (REQUIRED)`: Principal id recording the operation (ULID).
- `--ref <ref>`: Branch or tag to build from. [default: main]
- `--workflow <workflow>`: Release workflow file. [default: release.yml]
- `--reconciliation <reconciliation>`: How an earlier failed, canceled or uncertain attempt under the same identity was settled; required for a retry.

### `vericairn release observe`

Observe the run and the draft; record the outcome, the candidate release, and later the operator's manual publication.

Usage: `vericairn release observe [options]`

Options:
- `--version <version> (REQUIRED)`: The version to release, for example 0.2.0.
- `--repository <repository> (REQUIRED)`: GitHub owner/name of the source repository.
- `--by <by> (REQUIRED)`: Principal id recording the operation (ULID).
- `--qualified <qualified>`: Platform whose qualification is recorded for the candidate; repeatable.
- `--limitation <limitation>`: A known limitation of the candidate; repeatable.
- `--signing <signing>`: Signing state recorded on the candidate. [default: unsigned developer build]
- `--cancel <cancel>`: Settle an intent that was never dispatched, or an outcome reconciled as having had no effect, with this reason.

## `vericairn sources`

Repositories the estate uses: inspect read-only, adopt as a resource, observe again.

Usage: `vericairn sources [command] [options]`

Verbs: `inspect`, `adopt`, `observe`

### `vericairn sources inspect`

Observe a repository read-only: commit, branch, remote, working-tree state, tags, recent history.

Usage: `vericairn sources inspect <path> [options]`

Arguments:
- `<path>`: Path of a git working tree on this machine; never recorded.

### `vericairn sources adopt`

Adopt a repository as a resource by observation: a resource record and a first observation; nothing in the repository changes.

Usage: `vericairn sources adopt <path> [options]`

Arguments:
- `<path>`: Path of a git working tree on this machine; never recorded.

Options:
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).
- `--name <name> (REQUIRED)`: Display name of the resource.
- `--role <role> (REQUIRED)`: What the estate uses the repository for.
- `--visibility <visibility>`: public, private or n/a. [default: private]
- `--notes <notes>`: Free text.

### `vericairn sources observe`

Record a fresh observation of an adopted repository.

Usage: `vericairn sources observe <path> [options]`

Arguments:
- `<path>`: Path of a git working tree on this machine; never recorded.

Options:
- `--resource <resource> (REQUIRED)`: Resource record id of the adopted repository.
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).

## `vericairn check`

Run a check and record its evidence as a verification of the exact commit, or observe a workflow run.

Usage: `vericairn check [command] [options]`

Verbs: `run`, `observe`

### `vericairn check run`

Run a command in a repository and record a verification: commit, command, environment, exit code, duration and output as evidence.

Usage: `vericairn check run <command>... [options]`

Arguments:
- `<command>`: The command and its arguments; put -- before it.

Options:
- `--resource <resource> (REQUIRED)`: Resource record id of the repository checked.
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).
- `--in <in>`: Working tree the command runs in. [default: the current directory]
- `--role <role>`: self, independent-agent, operator or automation. [default: self]
- `--summary <summary>`: What the check establishes, in your words; the observed exit and duration are recorded regardless.
- `--timeout <timeout>`: Seconds after which the command is stopped and its result recorded as unknown. [default: 3600]

### `vericairn check observe`

Observe a workflow run at its host and record it as automation's verification of the commit it built.

Usage: `vericairn check observe [options]`

Options:
- `--resource <resource> (REQUIRED)`: Resource record id of the repository checked.
- `--repository <repository> (REQUIRED)`: GitHub owner/name.
- `--run <run> (REQUIRED)`: Workflow run id.
- `--workflow <workflow>`: Workflow file the run belongs to. [default: ci.yml]
- `--by <by> (REQUIRED)`: Authoring principal id (ULID).

## `vericairn runtime`

The daemon that owns an estate's live state on this machine: serve, start, stop, status, install, uninstall.

Usage: `vericairn runtime [command] [options]`

Verbs: `serve`, `start`, `stop`, `status`, `install`, `uninstall`

### `vericairn runtime serve`

Serve the estate in this process until stopped (Ctrl+C or 'runtime stop'); refuses when a daemon already serves it.

Usage: `vericairn runtime serve [options]`

Options:
- `--no-loopback`: Serve the local socket only, without the pinned loopback TLS endpoint.

### `vericairn runtime start`

Start a daemon for the estate in the background and wait until it answers.

Usage: `vericairn runtime start [options]`

### `vericairn runtime stop`

Ask the daemon serving the estate to stop; closing a client never stops it.

Usage: `vericairn runtime stop [options]`

### `vericairn runtime status`

Whether a daemon serves the estate and where, or whether commands run the embedded runtime.

Usage: `vericairn runtime status [options]`

### `vericairn runtime install`

Install the daemon as a per-user service that starts at login (launchd agent, systemd user unit or a logon task).

Usage: `vericairn runtime install [options]`

Options:
- `--plan`: Show the unit and the commands without installing anything.

### `vericairn runtime uninstall`

Remove the per-user service installed by 'runtime install'.

Usage: `vericairn runtime uninstall [options]`

## `vericairn recover`

After an interruption or a move: inspect what this copy holds and compare it with another.

Usage: `vericairn recover [command] [options]`

Verbs: `inspect`, `compare`

### `vericairn recover inspect`

What this copy knows, which devices can author from here, work in progress, unresolved effects and reconcile tasks.

Usage: `vericairn recover inspect [options]`

### `vericairn recover compare`

Records only in this copy, only in the other, or (never expected) differing in content.

Usage: `vericairn recover compare [options]`

Options:
- `--with <with> (REQUIRED)`: Root of the other copy.

## `vericairn objects`

Content-addressed objects: put, list, verify.

Usage: `vericairn objects [command] [options]`

Verbs: `put`, `list`, `verify`

### `vericairn objects put`

Store a file's bytes as an object named by their hash.

Usage: `vericairn objects put <file> [options]`

Arguments:
- `<file>`: File whose bytes become an object.

### `vericairn objects list`

List held objects.

Usage: `vericairn objects list [options]`

### `vericairn objects verify`

Read every object and check its bytes against its name.

Usage: `vericairn objects verify [options]`

## `vericairn status`

Show the estate's current state derived from its records.

Usage: `vericairn status [options]`

## `vericairn decisions`

List the estate's current decisions, or show one cleanly.

Usage: `vericairn decisions [<id>] [options]`

Arguments:
- `<id>`: Decision record id (any revision); omit to list.

## `vericairn doctor`

Check this machine's prerequisites.

Usage: `vericairn doctor [options]`

## `vericairn version`

Print the version.

Usage: `vericairn version [options]`
