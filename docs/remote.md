# Remote

Remote is Vericairn's phone companion for iOS and Android: a client of an estate's runtime on your local network, never a host
of one and never a separate estate. It uses the same presentation, state language and SDK as Workbench.

This guide describes Remote's pairing, attention, work, decision and policy views, added in 0.9.0 with REMOTE-01 to REMOTE-03
and POLICY-05, with the estate's history line and the stops of its runs from 0.9.2, the presence key that alone signs a
decision on the phone (REMOTE-11), from 0.9.4 answering asks with their recommendations in one act (OPS-16), and signing every ready item in one reviewed act (OPS-24). It is a **developer build**: it is not in an app
store, and no real-device qualification is recorded for it. From 0.9.5 (REMOTE-04), Remote can request interventions, inspect
worker observations and prepare desktop tasks for Workbench.

## What this build does

- **First use** explains the companion role and pairs the phone with an estate you already run.
- **Estates:** the phone keeps a connection to each estate it paired with; you choose which one the home shows, and pair with
  another from the home.
- **Home**, for the selected estate, with its connection state (connecting, current, cached, partial, denied or unavailable) and
  how fresh the content is, and, from 0.9.2, its history line in the words `status` prints: whether the estate has adopted history
  checkpoints, its heads by the sixteen hexadecimal digits a fresh machine names to `estate check --expect`, how many records they
  witness, the unwitnessed tail and the standing problems ([history checkpoints](operator-guide.md#history-checkpoints-092)):
  - **Attention:** what waits on the signed-in principal, from status and required communication attention, most urgent first.
    Each notice shows its subject, reason, work state, attention state, freshness and next action. Policy conditions and recurring
    duties retain their policy, rule, subject, level, owner and evaluation basis; each duty occurrence remains separate.
    From 0.9.2, below the notices, the stops of the estate's runs: each step that waits on a person, why, on whom and how it reaches
    them, the operator's next reserved step among them, as canonical status gives them, as of the estate's newest record and
    without its command, which a live read shows. Remote carries none of them out;
    a reserved act stays the operator's ([stops](operator-guide.md#stops-and-their-surfaces-092)).
    From 0.9.4 (OPS-22), below them, the possible todos: the optional operator requests status shows in their own section, each
    with its recommendation, destination, guide and the commands that fetch its kit and close it on a machine with the estate; they
    hold nothing, and Remote closes none of them in this build.
    From 0.9.4, below them, the due times of the runs' timed steps, each step with when it is due and why, its timing, zone,
    deadline and a missed-time rule it took, as canonical status gives them, before the step runs; Remote starts none of them.
    From 0.9.4 (PROC-19), below them, the decisions of the runs' decision steps: who decided each path, a rule, its default, a
    deadline, an agent or a person, when and why, with the evidence or the records a rule read by hash, or that a step waits on
    its decider, as canonical status gives them. A person decides with `vericairn goal decide` through the runtime that serves the
    estate; Remote records no decision of a run's step in this build.
    From 0.9.3, a work order proposed for a goal whose plan says `sponsor-approves` is a decision needed for the goal's
    sponsor, waiting for the sponsor, with the command that approves it; Remote approves nothing
    ([goals](operator-guide.md#17-goals-predicates-and-evaluation)).
  - **Work:** work orders that are not accepted or retired, and assignments that are not accepted. Open either to inspect
    the goal, admitted scope, attempts, results, runs and requested progress; **Back to work** returns to the list.
  - **Products:** the estate's products, with the option to prepare a desktop task for the selected revision.
  - **Nodes:** the runtime's direct connection, identity, address, current access and last successful check. **Check workers**
    reads how that runtime sees each in-flight run and shows the last heartbeat when available.
  - **Handoffs:** desktop tasks prepared for the acting person, with separate observations of preparation, Workbench opening
    and subject state.

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
clarifications to running attempts arrive with PROMPT-02, planned for 0.9.5.

## Interventions and nodes

In **Work**, open an order or assignment with a run, expand **Intervene on a run** and select the run. Review the precise target,
operation and expected effect. **Pause** stops new dispatch for the run's active goal. In-flight work continues and an
authorized person can resume the goal. **Cancel** asks the run's worker to terminate it with the runtime's configured grace
period. **Containment** requests the same run's launch boundary with no added grace before escalation. You can review
containment of a run whose earlier Remote cancel is still in progress. The review pins that earlier cancel.

**Request reviewed action** reads the target again. The runtime prepares the exact record and checks the reviewed active
goal status or run operation revision when the phone submits its signature. The phone signs with your admitted presence
key. If signing or submission is refused, the app explains why. Inspect the current state and use **Continue in Workbench**
to prepare a desktop handoff if the intervention is still needed.

The original request stays on this phone, including after restart. The result shows the pause record or the observed request,
transmission, harness acknowledgment, escalation and containment facts. If a request is refused, failed or superseded,
read the current state and review a new request. If its outcome is pending or uncertain, select the original under
**Intervention requests** and choose **Reconcile request** to read what the runtime has observed.

In **Nodes**, **Reconnect** checks the pinned runtime identity and this phone's current access. **Check workers** reads a
separately dated view of the work in flight, with every heartbeat and stop observation that runtime can supply. A current
connection and an unavailable worker observation can appear together. Cached worker facts keep the time they were read.

## Desktop tasks

From a work detail, decision or product choose **Continue in Workbench**. From a policy condition choose **Send task to
Workbench**. Review the exact subject and edit the next task, reason and optional draft. **Prepare for Workbench** retains
that context and its evidence references for your principal on the paired runtime. An offline draft is never queued. If the
response is lost, **Refresh handoffs** checks for the original task. After going Back or restarting Remote, composing for
the same subject restores the original task, draft and request identity. **Check original preparation** uses that identity.

In Workbench on the serving runtime's machine, use **Hand-over and resume** as the same principal and open the task from
**From Remote**. Check the pinned revision and evidence, plus any missing originals or newer subject state, before
continuing. On the phone, **Refresh handoffs** reads the opening time and the independently observed subject state. The
prepared context grants no authority. The runtime records your admitted phone's device identity when it prepares the task.
Workbench uses your selected admitted device to record the first opening on the runtime's machine. For tasks prepared
before the runtime recorded device origin, the preparing device is shown as unavailable. A task expires after 30 days, and the
runtime keeps up to 32 tasks per person. **Remove selected handoff** in Workbench removes the retained task and its
opening information.

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

Remote prepares these records with the phone's request key and submits them with its separate presence key. From 0.9.5,
the runtime accepts that second signature only from a device whose presence requirement it recognizes as `required`.
The recorded signature names the presence device, and the local-network write ledger names the request device that sent it.
See [Keys and custody](#keys-and-custody) for what an older pairing or an iPhone can still do. A phone without a presence key
can use **Add presence key** as described under [Pairing a phone](#pairing-a-phone).

**Defer** acknowledges only the original notice's attention state. It answers or accepts nothing. **Request changes** explains
the desktop route; this phone flow has no suitable change-request record and sends no substitute. Preparing handoffs for these
decision drafts remains a later step; the draft stays on this phone.

## Answering asks with their recommendations (0.9.4)

From 0.9.4 (OPS-16), an operator answers the asks waiting on them with their recommendations in one act from the phone. In
Attention, open **Answer asks with their recommendations** and choose **Read the asks waiting on me**: each ask is listed by its
exact revision and hash with its recommendation's SHA-256 and recommendation first, then the question and what happens while it
stays unanswered. Every ask starts selected; clear any you do not answer this way. Choose each decision's stake, read the act as
it will be signed, confirm that you reviewed it, then choose **Sign the act**. Reading answers nothing.

Before the first signature Remote reads the asks again, and the act refuses whole, signing and sending nothing, if any selected
ask or recommendation changed or no longer waits on you. Each ask then gets its own decision naming the act, with `decidedAt`
the act's time, signed by the phone's presence key, which asks for Face ID, Touch ID, a fingerprint or the device passcode at
each signature, as every phone signature does: a phone keeps one presence check per decision. Each decision is prepared by the
runtime, checked against the request and the ask as shown immediately before the presence key asks and again before its
signature leaves, and written as its own record; the runtime checks the ask and recommendation again when it writes. A change
during the act stops it before the next signature: the decisions already written stand as answers of the act, the message says
how many of how many were written, and an ask once answered is never answered again, so nothing is answered twice. Each decision's
`act.count`, and its synthesis, say how many asks the act set out to answer, the ones you chose before its first signature; after a
stop, fewer decisions name the act than its count, and those that do are the ones written. The act's
outcome is shown until the app closes; unlike a single decision's draft it is not kept in app storage, and the asks the act
answered no longer wait on you. The act uses the same presence device and runtime verification as a single decision.

## Ready items in one reviewed act (0.9.4)

From 0.9.4 (OPS-24), Remote offers the same reviewed list as `vericairn operator act --ready`. In Attention, open **Ready items:
one reviewed act** and choose **Read my ready items**: every operator item whose command is ready for you is listed with what its
signature records, its exact subject and basis, an acceptance's reviews (who reviewed, the outcome, the independence and the exact
hash reviewed), and an ask's recommendation by hash. An answer or a request starts chosen to sign; an acceptance starts waiting until
you choose **sign**; choose **wait** to leave an item waiting, or, for a request, **dismiss** with your reason. Read the act as it will be signed, confirm it, then choose **Sign
the act**. Reading signs nothing.

Before the first signature Remote reads the list again, and the act refuses whole, signing and sending nothing, if any item changed
or the list changed. Each chosen record is the one the runtime's act would sign, prepared by the runtime and signed by the phone's
presence key, one presence check each, as every phone signature is; it is checked against the item as shown immediately before the
presence key asks and again before its signature leaves, and the runtime checks it against the estate when it writes. A change during
the act stops it before the next signature: the records already written stand, the message says how many of how many were written,
and an item already signed is no longer ready, so nothing is signed twice. Nothing outside the reviewed list is signed. As with
answers, the act's outcome is shown until the app closes and is not kept in app storage.

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

Policy inspection offers **Refresh policy** and **Read context package**. Both recheck the connection and read current policy
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
the authority of the administrator who issued the code. The presence device's record says whether the runtime verified the
requirement, and its admission names the request device it pairs with. If the runtime refuses either device, it records neither.
If the phone cannot make the presence key (an iPhone without a passcode, an Android phone without a secure lock screen) or you
decline the prompt, the phone pairs with its request key alone. A 0.9.5 runtime offers Remote 0.9.0 through 0.9.4 only a request
key, even if the phone has a secure lock screen. The connection pins the runtime's identity.

From 0.9.5 (FIX-164), Remote pairs on Android. Before, it closed when **Pair** was tapped: .NET's TLS on Android ends the
process when it is given the local network's list of cipher suites, so an Android phone now offers none, the runtime chooses
from its own list, and the phone refuses any other suite before it sends any request. Neither of the phone's keys, the request
key or the presence key, could sign on Android either, which would have ended every pairing that got further. A pairing that
fails says why on the form, as does a code that is not one; an error Remote does not expect is shown on the form, or under the
connection line on the home, and Remote stays open.

A phone paired without a presence key can add one with an updated client. Open a decision and under **Add presence key**
enter a new code the administrator issued for the same person with the same command. The runtime must
prove the same identity and name the same estate and person before the phone makes the key; it then records and admits the
presence key as one more device of yours, under that code's authority. A code for anyone else is refused, and nothing is
recorded.

## Keys and custody

A paired phone holds a request key and, where supported, a presence key. Each key stays on the phone, with its own device
record for the same person describing custody and presence.

| Where | Request key | Presence key |
| --- | --- | --- |
| iPhone or iPad | `ios-secure-enclave`, presence `none`. The key stays on its device and is available while it is unlocked. | `ios-secure-enclave`, presence `declared`. Remote configures Face ID, Touch ID or the device passcode for every signature. The runtime cannot verify those access controls. |
| Android 11 or later | `android-keystore`, presence `none`. The key stays in Android Keystore, hardware-backed where the device provides it. | `android-keystore`, presence `required` and `presenceVerification: android-key-attestation` after verification. The hardware requires an authentication token for each signature. |
| Android 8 to 10 | As above. | None: per-use authentication with the device credential needs Android 11, so the phone signs no decision. |
| iOS simulator | `file-owner-only`, presence `none`: the simulator has no Secure Enclave, so the key is an owner-only file in the app's storage. | None: an owner-only file cannot require presence, so a simulator build claims none and signs no decision. |

The request key signs requests and connection certificates without prompting. Starting in 0.9.5 (REMOTE-12), the runtime
verifies Android presence keys during pairing. It validates the certificate chain with Google's pinned attestation root keys
and certificate status list. It matches the leaf public key and the exchange's fresh challenge, and checks for hardware-backed
per-use authentication, a locked bootloader and verified boot. It checks the `com.vericairn.remote` package name, requires
the package's signer digests and checks the OS version and patch dates reported by the platform.
A failed check refuses the pairing. The estate keeps the verification method on the device record and typed evidence,
including the certificate chain, in the administrator's signed admission. The runtime rechecks that evidence and revocation
status when it lists devices and before a different device signs a prepared record. If the runtime cannot obtain a fresh
certificate status list, it does not report or authorize verified presence. A status-list request goes from the runtime to
Google without a phone or estate identifier.

On iOS, App Attest can establish app integrity and bind data to an app assertion. Apple provides no attestation for the
per-use access controls of the separate Secure Enclave signing key Remote uses, so the runtime reports the key as `declared`.
The runtime reports previously paired Android and iOS presence keys without verification the same way, and any device a
pairing admitted that states another presence custody: before 0.9.5 a pairing took the stated custody on the device's word.
Re-enroll such a device at the desk if it needs to sign as a presence device. Both kinds of phone
can still use their admitted request keys for authorized requests. The API continues to accept a requesting device's
signature on its own prepared record when that write is otherwise authorized. Remote uses a separate presence device for
decisions, acceptances and reviewed acts, which the runtime refuses for a `declared` key. A locally held `declared` key may
still prompt you before the runtime refuses the submission. Complete the review and sign on the desktop in Workbench or
use an eligible Android pairing. Simulator and CI builds observe no phone prompt.

To replace an older Android pairing, update Remote, obtain a new pairing code for the same principal and choose **Add**
on the Remote home screen. Enter the same runtime address and the new code, then choose **Pair**. The new connection replaces
the saved connection for that estate and principal. Confirm the new presence device is listed as `required` on the runtime,
then revoke the two old device ids. Check drafts tied to the old connection before replacing it. On an iPhone or iPad,
use the desktop signing route and keep the phone's request key for continued access.

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
- Android: the .NET `android` workload (workload set `10.0.404`), an Android SDK and JDK 17.
  `dotnet build src/Vericairn.Remote.Android/Vericairn.Remote.Android.csproj -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk>`.

From 0.9.5 (CI-12), continuous integration builds both heads in the merge queue's full gate, which every change passes
before it merges, rather than on each pull request: iOS on macOS, Android on Linux. Installing on a real iPhone needs
your own Apple signing; Remote is not distributed through the App Store or Google Play.

## Limits

- No real-device qualification is recorded; simulator and CI builds prove neither, including the presence prompt on iPhone and
  Android.
- Remote reaches a runtime only on the same local network; the relay that would reach it from elsewhere is a design.
- The shared libraries are not trim-safe yet, so the phone apps trim only the platform's own assemblies.
