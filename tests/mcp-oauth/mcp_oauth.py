# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Read-only OAuth and MCP smoke client for a disposable public or private-overlay endpoint."""

import argparse
import base64
import datetime
import hashlib
import http.client
import http.server
import ipaddress
import json
import re
import secrets
import ssl
import sys
import time
import urllib.parse
import webbrowser

LEGACY = "2025-11-25"
MODERN = "2026-07-28"


class CheckFailed(Exception):
    pass


def check(condition, message):
    if not condition:
        raise CheckFailed(message)


def base64url(raw):
    return base64.urlsafe_b64encode(raw).decode("ascii").rstrip("=")


class Endpoint:
    def __init__(self, resource, ca_file, overlay_address):
        url = urllib.parse.urlsplit(resource)
        check(url.scheme == "https" and url.hostname and not url.username and not url.password
              and url.path == "/mcp" and not url.query and not url.fragment, "Expected an HTTPS /mcp URL.")
        self.host = url.hostname
        self.port = url.port if url.port is not None else 443
        check(self.port > 0, "Expected a valid HTTPS port.")
        authority = f"[{self.host}]" if ":" in self.host else self.host
        if self.port != 443:
            authority += f":{self.port}"
        self.issuer = "https://" + authority
        self.resource = self.issuer + "/mcp"
        self.tls = ssl.create_default_context(cafile=ca_file)
        self.overlay_address = ipaddress.ip_address(overlay_address) if overlay_address else None
        self.mcp_id = 0

    def send(self, stage, method, url, headers=None, body=None, expected=200):
        check(url.startswith(self.issuer + "/"), f"{stage} returned an unexpected endpoint.")
        with_connection = http.client.HTTPSConnection(self.host, self.port, timeout=15, context=self.tls)
        try:
            with_connection.connect()
            peer = ipaddress.ip_address(with_connection.sock.getpeername()[0])
            check(self.overlay_address is None or peer == self.overlay_address,
                  f"{stage} did not connect to the specified overlay address.")
            with_connection.request(method, url[len(self.issuer):], body, headers or {})
            response = with_connection.getresponse()
            check(response.status == expected, f"{stage} expected HTTP {expected}, got {response.status}.")
            payload = response.read(4 * 1024 * 1024 + 1)
            check(len(payload) <= 4 * 1024 * 1024, f"{stage} response exceeded the probe limit.")
            return response.headers, payload
        except (OSError, http.client.HTTPException) as error:
            raise CheckFailed(f"{stage}: {type(error).__name__}.") from None
        finally:
            with_connection.close()

    def json(self, stage, method, url, values=None, media_type="application/json", expected=200):
        body = None
        if values is not None:
            body = json.dumps(values) if media_type == "application/json" else urllib.parse.urlencode(values)
        _, result = self.send(stage, method, url, {"Content-Type": media_type}, body, expected)
        return json.loads(result)

    def mcp(self, bearer, revision, method, params=None, session=None):
        params = dict(params or {})
        headers = {"Authorization": "Bearer " + bearer,
                   "Content-Type": "application/json", "Accept": "application/json, text/event-stream"}
        if revision == MODERN:
            headers.update({"MCP-Protocol-Version": revision, "Mcp-Method": method})
            params["_meta"] = {"io.modelcontextprotocol/protocolVersion": revision,
                               "io.modelcontextprotocol/clientCapabilities": {}}
            if method == "tools/call":
                headers["Mcp-Name"] = params["name"]
        if session:
            headers.update({"MCP-Protocol-Version": revision, "Mcp-Session-Id": session})
        request = {"jsonrpc": "2.0", "method": method, "params": params}
        notify = method == "notifications/initialized"
        if not notify:
            self.mcp_id += 1
            request["id"] = self.mcp_id
        headers, payload = self.send(method, "POST", self.resource, headers, json.dumps(request), 202 if notify else 200)
        if notify:
            return None, None
        reply = json.loads(payload)
        check(reply.get("id") == self.mcp_id and "result" in reply, f"{method} did not return a JSON-RPC result.")
        return reply["result"], headers.get("Mcp-Session-Id")


class Callback(http.server.BaseHTTPRequestHandler):
    def log_message(self, _format, *_values):
        # An authorization code is in this URL. Do not log it.
        pass

    def do_GET(self):
        target = urllib.parse.urlsplit(self.path)
        values = urllib.parse.parse_qs(target.query)
        code = values.get("code", [])
        binding_matches = (target.path == "/callback" and values.get("iss") == [self.server.issuer]
                           and values.get("state") == [self.server.state])
        accepted = binding_matches and len(code) == 1 and "error" not in values
        if accepted:
            self.server.code = code[0]
        elif binding_matches and "error" in values:
            self.server.declined = True
        self.send_response(200 if accepted else 400)
        self.send_header("Content-Type", "text/plain; charset=utf-8")
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(b"MCP callback received. Return to the smoke client." if accepted else b"Callback refused.")


def authorize(endpoint, metadata, timeout):
    verifier = secrets.token_urlsafe(32)
    state = secrets.token_urlsafe(16)
    with http.server.HTTPServer(("127.0.0.1", 0), Callback) as callback:
        callback.state = state
        callback.issuer = endpoint.issuer
        callback.code = None
        callback.declined = False
        redirect = f"http://127.0.0.1:{callback.server_port}/callback"
        registration = endpoint.json("dynamic registration", "POST", metadata["registration_endpoint"], {
            "client_name": "Vericairn OAuth smoke client",
            "application_type": "native",
            "redirect_uris": [redirect],
            "token_endpoint_auth_method": "none",
        }, expected=201)
        client_id = registration["client_id"]
        authorization = metadata["authorization_endpoint"]
        check(authorization.startswith(endpoint.issuer + "/"), "Unexpected authorization endpoint.")
        url = authorization + "?" + urllib.parse.urlencode({
            "client_id": client_id, "response_type": "code", "redirect_uri": redirect, "state": state,
            "code_challenge": base64url(hashlib.sha256(verifier.encode("ascii")).digest()),
            "code_challenge_method": "S256", "resource": endpoint.resource, "scope": "read contribute",
        })
        print("Enter the read approval code on the browser page, then approve.", file=sys.stderr)
        check(webbrowser.open(url), "The browser could not be opened.")
        until = time.monotonic() + timeout
        while callback.code is None and not callback.declined and (remaining := until - time.monotonic()) > 0:
            callback.timeout = remaining
            callback.handle_request()
        check(not callback.declined, "The authorization server declined the request.")
        check(callback.code, "No callback with the expected issuer and state arrived.")
        token = endpoint.json("code exchange", "POST", metadata["token_endpoint"], {
            "client_id": client_id, "code": callback.code, "code_verifier": verifier,
            "grant_type": "authorization_code", "redirect_uri": redirect, "resource": endpoint.resource,
        }, media_type="application/x-www-form-urlencoded")
    check(token.get("scope") == "read" and token.get("token_type") == "Bearer" and token.get("expires_in") == 900,
          "Expected a 15-minute bearer token with read scope.")
    return token["access_token"]


def probe(args):
    endpoint = Endpoint(args.endpoint, args.ca_file, args.overlay_address)
    headers, _ = endpoint.send("unauthenticated MCP", "GET", endpoint.resource, expected=401)
    challenge = re.fullmatch(r'Bearer resource_metadata="([^"]+)"', headers.get("WWW-Authenticate", ""))
    check(challenge is not None, "MCP did not advertise protected-resource metadata.")
    protected = endpoint.json("protected-resource metadata", "GET", challenge[1])
    check(protected.get("resource") == endpoint.resource and protected.get("authorization_servers") == [endpoint.issuer],
          "Protected-resource metadata did not match the endpoint.")
    metadata = endpoint.json("authorization-server metadata", "GET", endpoint.issuer + "/.well-known/oauth-authorization-server")
    check(metadata.get("issuer") == endpoint.issuer and metadata.get("code_challenge_methods_supported") == ["S256"],
          "Authorization-server metadata did not identify this issuer and S256.")
    bearer = authorize(endpoint, metadata, args.timeout)

    for revision in [LEGACY, MODERN]:
        session = None
        if revision == LEGACY:
            initialized, session = endpoint.mcp(bearer, revision, "initialize", {
                "protocolVersion": revision, "capabilities": {},
                "clientInfo": {"name": "Vericairn OAuth smoke client", "version": "1"},
            })
            check(initialized.get("protocolVersion") == revision and session, "Legacy session was not negotiated.")
            endpoint.mcp(bearer, revision, "notifications/initialized", session=session)
        tools, new_session = endpoint.mcp(bearer, revision, "tools/list", session=session)
        names = {tool["name"] for tool in tools["tools"]}
        check("estate_status" in names and "work_attempt" not in names, f"{revision} tools did not match read scope.")
        result, _ = endpoint.mcp(bearer, revision, "tools/call", {"name": "estate_status", "arguments": {}}, session)
        check(not result.get("isError") and result.get("content"), f"{revision} estate_status did not return content.")
        check(revision != MODERN or new_session is None, "Modern tools/list opened a session.")

    print(json.dumps({
        "at": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "client": "Vericairn OAuth smoke client",
        "route": "private-overlay" if args.overlay_address else "public",
        "oauth": "passed", "scopesGranted": ["read"], "revisions": [LEGACY, MODERN],
        "toolsList": "passed", "tool": "estate_status", "toolCall": "passed",
    }, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("endpoint", help="HTTPS URL ending in /mcp")
    parser.add_argument("--ca-file", help="PEM file for the endpoint's trusted certificate authority")
    parser.add_argument("--overlay-address", help="Require every TLS connection to reach this overlay IP")
    parser.add_argument("--timeout", type=int, default=600, help="Seconds to wait for browser approval")
    args = parser.parse_args()
    try:
        probe(args)
    except CheckFailed as error:
        parser.exit(1, f"{error}\n")
    except (KeyError, TypeError, ValueError, OSError) as error:
        # Do not print exception details, response bodies, URLs with query strings, or credentials.
        parser.exit(1, f"Probe could not continue: {type(error).__name__}.\n")


if __name__ == "__main__":
    main()
