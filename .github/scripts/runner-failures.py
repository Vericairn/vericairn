# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

"""Decide whether a failed ci.yml run failed only because its runners did, and re-run it once if so.

A job is a runner failure when the runner ended it while it was still setting up, before it built or tested anything of
the change: every step up to the one that ended it is a setup step (the job's own set-up, the checkout, the .NET SDK, a
locked restore, a pinned tool or workload install), and GitHub's annotations say the process was killed (exit code 137) or
the runner was lost. A test failure, a build failure, a timeout or a cancellation is never one, and neither is a runner
lost once a job had begun to build or test. A run is re-run only when every job that neither passed nor was skipped, ci-gate
apart, is a runner failure: GitHub's re-run of failed jobs also re-runs cancelled and timed-out ones, so a run holding one is
never re-run. Only a first attempt is re-run, only if it is still the run's latest attempt, and a pull request's run only if no
newer run of its branch exists, since a re-run joins the pull request's concurrency group and would cancel the newer head's run.
It re-runs the failed jobs and the jobs that need them (ci-gate among them), so nothing a test decided is retried.

Exit status: 0 when every failed job of the attempt is a runner failure (re-run with --rerun), 3 when not, 1 when GitHub
could not be read.
"""

import argparse
import json
import re
import subprocess
import sys
from urllib.parse import quote

# Steps that run before anything of the change is built or tested, by the name GitHub's jobs API gives them.
SETUP_STEPS = frozenset({
    "Set up job",
    "Restore (locked)",
    "Install a secret service",
    "Install gitleaks for the scanner's tests",
    "Install the pinned workflow linter",
    "Install the iOS workload",
    "Install the Android workload",
    "Select Xcode",
})
SETUP_ACTIONS = ("Run actions/checkout@", "Run actions/setup-dotnet@", "Run actions/download-artifact@")
# GitHub's annotations when a runner's process is killed, or the runner is lost, shut down or never acquired.
RUNNER_ENDED = re.compile(r"exit code 137\b|lost communication with the server|received a shutdown signal"
                          r"|was not acquired by Runner|encountered an error while running your job", re.IGNORECASE)
# The aggregate check fails because the jobs it needs failed; it is re-run with them, never judged on its own.
GATE = "ci-gate"
SETTLED = ("success", "skipped")
NOT_RETRYABLE = 3


def is_setup(step):
    name = step.get("name") or ""
    return name in SETUP_STEPS or name.startswith(SETUP_ACTIONS)


def ending_step(steps):
    """The step the job ended in: the first that failed, was cancelled or timed out, else the first that never completed."""
    for step in steps:
        if step.get("conclusion") in ("failure", "cancelled", "timed_out"):
            return step
    return next((step for step in steps if step.get("status") != "completed"), None)


def runner_failure(job, annotations):
    """Why a failed job counts as a runner failure, or None when it does not."""
    steps = sorted(job.get("steps") or [], key=lambda step: step.get("number", 0))
    ended = ending_step(steps)
    if ended is None and steps:
        return None
    reached = steps[:steps.index(ended) + 1] if ended is not None else []
    if not all(is_setup(step) for step in reached):
        return None
    causes = [message for message in annotations if RUNNER_ENDED.search(message)]
    if not causes:
        return None
    where = f"in '{ended['name']}'" if ended is not None else "before its first step"
    return f"the runner ended it {where}: {causes[0]}"


def verdict(jobs, annotations):
    """(retry, lines): retry only when some job, the gate apart, neither passed nor was skipped, and every such job failed
    and is a runner failure. A cancelled or timed-out job, which a re-run of failed jobs would also take, refuses it."""
    unsettled = [job for job in jobs if job.get("name") != GATE and job.get("conclusion") not in SETTLED]
    lines, retry = [], bool(unsettled)
    for job in unsettled:
        if job.get("conclusion") == "failure":
            reason = runner_failure(job, annotations.get(job["id"], []))
            lines.append(f"{job.get('name')}: {reason}" if reason else f"{job.get('name')}: not a runner failure")
        else:
            reason = None
            lines.append(f"{job.get('name')}: ended {job.get('conclusion')}, which is never re-run")
        retry = retry and reason is not None
    if not unsettled:
        lines.append("No job failed apart from the gate.")
    return retry, lines


def still_current(repository, run, attempt):
    """None when the run may be re-run now, else why not: it must be complete, still at the judged attempt, and for a pull
    request the newest run of its branch."""
    body = gh(f"repos/{repository}/actions/runs/{run}")
    if body.get("status") != "completed" or body.get("run_attempt") != attempt:
        return f"run {run} is at attempt {body.get('run_attempt')} ({body.get('status')}), not the judged attempt {attempt}"
    if body.get("event") == "pull_request":
        branch = quote(str(body.get("head_branch") or ""), safe="")
        newest = gh(f"repos/{repository}/actions/workflows/{body['workflow_id']}/runs?event=pull_request&branch={branch}&per_page=1")
        latest = (newest.get("workflow_runs") or [{}])[0].get("id")
        if latest != run:
            return f"run {latest} is newer for branch {body.get('head_branch')}; re-running {run} would cancel it"
    return None


def gh(*args):
    result = subprocess.run(["gh", "api", *args], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        raise RuntimeError(f"gh api {' '.join(args)} failed: {result.stderr.strip()}")
    return json.loads(result.stdout) if result.stdout.strip() else None


def read_jobs(repository, run, attempt):
    jobs, page = [], 1
    while True:
        body = gh(f"repos/{repository}/actions/runs/{run}/attempts/{attempt}/jobs?per_page=100&page={page}")
        batch = body.get("jobs") or []
        jobs.extend(batch)
        if len(batch) < 100:
            return jobs
        page += 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", required=True, help="owner/name")
    parser.add_argument("--run", required=True, type=int, help="The ci.yml run's id.")
    parser.add_argument("--attempt", type=int, default=1, help="The attempt to judge; only a first attempt is ever re-run.")
    parser.add_argument("--rerun", action="store_true", help="Re-run the failed jobs when every one is a runner failure.")
    args = parser.parse_args()
    try:
        jobs = read_jobs(args.repository, args.run, args.attempt)
        annotations = {job["id"]: [item.get("message") or "" for item in gh(f"repos/{args.repository}/check-runs/{job['id']}/annotations") or []]
                       for job in jobs if job.get("conclusion") == "failure" and job.get("name") != GATE}
    except (RuntimeError, KeyError, TypeError, ValueError) as error:
        print(f"Not re-run: {error}")
        return 1
    retry, lines = verdict(jobs, annotations)
    for line in lines:
        print(line)
    if not retry:
        print("Not re-run: a job that did not pass is not a runner failure.")
        return NOT_RETRYABLE
    if args.attempt != 1:
        print(f"Not re-run: attempt {args.attempt} is already the re-run.")
        return NOT_RETRYABLE
    if args.rerun:
        try:
            refused = still_current(args.repository, args.run, args.attempt)
        except (RuntimeError, KeyError, TypeError, ValueError, IndexError) as error:
            print(f"Not re-run: {error}")
            return 1
        if refused:
            print(f"Not re-run: {refused}.")
            return NOT_RETRYABLE
        try:
            subprocess.run(["gh", "api", "--method", "POST", f"repos/{args.repository}/actions/runs/{args.run}/rerun-failed-jobs"],
                           check=True, capture_output=True, text=True)
        except subprocess.CalledProcessError as error:
            print(f"The re-run was refused: {error.stderr.strip()}")
            return 1
        print(f"Re-ran the failed jobs of run {args.run} once.")
    else:
        print("Every failed job is a runner failure.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
