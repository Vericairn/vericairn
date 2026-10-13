# Limitations and platforms, Vericairn 0.2.0

Taken from the published release record 01M4VM50H07HCAP529TE1VHV2E in the Vericairn estate, which names every artifact by hash. Built from commit 2ca88ac7ea1ec4f9279ac728000a329c3339c432 by release workflow run 38472778709; published October 13, 2026, 21:53 UTC at https://github.com/Vericairn/vericairn/releases/tag/v0.2.0.

## Qualified platforms

The tests ran on these platforms in the release build, so these are the platforms the release is qualified for:

- linux-x64 (ubuntu-latest)
- osx-arm64 (macos-latest)
- win-x64 (windows-latest)

The linux-arm64, osx-x64 and win-arm64 distributables are cross-published from the same commit and have not been executed anywhere. They are provided, not qualified.

## Signing

Unsigned developer build. The operator's signing material does not exist yet. Verify a download by its checksum against the estate's manifest or the release record, never against the tag name alone; `install.md` shows how.

## Record format

Reads record format 1, writes record format 1. Release 0.2.0 adds the `verification`, `observation` and `operation` record types, which release 0.1.0 cannot read; an estate records them only after upgrading to 0.2.0.

## Known limitations recorded with the release

- linux-arm64, osx-x64 and win-arm64 distributables are cross-published and not yet executed anywhere; linux-x64, osx-arm64 and win-x64 are the platforms the tests ran on.
- No code signing or notarization: the operator's signing material does not exist yet; verify the checksums against the estate's manifest, never against the tag alone.
- Release 0.2.0 writes record format 1 but adds the verification, observation and operation record types, which release 0.1.0 cannot read; the estate records them only after upgrading to 0.2.0.
- The loopback profile is pinned to the runtime certificate and device-signed; the local-network profile and key custody beyond owner-only files on macOS and Linux are milestone M3.
- The third-party notices are maintained by hand beside the generated bill of materials.

Limitations the accepted results of this release's work orders carry (see `release-notes.md`): the daemon does not yet run admitted work or supervise workers (milestone M4); a process of the same operating-system user can sign with the device key held in file custody on macOS and Linux, which is the recorded strength of that custody; `runtime install` generated and tested its service units but was not registered with a live service manager in that work; the GitHub Releases provider identifies a dispatched run by listing recent runs and records the operation as uncertain when none appears within thirty seconds; verifications record a command's exit, duration and output and do not parse test reports.

## Artifacts

| Artifact | Platform | Bytes | SHA-256 |
|---|---|---|---|
| vericairn-0.2.0-linux-x64.tar.gz | linux-x64 | 45397692 | f38a197a40a8cb179a47c672fe13b4e63b497a1ac2aa1f1a34ea1b3702396ece |
| vericairn-0.2.0-linux-arm64.tar.gz | linux-arm64 | 43214440 | 77dd56f328bf6c8b9a078c3e948fcf3cd792bcb6ccdb700ebd91b2440087685f |
| vericairn-0.2.0-osx-arm64.tar.gz | osx-arm64 | 43640710 | eb539bbba28975458703f411b3d0ecf3be50208aa3aceeb63b7c926b228486e7 |
| vericairn-0.2.0-osx-x64.tar.gz | osx-x64 | 45951740 | 60d452eb41b244499f343e7132b9de4a580fdc7f4f93c7316baa19d463e65c85 |
| vericairn-0.2.0-win-x64.zip | win-x64 | 46201643 | 658c2bb36ac0dc7b7724f700844abdb20dff628581a2632b3f57c148d073c236 |
| vericairn-0.2.0-win-arm64.zip | win-arm64 | 44338510 | b581227ac90450b97a02709198ca8c1295bad1ddfc26cfc21856dde972a7ff48 |
| vericairn-0.2.0-sbom.cdx.json | any | 10856 | 0bd59be9b4478737ab5d763a78f1549e0afca908fbb86263cf5c7b887318c009 |
| vericairn-0.2.0-THIRD-PARTY-NOTICES.md | any | 882 | 6207c0d18234e3554582f1b0332c4abe00382075c9a02424e8fefca463ecfb94 |

The `SHA256SUMS` file on the release page lists the same hashes.
