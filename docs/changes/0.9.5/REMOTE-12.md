# REMOTE-12: Verify an Android presence key during pairing

## docs/operator-guide.md

### In "3. Principals, devices and admission", replace the passage from "From 0.9.0 a phone paired with Remote" through "the phone's request key states presence none."

> A paired phone's request key has presence `none` and signs authorized requests without a prompt. From 0.9.5, the runtime
> recognizes an Android presence device as `required` when it can verify the key's retained attestation and certificate status.
> Earlier Android pairings and iPhone or iPad presence keys without such evidence are `declared`, as is any device a pairing
> admitted that states another presence custody, since a pairing before 0.9.5 took that custody on the device's word. Those phones retain request
> access, but Remote's separate presence signature for a decision, acceptance or reviewed act is refused. Use Workbench or
> the desktop CLI for those acts.
>
> To replace an older Android pairing, update Remote and run
> `vericairn devices pair --principal <principal> --by <administrator>` on the runtime for the same principal. On the Remote
> home screen, choose **Add**, enter the same address and the new code, and pair. On the runtime, use `vericairn devices list`
> to confirm the new presence device is `required`, then revoke both old device ids. A phone with only a request key can
> instead use **Add presence key** inside a decision with a new code. Remote 0.9.0 through 0.9.4 can still pair its request
> key with a 0.9.5 runtime. See [Remote pairing and custody](remote.md#pairing-a-phone).

### In "Answer open asks with their recommendations in one act (0.9.4)", after the paragraph beginning "With presence sessions on, the act lists up to 16 decisions"

> This action needs a presence device accepted by the runtime. For a phone whose presence device is `declared`, review
> and sign on the desktop. See [Remote custody](remote.md#keys-and-custody).

### In "Sign every ready item in one reviewed act (0.9.4)", after the paragraph beginning "With presence sessions on, the act asks one presence check"

> This action needs a presence device accepted by the runtime. A phone whose presence device is `declared` uses the
> desktop route. See [Remote custody](remote.md#keys-and-custody).

## docs/limitations-and-platforms.md

### In "Pairing and the local-network profile", after the bullet beginning "Pairing codes contain"

> From 0.9.5 (REMOTE-12), Android presence pairing requires a valid chain anchored at one of Google's pinned attestation
> public keys, fresh certificate status, a matching key and exchange challenge, hardware-enforced per-use authentication,
> a locked bootloader, verified boot and accepted app identity and patch metadata. Failure refuses the pairing. The signed
> admission keeps the proof for rechecking when the runtime lists devices or receives a signature from a device other than
> the requester. The runtime retrieves Google's status list without a phone or estate identifier. iOS App Attest can
> establish an app's integrity. Its proof does not cover the access controls of the separate Secure Enclave signing key.
> iOS devices and earlier phone presence keys without verification are reported as `declared`. Complete reviewed writes
> on the desktop when your phone holds only a declared presence key. See [Remote custody](remote.md#keys-and-custody).

## docs/api-v1.md

### In "Records signed with presence (REMOTE-11)", replace "and states presence `required`" with

> and is reported as presence `required` by the runtime

### In "Records signed with presence (REMOTE-11)", after the paragraph beginning "From 0.9.0 a phone signs its decisions"

> From 0.9.5 (REMOTE-12), the test for `required` uses the runtime's rechecked presence result. For an Android presence key
> the runtime matches the original device's exact signed admission and verifies the retained `androidAttestation` evidence
> against the device's current public key and the recorded challenge. It validates the historical chain at admission time and checks
> revocation against a fresh Google status list before accepting an alternate device signature and when reporting presence
> through `GET /v1/devices`. The status response may be cached for up to one day, subject to the freshness Google advertises.
> Missing, unverifiable or revoked proof, or an unavailable fresh list, leaves the device `declared` and ineligible as an
> alternate signer. A device still may sign its own prepared write under the first route above if that write is otherwise
> authorized. Remote uses the alternate-signer route for its decisions, acceptances and reviewed acts.

### In "Pairing", at the end of step 3 and in place of the sentence in step 4 beginning "When the hello asked for a presence device"

> In step 3, a client that supports the 0.9.5 presence rules adds optional `presenceAttestation: true` to its hello.
>
> In step 4, a runtime that supports those rules replies with `presenceAttestation: true` when the client announced it
> and the record format is at least 2. A 0.9.5 runtime offers `presenceRecord` only when the hello sets both capability
> flags and the principal and format allow a presence device. Clients from 0.9.0 through 0.9.4 receive a request-only
> exchange even when their hello asks for a presence device.

### In "Pairing", replace the passage in step 5 beginning "When the challenge named a presence record" through "the device pairs alone."

> When the challenge names a presence record and the client has a presence store, it makes that key and signs its own
> device record, prompting the owner once. It adds `presenceDevice` and `presenceSignature` to the confirmation. With
> `presenceAttestation: true` in the challenge, an Android client generates the key against the decoded runtime nonce
> and sends `presence: required` and `androidAttestation`, the leaf-first array of base64url DER certificates on the
> presence device. Its signed device body also contains `presenceVerification: android-key-attestation`. An iOS client
> sends `presence: declared` and omits the certificate array. If the presence key cannot be made or the owner declines
> the prompt, the client removes that key and pairs the request key alone.

### In "Pairing", replace the passage in step 6 beginning "With a presence device" through "names the issuing device and authority."

> With a presence device, the runtime checks both device signatures and refuses reuse of the request key as the presence
> key. An Android presence claim must pass attestation verification before the runtime writes either device. The runtime
> verifies the path against the pinned Google attestation roots by public key, checks certificate status, matches the leaf
> to the new key and exchange nonce and checks the per-use hardware authorizations, boot state, app identity and patch
> metadata. For that key, the administrator's signed admission pins the exact device record and carries structured
> `androidAttestation` evidence. Ordinary device admission cannot supply the evidence. An iOS presence device is
> recorded as `declared`. The runtime validates both devices and their admissions together before storing them. A refused
> presence device records neither device. The admission identifies the code's issuing device and authority.

### In "Pairing", replace the passage in step 7 beginning "A kept connection without a presence device" through "than the connection's."

> A kept connection without a presence device adds one with a new code for the same principal
> (`RuntimeConnections.AddPresenceAsync`). This is a single-device exchange, with the same attestation or declaration
> rules for the presence key. An offer for another runtime identity, estate or principal is refused before key creation.

### In "Pairing", after the transcript sentence ending "keeps the transcript of earlier releases."

> A true challenge capability appends a `presence-attestation` label to the challenge transcript. A confirmation
> carrying an Android chain appends the `android-attestation` label followed by the certificate arrays for the primary
> and secondary device, with an empty array for a device without attestation. The new labels are omitted from exchanges
> with clients that do not use this capability.

## docs/api-compatibility-0.9.5.md

### Under the 0.9.5 changes

> REMOTE-12 adds optional `presenceAttestation` to `PairingHello` and `PairingChallenge`, and `androidAttestation` to
> `PairingDevice`. `IAttestedDeviceKeyStore` supplies the platform evidence for a newly generated key. The runtime offers
> only a request key to a 0.9.0–0.9.4 client, even when its hello asks for a presence device. Update the client to pair a new
> presence key. Device lists may report `declared`. In record formats 2 and 3, `presence: declared`,
> `presenceVerification: android-key-attestation` and an admission's `androidAttestation` evidence are understood by readers
> from 0.9.5. Earlier readers refuse records carrying these additions. Upgrade readers before pairing devices that use them.
