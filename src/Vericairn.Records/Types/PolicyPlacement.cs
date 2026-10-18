// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

internal static class PolicyPlacement
{
    internal static void Validate(ValidationContext context)
    {
        BodyReader body = context.Body;
        if (body.Since(3, "releasePlan") && body.Nested("releasePlan", false) is { } plan)
        {
            PolicyRecords.IdentityField(context, plan, "product", "product");
            plan.Text("version", true, 100, Patterns.SemanticVersion);
            plan.NoOtherKeys();
        }
        if (body.Since(3, "placement") && body.Nested("placement", false) is { } placement)
        {
            switch (placement.Enum("kind", true, "release", "backlog"))
            {
                case "release":
                    PolicyRecords.IdentityField(context, placement, "carrier", "work-order");
                    PolicyRecords.IdentityField(context, placement, "product", "product");
                    placement.Text("version", true, 100, Patterns.SemanticVersion);
                    break;
                case "backlog": PolicyRecords.Key(placement); break;
            }
            placement.NoOtherKeys();
        }
    }
}
