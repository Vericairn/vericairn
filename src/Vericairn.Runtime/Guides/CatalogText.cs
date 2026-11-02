// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Runtime.Guides;

internal static class CatalogText
{
    internal const string Readme = """
        # Community build guides

        Proposed home: Vericairn/catalog. This bundle is local preparation. Repository creation and each publication require the operator's separate decisions. No repository, site, request or subscription is created by generating it.

        Planned first-party entries have community build guides. A guide does not make an entry available; a community implementation retains its publisher and does not become first-party, qualified or supported by appearing here.

        Browse [entries](indexes/entries.md), [families](indexes/families.md) and [roles](indexes/roles.md). [Tracking issues](indexes/tracking-issues.json) contain only explicitly supplied public observations; an empty map means no observations were supplied, not that demand is zero.

        Request implementation is unavailable until the repository and its request route are authorized. There are no seeded issues. The provider importer and publication operation are planned; the local validator and reconciliation only evaluate supplied observations.

        Read [contribution and request instructions](CONTRIBUTING.md), [licenses](LICENSE.md) and [attribution](NOTICE.md). Public source renditions live under sources/, the template convention under templates/, and exact public identities under manifests/. The output manifest lists every other file; it excludes itself.
        """;

    internal const string License = """
        # Licenses

        Guide prose, catalog and contract renditions, navigation and template prose in this proposed repository are offered under Creative Commons Attribution 4.0 International (CC BY 4.0): https://creativecommons.org/licenses/by/4.0/legalcode . Attribute Vericairn and its contributors, link to these terms and indicate changes. See NOTICE.md for scope and attribution.

        Original code samples, when separately reviewed and included under samples/, use Apache License, Version 2.0 (Apache-2.0): https://www.apache.org/licenses/LICENSE-2.0 . No code samples are included in this bundle.

        These terms do not relicense first-party extensions, trademarks, standards, schemas, third-party assets or material merely referenced by a guide. References grant no rights to those materials. No warranty or endorsement is offered. Review the exact notices and source rights before deciding publication.
        """;

    internal const string Notice = """
        # Attribution and scope

        Community build guides and cleared catalog renditions: Vericairn and its contributors. Original template and generator: Vericairn contributors. Preserve this attribution, the applicable license and notices of changes when adapting the material.

        The public sources are a reviewed contribution view. They contain selected facts and boundary quotations, not private source files or estate history. Their public identities identify these renditions only. A source patch must be reconciled to the authoritative catalog and reviewed again before regeneration.

        No third-party standards, executable samples, credentials or private implementation observations are included. Named products and marks remain their owners' property. No sponsorship, authority or support relationship is implied.
        """;

    internal const string ContributionRoute = "Issues report corrections, missing requirements or build observations. Pull requests change sources, boundary mappings, role recipes, templates or separately reviewed samples; never hand-edit generated guides. Reconcile accepted patches to the authoritative catalog, review rights and boundaries, then regenerate. A public merge is publication and requires the operator's decision. Until the repository is authorized, use the permitted product contribution route. Community packages retain their own publisher names; a listing grants no first-party identity, qualification or support.";

    internal const string Contributing = """
        # Contributing

        Issues report corrections, missing requirements and build observations. Pull requests change sources/catalog, sources/contracts, templates or separately reviewed samples. Do not hand-edit guides, indexes or manifests. An output-only patch is returned to its owning source.

        Reconcile a public source patch to the matching authoritative catalog revision before regeneration; a changed basis requires rebase and review. The public source tree is not an independent status ledger. The builder reviews source fidelity and checks, and relevant domain and rights reviewers examine boundaries and licensing. The operator decides repository creation and exact-content publication. A public merge is itself publication. Contributions to prose use CC BY 4.0, and original code samples use Apache-2.0; contributors must have the necessary rights. Until this repository is authorized, the permitted product contribution route is https://github.com/Vericairn/vericairn .

        ## Request implementation

        A request informs priority and sets no timeline. Only what you enter in the request is collected, with the hosting account attribution needed to count it. Do not include personal or confidential data. Public requests are visible on the hosting platform before and after submission. Never include an email address, private estate details or diagnostic uploads.

        Look up the stable entry key in indexes/entries.json and indexes/tracking-issues.json. Only planned scope and explicit planned remainders are requestable. Available or retired scope points to its retained page and replacement. Unknown keys return to the entry index.

        Without a tracking issue, the first deliberate, valid request uses the shared Request implementation form. With one, use Add my request on that issue: submit a structured comment with the same form fields below. Ordinary discussion, reactions, stars, subscriptions and offers alone are not votes. The native form creates an issue; it cannot atomically enforce uniqueness or append to an existing issue. Maintainers must validate the key against the exact inventory before admitting a request.

        ### Add my request

        Use these headings and checkbox labels. Only Entry key and the deliberate Request action are required. Intended use is optional, at most 500 characters. Optional offers are not assignments. Leave Status notices unchecked unless you want notices.

        ```markdown
        ### Entry key
        <stable entry key>

        ### Request
        - [x] I request implementation of this entry.

        ### Intended use

        ### Offer to test
        - [ ] I offer to test.

        ### Offer to contribute
        - [ ] I offer to contribute.

        ### Status notices
        - [ ] I would like status notices.
        ```

        One active request is one vote per hosting account and entry. Edit the original request to update it. Uncheck Request or delete the original to withdraw; restoring a valid deliberate request restores at most one vote. A withdrawal stops any future pack-mediated notice eligibility. Reconciliation selects the latest attributable account-entry action, including edits and withdrawals, and deduplicates provider retries. The earliest valid issue creation time, then stable issue ID in ordinal order, selects the canonical issue. Authorized maintainers link and close duplicates; copied text from a bot cannot become a vote. Closed duplicates retain their original attribution and point to the canonical issue. Reopening them adds no vote.

        The hosting account's authenticated attribution establishes control of that account, not one unique person. Bots, apps and unverifiable submissions are excluded. Paid, incentivized and automated voting are prohibited. Review suspected abuse with correction and appeal; do not infer identity from display names or enrich profiles.

        Subscribe or unsubscribe using the hosting platform's controls. The optional notice checkbox records intent only: it does not enable a subscription, mention, email or marketing. Notices are not enabled by this bundle. Notifications caused by your own issue activity follow your hosting settings separately.

        The importer is planned. Until it is implemented and qualified, an authorized maintainer may supply attributable observations and mark aggregates manual with their observation window. An incomplete observation yields no fresh totals and leaves prior totals stale or unavailable, never zero. Do not publish voter lists, intended-use text, offers or consent records. Counts inform priorities; they place no work and promise no date.

        ## Publication and recovery

        Generate twice from the exact reviewed index, allowlist, selection, retirement metadata and optional issue map; compare complete membership and bytes. Inspect all sources, guides, notices and public manifests. Independent source, rights and disclosure review precedes publication.

        Compare the proposed bundle with an explicit local destination checkout. Review additions, changes and removals. Every previously published guide address must remain a guide or retirement page. Keep the destination's private Git metadata out of the bundle; never copy private estate history.

        Obtain the operator's separate decisions for repository creation and publication of the exact reviewed bundle to the exact destination. Publication and the provider importer are planned; these local tools cannot perform either. Keep request actions unavailable until the route is separately authorized and qualified. A later activation must review links, subscriptions, the form and reconciliation at that destination.

        After an authorized publication, observe the destination and compare exact membership and bytes before claiming it current. If publication fails or its effect is uncertain, reconcile the original destination before any retry. Preserve the last observed good revision, prepare a corrective bundle from reviewed sources, compare it and obtain a new publication decision. A repository observation says nothing about a separate site. Retirement preserves its address, public date/revision, replacement, migration limits and prior published history.
        """;

    internal const string Template = """
        # Build-guide template convention

        Template: vericairn.extension-ecosystem/build-guide/1. Generator: vericairn.guides/2. Public rendition: vericairn.catalog-bundle/1.

        Each active guide retains all thirteen sections from the guide generator: identity, purpose, role, collaborators, reserved acts, rights, build plan, checks, contribution, community implementations, requests, migration and provenance. Complete boundary quotations are allocated once; other sections point to their public source labels. role-recipes.json contains the original role-specific build and check guidance. Source markup remains inert text.

        Public renditions replace private provenance with public source references and manifests. Request actions remain unavailable during local preparation. Retirements use reviewed public disposition metadata and keep the same guide address. Template changes require review of affected sources and complete regeneration; they never grant publication authority.
        """;
}
