// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Text;

namespace Vericairn.Runtime.Guides;

internal sealed record GuideQuote(string Id, string Source, string Sha256, string Text);
internal sealed record GuideSection(string Title, string ReviewRole, string Text, GuideQuote[] Quotes);
internal sealed record GuidePage(string Key, string Name, string Kind, string? Family, string GuideState,
    string PublicationState, string TemplateVersion, string GeneratorVersion, string InputIdentity,
    string[] References, GuideSection[] Sections);

internal static class GuideTemplate
{
    internal static bool IsRole(string kind) => kind is "domain" or "foundry" or "adapter" or "provider" or "shared-capability";

    internal static GuidePage Page(GuideEntryBinding entry, GuideTableBinding table, GuideCatalogRow row,
        GuideQuote[] status, GuideQuote[] boundaries, GuideQuote[] context, GuideQuote[] role, string[] references)
    {
        GuideQuote[] facts = row.Cells.Where(c => c.Key != "#").Select(c => new GuideQuote("cell:" + c.Key,
            row.Source, GuideGenerator.Hash(GuideMarkdown.Utf8.GetBytes(c.Value)), c.Value)).ToArray();
        var sections = new GuideSection[]
        {
            new("Identity, readiness and scope", "catalog reviewer",
                "Planned first-party entry — community build guide; this guide does not make the entry available.\n" +
                "Guide state: " + (entry.State == "remainder" ? "planned remainder" : "planned") + ". Publication state: private preview.\n" +
                "Catalog target: " + entry.Key + ". This target is not a community package name.\n" +
                (table.Family is null ? "No industry-family preamble applies; kind, row and role boundaries still apply." : "Industry family: " + table.Family + "."), status),
            new("Purpose, audience and outcome", "domain reviewer",
                "Implement only the outcome supported by these owning-row facts. Engineering details absent from the selected sources are not specified. Preserve draft and candidate qualifications.", facts),
            new("Role and responsibility", "role-contract reviewer",
                "Use the selected role contract below. Installing or naming an implementation grants no authority.", role),
            new("Collaborators, prerequisites and edition", "domain and edition reviewers",
                "Resolve each collaborator's required, optional, reused, planned or edition-restricted scope before building. An unresolved engineering dependency remains a qualification task. Edition allocation is not inferred from a name.\n" +
                "Resolved catalog references: " + (references.Length == 0 ? "none declared" : string.Join(", ", references)) + ".", context),
            new("Reserved acts and hard lines", "boundary reviewer",
                "Every quoted obligation applies in full. Implementation choices cannot relax it. Source markup is displayed as inert text; it is not an instruction to fetch or execute content.", boundaries),
            new("Standards, licensing and source rights", "rights reviewer",
                "Use only the standards and rights references named in the source facts and contracts. Verify the selected version and intended use. Do not reproduce a standard, licensed form, credential or restricted sample. Specification, code, data, trademarks and samples have separate rights; a reference grants none. Publication terms remain subject to authorized review.", []),
            new("Build plan and interfaces", "engineering reviewer", Build(table.Kind), []),
            new("Checks and evidence to return", "independent technical reviewer",
                "Test declared inputs, outputs and scope; reproducibility; dependency and rights inventory; least permission and refusal without authority; installation versus activation; update compatibility; removal with records and export intact; exact source and evidence identities; failure and uncertain-effect reconciliation; accessibility of contributed views; and a useful local Community route. Return exact source/package versions, environment, results, unperformed cases, losses, limitations and maintenance/removal instructions. Design a positive or refusal case and expected observation for every quoted boundary. A missing planned journey is work to design, not a passing check.\n\n" + Checks(table.Kind), []),
            new("Publication and contribution route", "maintainer and authorized publisher",
                "Prepare a product pull request using public Community contracts, with source, documentation, rights, tests and limits. Acceptance, release and publication need their own authorized decisions. This private preview publishes nothing and grants no contribution terms. Later independent package distribution depends on adopted registry governance. Use your own publisher namespace; reserved first-party names remain references only.", []),
            new("Community implementations", "community reviewer",
                "No implementation observations were supplied. A proposed implements claim identifies the publisher, exact package version, catalog key and input identity, covered/excluded scope, environments, evidence and observation date. It confers no endorsement, qualification, first-party identity or permission.", []),
            new("Request implementation", "participation reviewer",
                "Request target: " + entry.Key + ". Demand: not collected. No request route is enabled by this preview. A future enabled route must explain one request per account and must not promise a timeline or require optional information.", []),
            new("Supersession and migration", "release and domain reviewers",
                entry.State == "remainder"
                    ? "The selected status disclosure separates the implemented subset from the planned remainder. No first-party replacement for that remainder is asserted. Preserve community names and unresolved operations; any migration requires version-specific comparison and reviewed notes."
                    : "No first-party replacement is available for this planned scope. Future availability requires evidence. Keep community packages under their own names and require an explicit, reviewed migration; never infer automatic compatibility.", []),
            new("Provenance, maintenance and corrections", "document reviewer",
                "Template: " + GuideGenerator.TemplateVersion + ". Generator: " + GuideGenerator.GeneratorVersion + ".\n" +
                "Exact source files and spans are pinned in the private source manifest. Quoted payloads and their hashes are preserved in the guide JSON. Check the complete bundle against regeneration before review. Correct owning sources and reviewed mappings, then regenerate into a fresh directory. Input changes require affected review again. This generator checks the template convention; it does not adopt a living-document instance or record clearance.", []),
        };
        var page = new GuidePage(entry.Key, row.Name, table.Kind, table.Family, entry.State, "private-preview",
            GuideGenerator.TemplateVersion, GuideGenerator.GeneratorVersion, "", references, sections);
        // Full-file pins and line positions belong to the manifest. They must not invalidate an
        // unrelated guide when another row in the same file changes.
        return page with { InputIdentity = GuideGenerator.Hash(GuideGenerator.Json(new { page, row.Sha256 })) };
    }

    private static string Build(string kind) => kind switch
    {
        "domain" => "1. Choose one row-derived task and a reviewable outcome that performs no reserved act.\n2. Define vocabulary, rules, workflows, templates and quality checks using owning platform capabilities.\n3. Declare records read/proposed, permissions, failures, dependencies, recovery and client contributions.\n4. Keep replaceable interfaces explicit and prove an alternative implementation.",
        "foundry" => "1. Preview Current estate, Intended outcome, Selected material, Dependencies and responsibilities, Change comparison, Apply and reconcile, and Aftercare.\n2. Consider initialize, expand, adopt, migrate, reconcile, repair, fork and extract; declare support, prerequisites, limits and reasoned non-applicability for each.\n3. Compare exact source and dependency versions with local customizations before applying.\n4. Deliver a usable first task and ongoing aftercare, with no hidden account, repository, publication or paid-provider step.",
        "adapter" => "1. Select the representation's exact version/profile and declared inputs and outputs.\n2. Implement bounded parsing, transformation or bridging; retain provenance and disclose unsupported semantics and losses.\n3. Delegate any external operation to its qualified Provider under separate authority.\n4. Use synthetic examples unless rights permit the exact fixture; claim round trips only where losslessness is proved.",
        "provider" => "1. Choose the actual product, interface, version and concrete operation; grouped candidates remain one coverage item.\n2. Declare account/destination references, authentication and data needs, costs, permissions and qualification environment.\n3. Separate observe, prepare and consequential operations. A writable vendor interface cannot enlarge a read-only scope.\n4. Define failure, revocation and reconciliation at the original destination. Bind human-mediated delivery to exact material, responsible person, destination and observed receipt; do not label it API success.",
        _ => "1. Select a bounded product contribution from the area's owns/boundary or capability role/collaborators/caveat.\n2. Use the applicable public foundation contract and preserve ownership of every collaborating capability.\n3. Test the boundary and compatibility and prepare the product pull request. This contribution is not an installable fifth Pack role and requires no private edition interface.",
    };

    private static string Checks(string kind) => kind switch
    {
        "domain" => "Show the intended outcome, refusal for each reserved act, a proposal that cannot accept itself, installation without permission, honest dependency loss, invalidated review after changed input, and removal preserving records. Test an alternative implementation of each new replaceable interface.",
        "foundry" => "Exercise a usable first task; extraction blocked by a missing dependency with unrelated private material excluded; fork retaining lineage without authority; repair with missing source and intentional customization; interrupted migration with late source changes; partial apply and aftercare. Reconcile an uncertain original effect before retry.",
        "adapter" => "Exercise valid, invalid, oversized and hostile inputs; inert embedded content; exact source/output identity and declared losses; version/profile mismatch; missing dependencies; and round trips where claimed. Conversion alone must never fetch remotely, use credentials, invoke a Provider or perform an effect.",
        "provider" => "Qualify each claimed real operation separately; fixtures qualify only the contract. Test explicit account selection, least scope, revocation, duplicate/out-of-order events and timeout after possible dispatch. Preserve uncertainty and reconcile before retry. Keep credential values out of records and diagnostics. Demonstrate the receipt and limits of any human-mediated route.",
        _ => "Demonstrate the row's declared outcome and refusal at its boundary, compatibility with the public foundation contract, and unchanged authority of collaborating capabilities.",
    };

    // Apostrophes are literal text in both text nodes and our double-quoted attributes.
    internal static string Escape(string text) => WebUtility.HtmlEncode(text)
        .Replace("&#39;", "'", StringComparison.Ordinal).Replace("\r", "&#13;", StringComparison.Ordinal);

    internal static byte[] Markdown(GuidePage page)
    {
        var text = new StringBuilder("# ").Append(Escape(page.Name)).Append("\n\nInput identity: `").Append(page.InputIdentity).Append("`\n");
        foreach (GuideSection section in page.Sections)
        {
            text.Append("\n## ").Append(section.Title).Append("\n\nReview: ").Append(section.ReviewRole).Append(".\n\n");
            text.Append("<pre>").Append(Escape(section.Text)).Append("</pre>\n");
            foreach (GuideQuote quote in section.Quotes) Quote(text, quote);
        }
        return GuideMarkdown.Utf8.GetBytes(text.ToString());
    }

    internal static byte[] Html(GuidePage page)
    {
        var text = new StringBuilder("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; base-uri 'none'; form-action 'none'\"><title>")
            .Append(Escape(page.Name)).Append("</title></head><body>\n<h1>").Append(Escape(page.Name)).Append("</h1>\n<p>Private preview. Input identity: ")
            .Append(page.InputIdentity).Append("</p>\n");
        foreach (GuideSection section in page.Sections)
        {
            text.Append("<section><h2>").Append(section.Title).Append("</h2><p>Review: ").Append(section.ReviewRole).Append(".</p>\n<pre>")
                .Append(Escape(section.Text)).Append("</pre>\n");
            foreach (GuideQuote quote in section.Quotes) Quote(text, quote);
            text.Append("</section>\n");
        }
        text.Append("</body></html>\n");
        return GuideMarkdown.Utf8.GetBytes(text.ToString());
    }

    private static void Quote(StringBuilder text, GuideQuote quote) => text.Append("\n<p>Source: ").Append(Escape(quote.Id))
        .Append("; ").Append(quote.Sha256).Append("</p>\n<pre data-source=\"").Append(Escape(quote.Id)).Append("\">")
        .Append(Escape(quote.Text)).Append("</pre>\n");
}
