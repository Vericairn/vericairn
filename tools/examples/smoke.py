# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Run the examples read leg with the CLI unpacked from this run's checksummed archive."""

import argparse
import hashlib
import html
import json
import os
from pathlib import Path, PurePosixPath
import platform
import posixpath
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
from urllib.parse import unquote, urlsplit
import zipfile


ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "tests/examples/examples.json"
COMMIT = re.compile(r"[0-9a-f]{40}")
VERSION = re.compile(r"(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)")
REPOSITORY = re.compile(r"Vericairn/vericairn-example-[a-z0-9-]+")
RELEASE_TAG = "https://github.com/Vericairn/vericairn/releases/tag/v"
PLATFORM = {"Linux": "linux", "Darwin": "macos", "Windows": "windows"}


class SmokeFailure(Exception):
    """The candidate or the pinned input failed a check."""


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def examples():
    manifest = read_json(MANIFEST)
    if manifest["format"] != "vericairn.examples/1":
        raise SmokeFailure("Unexpected examples manifest format.")
    entries = manifest["examples"]
    if not entries or len({entry["key"] for entry in entries}) != len(entries):
        raise SmokeFailure("The examples manifest needs at least one unique example.")
    for entry in entries:
        repositories = entry["repositories"]
        release = entry["release"]
        if repositories and (not isinstance(release, str) or not VERSION.fullmatch(release)):
            raise SmokeFailure(f"Invalid release pin for {entry['key']}.")
        if not repositories and (release is not None or entry["guidedSteps"] is not None):
            raise SmokeFailure(f"{entry['key']} has no public records to pin.")
        if "read" in entry["legs"] and sum(repo["estate"] is True for repo in repositories) != 1:
            raise SmokeFailure(f"{entry['key']} needs exactly one public estate for its read leg.")
        for repo in repositories:
            if not REPOSITORY.fullmatch(repo["name"]) or not COMMIT.fullmatch(repo["commit"]):
                raise SmokeFailure(f"Invalid public repository or commit pin for {entry['key']}.")
    return entries


# The workflow's smoke step is capped at three minutes; the journeys get what the read leg left of it, less a margin for the
# report, so the smoke fails with its own message before the runner stops the step.
STEP_SECONDS = 180
REPORT_MARGIN_SECONDS = 10


def execute(argv, cwd, environment, timeout=120):
    return subprocess.run(argv, cwd=cwd, env=environment, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                          text=True, encoding="utf-8", errors="replace", timeout=timeout, check=False)


def git(root, environment, *args):
    answer = execute(["git", "-c", "core.autocrlf=false", "-c", "core.hooksPath=" + environment["GIT_TEMPLATE_DIR"],
                      "-c", "credential.helper=", "-c", "protocol.ext.allow=never", *args], root, environment)
    if answer.returncode != 0:
        raise SmokeFailure(f"git {args[0]} exited {answer.returncode}: {answer.stderr.strip()}")
    return answer.stdout


def isolated_environment(scratch):
    home = scratch / "home"
    store = scratch / "state"
    hooks = scratch / "empty-git-template"
    for path in (home, store / "updates", hooks):
        path.mkdir(parents=True)
    write_json(store / "updates/settings.json", {"enabled": False})
    # No caller-supplied Git configuration, release token, SSH agent, or harness sign-in reaches the read leg.
    names = ("PATH", "SYSTEMROOT", "WINDIR", "COMSPEC", "PATHEXT", "TMP", "TEMP", "TMPDIR", "LANG",
             "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64")
    environment = {name: os.environ[name] for name in names if name in os.environ}
    environment.update(HOME=str(home), USERPROFILE=str(home), DOTNET_CLI_HOME=str(home), VERICAIRN_HOME=str(store),
                       XDG_CONFIG_HOME=str(home / "config"), GIT_CONFIG_NOSYSTEM="1",
                       GIT_CONFIG_GLOBAL=os.devnull, GIT_TEMPLATE_DIR=str(hooks), GIT_TERMINAL_PROMPT="0",
                       DOTNET_EnableDiagnostics="0")
    return environment


def checkout(pin, destination, environment, local_source=None):
    destination.mkdir()
    git(destination, environment, "init", "--quiet")
    # The local source is an offline test input; the CI route always uses the public HTTPS repository.
    source = str(local_source) if local_source else f"https://github.com/{pin['name']}.git"
    remote_head = git(destination, environment, "ls-remote", "--symref", "--exit-code", source, "HEAD")
    default = re.search(r"(?m)^ref: (refs/heads/[^\t\n]+)\tHEAD$", remote_head)
    if default is None:
        raise SmokeFailure(f"{pin['name']} at {pin['commit']}: the repository advertises no default branch.")
    branch = default[1]
    git(destination, environment, "fetch", "--quiet", "--no-tags", source, branch)
    try:
        git(destination, environment, "merge-base", "--is-ancestor", pin["commit"], "FETCH_HEAD")
    except SmokeFailure as failure:
        raise SmokeFailure(f"{pin['name']} at {pin['commit']} is not reachable from its default branch {branch}.") from failure
    git(destination, environment, "checkout", "--quiet", "--detach", pin["commit"])
    if git(destination, environment, "rev-parse", "HEAD").strip() != pin["commit"]:
        raise SmokeFailure(f"{pin['name']} was not checked out at {pin['commit']}.")


def tracked_paths(repo, environment):
    listing = git(repo, environment, "ls-tree", "-r", "-t", "--full-tree", "-z", "HEAD")
    return {row.split("\t", 1)[1]: row.split(" ", 1)[0] for row in listing.split("\0") if row}


def readme_prose(markdown):
    # A comment can contain a literal fence, and a real fence can contain a literal comment opener.
    fence = None
    in_comment = False
    for line in markdown.splitlines():
        delimiter = None if in_comment else re.match(r"^ {0,3}(`{3,}|~{3,})(.*)$", line)
        if fence is not None:
            if delimiter and delimiter[1][0] == fence[0] and len(delimiter[1]) >= len(fence) and not delimiter[2].strip():
                fence = None
            yield ""
            continue
        if delimiter and (delimiter[1][0] == "~" or "`" not in delimiter[2]):
            fence = delimiter[1]
            yield ""
            continue
        content = []
        while line:
            before, marker, line = line.partition("-->" if in_comment else "<!--")
            if not in_comment:
                content.append(before)
            if not marker:
                break
            in_comment = not in_comment
        yield " ".join(content)


def readme_destinations(markdown):
    target = r"<[^<>\n]+>|(?:\\.|[^\s()<>]|\([^()\n]*\))+"
    container = r"[ \t]*(?:(?:>[ \t]*|(?:[-+*]|[0-9]{1,9}[.)])[ \t]+))*"
    attribute = r"""([A-Za-z_:][\w.:-]*)(?:\s*=\s*("[^"]*"|'[^']*'|[^\s"'<>`]+))?"""
    # Consume inline destinations and HTML tags before checking stand-alone angle-bracket destinations.
    pattern = (
        rf"\]\(\s*(?P<inline>{target})"
        rf"|^{container}\[[^\]\n]+\]:(?:[ \t]*\n{container})?[ \t]*(?P<reference>{target})"
        rf"|</?[A-Za-z][A-Za-z0-9-]*(?P<attributes>(?:\s+{attribute})*)\s*/?>"
        r"|<[^\s/<>@]+@[^\s/<>@]+\.[^\s/<>@]+>"
        r"|(?<!\\)<(?P<angle>[^\s<>]+)>"
    )
    for match in re.finditer(pattern, "\n".join(readme_prose(markdown)), re.M):
        destination = match["inline"] or match["reference"] or match["angle"]
        if destination:
            yield destination.removeprefix("<").removesuffix(">")
        elif match["attributes"]:
            for field in re.finditer(attribute, match["attributes"]):
                name, value = field.groups()
                if name.lower() in {"href", "src"} and value:
                    yield value[1:-1] if value[0] in "\"'" else value


def readme_findings(repo, environment, expected_release):
    paths = tracked_paths(repo, environment)
    readme = repo / "README.md"
    if paths.get("README.md") not in {"100644", "100755"}:
        return [("error", "readme.missing", "README.md")]
    text = readme.read_text(encoding="utf-8")
    findings = []
    if expected_release is not None:
        manifest = repo / "estate/manifest.json"
        if paths.get("estate/manifest.json") not in {"100644", "100755"}:
            return [("error", "readme.manifest-missing", "estate/manifest.json")]
        release = read_json(manifest)["vericairn"]
        if release["mode"] != "release" or release["version"] != expected_release:
            findings.append(("error", "readme.estate-pin", "estate/manifest.json"))
        named = []
        versions = []
        for line in readme_prose(text):
            if re.search(r"\b[Pp]ins[ \t]+\[Vericairn ", line):
                named.extend(re.findall(r"\[Vericairn ([^\]]+)\](?:\(([^)]*)\))?", line))
                versions.extend(re.findall(r"\bVericairn v?(\d+\.\d+\.\d+(?:[-+][\w.-]+)?)\b", line))
        expected = (release["version"], RELEASE_TAG + release["version"])
        if not named or any(pin != expected for pin in named) or any(version != release["version"] for version in versions):
            findings.append(("error", "readme.release-pin", "README.md"))
    for target in dict.fromkeys(readme_destinations(text)):
        target = html.unescape(re.sub(r"\\([!-`{-~])", r"\1", target))
        uri = urlsplit(target)
        if uri.scheme or uri.netloc or not uri.path:
            continue
        name = posixpath.normpath(unquote(uri.path).lstrip("/"))
        if name == ".":
            continue
        if name.startswith("../") or name == ".." or "\\" in name or name not in paths:
            findings.append(("error", "readme.relative-link", "README.md:" + target))
    return findings


def offline_inputs(dist, directory, version, rid, commit):
    extension = "zip" if rid.startswith("win-") else "tar.gz"
    selected = f"vericairn-{version}-{rid}.{extension}"
    sums = dist / "SHA256SUMS"
    archives = []
    checksums = {}
    for line in sums.read_text(encoding="utf-8").splitlines():
        matched = re.fullmatch(r"([0-9a-f]{64}) [ *]([A-Za-z0-9._-]+)", line)
        if not matched or matched[2] in checksums:
            raise SmokeFailure("Malformed or duplicate SHA256SUMS entry.")
        checksums[matched[2]] = matched[1]
        if re.fullmatch(rf"vericairn-(?:workbench-)?{re.escape(version)}-(?:linux|osx|win)-(?:x64|arm64)\.(?:zip|tar\.gz)", matched[2]):
            archives.append(matched[2])
    if selected not in archives:
        raise SmokeFailure(f"SHA256SUMS has no archive for {rid}.")
    archive = directory / selected
    with (dist / selected).open("rb") as source, archive.open("wb") as output:
        shutil.copyfileobj(source, output)
    with archive.open("rb") as input_file:
        if hashlib.file_digest(input_file, "sha256").hexdigest() != checksums[selected]:
            raise SmokeFailure(f"{selected} differs from SHA256SUMS.")
    shutil.copyfile(sums, directory / "SHA256SUMS")
    tag = "v" + version
    write_json(directory / "release.json", {
        "tag_name": tag, "draft": False, "prerelease": False, "published_at": "2000-01-01T00:00:00Z",
        "html_url": RELEASE_TAG + version, "target_commitish": commit,
        "assets": [{"name": name, "browser_download_url": f"https://github.com/Vericairn/vericairn/releases/download/{tag}/{name}"}
                   for name in ["SHA256SUMS", *archives]],
    })
    write_json(directory / "tag.json", {"ref": f"refs/tags/{tag}", "object": {"type": "commit", "sha": commit}})
    return archive


def bundled_cli(archive, destination):
    destination.mkdir()
    # Extract the generated package into scratch only. No archive from the outside estate is extracted.
    if archive.suffix == ".zip":
        with zipfile.ZipFile(archive) as package:
            for name in package.namelist():
                path = PurePosixPath(name)
                if path.is_absolute() or ".." in path.parts or "\\" in name or ":" in name:
                    raise SmokeFailure(f"Unsafe path in {archive.name}.")
            package.extractall(destination)
        return destination / "bin/vericairn.exe"
    with tarfile.open(archive) as package:
        package.extractall(destination, filter="data")
    return destination / "bin/vericairn"


def run_cli(cli, root, environment, args):
    answer = execute([str(cli), "--estate", str(root), "--json", *args], root, environment)
    try:
        payload = json.loads(answer.stdout)
    except ValueError as failure:
        raise SmokeFailure(f"{' '.join(args[:2])} exited {answer.returncode} without JSON: {answer.stderr.strip()}") from failure
    return answer.returncode, payload


def read_estate(cli, estate, environment, inputs, version, report):
    findings = set()
    for args in (["estate", "check"], ["status"], ["estate", "upgrade", "--check", "--to", version, "--from", str(inputs)]):
        step = {"command": " ".join(args[:2]) if args[0] != "status" else "status", "driver": "process", "standIns": []}
        if args[:2] == ["estate", "upgrade"]:
            step["standIns"] = ["offline release snapshot"]
        report.append(step)
        code, output = run_cli(cli, estate, environment, args)
        step["exitCode"] = code
        for finding in output.get("findings", []):
            findings.add(finding_key(finding))
        if code != 0 and (args[:2] == ["estate", "upgrade"] or code != 1 or not output.get("findings")):
            raise SmokeFailure(f"{step['command']} exited {code}.")
        if args[:2] == ["estate", "upgrade"] and output.get("applied") is not False:
            raise SmokeFailure("The upgrade check did not return an unapplied plan.")
    return findings


def finding_key(finding):
    fields = {key.lower(): value for key, value in finding.items()}
    return tuple(fields[name] for name in ("severity", "code", "location"))


def check_findings(entry, pin, findings):
    expected = {finding_key(item) for item in entry["expectedReadFindings"]
                if item["repository"] == pin["name"] and item["commit"] == pin["commit"]}
    actual = set(findings)
    if actual != expected:
        raise SmokeFailure(f"Unexpected findings: {sorted(actual - expected)}; expected but absent: {sorted(expected - actual)}.")


def journey_leg(cli, scratch, environment, selected, report, started):
    if not any("tier2" in entry["legs"] for entry in selected):
        return
    budget = STEP_SECONDS - REPORT_MARGIN_SECONDS - (time.monotonic() - started)
    if budget <= 0:
        raise SmokeFailure("The read leg used the smoke step's time; no time is left for the process journeys.")
    journey_report = scratch / "journeys.json"
    environment.update(VERICAIRN_EXAMPLES_BINARY=str(cli), VERICAIRN_EXAMPLES_JOURNEY_REPORT=str(journey_report))
    test = ROOT / "artifacts/bin/Vericairn.Cli.Tests/release/Vericairn.Cli.Tests.dll"
    try:
        result = execute(["dotnet", str(test), "--filter-method",
                          "Vericairn.Cli.Tests.ExamplesJourneyTests.TierTwoRunsEveryRegisteredJourney"], ROOT, environment, timeout=budget)
    except subprocess.TimeoutExpired as expired:
        raise SmokeFailure(f"The process journeys did not finish in the {budget:.0f} seconds the smoke step had left.") from expired
    if journey_report.is_file():
        report["journeys"] = read_json(journey_report)
    if result.returncode or "journeys" not in report:
        raise SmokeFailure("The process journeys failed: " + result.stdout + result.stderr)


def smoke(dist, version, rid, commit, report, local_source=None):
    started = time.monotonic()
    report.update(platform=rid, commit=commit, version=version, leg="read", examples=[])
    with tempfile.TemporaryDirectory(prefix="vericairn-examples-") as scratch:
        scratch = Path(scratch)
        environment = isolated_environment(scratch)
        inputs = scratch / "inputs"
        inputs.mkdir()
        cli = bundled_cli(offline_inputs(dist, inputs, version, rid, commit), scratch / "candidate")
        selected = [entry for entry in examples() if PLATFORM[platform.system()] in entry["platforms"]]
        for entry in selected:
            if "read" not in entry["legs"]:
                continue
            outcome = {"example": entry["key"], "releasePin": entry["release"], "steps": [], "repositories": []}
            report["examples"].append(outcome)
            for index, pin in enumerate(entry["repositories"]):
                clone = scratch / f"{entry['key']}-{index}"
                repository_report = {"name": pin["name"], "commit": pin["commit"]}
                outcome["repositories"].append(repository_report)
                try:
                    checkout(pin, clone, environment, local_source)
                    findings = set(readme_findings(clone, environment, entry["release"] if pin["estate"] else None))
                    if pin["estate"]:
                        findings.update(read_estate(cli, clone, environment, inputs, version, outcome["steps"]))
                    repository_report["findings"] = sorted(findings)
                    check_findings(entry, pin, findings)
                    if git(clone, environment, "status", "--porcelain=v1", "--untracked-files=all", "--ignored") != "":
                        raise SmokeFailure("The pinned repository's tree is no longer clean.")
                except SmokeFailure as failure:
                    raise SmokeFailure(f"{pin['name']} at {pin['commit']}: {failure}") from failure
        regression_environment = dict(environment, VERICAIRN_EXAMPLES_TEST_CLI=str(cli))
        regression = execute([sys.executable, "-B", str(ROOT / ".github/scripts/test_examples_smoke.py")],
                             ROOT, regression_environment)
        report["readRegressions"] = "passed" if regression.returncode == 0 else "failed"
        if regression.returncode:
            raise SmokeFailure("The read leg regression tests failed: " + regression.stderr)
        journey_leg(cli, scratch, environment, selected, report, started)
    if not report["examples"]:
        raise SmokeFailure("The manifest selects no read leg for the running platform.")
    report["seconds"] = round(time.monotonic() - started, 3)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dist", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--rid", required=True, choices=("linux-x64", "linux-arm64", "osx-x64", "osx-arm64", "win-x64", "win-arm64"))
    parser.add_argument("--commit", required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--local-source", type=Path, help="Offline test input for the pinned estate.")
    args = parser.parse_args()
    if not VERSION.fullmatch(args.version) or not COMMIT.fullmatch(args.commit):
        parser.error("A plain release version and a full source commit are required.")
    report = {}
    try:
        smoke(args.dist.resolve(), args.version, args.rid, args.commit, report,
              args.local_source.resolve() if args.local_source else None)
        report["result"] = "passed"
    except (SmokeFailure, OSError, ValueError, KeyError, subprocess.TimeoutExpired) as failure:
        report.update(result="failed", failure=str(failure))
    args.report.parent.mkdir(parents=True, exist_ok=True)
    write_json(args.report, report)
    print(json.dumps(report))
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with Path(summary).open("a", encoding="utf-8") as stream:
            stream.write("\n### Examples smoke read leg\n\n    " + json.dumps(report) + "\n")
    return 0 if report["result"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
