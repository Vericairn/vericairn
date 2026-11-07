# Remote

Remote is Vericairn's phone companion for iOS and Android: a client of an estate's runtime on your local network, never a host
of one and never a separate estate. It uses the same presentation, state language and SDK as Workbench.

This guide describes Remote's pairing, attention, work, decision and policy views (REMOTE-01 to REMOTE-03 and POLICY-05, 0.9.0)
and the presence key that alone signs a decision on the phone (REMOTE-11). It is a **developer build**: it is not in an app
store, and no real-device qualification is recorded for it.

## What this build does

- **First use** explains the companion role and pairs the phone with an estate you already run.
- **Estates:** the phone keeps a connection to each estate it paired with; you choose which one the home shows, and pair with
  another from the home.
- **Home**, for the selected estate, with its connection state (connecting, current, cached, partial, denied or unavailable) and
  how fresh the content is:
  - **Attention:** what waits on the signed-in principal, from status and required communication attention, most urgent first.
    Each notice shows its subject, reason, work state, attention state, freshness and next action. Policy conditions and recurring
    duties retain their policy, rule, subject, level, owner and evaluation basis; each duty occurrence remains separate. From
    0.9.2, below the notices, the stops of the estate's runs: each step that waits on a person, why, on whom and how it reaches
    them, and for the operator's next reserved step its exact command, as status gives them. Remote carries none of them out;
    a reserved act stays the operator's ([stops](operator-guide.md#stops-and-their-surfaces-092)).
  - **Work:** work orders that are not accepted or retired, and assignments that are not accepted. Open either to inspect
    the goal, admitted scope, attempts, results, runs and requested progress; **Back to work** returns to the list.
  - **Products:** the estate's products.
  - **Nodes:** the runtime this phone reaches: its identity, address, who the phone acts as, the phone's device and the last
    time a check was current. A healthy connection here says nothing about workers running on other machines.
  - **Handoffs:** policy and duty context can be prepared from their Attention detail. Desktop task delivery and opening remain
    in Workbench; preparing a package does not establish that a desktop opened it.

Every area, input and command has a screen-reader name; states are words, never colour alone; long text wraps rather
than truncating; and text follows the phone's text-size setting (Dynamic Type on iOS, font size on Android) as Remote
starts, so a change applies the next time it opens.

Select an attention item and choose **Open subject**. A work order, assignment, attempt, result or agent run opens its work
in the Work area. An ask addressed to you, a result awaiting review or a release candidate also opens its decision review.
For subjects Remote cannot display completely, it explains why and directs you to the desktop.

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
freshness and a preview using the same presentation as Workbench. These settings remain read-only in this step;
clarifications to running attempts arrive with PROMPT-02, planned for 0.9.2.

## Decisions and acceptances

The review identifies the estate, runtime, person and phone device, followed by the exact subject revision and hash.
For an ask it shows the recommendation first, the question, every option, the consequence of leaving it unanswered, and
the records it names with their hashes. No option is selected automatically. Choosing an option copies its exact words;
you can also write your own answer. The statement stays verbatim. The proposed record shows every field before submission,
including the answer's summary, synthesis and effect, which use those same words without adding an interpretation.

Record format 3 pairs an exact ask answer with a document change set. This phone flow supports answers with **no design
document changes**, on the basis the runtime supplies: it reads the estate repository's head commit and the design documents
changed since it (`GET /v1/decisions/basis`), and the review shows that head under **Estate basis**. The answer's change set
uses that head as its base and head and pins no documents; you type no commit, and the phone never substitutes the Vericairn
software pin for it. When the runtime cannot supply the basis (no repository it can observe, or an earlier runtime), or design
documents changed since the head, the review says so and the answer is decided on the desktop, which pins the documents. The
runtime checks the stated basis again when it prepares the record and when it writes it, so an answer prepared before the
head moved is refused rather than recorded on a stale basis. The answer pins the exact ask id, hash and answer time.

A result or candidate review includes its complete recorded fields, reviews of that exact subject, verification records
and evidence summaries. Acceptance pins that subject's exact revision and hash; its basis names the displayed reviews of
the same revision, and its self-attribution follows who authored the subject. An acceptance records your judgment. It does
not publish a release or establish another external effect.

Signing is available only to an operator reviewing a currently waiting ask or an unaccepted result or candidate, with all
material accessible here. Referenced records are read in full. Hashed evidence must be available as verified, readable text;
missing evidence, binary material, desktop document readers and material exceeding the reader's capacity lead to the
desktop. Nothing is truncated to make a decision eligible. The runtime still judges every write and the device's current
authority.

Confirm review of the material and proposed record, then choose **Sign and submit**. Remote reads the subject, material and
basis again, asks the runtime to prepare the exact record, checks that it matches what you reviewed, and rechecks the current
material immediately before the phone's **presence key** signs its digest. The presence key asks for Face ID, Touch ID, a
fingerprint or the device passcode at that signature; the phone reads the material once more after you confirm, keeps the
record's identity, and only then sends the signature. A change at either check leaves your words and original material as a
draft beside the new revision; another review is required. A declined or failed presence prompt signs and sends nothing, and
your words stay a draft for another review.

Only the presence key signs a decision or an acceptance. The phone's request key, which signs every request without asking,
never does. A phone without a presence key for the estate cannot sign a decision: the review says so and offers **Add presence
key**, described under [Pairing a phone](#pairing-a-phone). The runtime records the presence key's signature, and its local-network
write ledger names the request device that sent it.

**Defer** acknowledges only the original notice's attention state. It answers or accepts nothing. **Request changes** explains
the desktop route; this phone flow has no suitable change-request record and sends no substitute. Preparing handoffs for these
decision drafts remains a later step; the draft stays on this phone.

## Drafts and observed outcomes

Input is kept in app storage with its original material and target, separately for each connection, estate, runtime identity,
principal and device. Attention lists **Local drafts and original submissions** so you can reopen them. Offline input is a
draft only: no prepare, signature or submission is queued. Reconnecting refreshes the current facts and requires a new review;
it never submits the draft automatically. Input and material confirmations are separate, and a confirmation is never restored
after restart or a target switch. A storage failure remains visible and blocks signing.

Before signing, Remote durably retains the prepared record's original id, hash, request and reviewed material. Repeated taps
cannot create another submission, and an attempted original stays blocked from resubmission across app restarts. A receipt
is retained before any follow-up read. **Accepted for processing** means a receipt was returned; **completed** means the exact
record and this phone's presence-key signature were read back. **Rejected** reports a runtime refusal. A lost response, a contradictory
read or a runtime recovery problem remains **uncertain**. **Reconcile** reads the original record, never sends it again;
an absent record is not permission to retry. These outcomes concern the recorded answer or acceptance only.

An uncertain original also blocks a new decision on a later revision of the same subject. Reconcile that original first.
After completion or an observed refusal, a later revision can open a separate blank draft; the earlier identity and receipt
remain in the retained list. A new revision still needs its own words and complete review.

Draft and submission storage is separate from cached observations. Clearing observations does not delete drafts, change estate
records or cancel any remote effect. Preserve uncertain originals for reconciliation, including when app storage needs recovery.

## Policy conditions and recurring duties

Policy attention uses the runtime's existing evaluation: failed checks, information and release blocks, including due duties
within grace, missed occurrences, late completions, exceptions and unavailable sources. **Due within grace** says the work is
incomplete even though the policy has not yet been breached. An exception retains the underlying unmet verdict and missed
history. A late completion retains the deadline it missed. Unavailable inputs remain unavailable, including an incomplete
evaluation; an empty count is never substituted for missing evidence.

Each condition names its basis digest and **as of recorded time**. This time can precede the phone's observation time and today's
deadline. Opening or refreshing the view neither advances that evaluation clock nor creates a record. The phone shows the
observation's age separately.

Choose **Open subject** to inspect the exact original condition beside a freshly read policy, declarations and exception
history. The detail retains occurrence identity, due and grace deadlines, completed and missed flags, exact result and proof
references, and every completion interval, including a withdrawn qualification. Operator preparation and linked asks remain
with their original finding, owner and basis. Acknowledgment neither completes the work nor removes its debt or release hold.
It lasts while the condition is unchanged: a newer basis from unrelated records keeps it, while a changed verdict,
disposition, evidence, exception, completion or linked ask returns the notice as unacknowledged.

Policy inspection offers **Refresh policy** and **Prepare desktop handoff**. Both recheck the connection and read current policy
authority and basis. The handoff reads the existing complete context package, then checks the policy basis and authority again;
if either changed during preparation, that package is discarded and another refresh is needed. The original subject, hash,
rule and occurrence stay explicit beside any newer history. The desktop route identifies the estate and runtime, exact policy
and subject, intended review, and why the full sources and proofs need desktop readers. Its package includes the runtime's
original material and reports any refusal.

This policy flow offers no phone approval, policy change, duty completion or record submission. Continue on the desktop and
refresh its authority and basis before reviewing or acting. Preparing context transfers no grant, records no acceptance and
does not claim a desktop opening or downstream effect. General decision flows described above retain their existing scope.
Use an updated runtime with POLICY-05 to receive the typed policy attention; older attention responses do not establish that
there are no policy conditions.

## Cached observations

The app keeps the last successful home observation and each work detail you opened in its own storage, separately for each
connection, estate, runtime identity, principal and device. The cache includes attention, work, products and communication
settings, policy attention, opened policy histories and prepared policy context. It contains no pairing code or signing key.
Clearing the app's cached observations removes those local copies; it changes no record, acknowledgment or running work on the runtime.

When the runtime cannot be reached, the retained view is labeled **cached**, with its observation time and age. Its age continues
to advance without sending requests. A detail that was never opened says it was not cached. Restored observations start cached;
opening the app checks the selected estate again. A refreshed home does not renew a previously opened work or policy detail's timestamp.
If app storage cannot retain an observation, Remote keeps that limitation visible; restarting cannot restore unsaved content.

Acknowledgment is disabled while offline and is never queued. Opening a cached detail and advancing its displayed age send
nothing. **Refresh** explicitly checks whether the runtime can be reached again. If an acknowledgment loses its response,
Remote says it was not confirmed and asks you to refresh its state; it never silently resends it. An unavailable observation says
nothing about whether work or attention exists.

Offline policy inspection retains the original basis, exact subjects, history and age. A policy history that was never opened
says it was not observed. Policy refresh and handoff preparation are disabled offline; no request or approval is queued. A cached
package and cached action list convey no current authority.

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

   The code has 16 characters, is valid for ten minutes, and admits one phone in at most five attempts: its request device and,
   where the phone can require presence, its presence device.

On the phone, enter the runtime's address, the code and a name for the phone, then choose **Pair**. iOS asks once whether
Remote may use the local network. The runtime proves its identity and its knowledge of the code; the phone creates its request
key and, where its custody can require presence, its presence key, and the phone asks for your presence once, so the presence
key can sign its own device record. The runtime records both devices for the same person and profiles, and admits both under
the authority of the administrator who issued the code; the presence device's record states presence required, and its admission
names the request device it pairs with. If the runtime refuses either device, it records neither. If the phone cannot make the
presence key (an iPhone without a passcode, an Android phone without a secure lock screen) or you decline the prompt, the phone
pairs with its request key alone and can add the presence key later. The connection pins the runtime's identity.

A phone paired before it had a presence key, or one whose runtime did not offer one, adds it later: open a decision, and under
**Add presence key** enter a new code the administrator issued for the same person with the same command. The runtime must
prove the same identity and name the same estate and person before the phone makes the key; it then records and admits the
presence key as one more device of yours, under that code's authority. A code for anyone else is refused, and nothing is
recorded.

## Keys and custody

A paired phone holds two keys, each recorded as its own device of the same person. Neither leaves the phone, and each device
record says how its key is held and whether it needs its owner's presence:

| Where | Request key | Presence key |
| --- | --- | --- |
| iPhone or iPad | `ios-secure-enclave`, presence `none`: generated in the Secure Enclave, usable only on that device while it is unlocked, never exported or backed up. | `ios-secure-enclave`, presence `required`: generated in the Secure Enclave with user presence, so every signature needs Face ID, Touch ID or the device passcode; it exists only while the device has a passcode. |
| Android 11 or later | `android-keystore`, presence `none`: generated in Android Keystore, never exported, hardware-backed where the device provides it. | `android-keystore`, presence `required`: generated with per-use authentication, so every signature needs a strong biometric or the device credential; it exists only while the device has a secure lock screen. |
| Android 8 to 10 | As above. | None: per-use authentication with the device credential needs Android 11, so the phone signs no decision. |
| iOS simulator | `file-owner-only`, presence `none`: the simulator has no Secure Enclave, so the key is an owner-only file in the app's storage. | None: an owner-only file cannot require presence, so a simulator build claims none and signs no decision. |

The request key signs every request to the runtime, and its connection certificates, without asking for anything. The presence
key signs only records, and only after you confirm your presence; the phone's keystore enforces that, not Remote. Exactly the
presence keys of iPhones and iPads and of Android 11 or later phones enforce presence; the request keys, the simulator and older
Android phones do not, and their records say so. A presence key's device record is the phone's own statement, which the runtime
cannot verify: presence on a real iPhone or Android phone is recorded as qualified only when a platform qualification observes
it on a device. Simulator and CI builds observe no prompt.

Remote's connections are excluded from backups: a replacement phone pairs again instead of inheriting access. To retire a lost
phone, revoke both of its devices with `vericairn devices revoke <device>`. That erases no estate history. Enrolling a new
fingerprint or face on Android can invalidate the presence key, after which it signs nothing and the review keeps your words as
a draft; pair the phone again and revoke its old devices. Rotating a phone's key is not built yet; pair the phone again and
revoke the old devices.

## Building from source

The phone heads are in `src/Vericairn.Mobile.slnx`, not in `Vericairn.slnx`, because each needs its platform's workload:

- iOS: the .NET `ios` workload (workload set `10.0.401`) and exactly Xcode 26.6 on macOS. Later sets of the band carry the iOS
  27.1 bindings, which Avalonia.iOS does not build against yet.
  `dotnet build src/Vericairn.Remote.iOS/Vericairn.Remote.iOS.csproj` builds for the Apple-silicon simulator.
- Android: the .NET `android` workload (workload set `10.0.403`), an Android SDK and JDK 17.
  `dotnet build src/Vericairn.Remote.Android/Vericairn.Remote.Android.csproj -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk>`.

Continuous integration builds both heads on every change: iOS on macOS, Android on Linux. Installing on a real iPhone needs
your own Apple signing; Remote is not distributed through the App Store or Google Play.

## Limits

- No real-device qualification is recorded; simulator and CI builds prove neither, including the presence prompt on iPhone and
  Android.
- Remote reaches a runtime only on the same local network; the relay that would reach it from elsewhere is a design.
- The shared libraries are not trim-safe yet, so the phone apps trim only the platform's own assemblies.
