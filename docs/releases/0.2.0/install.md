# Installing Vericairn 0.2.0

Vericairn 0.2.0 ships as a self-contained distributable per platform: nothing else needs to be installed to run it, not even a .NET runtime. `git` is needed for the commands that read or adopt repositories and for a repository-synced estate; the GitHub CLI `gh`, logged in as you, is needed only for the release provider and for observing workflow runs, because Vericairn never holds a token of its own.

There are two ways to install, and they end in the same place.

## Route A: as part of an estate, through its bootstrap

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
2. Fetches `vericairn-0.2.0-<platform>.tar.gz` (or `.zip` on Windows) from the release page, unless `VERICAIRN_RELEASE_BASE` names another directory or URL to fetch from.
3. Computes the archive's SHA-256 and refuses to continue when it differs from the manifest. The hash comes from the manifest, never from the tag name alone.
4. Unpacks the archive under `.vericairn/toolchain/release/0.2.0/<platform>/` inside the clone, a directory git ignores, and remembers the verified hash beside it so later runs skip the fetch.
5. Runs `bin/vericairn --estate <the clone> <your arguments>`; with no arguments it runs `estate check`.

Expected observation: `estate check` ends with a line like `N records: 0 errors, 0 warnings`, and `status` renders the estate: its work orders, releases, assignments, decisions and documents. The bootstrap prints `bootstrap: fetching vericairn-0.2.0-<platform>...` the first time only.

If the manifest pins a source commit instead of a release (an estate younger than its first release), the bootstrap builds Vericairn from that commit and needs the .NET SDK named by the source's `global.json`; set `VERICAIRN_SOURCE` to a local clone of the Vericairn source to build from it instead of fetching.

## Route B: direct download

1. From https://github.com/Vericairn/vericairn/releases/tag/v0.2.0 download the distributable for your platform and `SHA256SUMS`.
2. Verify the download. Linux: `sha256sum -c SHA256SUMS --ignore-missing`. macOS: `shasum -a 256 -c SHA256SUMS --ignore-missing`. Windows PowerShell: `(Get-FileHash .\vericairn-0.2.0-win-x64.zip).Hash` and compare with the line in `SHA256SUMS`. The expected hashes are also in `limitations-and-platforms.md` of this set and in the estate's published release record, which is the authority: a tag can be moved, a record cannot.
3. Unpack: `tar -xzf vericairn-0.2.0-<platform>.tar.gz -C <directory>` on Linux and macOS; `Expand-Archive` on Windows.
4. Run `<directory>/bin/vericairn version` (`bin\vericairn.exe` on Windows). It prints `0.2.0+2ca88ac7ea1ec4f9279ac728000a329c3339c432`.
5. Put `bin` on your `PATH` if you want to type `vericairn` alone. Every command takes `--estate <path>`; without it the current directory is the estate.

Platform notes, stated honestly because the build is unsigned:

- macOS may refuse to run a binary that a browser downloaded, because it carries the quarantine attribute and is not notarized. Downloading with `curl` avoids the attribute; otherwise remove it with `xattr -dr com.apple.quarantine <directory>` after verifying the checksum. The estate bootstrap fetches with `curl`, so Route A does not hit this.
- Windows SmartScreen may warn about an unsigned program the first time. PowerShell may require `-ExecutionPolicy Bypass` to run `bootstrap.ps1` when the policy is restricted.
- Linux: `tar` preserves the executable bit; nothing else is needed.

## After installing

`vericairn doctor` reports the version, the runtime, `git` and `dotnet` if present, the estate it sees, the key custody this platform provides (owner-only files on macOS and Linux, the platform key store on Windows) and whether a daemon serves the estate. Per-user state (device keys, each estate's live store and daemon endpoint, logs) lives in the per-user application-support directory `doctor` prints; set `VERICAIRN_HOME` to use another.

Nothing runs in the background until you ask: commands run the embedded runtime unless a daemon serves the estate (`vericairn runtime status` says which), and `vericairn runtime install --plan` shows what installing the daemon as a per-user service would do before `runtime install` does it. The operator guide covers both.

## Upgrading an estate to 0.2.0

An estate pinned to 0.1.0 is upgraded by the 0.2.0 binary, not by the one it runs on: install 0.2.0 by Route B, then in the estate `vericairn estate upgrade --to 0.2.0 --check` to see the plan and `vericairn estate upgrade --to 0.2.0 --by <your principal id>` to apply it. The upgrade needs the published release record of 0.2.0 in the estate, pins the manifest to 0.2.0 with the artifact hashes, writes a migration record, and changes nothing else, since the record format did not change. Commit the result; from then on the bootstrap fetches 0.2.0.
