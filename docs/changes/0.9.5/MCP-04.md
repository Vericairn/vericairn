# MCP-04: OAuth token mode and the public endpoint

## docs/operator-guide.md

### In "An agent on another machine: network MCP (0.9.2)", before "Agents hear what changed"

> From 0.9.5, an OAuth-only client can work as an admitted agent or service over a separate HTTPS MCP listener. Configure it
> with `vericairn runtime configure --mcp on --public-endpoint <host:port> --public-certificate <pem> --public-key <pem>`,
> then restart the runtime. Supply a certificate chain the client trusts and set the Unix key file mode to `600` or `400`.
> To listen on a private overlay, name its interface's address and matching port with `--public-bind`. `runtime status` shows the endpoint and its certificate's expiry.
> Attention alerts the operator in the last 14 days. An expired or unreadable certificate takes only the public HTTPS
> endpoint out of service. Keep the renewed certificate and key at the configured paths and run
> `vericairn runtime stop` followed by `vericairn runtime start` to load them.
>
> On the runtime's machine, `vericairn mcp authorize --principal <agent> --by <administrator> --scope read|contribute` shows a
> one-time approval code valid for ten minutes. In the client's browser authorization, enter that code and confirm the exact
> client, estate, principal, scope and expiry. Tool results go to that client and any model it uses. Use `--days` for an expiry
> other than 30 days, up to 90, and `--renew <device>` to reauthorize the same admitted token device. `mcp clients` lists the
> devices and their admission standing, with the client name or identifier, scope and expiry for families held here.
> `devices revoke` ends a client's access as soon as it is recorded. If the device
> signed a record in the same second, the historical signature cutoff is set to the next whole second.

## docs/limitations-and-platforms.md

### In "Network MCP limits (0.9.2)", after the existing signed-mode limits

> The public OAuth listener is off by default. It serves HTTPS MCP and the discovery and authorization endpoints, with its own
> connection and rate limits. Its certificate and key are supplied by the operator. It supports public Client ID Metadata
> Documents and dynamic registration. Read scope has no write tools. Contribute scope offers only the network contributor writes.
> Each bearer access token lasts at most 15 minutes. A refresh token rotates on use, and reuse ends its family.
> The public listener serves up to 12 open connections, with up to four from one peer and five seconds to finish TLS.
> Peers behind the same network address share their allowance, as do IPv6 addresses in one /64 subnet. The listener keeps
> separate request allowances for MCP, the token endpoint and the other OAuth paths. It holds up to eight pending registrations
> and eight pending browser flows per peer, within shared caps of 128 registrations and 256 flows. Unused registrations lapse
> after a day, browser flows after ten minutes, and approved registrations lapse when their last authorization expires.

## docs/api-v1.md

### In "Network MCP at /mcp (0.9.2)", after the signed transport

> From 0.9.5, the public HTTPS listener serves `/mcp` under both MCP revisions, using bearer access tokens. Discovery is through
> OAuth protected-resource and authorization-server metadata. Client ID Metadata Documents are preferred, with dynamic registration
> as a fallback. Authorization uses the code flow, S256 PKCE, an exact registered redirect and resource binding at authorization
> and exchange. Success and any redirectable error carry the authorization server issuer in `iss`. The OAuth listener serves no
> `/v1` operations. On the local socket, `POST /v1/runtime/mcp/authorize` issues an approval under an administrator's authority.

## docs/api-compatibility-0.9.5.md

### In the additive API changes

> MCP-04 adds the local-socket authorization operation `POST /v1/runtime/mcp/authorize` and the optional `publicMcp` member on
> the daemon's published endpoint. The OAuth listener is a separate surface. Record format 3 gains the exclusive, unattended
> `token` device profile for an agent or service. No records carry an approval code, authorization code or bearer credential.
