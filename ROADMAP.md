# Vericairn roadmap

This page shows where Vericairn is headed, release by release. Everything after the current published
release is planned, not promised, and has no dates. Each release's notes and documentation set are the
record of what shipped.

## 0.9.2 — current published release

0.9.2 follows 0.9.1. A goal can run a declared flow: each step's position, hand-offs and reviews come from the
records, each step is routed to its role one step per command, and a run stops where a person holds the role, a
reserved step showing its exact command only when it is next. The software pack declares the release and estate
upgrade flows, and the build tooling assembles, freezes and indexes a release's documentation set. An estate
signs its history: checkpoints witness every record it holds, so a removed, altered or backdated record no longer
passes unnoticed, and closings bound what a revoked device, a replaced key or a narrowed grant still accounts for.
Clients follow one event journal from where they left off instead of polling, and an agent is told when what it
reads changes. An admitted agent on another machine works an estate over MCP's Streamable HTTP transport, each
call judged as its API operation for that device, and keyword search selects the decisions and documents an
agent's brief needs, with every selected source pinned and nothing it may not read revealed. An upgrade replaces
an older background runtime, security reports hold a release until they are triaged, an acceptance rests only on
reviews that passed, and a stopped operator action can be finished. Every distributable is built on .NET SDK
10.0.404 and embeds .NET and ASP.NET Core runtime 10.0.15, and 0.9.2 hardens how admissions and an estate's root
of trust are read, which records give roles and scopes, policy authority after an operator role is removed,
per-user state on Linux, held-term and blocked-term matching and request authentication, as the release notes'
Security section describes. A flow's exchange steps are shown stopped, not executed, and Community moves a run
one step per command. Everything 0.9.1 brought remains, with Remote still a developer build you make from source.
Its [release notes](docs/releases/0.9.2/release-notes.md) and [documentation set](docs/releases/0.9.2/README.md)
describe what shipped and its limits.

## 0.9.3 — planned

0.9.3 corrects what the estates running 0.9.2 reported and readies the release flow for the next release.
Export and restore keep each file's executable mode, a timestamp in the wrong form is refused with the form
required, `doctor --json` returns one documented result, a failed document check names what failed, and a goal
is recorded impossible only after its evaluator's verdict. Every operator action announces and counts its
presence prompts before each one, release steps make no throwaway signature with a presence key, and an estate
with many key rotations or closings is checked in near-linear time. A change merges through a repository's
required merge queue with its outcome recorded, an operator action's push and merge trail reaches the estate,
and a work order whose current result is accepted reads as accepted, so a release can run on its declared
flow. A command names an estate's first authority where its manifest needs one, a guide tells an expired,
refused or timed-out presence prompt apart during enrollment and reconciles it safely, and an estate's root can
hold its owner's agent instruction files. Design work prepares the releases after it: measured agent work,
smaller milestones after M3, an opt-in estate advisor and one presence check per command.

## 0.9.4 — planned

0.9.4 is the first release Vericairn's own estate runs on the software pack's release flow. A flow's steps run
on their timing, at once, after a delay, at a time, on a schedule, in a window or on an event, and a step may end
in a decision among named paths, made by a rule, an agent in a role or a person in a role. Standard and Pro carry
a run from step to step by themselves, stopping only where a person holds the role, while Community keeps one
step per command. Agents keep working on a product between releases as standing agents, within their sponsor's
caps, and every output is a possibility a person adopts. When an agent holding work goes quiet, its sponsor is
asked and the step moves to another holder. People say when they are away and set quiet hours that hold notices,
letting incidents through by default. Removing a person's role closes what they may still write, and an estate
shows its progress through a target its configuration chooses, GitHub Discussions first. Installers open
without warnings on macOS and Windows, and Vericairn installs and upgrades with one command through Homebrew,
winget or an install script that verifies the signed release manifest before installing.

## 0.9.5 — planned

Remote grows from attention and decisions to supervision: from the phone you steer running work, see the
estate's nodes and connections and hand a task to Workbench; Remote notifies you over the local network,
reminds you before a supervised window opens, handles lost, replaced and shared phones and opens readable
records. From Workbench or the phone, a saved prompt can be sent to a running agent in one step; it steers the
agent without widening what the agent may do. Paired devices reach the runtime from any network through a
relay that forwards bytes it cannot read, and Remote and Workbench say whether a connection is direct or
relayed. MCP clients that sign in only with OAuth work an estate with short-lived tokens.

## 0.9.6 — planned

An estate follows the GitHub conversations it subscribes to and answers only under recorded authority, and it
follows each report it files with another project through to the release that carries the fix. A release's
advisories are drafted from its Security section for you to publish, and every release's bill of materials is
checked against published advisories before acceptance and daily after. You can run Vericairn as an always-on
server for your estates, with every hosted estate keeping a replica you hold.

## 0.10.x — planned

The first creator domains arrive in this order: websites and web applications; books, book series and
world building; courses and video, single and in series; and crowdfunding. Each first version takes the
work from drafting through templates and review to a publishable package with its metadata, such as an
EPUB and print-ready PDF or a deployable site. You publish on the platform, and Vericairn records the
publication; direct platform integrations follow. These domains ship within the Community distributable
and move to separate packages at the packages milestone below.

## Editions — planned direction

Community stays free and open source, and no Community feature will move behind payment. Standard is
planned to license the coordinated production features for one person and Pro the same features for teams
and organizations; there are no offers or prices yet. Every paid service Vericairn plans, such as hosting,
a relay for Remote, backups, managed workers or first-tier support by agents, is planned as the same
software you can run yourself at the edition that sells it. You will never be locked in:

1. Every paid service has a self-hosted path at the edition that sells it.
2. No access to your estate requires a Vericairn account; an account only pays for and manages hosted services.
3. An estate moves between Vericairn's hosting and your own in either direction without loss.
4. Paid rights are verified offline, without contacting Vericairn, and outlive its servers.
5. Ending a subscription ends only Vericairn's operation of a service, never self-hosting of covered releases or any Community right.

## Later milestones: hosted storage — planned

Today an estate's records live in a git repository beside your work, or in a store on one machine. Four
hosted options are planned, each built and qualified on its own before it is offered: a runtime on one of
your own machines, reached over your network; a runtime hosted with your Vericairn account; a shared
runtime an organization operates for its members; and managed hosting from a provider you choose.

## Later milestone: coordination, packages and websites — planned

Coordination and scheduling across work, extension packages and foundations for a package registry.
Readers for Knowledge and the catalog, and websites generated from the same records, including this
roadmap.

## Later milestone: specialist workflows — planned

Unity projects and games follow, with the further foundries, adapters and providers of the creator
domains beyond their first versions. Vericairn's catalog plans more
than 3,000 entries across creative and technical work and 27 industries, listed in the
[README](README.md#planned); each further domain is placed on this roadmap when its milestone is chosen.
