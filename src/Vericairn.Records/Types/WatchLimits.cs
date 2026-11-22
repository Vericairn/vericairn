// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Records.Types;

/// <summary>The bounded watching overrides shared by goal and roster limits; omitted fields inherit at launch.</summary>
internal static class WatchLimits
{
    public static void Read(BodyReader limits)
    {
        BodyReader? watch = limits.Nested("watch", false);
        if (watch is null)
        {
            return;
        }

        watch.Number("normalTokensPerMinute", false, 1, 1_000_000_000);
        watch.Number("burnMultiplier", false, 2, 100);
        watch.Number("burnMinimumTokens", false, 1, 1_000_000_000_000);
        watch.Number("burnWindowSeconds", false, 1, 86400);
        watch.Number("burnCapPercent", false, 1, 100);
        long? warning = watch.Number("loopWarning", false, 2, 1000);
        long? stop = watch.Number("loopStop", false, 3, 10000);
        long? window = watch.Number("loopWindow", false, 3, 10000);
        if ((warning is not null && stop is not null && warning >= stop)
            || (stop is not null && window is not null && stop > window)
            || (warning is not null && window is not null && warning >= window))
        {
            watch.Error("watch.limits", "loopStop", "Watching limits must satisfy loopWarning < loopStop <= loopWindow; omitted fields are checked after inheritance before launch.");
        }

        // From 0.9.4 (ID-11), additive in record format 3: how long a lapsed session holding a step stays lapsed before the runtime asks
        // about reassignment. A reader before 0.9.4 refuses it as an unknown key, and a format-2 record cannot carry it.
        if (watch.Since(3, "reassignAfterSeconds"))
        {
            watch.Number("reassignAfterSeconds", false, 60, 86_400);
        }

        watch.NoOtherKeys();
    }
}
