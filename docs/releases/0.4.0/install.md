# Installing Vericairn 0.4.0

Vericairn 0.4.0 ships as self-contained distributables per platform: nothing else needs to be installed to run them, not even a .NET runtime. There are two distributables per platform: `vericairn-0.4.0-<platform>` holds the command line (`bin/vericairn`), and `vericairn-workbench-0.4.0-<platform>` holds the desktop (`bin/vericairn-workbench`) with a copy of the command line beside it, because Workbench drives the daemon through that command line. `git` is needed for the commands that read or adopt repositories and for a repository-synced estate; the GitHub CLI `gh`, logged in as you, is needed only for the release provider and for observing workflow runs, because Vericairn never holds a token of its own.

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
2. Fetches `vericairn-0.4.0-<platform>.tar.gz` (or `.zip` on Windows) from the release page, unless `VERICAIRN_RELEASE_BASE` names another directory or URL to fetch from.
3. Computes the archive's SHA-256 and refuses to continue when it differs from the manifest. The hash comes from the manifest, never from the tag name alone.
4. Unpacks the archive under `.vericairn/toolchain/release/0.4.0/<platform>/` inside the clone, a directory git ignores, and remembers the verified hash beside it so later runs skip the fetch.
5. Runs `bin/vericairn --estate <the clone> <your arguments>`; with no arguments it runs `estate check`.

Expected observation: `estate check` ends with a line like `N records: 0 errors, 0 warnings`, and `status` renders the estate: its work orders, releases, assignments, decisions and documents. The bootstrap prints `bootstrap: fetching vericairn-0.4.0-<platform>...` the first time only.

If the manifest pins a source commit instead of a release (an estate younger than its first release), the bootstrap builds Vericairn from that commit and needs the .NET SDK named by the source's `global.json`; set `VERICAIRN_SOURCE` to a local clone of the Vericairn source to build from it instead of fetching.

## 2. Route B: direct download of the command line

1. From https://github.com/Vericairn/vericairn/releases/tag/v0.4.0 download the distributable for your platform and `SHA256SUMS`.
2. Verify the download. Linux: `sha256sum -c SHA256SUMS --ignore-missing`. macOS: `shasum -a 256 -c SHA256SUMS --ignore-missing`. Windows PowerShell: `(Get-FileHash .\vericairn-0.4.0-win-x64.zip).Hash` and compare with the line in `SHA256SUMS`. The expected hashes are in the estate's published release record, which is the authority: a tag can be moved, a record cannot.
3. Unpack: `tar -xzf vericairn-0.4.0-<platform>.tar.gz -C <directory>` on Linux and macOS; `Expand-Archive` on Windows.
4. Run `<directory>/bin/vericairn version` (`bin\vericairn.exe` on Windows). It prints `0.4.0+<commit>`.
5. Put `bin` on your `PATH` if you want to type `vericairn` alone. Every command takes `--estate <path>`; without it the current directory is the estate.

Platform notes, stated honestly because the build is unsigned:

- macOS may refuse to run a binary that a browser downloaded, because it carries the quarantine attribute and is not notarized. Downloading with `curl` avoids the attribute; otherwise remove it with `xattr -dr com.apple.quarantine <directory>` after verifying the checksum. The estate bootstrap fetches with `curl`, so Route A does not hit this.
- Windows SmartScreen may warn about an unsigned program the first time. PowerShell may require `-ExecutionPolicy Bypass` to run `bootstrap.ps1` when the policy is restricted.
- Linux: `tar` preserves the executable bit; nothing else is needed.

## 3. Workbench, the desktop

1. From the same release page download `vericairn-workbench-0.4.0-<platform>.tar.gz` (`.zip` on Windows) and verify it against `SHA256SUMS` as above.
2. Unpack it to a directory of its own. It holds `bin/vericairn-workbench` and `bin/vericairn` (with `.exe` on Windows), `LICENSE`, `NOTICE` and the third-party notices.
3. Run it:

```sh
<directory>/bin/vericairn-workbench --estate <estate root>
```

Workbench opens the estate's root (the directory with `estate/manifest.json`), connects to the daemon that serves it, and starts one through the command line beside it when none answers; nothing runs in the daemon's place inside Workbench. Without `--estate` it shows the first-run page, where the root is typed or chosen from the recent estates. `--as <principal id>` names the principal Workbench acts for: the selector in the target bar then shows that principal's admitted device held on this machine, the one the runtime signs with, and choosing another device in the selector changes the acting principal. When the named principal has no device held here, the target bar says whether its writes are refused (the estate requires signatures) or written unsigned. Without `--as`, Workbench chooses among the admitted devices held here and says when it is read-only. `VERICAIRN_CLI` names another command-line executable to drive; by default the one beside Workbench is used, then one on the `PATH`.

Workbench is an unsigned developer build with no application bundle, installer or signature: on macOS the same quarantine note applies (remove the attribute after verifying the checksum, or download with `curl`); Windows SmartScreen may warn the first time; on Linux it runs under X11 and XWayland. It is qualified only where the platform obligations in the Vericairn estate say, which when this set was frozen was the measurement and native first launch of the shipped 0.3.1 on macOS and headless journeys on the three platforms; see `limitations-and-platforms.md` and `workbench.md`.

## 4. The MCP server for coding agents

A coding agent reaches an estate through `vericairn mcp serve`, a server over standard input and output that every agent harness can start. It is a client of the daemon, holds no key of its own, and its write tools exist only for a principal whose admitted device key this machine holds:

```sh
vericairn --estate <estate root> mcp serve --as <principal id>
```

Claude Code: `claude mcp add vericairn -- vericairn --estate <estate root> mcp serve --as <principal id>`. `vericairn mcp orientation --out <directory>` writes the orientation file an agent reads first (`AGENTS.md`, generated from the records, with `CLAUDE.md` and `GEMINI.md` as pointers); `mcp.md` in this set has the protocol subset, the tools and the limitations.

## 5. After installing

`vericairn doctor` reports the version, the runtime, `git` and `dotnet` if present, the estate it sees, the key custody this platform provides (owner-only files on macOS and Linux, the platform key store on Windows) and whether a daemon serves the estate. Per-user state (device keys, each estate's live store and daemon endpoint, logs, Workbench's layout preference) lives in the per-user application-support directory `doctor` prints; set `VERICAIRN_HOME` to use another.

Nothing runs in the background until you ask: commands run the embedded runtime unless a daemon serves the estate (`vericairn runtime status` says which). A daemon started by `vericairn runtime start` (or by Workbench, through that command) writes its output to `daemon.log` beside the estate's live store, never to the terminal or the program that started it. When the daemon stops before it serves, `runtime start` exits with an error that quotes what the daemon wrote to its log and says where the log is. And `vericairn runtime install --plan` shows what installing the daemon as a per-user service would do before `runtime install` does it; Workbench's runtime workspace shows the same plan before it installs or uninstalls. The operator guide covers both.

## 6. Upgrading an estate to 0.4.0

An estate pinned to 0.3.1 is upgraded by the 0.4.0 binary, not by the one it runs on: install 0.4.0 by Route B, then in the estate `vericairn estate upgrade --to 0.4.0 --check` to see the plan and `vericairn estate upgrade --to 0.4.0 --by <your principal id>` to apply it. The upgrade needs the published release record of 0.4.0 in the estate, pins the manifest to 0.4.0 with the artifact hashes, writes a migration record, and changes nothing else: 0.4.0 reads and writes record format 1, as 0.3.1 does. Commit the result; from then on the bootstrap fetches 0.4.0.

0.4.0 adds an optional release profile to the product record (the destination and the artifacts a complete release carries; the operator guide, section 8). Earlier builds do not accept a product record that declares one, so declare it only after every clone that writes to the estate runs 0.4.0.
