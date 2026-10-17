# Operator guide: release notices and upgrades

This working source documents UPDATE-01 for 0.7.3. The existing estate workflows are in the
[0.7.2 operator guide](releases/0.7.2/operator-guide.md); published builds keep their own frozen guide
in the [release index](releases/README.md). This page is the source for the release-notice section of
the next operator guide.

## Learning of a release

`vericairn version`, `vericairn doctor`, `vericairn status` and Workbench disclose a release check
enabled by default. It asks GitHub's `Vericairn/vericairn` latest-release endpoint for the newest
published release, at most once per rolling 24 hours across both clients using the same Vericairn home.
Failed attempts also count. Workbench checks at opening and periodically while open; it never waits
for the network on its UI thread. The daemon, MCP and ordinary SDK estate calls do not initiate checks.

The request contains no credentials, installed version, user, machine or estate identity, estate data
or usage telemetry. GitHub requires the constant `User-Agent: Vericairn` header. Cookies, redirects and
trace propagation are disabled. Drafts, prereleases and invalid responses do not replace the cached answer.
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
