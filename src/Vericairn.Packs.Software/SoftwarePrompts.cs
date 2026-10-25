// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using Vericairn.Protocol;
using Vericairn.Runtime.Service;

namespace Vericairn.Packs.Software;

/// <summary>The software pack's reviewed prompts.</summary>
public static class SoftwarePrompts
{
    /// <summary>Exact definitions versioned with this pack.</summary>
    public static IReadOnlyList<PromptRow> All { get; } = Create();

    private static PromptRow[] Create()
    {
        string version = typeof(SoftwarePrompts).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        return
        [
            PromptCatalog.BuiltIn("software", version, PromptCatalog.Definition("review-change", "Review a change", "Review an assigned change against its stated behavior.", "Software/Review", "Review {change} within the assigned scope. Explain observed defects and missing evidence with precise references; retain the existing acceptance boundary.", "change", "The already permitted change to inspect.", "reviewer", "operator"), draft: false),
            PromptCatalog.BuiltIn("software", version, PromptCatalog.Definition("plan-tests", "Plan useful tests", "Select checks for the assigned behavior.", "Software/Testing", "For {behavior}, identify checks of externally observable behavior and relevant regressions within the assignment's existing time and effect limits.", "behavior", "The assigned behavior to verify."), draft: false),
            PromptCatalog.BuiltIn("software", version, PromptCatalog.Definition("explain-failure", "Explain a failure", "Use existing evidence to explain a failing check.", "Software/Testing", "Explain the available evidence for {failure}. Distinguish the observed failure from possible causes and propose the next permitted check.", "failure", "The failure already in scope."), draft: false),
            PromptCatalog.BuiltIn("software", version, PromptCatalog.Definition("check-compatibility", "Check compatibility", "Inspect the compatibility of an assigned interface change.", "Software/Review", "Inspect {change} for compatibility with existing callers and stored data. State what the evidence establishes and what remains unknown; request an amendment for additional scope.", "change", "The assigned interface or data change."), draft: false),
        ];
    }
}
