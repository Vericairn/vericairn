# vericairn-example-estate

> **A demonstration.** This is Vericairn's public example estate, not a customer's estate. Its organization, Example Ventures, is fictional, and its principals and device keys are demonstration-only: a demonstration operator, an example builder agent and an example reviewer agent, all operated by Vericairn's own builder for this example. Nothing here acts for a real person or organization.

A real [Vericairn](https://github.com/Vericairn/vericairn) estate, created with a published release's `estate init`, that manages one small product, [tally](https://github.com/Vericairn/vericairn-example-app), end to end. Every record here is a real record of a real operation on that product: a work order, an agent's recorded run through Vericairn's worker, an independent review, the operator role's acceptance, a checked merge and a release.

## Read the loop on GitHub

The records are canonical JSON under [`records/`](records/). The generated registers render them as Markdown, one file per revision:

- **Work:** [`docs/registers/work/`](docs/registers/work/), the work orders and their placement in a release.
- **Decisions:** [`docs/registers/decisions/`](docs/registers/decisions/).
- **Releases:** [`docs/registers/releases/`](docs/registers/releases/).

Records the registers don't render yet read directly as JSON: assignments (`records/assignment/`), the agent's attempts and results (`records/attempt/`, `records/result/`), reviews (`records/review/`), acceptances (`records/acceptance/`), the worker's runs and the checked pushes, pull requests and merges (`records/operation/`).

## By domain

- **Software:** the `tally` product (`records/product/`), its repository (`records/resource/`) and the work below.

Later domains add their own example products to this estate as their packs ship.

## The work so far

| Work order | What it asked | Where it stands |
|---|---|---|
| `TALLY-01` | Count words and characters as well as lines | Accepted. The first attempt was returned when Windows miscounted text outside ASCII; the second passed review and merged in [pull request 2](https://github.com/Vericairn/vericairn-example-app/pull/2) |
| `TALLY-02` | Machine-readable counts with `--json` | Accepted; merged in [pull request 3](https://github.com/Vericairn/vericairn-example-app/pull/3) |
| `REL-01` | Release tally 0.1.0, carrying both | Done: the candidate was built from an exact commit, reviewed and accepted, then [published](https://github.com/Vericairn/vericairn-example-app/releases/tag/v0.1.0) by the operator |

The goal that funded both runs, `tally 0.1.0 through the worker`, is achieved: an evaluator who did none of its work found its condition met, and its sponsor accepted it.

## What a run records

Each agent run goes through `vericairn worker run`: its cap is recorded before it starts and its usage after, its workspace is a repository of its own, and its execution record names the harness, model and effort it ran with, as Vericairn records for every run. The agent records its own attempt and result through Vericairn's MCP server, signed by its own key. A result is a claim: the reviewer checks it, and only the operator role accepts it.

## Run it yourself

With git, clone this repository and run the bootstrap: it fetches the pinned Vericairn release, verifies it against the manifest's hash and opens the estate.

```sh
git clone https://github.com/Vericairn/vericairn-example-estate
cd vericairn-example-estate
./bootstrap status        # bootstrap.ps1 on Windows
./bootstrap estate check
```

A clone without the demonstration keys reads everything and signs nothing. The [check workflow](.github/workflows/check.yml) does the same cold start on Linux, macOS and Windows for every change, and the estate is upgraded with every Vericairn release. It pins [Vericairn 0.9.3](https://github.com/Vericairn/vericairn/releases/tag/v0.9.3), the current published release, which the bootstrap fetches and verifies.
