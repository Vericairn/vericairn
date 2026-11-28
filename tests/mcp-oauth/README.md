# MCP-04 OAuth compatibility run

Run against the exact Release candidate on a disposable format-3 estate. Use a separate
runtime home, test administrator, test agent principal and client profile. Start with a
certificate for the endpoint's DNS name that the browser and client trust. Keep the private
key owner-only. Configure the direct public listener and start the runtime.

```sh
vericairn --estate "$TEST_ESTATE" runtime configure --mcp on \
  --public-endpoint "$PUBLIC_HOST:$PORT" --public-certificate "$CERTIFICATE" --public-key "$KEY"
vericairn --estate "$TEST_ESTATE" runtime start
```

Issue a fresh read approval on the runtime's machine for each client and each endpoint.

```sh
vericairn --estate "$TEST_ESTATE" mcp authorize --principal "$TEST_AGENT" \
  --by "$TEST_ADMINISTRATOR" --scope read
```

## Direct public clients

1. Add a remote Streamable HTTP MCP connector in ChatGPT with the public HTTPS `/mcp` URL
   and OAuth. Let it use server discovery and the client registration it supports. Follow
   the authorization link, enter the approval code and verify the client, principal and
   read scope on the consent page. Approve and check that the connector reports success.
2. Inspect the connector's discovered tools in its settings or trace. Confirm
   `estate_status` is listed and `work_attempt` is absent. Invoke `estate_status` through
   the connector and verify a successful result from this test estate in the tool trace.
3. Repeat the same three checks in a second OAuth-only client. For a Streamable HTTP
   client in Claude Code, register it with
   `claude mcp add --transport http vericairn-compat "$PUBLIC_MCP_URL"`, then select the
   server in `/mcp` and authenticate. Use the client's tool list and actual tool invocation
   to verify the result. Record the client and version used, including any unsupported
   redirect or registration behavior.
4. Remove both test connections after recording their results and revoke their devices on
   the disposable runtime.

The smoke script can confirm the public setup before either third-party run. Issue a fresh
read approval and run the script from a machine with the route and a browser.

```sh
python3 tests/mcp-oauth/mcp_oauth.py "$PUBLIC_MCP_URL" > public-smoke.json
```

## Private overlay case

Configure the same kind of listener with the overlay host name and add
`--public-bind "$OVERLAY_IP:$PORT"`, using brackets around an IPv6 bind address. Use a
certificate for the overlay host. Restart the runtime and issue a new read approval.
From another machine on the overlay, where both Python and the browser can resolve the
name and reach the interface, run the independent OAuth client.

```sh
python3 tests/mcp-oauth/mcp_oauth.py "https://$OVERLAY_HOST:$PORT/mcp" \
  --overlay-address "$OVERLAY_IP" > overlay-smoke.json
```

For an internal CA, establish browser trust and pass its PEM to the script with
`--ca-file`. The script checks the connected address on every TLS request, discovers
OAuth, dynamically registers a native loopback callback, opens the real browser, checks
the returned issuer and state, exchanges with S256, and lists and calls the read tool
under both MCP revisions. Its standard output has only the results. The third-party
client checks are separate observations. Revoke the smoke client's device when done.

## Results to record

Record the Release candidate hash, run date, OS and architecture, client name and version,
transport revision when visible, and each actual outcome. Use the first failed stage and
its HTTP status or a short description when a check fails. Copy only the observations
into the compatibility matrix. Exclude approval codes, callback URLs, bearer tokens, TLS
key paths, connection identifiers and tool payloads.

| Route | Client and version | Browser approval | Listed tools | Called `estate_status` | Notes |
| --- | --- | --- | --- | --- | --- |
| Direct public | ChatGPT connector | Pending | Pending | Pending | |
| Direct public | Second OAuth-only client | Pending | Pending | Pending | |
| Private overlay | OAuth smoke client | Pending | Pending | Pending | |
