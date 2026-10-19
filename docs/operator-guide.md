# Operator guide: 0.7.3 changes

For the development build's 0.8.0 additions, [Inspecting estate policies](policies.md)
describes the policy commands, recurring-duty evidence, explicit-time previews and
writing-exception registry reads.

This maintained guide describes the five changes in 0.7.3. The complete estate workflows are in the
[0.7.3 operator guide](releases/0.7.3/operator-guide.md); earlier releases keep their own frozen guides
in the [release index](releases/README.md).

## Learning of a release

`vericairn version`, `vericairn doctor`, `vericairn status` and Workbench disclose a release check
enabled by default. It asks GitHub's `Vericairn/vericairn` latest-release endpoint for the newest
published release, at most once per rolling 24 hours across both clients using the same Vericairn home.
Failed attempts also count. Workbench checks at opening and periodically while open; it never waits
for the network on its UI thread. The daemon, MCP and ordinary SDK estate calls do not initiate checks.

The request contains no credentials, installed version, user, machine or estate identity, estate data
or usage telemetry. GitHub requires the constant `User-Agent: Vericairn` header. Cookies, redirects and
trace propagation are disabled. The check reaches only GitHub and ignores system proxies. Drafts,
prereleases and invalid responses do not replace the cached answer.
Network failures are silent and preserve previous knowledge. A check never fails an otherwise working
command, installs software, authors a record or moves an estate's pin.

CLI disclosure and notices go to stderr, also with `--json`. The existing stdout and canonical status
document remain intact. Workbench shows the disclosure and any newer-release notice in its shell,
including before opening an estate. Notices name the version, release notes and all upgrade routes.

## Machine-local state

Use `vericairn updates configure` to see the setting, or add `--json` for `{"enabled":true}` or
`{"enabled":false}`. Disable requests with `vericairn updates configure --enabled false`; re-enable them
with `vericairn updates configure --enabled true`. For NuGet installations substitute `dotnet vc` for
`vericairn`. Configuring the setting itself makes no request. Disabling retains cached information, which
may still produce a notice or an estate warning. An unreadable or invalid setting also prevents requests.

`updates/settings.json`, `updates/check.json` and the coordination lock live under the user's Vericairn
home, outside repositories and estates. The cache holds the release tag, last attempt time and last
successful answer time. The home defaults to the platform's per-user application-support directory
(`Vericairn` on Windows and macOS, `vericairn` under the XDG configuration directory on Linux).
`VERICAIRN_HOME` can select another machine-local directory. Do not commit this state or put the home
inside an estate or Git repository. Clients share the daily limit through the saved timestamp and lock;
if they cannot safely save an attempt, they skip the request.

## Installing and changing an estate's pin

Read the linked release notes. For the NuGet CLI, run
`dotnet tool update --global dotnet-vc --version <published-version>`; omit `--global` for a local tool.
For archive installations and Workbench, download the archive for your platform from the linked release
page and verify its hashes as the release's installation guide describes. These actions update the
installed client. They do not update an estate's manifest.

`vericairn estate check` separately warns with `manifest.release-behind` when an estate is pinned below
the newest published stable release it knows. It reads the estate's release records for the manifest's
repository and, for `Vericairn/vericairn`, the local release cache. It makes no network request. Unrelated
products, candidate releases and prereleases do not count. An equal or higher pin, a source pin, or no
known release produces no warning. The finding is always a warning, so it does not fail the check.

Once the target's published release record and required evidence are in the estate, use that release's
CLI to review `vericairn estate upgrade --to <published-version> --check`, then apply it with
`vericairn estate upgrade --to <published-version> --by <operator-id>`. The existing upgrade checks,
authority rules and migration record still apply. A cached network answer grants no upgrade authority.

## Required attention

The next release adds [the operator action queue](operator-actions.md), available with
`vericairn operator list`: every recorded waiting operator action, its reason, holds, exact command
and required input. This addition is separate from the frozen release guides.

In 0.7.3, `status`, `progress` and Workbench show compact references to obligations
that remain open, with their current state and an inspection command. Later
verification, result, review, acceptance and operation records resolve the
relevant items; ordinary `after` ordering remains planning. Quiet settings do not
hide required attention, and an unavailable resolution stays unknown. Open the
item to read the complete record before acting. See [required attention](required-attention.md)
for the precise rules, including historical corrections that have no typed closure.

Canonical status JSON derives attention from records alone and agrees on machines holding the same
records. Human status, progress and Workbench also show labelled local worker observations. A machine
cannot infer resolution from prose, unrelated acceptance or absent records; complete typed evidence is
needed. A later passing verification matches the exact subject, method, environment and command.
Retiring a work order does not withdraw a review's current result subject. Acknowledging an item, quiet
settings or settling a run does not dispose of every worker signal.

## Worker repositories and command observations

FIX-19 gives each workspace its own repository, with read-only object borrowing from the registered clone.
Codex's sandbox adds only that workspace's `.git` to its writable roots. The seventh live run committed
successfully on macOS; live qualification on other platforms is not established by that run.
After settlement, the worker fetches the audited branch head for review with a guarded reference update.
Incomplete committed audits, changed workspace heads, checked-out destination branches and concurrent clone branch
moves refuse transfer and leave a local `audit-unverified` warning. Preserve the workspace and reconcile
before retrying. Uncommitted changes remain in it. Worker Git disables hooks and ambient configuration.
Borrowed objects depend on the clone; preserve work before pruning objects or removing either repository.
Legacy linked worktrees require preservation and `git worktree remove`, not silent conversion.

FIX-20 recognizes the Vericairn credential stores, `gh auth token`, `gh auth status --show-token`,
`git credential fill` and Git credential-helper `get`, as well as additional write forms. Recognized
credentials and outside writes request containment; null-device output is exempt from outside-write
classification. Ordinary outside reads, network attempts and unverified effects remain warnings. Listing
the Vericairn home itself, including a recursive listing, and reading a per-clone `live.db` are outside-read
warnings; they do not request a stop. Independently recognized credential paths retain their critical signals. The
command audit observes harness reports that can arrive after execution and cannot enforce isolation.
The [software guide](software.md#command-observations-070) lists recognized forms and remaining gaps.

FIX-26 permits reading only the home's location and matches Codex's own tools to their admitted native names.
Credential-store reads, copies and archives remain critical. Timed-out checks receive one retry with a generous
limit; an unfinished credential check then requests a stop. Settlement excludes ignored build output and untracked
worker brief files while counting tracked and other untracked changes. A complete committed audit can retain the
branch for review even when the separate file inventory is incomplete.

## Runtime certificate concurrency

FIX-21 initializes the typed extension caches of loaded and rotated certificates before publishing
them to TLS listeners, correcting the concurrent first-use race present since 0.6.1. When daemon logging
is configured, handshake failures include exception type and message without stacks, inner exceptions,
peer certificates or request data. Other trust, authentication, network and resource failures can still
reject a handshake. The [API transport guide](api-v1.md#transports) retains the existing trust and rotation rules.
