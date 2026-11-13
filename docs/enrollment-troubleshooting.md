# Enrollment troubleshooting: a presence prompt that expired, was refused or timed out

This recipe is for an enrollment step that ended without a clear answer while a device asked for its owner's presence: on a
Mac, a key in the Secure Enclave, whose every signature shows a Touch ID or password dialog. It applies to the steps of
[devices and admission](operator-guide.md#3-principals-devices-and-admission) that sign with such a key: `devices create` for
a person's device, whose new key signs its own device record; `devices admit`, signed by the admitting device; and your first
device admitting itself, which in a new estate also signs the adoption checkpoint. It tells the endings apart from what the
command prints, then reconciles from what the estate holds, without replaying the enrollment.

An authenticated prompt is no completion receipt. Touch ID accepting your finger, or the dialog accepting your password, lets
the key make one signature; it does not say that the record was stored. Only the stored record, read back with the commands
below, says what happened.

## What the command prints

From 0.9.3 a command that signs with such a device announces every record it will sign before the first prompt, on standard
error, and counts each prompt ([presence prompts](operator-guide.md#presence-prompts-announced-and-counted-093)). Admitting a
device you hold, from a device that asks for presence, prints:

```text
Writing the admission record asks for your presence once, for the one record it signs:
  1. admission device <desktop-id>: admits the device
Signature 1 of 1: admission device <desktop-id>: admits the device
```

When the signature is made the action ends with `Signed 1 of 1.` and standard output prints the record's path, id and hash.
Three other endings look alike but are not the same:

- **Presence expired.** Nobody answered the dialog. Vericairn waits 300 seconds for the `vericairn-enclave` helper to make one
  signature, then stops the helper, and the command prints, about five minutes after the prompt line:

  ```text
  Stopped at signature 1 of 1, which was not made; 0 were signed.
  error: The record could not be signed on this machine (the device's key store refused to sign, InvalidOperationException).
  ```

- **An explicit refusal.** You pressed Cancel in the dialog. The command prints the same two lines at once. The runtime gives
  an expiry and a refusal the same refusal, so tell them apart by the time it took and by what you did. Either way the
  signature was not made.

- **A client timeout with an unknown outcome.** The command stopped waiting for the runtime and exits with code 124:

  ```text
  error: The request timed out. The outcome is unknown; reconcile the original operation before retrying.
  ```

  The write may have finished, or may still finish: a daemon serving the estate keeps going after the command stops waiting,
  and its dialog can still be on screen. A write is observed for six minutes, longer than one prompt waits, but your first
  device's admission in a new estate asks for two prompts, and two slow answers can outlast it.

A line that counts signatures, `Stopped at signature k of n, which was not made; m were signed.`, names the first signature
that was not made: the signatures before it were made, and their records can be stored even though the command reports an
error or a warning.

## Before any retry, inspect

Read what the estate holds before you run anything again:

```sh
vericairn devices list --json
vericairn records show <admission-id>
vericairn status
vericairn estate check
```

`devices list --json` gives each device its `admission`: `admitted`, `not admitted` or `revoked`; its `presence`; and
`heldHere`, whether this machine holds its key. A device that `devices create` could not sign is not listed, and its key was
removed with it. When a command printed a record's path, id and hash on standard output, the record was stored:
`records show <admission-id>` reads it. `status` shows the history line, such as
`History: not adopted · no heads · 0 witnessed · tail 4 · no problems` for an estate that has not adopted history checkpoints.
For your first device, `vericairn estate first-authority` lists its self-admission and whether the estate reads it as its first
authority; it writes nothing.

## Reconcile: reopen only the operation that did not complete

Keep the principals, devices and admissions the estate holds. Never run `devices create` again for a device `devices list`
shows, and never admit a device that reads `admitted` again: `devices admit` of a device already admitted is not refused, and
writes a second admission. Run the one missing step, once. It asks a fresh presence prompt, with its own five minutes; nothing
retries a signature by itself.

| What inspection shows | The step to reopen |
| --- | --- |
| The device you created is not listed | `devices create` again: the failed one left no record and no key |
| The device reads `not admitted` | `vericairn devices admit <desktop-id> --by <operator-id> --note "The operator's desktop."` |
| Your first device reads `admitted`, and `status` says `History: not adopted` in a new estate | the adoption checkpoint, not another admission (below) |
| The device reads `admitted` and nothing else is missing | nothing: the step completed |

Estate records and this machine's key custody are kept apart. A device's key lives in this machine's custody, never in the
estate, and the records live in the estate. Do not delete or edit key files, do not end `vericairn-enclave`, the daemon or a
command by killing its process, and do not edit records or `estate/manifest.json` by hand: each step above writes through the
runtime, which judges it as any write.

## Worked example: your first device admitted, its adoption checkpoint expired

In a new estate your first device admits itself and signs the adoption checkpoint in the same step, two prompts:

```sh
vericairn devices admit <laptop-id> --by <operator-id> --note "The first authority."
```

You answered the first prompt and left the second. Standard output printed the admission's path, id and hash, and standard
error printed:

```text
Writing the admission record asks for your presence twice, once for each record it signs:
  1. admission device <laptop-id>: admits the device
  2. history-checkpoint trigger adoption: adopts history checkpoints, witnessing the estate's history up to now
Signature 1 of 2: admission device <laptop-id>: admits the device
Signature 2 of 2: history-checkpoint trigger adoption: adopts history checkpoints, witnessing the estate's history up to now
Stopped at signature 2 of 2, which was not made; 1 was signed.
warning: No history checkpoint with trigger adoption was written: The record could not be signed on this machine (the device's key store refused to sign, InvalidOperationException).
```

The admission was signed and stored; only the checkpoint was not. Inspection confirms it:

```sh
vericairn devices list --json
vericairn records show <admission-id>
vericairn estate first-authority
vericairn status
```

The device reads `admitted` and `heldHere`; the record is your admission; `estate first-authority` shows it as the estate's
first authority, named by the manifest; and `status` shows `History: not adopted · no heads · 0 witnessed`. A second
`devices admit` would not adopt: it would write another admission, signed by the device itself, and leave the estate not
adopted. Reopen the adoption alone, which asks one fresh prompt:

```sh
vericairn estate checkpoint --adopt --check
vericairn estate checkpoint --adopt --expect-plan <plan> --by <operator-id>
```

The check lists what adoption witnesses and anchors and prints `plan: sha256:<hash>`; the second command signs it:

```text
Writing the history-checkpoint record asks for your presence once, for the one record it signs:
  1. history-checkpoint trigger adoption: adopts history checkpoints, witnessing the estate's history up to now
Signature 1 of 1: history-checkpoint trigger adoption: adopts history checkpoints, witnessing the estate's history up to now
Signed 1 of 1.
```

`status` then shows `History: adopted`, and `vericairn estate check` reports no errors. The estate holds one admission of your
device.

## Worked example: an admission that expired with nothing written

You admit your second device from your first, which asks for presence, and the prompt expires:

```sh
vericairn devices admit <desktop-id> --by <operator-id> --note "The operator's desktop."
```

```text
Writing the admission record asks for your presence once, for the one record it signs:
  1. admission device <desktop-id>: admits the device
Signature 1 of 1: admission device <desktop-id>: admits the device
Stopped at signature 1 of 1, which was not made; 0 were signed.
error: The record could not be signed on this machine (the device's key store refused to sign, InvalidOperationException).
```

Nothing was written. `vericairn devices list --json` shows the desktop `not admitted` and its key `heldHere`: the device and
its key stand. Run the same `devices admit` once more, and answer its prompt; it ends with `Signed 1 of 1.` and the device
reads `admitted`. `devices create` is not run again.

## Counterexample: an admission that had completed

You admitted the desktop and saw the dialog accept your finger, but the terminal was closed before you read the result, or the
command timed out with an unknown outcome. Inspect first:

```sh
vericairn devices list --json
vericairn records show <admission-id>
```

The desktop reads `admitted`, and its admission is stored: the step completed, and there is nothing to reopen. Running
`devices admit` again would not be refused today; it writes a second admission of the same device, which is why the estate is
read before anything runs again.

## Two first devices: `manifest.first-authority`

If the enrollment of a new estate was repeated on another clone and the clones were merged, the estate can hold two
self-admissions of your devices. When the manifest names neither, `vericairn estate check` reports `manifest.first-authority`,
with no device's admission counting. From 0.9.3 you name your first device's self-admission with
`vericairn estate first-authority`, after reading its evidence ([the command](cli.md#vericairn-estate-first-authority-093)).
Every other self-admission still reads `signature.self-admission`: remove its record file yourself.
