# Presence session qualification

ID-15 (0.9.4) adds presence sessions to the macOS enclave helper, `vericairn-enclave`: after one
presence check, one helper process signs only the records an operator action announced, for at most
the session's limit, and ends at its first refusal, a screen lock, sleep, the close of its input or
its parent's exit, with a dialog whose text it writes itself. The source cases are `D49`, the
estate's `design/presence-sessions.md`, sections 5 to 11 and 15.

Context reuse, binding to one process and the end at lock and sleep are platform behavior. A release
opens sessions only on the macOS major versions it lists, and its acceptance requires, for each one, a
passing run of this procedure on a real Mac with a presence key, against the release candidate's own
signed helper, recorded as a verification of the candidate (`OPS-19`). A version whose run fails or is
missing leaves the list by a rebuild before acceptance; a build that lists none ships with sessions off.
This procedure is evidence for review, not release acceptance.

## What the automated tests cover

The helper's tests run on macOS with keys without presence, so the session's checks need no prompt.
They run every session of the shared vectors (`tests/fixtures/presence-sessions/vectors.json`, which the
runtime's checks, `ID-16`, are to read too) against a test build of the helper: every check of the
protocol, every refusal code, the dialog's text, the fingerprint, the ends a test can raise, the canonical
parser against the runtime's serializer and the self-check's refusals. The test build adds a controlled clock and other test
hooks; the release build is compiled without them. What follows is what no automated test can show: the
dialog itself, context reuse, the process binding, the hardening, real locks and sleep, and the runtime's
use of the helper.

## Before you start

1. **The candidate's helper.** Take `vericairn-enclave` from the candidate's macOS archive for this Mac's
   architecture, after checking the archive against the candidate's `SHA256SUMS`. Record its SHA-256.
2. **Its signature.** `codesign -dv --verbose=2 vericairn-enclave` must show a `Developer ID Application`
   authority, the release's `TeamIdentifier`, the helper's identifier and `flags=0x10000(runtime)`;
   `codesign -d --entitlements - vericairn-enclave` must show no `com.apple.security.get-task-allow`. A
   helper that does not meet this refuses every session with `helper.unsigned`.
3. **The Mac.** Record the macOS version and build (`sw_vers`), the hardware model (`sysctl hw.model`)
   and whether it has Touch ID.
4. **A throwaway presence key.** From the product repository at the candidate's commit, run
   `python3 tools/presence-session-qualification.py --helper ./vericairn-enclave key`. It makes a key that
   asks for presence in a new owner-only directory and prints its blob. Never use a device's key: the
   driver signs synthetic records that belong to no estate.
5. **The driver.** Every command below is
   `python3 tools/presence-session-qualification.py --helper ./vericairn-enclave <command> --blob <blob>`.
   It stands in for the runtime on the helper's protocol, starts the helper with an empty environment,
   prints every message the helper answers with the seconds since it started, and prints the dialog's
   expected text and fingerprint before the dialog opens. Keep its output as the evidence.

Run points 1 and 3 in each form of the dialog: with Touch ID, with the password alone on a Mac without
Touch ID, and with Touch ID falling back to the password (choose **Use Password…** in the dialog). Run the
other points once, in any form.

## The eleven points

**1. One dialog opens a session.** Run `open --count 128 --spread 280`. Exactly one dialog opens; after
it is answered, 128 signatures arrive over about 280 seconds and the driver prints
`signed 128 of 128 with one presence check`. Then run `open --count 3` and choose **Cancel**: the helper
answers `refused` with `check.declined` and `ended` with `declined`, and nothing is signed. Run it again
and press Control-C in the driver while the dialog is open: the dialog closes and nothing is signed. Run
it once more and leave the dialog unanswered: after 300 seconds the helper refuses with `check.declined`
and nothing is signed.

**2. The context satisfies the key.** During point 1, no second dialog opens for any signature, and no
dialog opens before the one that begins "open Vericairn session". If each signature asks again, this
point fails: the build must not list this macOS version, so every signature keeps its own check.

**3. The dialog's text is whole.** Run `maximum`. The dialog shows the whole text the driver printed as
expected: 200 characters, ending with an ellipsis, the fingerprint included. Record the process name the
dialog shows.

**4. Another process gets its own dialog.** Run `hold --every 10` and answer its dialog. While it holds the
session, in a second terminal run `./vericairn-enclave sign <blob> <64 zeros> "a second process"`: a
dialog of its own opens, reading "sign an unread Vericairn digest: a second process". Then copy the
helper, sign the copy ad hoc (`codesign -s - -f <copy>`) and run the same `sign` with the copy: it opens a
dialog and, once approved, signs. That confirms what the design states: nothing binds a presence key to
the helper's code.

**5. The release helper refuses attachment and injection.** With `hold` running, `lldb -p <helper process>`
fails to attach. Make an unhardened copy (`codesign -s - -f <copy>`, without `-o runtime`) and run `hold`
with it: `lldb -p` attaches to the copy. For injection, build a library that writes a line when it loads
(`__attribute__((constructor)) static void loaded(void) { fprintf(stderr, "loaded\n"); }`, compiled with
`clang -dynamiclib`) and run `DYLD_INSERT_LIBRARIES=<library> ./vericairn-enclave available`: the release
helper never writes `loaded`, and the unhardened copy does. A context taken out of the helper is unusable
elsewhere: the evaluated context is a handle into the system's authentication service, bound to the
helper's process, which no message or file of the helper carries; record that the attached unhardened
copy offers no way to hand it to another process, which still gets its own dialog (point 4).

**6. Nothing signs without a new dialog.** Run `open --count 2` twice: each run opens its own dialog. Run
`hold`, answer it, and kill the helper by its process id (`kill -9 <helper process>`): the driver reports
that the helper's output ended, and a new `open` opens a new dialog.

**7. Signing ends at the limit.** Run `hold --limit 60 --every 5`: the helper answers `ended` with `limit`
about 60 seconds after `opened`, and nothing is signed after it. Run `hold --limit 120 --every 5` and move
the wall clock an hour forward (System Settings, General, Date & Time, with automatic time off): the
session still ends about 120 seconds after `opened` on the driver's clock. Repeat moving it an hour back,
then turn automatic time on again. A session over which the Mac sleeps ends at the sleep (point 8); record
that `sleep` came first.

**8. Each of these ends the session.** For each event, run `hold --every 5`, answer its dialog, and cause
the event: lock with Control-Command-Q; lock with a hot corner set to Lock Screen; start a screen saver
that asks for the password immediately; switch to another user (fast user switching); close the lid on a
portable Mac; sleep the display (`pmset displaysleepnow`); sleep the Mac (`pmset sleepnow`). The helper
answers `ended` with `lock` or `sleep` within a second of the event. Record each reason. Separately,
record whether the enclave itself refuses at the lock screen: make a key without presence
(`./vericairn-enclave create <directory>/plain.se`), run `sleep 15; ./vericairn-enclave sign
<directory>/plain.se <64 zeros>`, lock the screen within 15 seconds and record whether it signed.

**9. Its parent's exit ends the helper.** Run `parent`. The driver starts the helper as the child of a
shell, keeps the helper's input open as another child of the runtime could, and kills the shell: the
helper answers `ended` with `parent` and the driver prints `the helper exited` within five seconds.

**10. The daemon and a missing graphical session.** With the candidate's runtime started by
`vericairn runtime start`, run an operator action that announces a list on this device: the dialog
appears although the daemon hosts the runtime (this needs `ID-16`'s runtime in the candidate). Then, over
Remote Login (`ssh` to this Mac, with no graphical session), run `open --count 2`: the helper refuses with
`check.unavailable` and ends, and does not wait.

**11. An unqualified macOS version keeps one check per signature.** With a candidate build that does not
list this macOS version, `vericairn doctor` says presence sessions are off on this macOS version because
the release has not qualified it, and an operator action asks one check per signature (this needs
`ID-16`'s runtime in the candidate).

## Recording the result

Record one verification of the candidate per macOS major version: the macOS version and build, the
hardware model, the forms of the dialog exercised, the helper's SHA-256 and signing identity, each point's
outcome with the driver's output, and anything that did not hold. A failed point removes that version from
the release's list by a rebuild before acceptance. Remove the throwaway key's directory afterwards.
