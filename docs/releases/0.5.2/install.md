# Installing Vericairn 0.5.2

Vericairn 0.5.2 ships as self-contained distributables per platform: nothing else needs to be installed to run them, not even a .NET runtime. There are two distributables per platform: `vericairn-0.5.2-<platform>` holds the command line (`bin/vericairn`, and on macOS also `bin/vericairn-enclave`, the helper the command line calls for Secure Enclave custody), and `vericairn-workbench-0.5.2-<platform>` holds the desktop (`bin/vericairn-workbench`) with a copy of the command line beside it, because Workbench drives the daemon through that command line. `git` is needed for the commands that read or adopt repositories and for a repository-synced estate; the GitHub CLI `gh`, logged in as you, is needed only for the release provider and for observing workflow runs, because Vericairn never holds a token of its own. On Linux, secret-service custody needs `secret-tool` (from libsecret) and a running secret service such as GNOME Keyring; without them device keys are kept in owner-only files.

There are two ways to install the command line, and they end in the same place. Workbench is installed by direct download only (section 3).

## 1. Route A: as part of an estate, through its bootstrap

An estate repository pins the Vericairn release it runs on in `estate/manifest.json`, by version and by the SHA-256 of every distributable. Its `bootstrap` script fetches that release, verifies it and runs it. This is the cold start, and it is exactly what the estate's continuous integration does on Ubuntu, macOS and Windows.

Linux and macOS:

```sh
git clone <the estate repository> my-estate
cd my-estate
./bootstrap estate check
./bootstrap status
```

Windows, in PowerShell 5.1 or PowerShell 7:

```powershell
git clone <the estate repository> my-estate
cd my-estate
./bootstrap.ps1 estate check
./bootstrap.ps1 status
```

What the bootstrap does, step by step:

1. Reads `estate/manifest.json`. In release mode it finds the pinned version and the hash recorded for this machine's platform (linux-x64, linux-arm64, osx-x64, osx-arm64, win-x64 or win-arm64).
2. Fetches `vericairn-0.5.2-<platform>.tar.gz` (or `.zip` on Windows) from the release page, unless `VERICAIRN_RELEASE_BASE` names another directory or URL to fetch from.
3. Computes the archive's SHA-256 and refuses to continue when it differs from the manifest. The hash comes from the manifest, never from the tag name alone.
4. Unpacks the archive under `.vericairn/toolchain/release/0.5.2/<platform>/` inside the clone, a directory git ignores, and remembers the verified hash beside it so later runs skip the fetch.
5. Runs `bin/vericairn --estate <the clone> <your arguments>`; with no arguments it runs `estate check`.

Expected observation: `estate check` ends with a line like `N records: 0 errors, 0 warnings`, and `status` renders the estate: its work orders, releases, assignments, decisions, documents and the living documents that are due. The bootstrap prints `bootstrap: fetching vericairn-0.5.2-<platform>...` the first time only.

If the manifest pins a source commit instead of a release (an estate younger than its first release), the bootstrap builds Vericairn from that commit and needs the .NET SDK named by the source's `global.json`; set `VERICAIRN_SOURCE` to a local clone of the Vericairn source to build from it instead of fetching.

## 2. Route B: direct download of the command line

1. From https://github.com/Vericairn/vericairn/releases/tag/v0.5.2 download the distributable for your platform and `SHA256SUMS`.
2. Verify the download. Linux: `sha256sum -c SHA256SUMS --ignore-missing`. macOS: `shasum -a 256 -c SHA256SUMS --ignore-missing`. Windows PowerShell: `(Get-FileHash .\vericairn-0.5.2-win-x64.zip).Hash` and compare with the line in `SHA256SUMS`. The expected hashes are in the estate's published release record, which is the authority: a tag can be moved, a record cannot.
3. Unpack: `tar -xzf vericairn-0.5.2-<platform>.tar.gz -C <directory>` on Linux and macOS; `Expand-Archive` on Windows. The archive holds `bin/vericairn` (with `bin/vericairn-enclave` beside it on macOS), `LICENSE`, `NOTICE`, the third-party notices and `licenses/` with the license and notice texts of every component the command line bundles.
4. Run `<directory>/bin/vericairn version` (`bin\vericairn.exe` on Windows). It prints `0.5.2+<commit>`.
5. Put `bin` on your `PATH` if you want to type `vericairn` alone. Every command takes `--estate <path>`; without it the current directory is the estate. Keep `vericairn-enclave` beside `vericairn`: the command line looks for the helper next to itself, and without it Secure Enclave custody is unavailable.

Platform notes, stated honestly because the build is unsigned:

- macOS may refuse to run a binary that a browser downloaded, because it carries the quarantine attribute and is not notarized. Downloading with `curl` avoids the attribute; otherwise remove it with `xattr -dr com.apple.quarantine <directory>` after verifying the checksum, which covers the helper too. The estate bootstrap fetches with `curl`, so Route A does not hit this. The helper uses CryptoKit's Secure Enclave keys, which need no entitlement and no developer signature; on a Mac without a Secure Enclave, or in a virtual machine, `vericairn doctor` says the enclave is unavailable and keys stay in owner-only files.
- Windows SmartScreen may warn about an unsigned program the first time. PowerShell may require `-ExecutionPolicy Bypass` to run `bootstrap.ps1` when the policy is restricted.
- Linux: `tar` preserves the executable bit; nothing else is needed.

## 3. Workbench, the desktop

1. From the same release page download `vericairn-workbench-0.5.2-<platform>.tar.gz` (`.zip` on Windows) and verify it against `SHA256SUMS` as above.
2. Unpack it to a directory of its own. It holds `bin/vericairn-workbench` and `bin/vericairn` (with `.exe` on Windows, and with `bin/vericairn-enclave` on macOS), `LICENSE`, `NOTICE`, the third-party notices and `licenses/` with the license and notice texts of every bundled component, the fonts' among them.
3. Run it:

```sh
<directory>/bin/vericairn-workbench --estate <estate root>
```

Workbench opens the estate's root (the directory with `estate/manifest.json`), connects to the daemon that serves it, and starts one through the command line beside it when none answers; nothing runs in the daemon's place inside Workbench. Without `--estate` it shows the first-run page, where the root is typed or chosen from the recent estates. `--as <principal id>` names the principal Workbench acts for: the selector in the target bar then shows that principal's admitted device held on this machine, the one the runtime signs with, and choosing another device in the selector changes the acting principal. When the named principal has no device held here, the target bar says whether its writes are refused (the estate requires signatures) or written unsigned. Without `--as`, Workbench chooses among the admitted devices held here and says when it is read-only. `VERICAIRN_CLI` names another command-line executable to drive; by default the one beside Workbench is used, then one on the `PATH`. A device whose key needs its owner's presence is meant to ask for Touch ID or the login password whenever the runtime signs with it; when this set was frozen no signature with presence had been observed, through Workbench or otherwise (`limitations-and-platforms.md`).

Workbench is an unsigned developer build with no application bundle, installer or signature: on macOS the same quarantine note applies (remove the attribute after verifying the checksum, or download with `curl`); Windows SmartScreen may warn the first time; on Linux it runs under X11 and XWayland. Workbench is not qualified on any platform. The platform obligations in the Vericairn estate record what has been observed: when this set was frozen, on macOS, native first launches of the shipped 0.3.1 and 0.4.2, and of the withdrawn 0.5.0 and 0.5.1 in their independent reviews, and the accessibility probe's measurement of their release workspace (no target below 24 layout units; from 0.4.2, every contrast pair at or above its minimum), and the operator's acceptance of 0.3.1 written through the daemon; on all three platforms, headless journeys run as automation. See `limitations-and-platforms.md` and `workbench.md`.

## 4. The MCP server for coding agents

A coding agent reaches an estate through `vericairn mcp serve`, a server over standard input and output that every agent harness can start. It is a client of the daemon, holds no key of its own, and its write tools exist only for a principal whose admitted device key this machine holds:

```sh
vericairn --estate <estate root> mcp serve --as <principal id>
```

Claude Code: `claude mcp add vericairn -- vericairn --estate <estate root> mcp serve --as <principal id>`. `vericairn mcp orientation --out <directory>` writes the orientation file an agent reads first (`AGENTS.md`, generated from the estate's identity and the tool registry, with `CLAUDE.md` and `GEMINI.md` as pointers), and `--check` says whether existing copies are current; `mcp.md` in this set has the protocol subset, the tools and the limitations. An agent's device never needs its owner's presence, so an agent signs without a prompt.

## 5. After installing

`vericairn doctor` reports the version, the runtime, `git` and `dotnet` if present, the estate it sees, the key custody a new device gets by default in the estate it sees, whether Secure Enclave custody is available on a Mac and why not when it is not, the record formats the build reads and writes (1 and 2) and whether a daemon serves the estate. Per-user state (device keys or, for enclave keys, their enclave-wrapped blobs, each estate's live store and daemon endpoint, logs, Workbench's layout preference) lives in the per-user application-support directory `doctor` prints; set `VERICAIRN_HOME` to use another. A device key created in an estate still at record format 1 is kept in the strongest custody format 1 can name (the platform key store on Windows, the secret service on Linux where one runs, otherwise an owner-only file); the Secure Enclave and presence need the estate at record format 2 (section 6), after which `vericairn devices rotate <device> --custody secure-enclave` moves a key there.

Nothing runs in the background until you ask: commands run the embedded runtime unless a daemon serves the estate (`vericairn runtime status` says which). A daemon started by `vericairn runtime start` (or by Workbench, through that command) writes its output to `daemon.log` beside the estate's live store, never to the terminal or the program that started it. When the daemon stops before it serves, `runtime start` exits with an error that quotes what the daemon wrote to its log and says where the log is. And `vericairn runtime install --plan` shows what installing the daemon as a per-user service would do before `runtime install` does it; Workbench's runtime workspace shows the same plan before it installs or uninstalls. The operator guide covers both.

## 6. Upgrading an estate to 0.5.2 and record format 2

An estate pinned to 0.4.2 is upgraded by the 0.5.2 binary, not by the one it runs on: install 0.5.2 by Route B, then in the estate `vericairn estate upgrade --to 0.5.2 --check` to see the plan and `vericairn estate upgrade --to 0.5.2 --by <your principal id>` to apply it. The upgrade needs the published release record of 0.5.2 in the estate. It pins the manifest to 0.5.2 with the artifact hashes, moves the manifest's `recordFormat` from 1 to 2 as the step `record-format-2`, and writes the migration record, the estate's first format-2 record. If any admission in the estate was recorded without a signature, which format 1 allowed while signatures were optional, the migration lists those in force, and only that list keeps them counting at format 2: `--check` names them, and the operator, or a principal with the `administer` scope, applies the upgrade from a machine that holds their admitted device, so that the migration that lists them is signed; anyone else is refused before anything changes. No existing record is rewritten: every record's bytes and hash are the same before and after, and every record written before keeps the rules of format 1. Commit the result; from then on the bootstrap fetches 0.5.2. Releases 0.5.0 and 0.5.1 were withdrawn before publication and are not upgrade targets.

Published releases before 0.5.2 do not read record format 2: release 0.4.2 refuses an estate at format 2 rather than misread it. After the upgrade, every clone that writes to the estate runs 0.5.2, which a clone's bootstrap fetches from the new pin; a clone that has not pulled the upgrade yet can still add format-1 records, which the estate reads under format 1's rules. From format 2, a new decision must state its `stake` and what it does not authorize (`notAuthorized`), and scripts that write decisions need those two fields (the operator guide, section 4). Every admission and every revocation is signed from format 2, so one is made on a machine that holds an admitted device of its author, or with the operator's recovery key (the operator guide, section 3). Upgrading moves no device key: each device keeps its custody until its owner rotates it on the machine that holds it (the operator guide, section 3).
