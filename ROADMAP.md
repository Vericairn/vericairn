# Vericairn roadmap

This page shows where Vericairn is headed, release by release. Everything after the current published
release is planned, not promised, and has no dates. Each release's notes and documentation set are the
record of what shipped.

## 0.9.0 — current published release

0.9.0 follows 0.8.4. Remote, the phone companion for iOS and Android, brings pairing, attention and work to
your phone on the local network, with decisions and acceptances signed by the phone's presence key and an
estate's policy and recurring-duty findings handed to the desktop for review; it is a developer build you
make from source, not in an app store. Releases follow lines and channels, an estate follows a release line,
and a release cadence is a recurring duty; under a disclosure hold, Vericairn's checked push refuses a fix's
held description in a public repository until its release, and configured blocked terms are refused in
records and generated documents. Agents report progress
when their configuration asks for it, with a signal when a report is overdue. A support bundle collects what
a support case needs, with secrets and paths removed, and shows you everything in it before you share it, and
design documents' revision lines are generated from the revisions the estate records. The optional local
efficiency adapter is built and disabled by default, for the operator to admit on a machine. Guides show how
to run Vericairn with people only, with people and agents, and with agents only, and how to connect LangGraph,
CrewAI and AutoGen agents over MCP, and a public example estate and app show the whole loop on GitHub and are
upgraded with every release. Community build guides and the proposed public catalog repository are prepared
locally; publishing them and opening implementation requests remain separate decisions. `status` stays fast
as an estate grows, the release check gives up within seconds instead of holding a command, and a run whose
settlement was interrupted can still settle. Each worker run has its own temporary directory, a charter that
requires signatures changes only by the root operator's signed revision, and a document clears only as its
declaration allows, as the release notes' Security section describes. This release's cycle also designed the
editions and their services, the contract for foundries with the first software foundries, and the website,
web application, book, book series and world-building domains, among designs whose builds follow in later
releases. Its [release notes](docs/releases/0.9.0/release-notes.md) and
[documentation set](docs/releases/0.9.0/README.md) describe what shipped and its limits.

## 0.9.1 — planned

A security patch. Every distributable is built on .NET SDK 10.0.403 and embeds .NET and ASP.NET Core runtime
10.0.14, replacing the 10.0.11 that 0.9.0 embeds. Policy evaluation reads historical repository content in
batches, so estates that record maintenance duties keep fast writes and checks. A worker run can prepare its
workspace before the agent starts, a decision cannot be dated after its own record, and the bill of materials
names the release's version.

## 0.9.2 — planned

Remote grows from attention and decisions to supervision: from the phone you ask for running work to be
paused, cancelled or contained and see the observed result, see the estate's nodes and connections, and hand a
task to Workbench; Remote notifies you over the local network, reminds you before a supervised window opens,
handles lost, replaced and shared phones and opens readable records. From Workbench or the phone, a saved
prompt can be sent to a running agent in one step; it steers the agent without widening what the agent may
do. A goal can run a declared flow: each step's position, and each required item's provenance, evidence and
review, comes from the records, agents carry the steps they may take, and each step that waits on a person
shows its exact command only when it is next. Clients hear what changed through an event stream instead of
polling. An upgrade replaces an older background runtime, so the estate is served only by the release it
pins. An agent finds the decisions and documents its brief needs by searching the estate, with every selected
source pinned and nothing it may not read revealed. Vericairn installs with one command through Homebrew,
winget or a checked install script.

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
