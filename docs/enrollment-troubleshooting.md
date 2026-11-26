# Enrollment troubleshooting: a presence prompt that expired, was refused or timed out, or a signed command that failed at once

This recipe is for an enrollment step that ended without a clear answer while a device asked for its owner's presence: on a
Mac, a key in the Secure Enclave, whose every signature shows a Touch ID or password dialog. It applies to the steps of
[devices and admission](operator-guide.md#3-principals-devices-and-admission) that sign with such a key: `devices create` for
a person's device, whose new key signs its own device record; `devices admit`, signed by the admitting device; and your first
device admitting itself, which in a new estate also signs the adoption checkpoint. It tells the endings apart from what the
command prints, then reconciles from what the estate holds, without replaying the enrollment. From 0.9.4 it also covers any
signed command that fails at once, before a dialog appears, because the process that ran it cannot reach the signing
capability ([a signed command that fails at once](#a-signed-command-that-fails-at-once-094)).

An authenticated prompt is no completion receipt. Touch ID accepting your finger, or the dialog accepting your password, lets
the key make one signature; it does not say that the record was stored. Only the stored record, read back with the commands
below, says what happened.

## What the command prints

From 0.9.3 a command that signs with such a device announces every record it will sign before the first prompt, on standard
error, and counts each prompt ([presence prompts](operator-guide.md#presence-prompts-announced-and-counted-093)). From 0.9.4
each prompt's line ends with the record's short hash, the first twelve hexadecimal digits of its content hash, shown below as
`<short hash>`; from 0.9.4 the helper's own label in the dialog names the same short hash. Each of these lines begins with
`runtime:`, since its words are the runtime's: the command line marks every line it takes from the runtime so, shows its control
characters escaped and folds a line longer than the terminal, or than 80 columns in a file or a pipe, into rows that each begin with
`runtime:`. The examples show the rows of 80 columns, folded where the full identity, not its shorter placeholder, reaches the last
column. Admitting a device you hold, from a device that asks for presence, prints:

```text
runtime: Writing the admission record asks for your presence once, for the one
runtime: record it signs:
runtime:  1. admission device <desktop-id>: admits the device
runtime: Signature 1 of 1: admission device <desktop-id>: admits
runtime: the device (<short hash>)
```

When the signature is made the action ends with `runtime: Signed 1 of 1.` and standard output prints the record's path, id and hash.
Three other endings look alike but are not the same:

- **Presence expired.** Nobody answered the dialog. Vericairn waits 300 seconds for the `vericairn-enclave` helper to make one
  signature, then stops the helper, and the command prints, about five minutes after the prompt line:

  ```text
  runtime: Stopped at signature 1 of 1, which was not made; 0 were signed.
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

A line that counts signatures, `runtime: Stopped at signature k of n, which was not made; m were signed.`, names the first signature
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

## A signed command that fails at once (0.9.4)

A signed command can also fail at once, before any dialog appears, because the process that ran it cannot reach the Secure
Enclave: a command run by a sandboxed agent process, in a container, from a service or over a remote shell into the Mac. That
is neither an expired nor a refused prompt, and it does not show that the Mac lacks the hardware. Three cases end alike or
nearly so. Tell them apart by what the command printed, by whether a dialog appeared, and by what `vericairn doctor` reports
when you run it from the same context that tried to sign, the same sandbox, container, shell or service, not another terminal:

```sh
vericairn doctor
```

On a Mac its `enclave` line gives the reason; from 0.9.3 `vericairn --json doctor` gives it as `enclave`'s `state` and
`detail`.

- **Missing helper.** `doctor` reports:

  ```text
  enclave   Secure Enclave custody unavailable (no vericairn-enclave helper beside the command line)
  ```

  The command line looks for the `vericairn-enclave` helper next to itself, so this is an installation matter. Without the
  helper no Secure Enclave key on this machine can be read: `devices list --json` shows each such device with `heldHere` false,
  and a signed command announces no signature. It signs with another admitted device of the same principal whose key this
  machine holds, if there is one; otherwise it refuses, an admission with:

  ```text
  error: An admission is signed by an admitted device of its author, and principal <operator-id> has none whose key this machine holds: admit from a machine that holds one, or with the operator's recovery key (--recovery). Nothing was written.
  ```

  In an estate that does not require signatures, a record other than an admission is written unsigned instead, so check what
  was stored. Put the helper back beside the command line by installing the release's archive or tool package again. Do not
  follow a refusal's advice to create a device: the device and its key still stand, and only the helper is missing.

- **Capability unavailable to the invoking process.** The helper is there but reports that no Secure Enclave is available to
  the process it runs in. It checks this before any prompt, so no dialog appears and nothing is signed. A device that asks for
  presence prints its prompt's line and then, at once, the same two lines as an expired or refused prompt:

  ```text
  runtime: Stopped at signature 1 of 1, which was not made; 0 were signed.
  error: The record could not be signed on this machine (the device's key store refused to sign, InvalidOperationException).
  ```

  A device that never asks for presence, such as an agent's, prints the error line alone. From 0.9.2 the runtime describes a
  signing failure by its kind, never by its message, so the helper's own words, `This Mac has no Secure Enclave available to
  this process.`, which earlier releases printed, no longer appear; `doctor` gives them instead. Run from that same context, it
  reports:

  ```text
  enclave   Secure Enclave custody unavailable (no Secure Enclave available to this process)
  ```

  while an ordinary terminal session on the same Mac can report the enclave available. The probe runs when `doctor` runs, so
  its line describes the context it ran in, not the machine.

- **Presence refused or expired.** A dialog appeared. You pressed Cancel and the command failed at once, or nobody answered and
  it failed after 300 seconds: [what the command prints](#what-the-command-prints) applies. `doctor` in that context reports
  the enclave available, on a line that begins `enclave   Secure Enclave custody available`.

Reaching the runtime does not qualify a process to sign. `vericairn status`, `devices list` and every other read that works from
the invoking process show that it reaches the runtime and the estate, not that it can sign. `heldHere` says that this machine
holds the device's key, not that the asking process can use the Secure Enclave. `doctor`, run where the signing will happen, is
the supported check: it reports whether Secure Enclave custody is available there and, when not, why. It neither signs nor asks
for presence, so it cannot stand in for the signing step itself, whose dialog can still be refused or expire. Run the signed
command from a process that can reach the capability, such as your own terminal session on the Mac, outside the sandbox.

Recover as for the other endings:

1. Keep the failed command, what it printed on both streams and its exit code. Do not run it again yet.
2. Inspect, as [before any retry](#before-any-retry-inspect): a command that signs several records can have stored the ones
   before the failure, as its `Stopped at signature k of n` line says.
3. Reopen only what is missing, as [the reconciliation](#reconcile-reopen-only-the-operation-that-did-not-complete) says.
4. Make any retry once, separately authorized for that step, from a context where `doctor` reports the enclave available.

Do not create or admit a device again, move the key to weaker custody such as a file key, copy key files, loosen the sandbox so
that the agent signs by itself, or retry automatically. An agent that meets this ending stops, keeps the command and its output,
and reports to its operator, who decides the retry and where it runs.

### Worked example: refused in a sandbox, signed from your own terminal

You admit your desktop from your laptop, in a terminal that runs inside a sandbox, such as an agent tool's:

```sh
vericairn devices admit <desktop-id> --by <operator-id> --note "The operator's desktop."
```

It fails at once, and no dialog appears:

```text
runtime: Writing the admission record asks for your presence once, for the one
runtime: record it signs:
runtime:  1. admission device <desktop-id>: admits the device
runtime: Signature 1 of 1: admission device <desktop-id>: admits
runtime: the device (<short hash>)
runtime: Stopped at signature 1 of 1, which was not made; 0 were signed.
error: The record could not be signed on this machine (the device's key store refused to sign, InvalidOperationException).
```

These are the lines an expired prompt prints, in the [second worked example](#worked-example-an-admission-that-expired-with-nothing-written);
what differs is that no dialog appeared and the command ended at once. Keep them. `vericairn doctor`, run in the same sandboxed
terminal, reports `no Secure Enclave available to this process`. Inspect:

```sh
vericairn devices list --json
```

The laptop reads `admitted` and `heldHere` though this process could not sign with it, and the desktop reads `not admitted`:
nothing was written. In your own terminal session on the Mac, outside the sandbox, `vericairn doctor` reports Secure Enclave
custody available. There, run the same `devices admit` once more and answer its prompt: it ends with `Signed 1 of 1.`, and the
desktop reads `admitted`.

## Worked example: your first device admitted, its adoption checkpoint expired

In a new estate your first device admits itself and signs the adoption checkpoint in the same step, two prompts:

```sh
vericairn devices admit <laptop-id> --by <operator-id> --note "The first authority."
```

You answered the first prompt and left the second. Standard output printed the admission's path, id and hash, and standard
error printed:

```text
runtime: Writing the admission record asks for your presence twice, once for
runtime: each record it signs:
runtime:  1. admission device <laptop-id>: admits the device
runtime:  2. history-checkpoint trigger adoption: adopts history checkpoints,
runtime: witnessing the estate's history up to now
runtime: Signature 1 of 2: admission device <laptop-id>: admits
runtime: the device (<short hash>)
runtime: Signature 2 of 2: history-checkpoint trigger adoption: adopts history
runtime: checkpoints, witnessing the estate's history up to now (<short hash>)
runtime: Stopped at signature 2 of 2, which was not made; 1 was signed.
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
runtime: Writing the history-checkpoint record asks for your presence once, for
runtime: the one record it signs:
runtime:  1. history-checkpoint trigger adoption: adopts history checkpoints,
runtime: witnessing the estate's history up to now
runtime: Signature 1 of 1: history-checkpoint trigger adoption: adopts history
runtime: checkpoints, witnessing the estate's history up to now (<short hash>)
runtime: Signed 1 of 1.
```

`status` then shows `History: adopted`, and `vericairn estate check` reports no errors. The estate holds one admission of your
device.

## Worked example: an admission that expired with nothing written

You admit your second device from your first, which asks for presence, and the prompt expires:

```sh
vericairn devices admit <desktop-id> --by <operator-id> --note "The operator's desktop."
```

```text
runtime: Writing the admission record asks for your presence once, for the one
runtime: record it signs:
runtime:  1. admission device <desktop-id>: admits the device
runtime: Signature 1 of 1: admission device <desktop-id>: admits
runtime: the device (<short hash>)
runtime: Stopped at signature 1 of 1, which was not made; 0 were signed.
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

## Two first devices

If the enrollment of a new estate was repeated on another clone and the clones were merged, the estate holds two
self-admissions of your devices, one from each clone. What to do depends on whether each clone's adoption checkpoint was
written.

When neither was written, for example because each adoption prompt expired as in the first worked example, the manifest
decides. Each clone's runtime named its own first device's self-admission in its manifest; when the merged manifest names yours,
the estate reads it and nothing needs naming. When it names neither, `vericairn estate check` reports `manifest.first-authority`,
with no device's admission counting: name your first device's self-admission with `vericairn estate first-authority`, after
reading its evidence ([the command](cli.md#vericairn-estate-first-authority-093)).

When both enrollments completed, each clone's first device also signed its own adoption checkpoint, so each self-admission is
witnessed first by history read with it as the first authority. With the manifest naming one, the copy reads
`history.root-ambiguous` and no record counts; with it naming neither, `estate first-authority` refuses, since witnessed history
speaks to the first authority. Follow the refusal's guidance instead: compare the copy with a head known elsewhere
(`vericairn estate check --expect <checkpoint>`) and remove the records that do not belong, the other clone's self-admission and
the history checkpoints its device signed.

Either way, every other self-admission still reads `signature.self-admission` until you remove its record file yourself, and the
device whose self-admission you removed is then admitted from your first device, as any later device is:

```sh
vericairn devices admit <desktop-id> --by <operator-id> --note "The operator's desktop."
```
