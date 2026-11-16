#!/usr/bin/env python3
# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Drive the release helper's presence sessions for their qualification.

docs/presence-session-qualification.md runs this against a release candidate's signed
vericairn-enclave on a real Mac with a presence key. It stands in for the runtime on the
helper's protocol (vericairn.presence-session/1, design/presence-sessions.md section 6):
it composes terms over synthetic push records, opens a session, prints every message the
helper answers with the time it arrived, and signs records as a runtime would. The
records are made up for the qualification and never enter an estate; the key is a
throwaway presence key the key command makes, never a device's key.
"""

import argparse
import base64
import hashlib
import json
import os
import queue
import secrets
import signal
import subprocess
import sys
import tempfile
import threading
import time

FORMAT = "vericairn.presence-session/1"
CROCKFORD = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
PRINCIPAL = "01M7H3QQ000000000000000001"
DEVICE = "01M7H3QQ000000000000000002"
PRODUCT = "01M7H3QQ000000000000000003"
REPOSITORY = "01M7H3QQ000000000000000004"
PRODUCT_HASH = "sha256:" + hashlib.sha256(b"presence session qualification").hexdigest()
HEAD = hashlib.sha1(b"presence session qualification").hexdigest()
PROVIDER = "github-pull-requests"
STATUSES = sorted(["not-started", "dispatched", "observed-success", "known-failure", "canceled", "uncertain"])
BRANCH = "qualification/presence-session"
# What a build compiled with the helper's test hooks carries and a release build must not: the hooks' environment names, their
# messages and their names in the code. EnclaveSessionTests holds a release build to the same list.
TEST_HOOK_MARKERS = [b"VERICAIRN_TEST", b"VERICAIRN_SESSION_TESTS", b"test-advance", b"test-signal", b"testEnvironment", b"testMessage"]


def ulid():
    return "0" + "".join(secrets.choice(CROCKFORD) for _ in range(25))


def b64url(data):
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def canonical(value):
    # Every name and value the driver writes is ASCII, so sorting by code point is sorting by UTF-16 code unit.
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True).encode()


def public_key(helper, blob):
    done = subprocess.run([helper, "public", blob], capture_output=True, text=True, env={}, check=False)
    if done.returncode != 0:
        sys.exit("The helper cannot read the key blob: " + done.stderr.strip())
    return b64url(base64.b64decode(done.stdout.strip()))


def push_entry(branch, count):
    fixed = {"provider": PROVIDER, "repository": REPOSITORY, "product": PRODUCT, "productHash": PRODUCT_HASH, "branch": branch, "head": HEAD}
    return {"entry": "kind", "type": "operation", "action": "change-push", "count": count, "atMost": False, "statuses": STATUSES, "fixed": fixed}


def terms_for(key, entries, limit):
    return {"format": FORMAT, "action": "changes push", "actionIdentity": "sha256:" + hashlib.sha256(ulid().encode()).hexdigest(), "principal": PRINCIPAL,
            "device": DEVICE, "publicKey": key, "session": ulid(), "limitSeconds": limit, "entries": entries, "references": []}


def expected_dialog(terms, fingerprint):
    """The dialog's text for the driver's own terms: push entries, each on its own branch (design section 8)."""
    total = sum(e["count"] for e in terms["entries"])
    head = f"open Vericairn session {fingerprint}: {terms['action']}, {total} signature{'' if total == 1 else 's'} in {terms['limitSeconds']} s"
    parts = "".join(f"; {e['count']} push record{'' if e['count'] == 1 else 's'}, branch {e['fixed']['branch']}" for e in terms["entries"])
    if len(head) + len(parts) <= 200:
        return head + parts
    return head + parts[: 200 - len(head) - 1].rstrip(" ;,") + "…"


def push_record(branch, at, previous):
    identity = f"{PROVIDER}:{REPOSITORY}:change-push:{branch}:{HEAD}:{PRODUCT_HASH[7:]}"
    target = {"branch": branch, "head": HEAD, "product": PRODUCT, "productHash": PRODUCT_HASH, "repository": REPOSITORY}
    body = {"action": "change-push", "evidence": [], "identity": identity, "intent": "A synthetic push for the presence session qualification.",
            "provider": PROVIDER, "status": "not-started" if previous is None else "uncertain", "target": target}
    record = {"format": "vericairn.record/3", "type": "operation", "id": ulid(), "at": at, "by": PRINCIPAL, "subject": {"type": "product", "id": PRODUCT}, "body": body}
    if previous is not None:
        record["supersedes"] = previous
    return record


class Session:
    """One helper process on the protocol, started as a runtime starts it: an empty environment and only its standard streams."""

    def __init__(self, command):
        self.started = time.time()
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env={}, close_fds=True)
        self.lines = queue.Queue()
        threading.Thread(target=self._read, daemon=True).start()
        threading.Thread(target=self._errors, daemon=True).start()

    def _read(self):
        for line in self.process.stdout:
            self.lines.put(line.decode().rstrip("\n"))
        self.lines.put(None)

    def _errors(self):
        for line in self.process.stderr:
            self.say("diagnostic: " + line.decode().rstrip("\n"))

    def say(self, text):
        print(f"[{time.time() - self.started:7.1f} s] {text}", flush=True)

    def send(self, message):
        try:
            self.process.stdin.write(canonical(message) + b"\n")
            self.process.stdin.flush()
        except BrokenPipeError:
            self.say("the helper's input is closed")

    def line(self, timeout):
        try:
            return self.lines.get(timeout=timeout)
        except queue.Empty:
            return ""

    def next(self, timeout=330):
        line = self.line(timeout)
        if line is None:
            self.say("the helper's output ended")
            return None
        if line == "":
            self.say(f"no answer within {timeout} seconds")
            return None
        message = json.loads(line)
        self.say("helper: " + line)
        return message


def open_session(session, terms, blob_bytes):
    session.send({"type": "open", "terms": b64url(canonical(terms)), "bytes": blob_bytes})
    prompting = session.next(30)
    if not prompting or prompting.get("type") != "prompting":
        return None
    fingerprint = prompting["fingerprint"]
    session.say("expected dialog: " + expected_dialog(terms, fingerprint))
    session.say("answer the dialog now; the fingerprint is " + fingerprint)
    opened = session.next(330)
    if not opened or opened.get("type") != "opened":
        return None
    return opened


def sign_chain(session, opened, count, every, branch=BRANCH):
    previous = None
    for index in range(count):
        if index and every:
            time.sleep(every)
        record = push_record(branch, opened["openedAt"], previous)
        session.send({"type": "sign", "record": b64url(canonical(record))})
        answer = session.next(30)
        if not answer or answer.get("type") != "signed":
            session.say(f"signature {index + 1} of {count} was not made")
            return index
        session.say(f"signature {index + 1} of {count} under the session's check")
        previous = record["id"]
    return count


def command_build(arguments):
    with open(arguments.helper, "rb") as helper:
        content = helper.read()
    found = [marker.decode() for marker in TEST_HOOK_MARKERS if marker in content]
    answered = []
    for command in (["canonical"], ["fingerprint", arguments.helper, "AAAAAAAAAAAAAAAAAAAAAA"]):
        done = subprocess.run([arguments.helper] + command, input=b"{}", capture_output=True, env={}, check=False)
        if done.returncode != 2:
            answered.append(command[0])
    if found or answered:
        print("this helper carries test hooks:", ", ".join(found + [f"answers {name}" for name in answered]))
        sys.exit(1)
    print("no test hook: none of the hooks' names is in the helper, and it refuses the test commands as usage errors")


def command_key(arguments):
    directory = tempfile.mkdtemp(prefix="vericairn-qualification-")
    os.chmod(directory, 0o700)
    blob = os.path.join(directory, "presence.se")
    done = subprocess.run([arguments.helper, "create", blob, "--presence"], capture_output=True, text=True, env={}, check=False)
    if done.returncode != 0:
        sys.exit("The helper could not make a presence key: " + done.stderr.strip())
    print("blob:", blob)
    print("public key:", b64url(base64.b64decode(done.stdout.strip())))
    print("Remove the directory when the qualification is recorded:", directory)


def command_open(arguments):
    key = public_key(arguments.helper, arguments.blob)
    terms = terms_for(key, [push_entry(BRANCH, arguments.count)], arguments.limit)
    session = Session([arguments.helper, "session", arguments.blob])
    session.say(f"helper process {session.process.pid}")
    opened = open_session(session, terms, [])
    if opened is None:
        session.next(10)
        return
    every = arguments.spread / max(1, arguments.count - 1)
    signed = sign_chain(session, opened, arguments.count, every)
    if signed == arguments.count:
        session.send({"type": "close"})
    session.next(10)
    session.say(f"signed {signed} of {arguments.count} with one presence check")


def command_hold(arguments):
    key = public_key(arguments.helper, arguments.blob)
    terms = terms_for(key, [push_entry(BRANCH, 128)], arguments.limit)
    session = Session([arguments.helper, "session", arguments.blob])
    session.say(f"helper process {session.process.pid}; lock, switch user, close the lid, sleep or wait for the limit, then read the end below")
    opened = open_session(session, terms, [])
    if opened is None:
        session.next(10)
        return
    previous = None
    signed = 0
    while signed < 128:
        line = session.line(arguments.every)
        if line is None:
            session.say("the helper's output ended")
            break
        if line:
            session.say("helper: " + line)
            if json.loads(line).get("type") == "ended":
                break
            continue
        record = push_record(BRANCH, opened["openedAt"], previous)
        session.send({"type": "sign", "record": b64url(canonical(record))})
        previous = record["id"]
        signed += 1
    session.say(f"signed {signed}; the wall clock read {time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}")


def command_parent(arguments):
    key = public_key(arguments.helper, arguments.blob)
    terms = terms_for(key, [push_entry(BRANCH, 3)], arguments.limit)
    # The shell stands in for the runtime: it starts the helper, hands it this driver's pipes and is then killed. The driver keeps the
    # helper's input open, as another child of the runtime could, so only the helper's watch on its parent can end the session.
    session = Session(["/bin/sh", "-c", '"$0" session "$1" <&0 & echo "$!"; wait', arguments.helper, arguments.blob])
    child = int(session.line(10))
    session.say(f"shell {session.process.pid}, helper process {child}")
    if open_session(session, terms, []) is None:
        return
    os.kill(session.process.pid, signal.SIGKILL)
    session.say("killed the shell, the helper's parent")
    session.next(10)
    for _ in range(50):
        try:
            os.kill(child, 0)
        except ProcessLookupError:
            session.say("the helper exited")
            return
        time.sleep(0.1)
    session.say("the helper is still running five seconds after its parent exited: this point fails")
    os.kill(child, signal.SIGKILL)


def command_maximum(arguments):
    key = public_key(arguments.helper, arguments.blob)
    entries = [push_entry("q/" + letter * 98, 1) for letter in "abc"]
    terms = terms_for(key, entries, arguments.limit)
    session = Session([arguments.helper, "session", arguments.blob])
    opened = open_session(session, terms, [])
    if opened is None:
        session.next(10)
        return
    session.say("compare the whole dialog with the expected text above: 200 characters ending with an ellipsis")
    sign_chain(session, opened, 1, 0, branch=entries[0]["fixed"]["branch"])
    session.send({"type": "close"})
    session.next(10)


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--helper", required=True, help="the release candidate's signed vericairn-enclave")
    commands = parser.add_subparsers(dest="command", required=True)
    commands.add_parser("build", help="check that the helper carries no test hook")
    commands.add_parser("key", help="make a throwaway presence key and print its blob")
    for name, text in [("open", "open a session over push records and sign them, spread over the given seconds"),
                       ("hold", "open a session and sign one record every few seconds until it ends"),
                       ("parent", "open a session as the child of a shell, kill the shell and watch the helper end"),
                       ("maximum", "open a session whose dialog text is cut at 200 characters")]:
        command = commands.add_parser(name, help=text)
        command.add_argument("--blob", required=True, help="the throwaway presence key's blob")
        command.add_argument("--limit", type=int, default=300, help="the session's limit in seconds, 60 to 300")
        if name == "open":
            command.add_argument("--count", type=int, default=22, help="how many records to sign, at most 128")
            command.add_argument("--spread", type=float, default=0, help="the seconds the signatures are spread over")
        if name == "hold":
            command.add_argument("--every", type=float, default=5, help="seconds between signatures")
    arguments = parser.parse_args()
    {"build": command_build, "key": command_key, "open": command_open, "hold": command_hold, "parent": command_parent, "maximum": command_maximum}[arguments.command](arguments)


if __name__ == "__main__":
    main()
