# Vericairn roadmap

This page shows where Vericairn is headed, release by release. Everything after the current published
release is planned, not promised, and has no dates. Each release's notes and documentation set are the
record of what shipped.

## 0.9.1 — current published release

0.9.1 follows 0.9.0 as a security patch. Every distributable is built on .NET SDK 10.0.403 and embeds .NET and
ASP.NET Core runtime 10.0.14, replacing the 10.0.11 that 0.9.0 embedded, and a worker run gives Vericairn's MCP
server its temporary directory explicitly, never the run's own, as the release notes' Security section
describes. A worker run can prepare its workspace before the agent starts, with setup commands configured on the
worker's machine; a decision cannot be dated after its own record; the bill of materials names the release's
version; and policy evaluation reads historical repository content in batches, so estates that record
maintenance duties keep fast writes and checks. Everything 0.9.0 brought remains: Remote, the phone companion
for iOS and Android, as a developer build you make from source; release lines, channels and cadence; the
disclosure hold and blocked terms; requested progress; the support bundle; generated revision lines; the
optional local efficiency adapter, disabled by default; the guides for the three ways to run Vericairn and for
LangGraph, CrewAI and AutoGen agents; and the public example estate. Its
[release notes](docs/releases/0.9.1/release-notes.md) and [documentation set](docs/releases/0.9.1/README.md)
describe what shipped and its limits, and [0.9.0's](docs/releases/0.9.0/release-notes.md) describe the
release it patches.

## 0.9.2 — planned

A goal can run a declared flow: each step's position comes from the records, each step is routed to its role
one step per command, and a run stops only where a person holds the role, each reserved step showing its exact
command only when it is next. The software pack declares the release and estate upgrade flows: the traps of
earlier releases are checked as preconditions, each step hands on exact outputs that the next step pins, with
independent review where it matters and failure routes that never lose work, the documentation set assembles
itself, and 0.9.2's own release runs as a goal. An estate signs its history: checkpoints list every record it
holds, so a removed, altered or backdated record no longer passes unnoticed, and a revoked device, a replaced
key or a narrowed grant accounts only for what came before its closing. Clients hear what changed through one
event stream that each follows from where it left off, instead of polling, and an agent is told when what it
reads changes. An admitted agent on another machine works an estate over MCP's Streamable HTTP transport, each
call judged as its API operation for that device. An agent finds the decisions and documents its brief needs
by searching the estate, with every selected source pinned and nothing it may not read revealed. An upgrade
replaces an older background runtime, so the estate is served only by the release it pins. Security reports
reach the operator within minutes and hold a release until they are triaged, an acceptance rests only on
reviews that passed, and a stopped operator action can be finished. Every distributable is built on .NET SDK
10.0.404 and embeds .NET and ASP.NET Core runtime 10.0.15.

## 0.9.3 — planned

Remote grows from attention and decisions to supervision: from the phone you steer running work, see the
estate's nodes and connections and hand a task to Workbench; Remote notifies you over the local network,
reminds you before a supervised window opens, handles lost, replaced and shared phones and opens readable
records. From Workbench or the phone, a saved prompt can be sent to a running agent in one step; it steers the
agent without widening what the agent may do. Paired devices reach the runtime from any network through a
relay that forwards bytes it cannot read. Agents keep working on a product between releases as standing
agents, within their sponsor's caps, and every output is a possibility a person adopts. An estate follows the
GitHub conversations it subscribes to and the reports it files with other projects, a release's advisories are
drafted from its Security section for you to publish, and every release's bill of materials is checked
against published advisories. Installers open without warnings on macOS and Windows, Vericairn installs with
one command through Homebrew, winget or a checked install script, and you can run Vericairn as an always-on
server for your estates.

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
