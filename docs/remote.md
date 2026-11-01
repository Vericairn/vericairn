# Remote

Remote is Vericairn's phone companion for iOS and Android: a client of an estate's runtime on your local network, never a host
of one and never a separate estate. It uses the same presentation, state language and SDK as Workbench.

This guide describes Remote's pairing, attention and work views (REMOTE-01 and REMOTE-02, 0.9.0). It is a **developer build**:
it is not in an app store, and no real-device qualification is recorded for it.

## What this build does

- **First use** explains the companion role and pairs the phone with an estate you already run.
- **Estates:** the phone keeps a connection to each estate it paired with; you choose which one the home shows, and pair with
  another from the home.
- **Home**, for the selected estate, with its connection state (connecting, current, cached, partial, denied or unavailable) and
  how fresh the content is:
  - **Attention:** what waits on the signed-in principal, from status and required communication attention, most urgent first.
    Each notice shows its subject, reason, work state, attention state, freshness and next action.
  - **Work:** work orders that are not accepted or retired, and assignments that are not accepted. Open either to inspect
    the goal, admitted scope, attempts, results, runs and requested progress; **Back to work** returns to the list.
  - **Products:** the estate's products.
  - **Nodes:** the runtime this phone reaches: its identity, address, who the phone acts as, the phone's device and the last
    time a check was current. A healthy connection here says nothing about workers running on other machines.
  - **Handoffs:** not in Remote yet; prepare and open them in Workbench.

Every area, input and command has a screen-reader name; states are words, never colour alone; long text wraps rather
than truncating; and text follows the phone's text-size setting (Dynamic Type on iOS, font size on Android) as Remote
starts, so a change applies the next time it opens.

Select an attention item and choose **Open subject**. A work order, assignment, attempt, result or agent run opens its work
in the Work area. For subjects Remote cannot display yet, it names Workbench or the command-line action to use.

**Acknowledge** changes only your attention state. The runtime keeps it per principal, keyed by the notice's kind and subject,
so your other devices, Workbench's attention list and human `vericairn status` show the same acknowledgment after refreshing.
It creates no record, asks for no record signature, and never resolves, hides or removes the item. The underlying state and next
action stay visible. A changed item clears its acknowledgment; a stale tap is refused and asks you to refresh. Acknowledgment
over the local network is kept in the runtime's write ledger even when the person's local signing devices require presence.
Record-authoring writes still follow their own signing and presence rules.

A work detail retains the complete admitted brief and result fields, including limitations, unresolved effects and the next
step. The latest result remains a claim to inspect. The agent's requested progress is labeled as its claim, beside the worker's
separate heartbeat and event observation. Each has its observation time and age; a fresh claim never establishes worker liveness,
and a current phone connection never establishes that another machine's worker is current.
Opening a result or run from an earlier assignment retains that assignment's exact admitted brief and work-order pin. Its
historical context is labeled, including when the current work view does not establish its acceptance state.

**Communication**, reachable from the shell, shows the person's effective settings, inheritance, source revisions, conflicts,
freshness and a preview using the same presentation as Workbench. These settings are read-only in this step. Phone-signed
changes belong to REMOTE-03; clarifications to running attempts arrive with PROMPT-02 in 0.9.1.

## Cached observations

The app keeps the last successful home observation and each work detail you opened in its own storage, separately for each
connection, estate, runtime identity, principal and device. The cache includes attention, work, products and communication
settings. It contains no pairing code or signing key. Clearing the app's cached observations removes those local copies;
it changes no record, acknowledgment or running work on the runtime.

When the runtime cannot be reached, the retained view is labeled **cached**, with its observation time and age. Its age continues
to advance without sending requests. A detail that was never opened says it was not cached. Restored observations start cached;
opening the app checks the selected estate again. A refreshed home does not renew a previously opened work detail's timestamp.
If app storage cannot retain an observation, Remote keeps that limitation visible; restarting cannot restore unsaved content.

Acknowledgment is disabled while offline and is never queued. Opening a cached detail and advancing its displayed age send
nothing. **Refresh** explicitly checks whether the runtime can be reached again. If an acknowledgment loses its response,
Remote says it was not confirmed and asks you to refresh its state; it never silently resends it. An unavailable observation says
nothing about whether work or attention exists.

## Pairing a phone

On the machine running the estate's runtime:

1. Give the runtime a local-network address that the phone can reach, then start it:

   ```sh
   vericairn runtime configure --local-network 192.168.1.20:7443
   vericairn runtime start
   ```

2. Issue a pairing code for the person who will use the phone. The operator or a principal holding the `administer` scope
   runs this:

   ```sh
   vericairn devices pair --principal <principal> --by <administrator>
   ```

   The code has 16 characters, is valid for ten minutes, and admits one device in at most five attempts.

On the phone, enter the runtime's address, the code and a name for the phone, then choose **Pair**. iOS asks once whether
Remote may use the local network. The runtime proves its identity and its knowledge of the code; the phone creates its own
key and presents it; the runtime records the device and admits it. The connection pins the runtime's identity.

## Keys and custody

The phone's device key never leaves the phone, and its device record says how the key is held:

| Where | Custody | What it means |
| --- | --- | --- |
| iPhone or iPad | `ios-secure-enclave` | Generated in the Secure Enclave; usable only on that device while it is unlocked; never exported or backed up. |
| Android | `android-keystore` | Generated in Android Keystore; never exported; hardware-backed where the device provides it. |
| iOS simulator | `file-owner-only` | The simulator has no Secure Enclave, so the key is an owner-only file in the app's storage. |

The key signs every request to the runtime without asking for Face ID, Touch ID or a passcode, so the phone's device does
not claim presence. Remote's connections are excluded from backups: a replacement phone pairs again instead of inheriting
access. To retire a lost phone, revoke its device with `vericairn devices revoke <device>`. That erases no estate history.
Rotating a phone's key is not built yet; pair the phone again and revoke the old device.

## Building from source

The phone heads are in `src/Vericairn.Mobile.slnx`, not in `Vericairn.slnx`, because each needs its platform's workload:

- iOS: the .NET `ios` workload (workload set `10.0.303.1`) and Xcode 26.5 or later on macOS.
  `dotnet build src/Vericairn.Remote.iOS/Vericairn.Remote.iOS.csproj` builds for the Apple-silicon simulator.
- Android: the .NET `android` workload (workload set `10.0.303.1`), an Android SDK and JDK 17.
  `dotnet build src/Vericairn.Remote.Android/Vericairn.Remote.Android.csproj -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk>`.

Continuous integration builds both heads on every change: iOS on macOS, Android on Linux. Installing on a real iPhone needs
your own Apple signing; Remote is not distributed through the App Store or Google Play.

## Limits

- No real-device qualification is recorded; simulator and CI builds prove neither.
- Remote reaches a runtime only on the same local network; the relay that would reach it from elsewhere is a design.
- The shared libraries are not trim-safe yet, so the phone apps trim only the platform's own assemblies.
