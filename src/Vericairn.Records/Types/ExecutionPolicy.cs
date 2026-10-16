// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Records.Types;

/// <summary>Format-2 execution defaults and cumulative restrictions, shared by rosters, goals, roles and assignments.</summary>
public static partial class ExecutionPolicy
{
    /// <summary>Portable selection fields. A value is an identity, never a local binding or a credential.</summary>
    public static readonly string[] Choices = ["model", "effort", "provider", "destination", "dataBoundary", "adapter"];

    /// <summary>Reads an optional execution policy from its parent.</summary>
    public static void Read(BodyReader parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent.Since(2, "execution") && parent.Nested("execution", false) is BodyReader policy)
        {
            Validate(policy);
        }
    }

    /// <summary>Checks choices and restrictions without interpreting one as permission to weaken another.</summary>
    public static void Validate(BodyReader policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.Nested("choices", false) is BodyReader choices)
        {
            ReadChoices(choices);
        }

        if (policy.Nested("restrictions", false) is BodyReader restrictions)
        {
            foreach (string key in new[] { "requiredModel", "requiredEffort", "exactRevision", "dataBoundary" })
            {
                restrictions.Text(key, false, 160, IdentityPattern(), "a portable execution identity");
            }

            foreach (string key in new[] { "permittedProviders", "permittedDestinations" })
            {
                foreach (string value in restrictions.StringArray(key, false, allowEmpty: false, maxItemLength: 160) ?? [])
                {
                    if (!IsIdentity(value))
                    {
                        restrictions.Error("execution.identity", key, "Execution identities contain no paths or environment values.");
                    }
                }
            }

            if (restrictions.Nested("maxCurrency", false) is BodyReader cost)
            {
                cost.Number("amount", true, 0, Canonical.CanonicalJson.MaxSafeInteger);
                cost.Text("unit", true, 3, CurrencyPattern(), "three uppercase currency letters");
                cost.NoOtherKeys();
            }

            restrictions.NoOtherKeys();
        }

        policy.NoOtherKeys();
    }

    /// <summary>Checks just the explicit choices, also used for run overrides.</summary>
    public static void ReadChoices(BodyReader choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        foreach (string key in Choices)
        {
            choices.Text(key, false, 160, IdentityPattern(), "a portable execution identity");
        }

        choices.NoOtherKeys();
    }

    /// <summary>Whether a value can be retained as an execution identity.</summary>
    public static bool IsIdentity(string value) => value is not null && IdentityPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:@+-]{0,159}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityPattern();

    [GeneratedRegex("^[A-Z]{3}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
