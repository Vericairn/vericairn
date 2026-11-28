# REMOTE-06: A paired device can reach its runtime through a relay

## docs/operator-guide.md

### With the runtime's local-network configuration

> From 0.9.5, `vericairn runtime configure --relay <https-url>` sets the relay used at the runtime's next start. Use `--relay off` to stop registering at the next start. Pair on the local network. A paired device permitted the relay profile can refresh its ticket over an authenticated runtime connection.
>
> Each release ships `vericairn-relay` per platform beside the command line (`vericairn-relay-<version>-<platform>.tar.gz`, or `.zip` on Windows), listed in `SHA256SUMS`. Run it with an HTTPS Kestrel endpoint and a certificate clients trust for the relay's host. The program forwards the bytes of a separate authenticated TLS connection between the device and its runtime. A relay address is the hash of the runtime's registration key, so only that key's holder can register it; a runtime whose key changes moves to the new address, and its next ticket carries it. The relay bounds open connections (4,096, and 256 per client) and registrations (256, and 32 per client) in total and per client, counting an IPv6 client by its /64 and every client behind one NAT address as one. Each registration keeps up to 16,384 revocations, about 4 MB at most with its connections, so the registration total bounds the relay's memory (about 1 GB at the default); set `Relay:Registrations` and `Relay:RegistrationsPerClient` on the command line (`--Relay:RegistrationsPerClient=128`) or in the environment (`Relay__RegistrationsPerClient=128`) to change them, for example on a host whose many estates register from one address. Joins whose ticket does not verify draw on a budget of their own for each address in each ten-second window: past 32 from one client, or past 256 in total for a client that has failed at least once, that client is refused before any signature check until the window ends. A client that has not failed in the window is always verified, so a flood from other clients never closes an address to it. The relay allows a runtime clock up to five seconds ahead of its own.

## docs/limitations-and-platforms.md

### With remote access from other networks

> The relay sees relay addresses derived from registration keys, device identifiers from tickets, connection times and traffic volumes. Device and runtime authenticate each other inside the relayed stream. A relay outage leaves the runtime's configured local-network listener available.

## docs/api-v1.md

### With the daemon-only pairing routes

> From 0.9.5, a device authenticates a `GET /v1/relay/ticket` to obtain a fresh ticket for its own admitted identity. A device that understands relay tickets can request one in the pairing hello. The pairing completion authenticates the returned relay URL and first ticket with its existing transcript MAC. Tickets expire in at most 24 hours. Relayed operations require the relay profile and use the same signed requests, principal checks and machine-local refusals as local-network operations.
