// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.RegularExpressions;

namespace Vericairn.Runtime.Workers;

public static partial class CommandAudit
{
    internal static TimeSpan RetryLimit { get; } = TimeSpan.FromSeconds(5);

    // Retry the matching operation itself, before a caller consumes its results or mutates shell state.
    // Materialize matches here: MatchCollection otherwise defers work beyond the timeout handler.
    private static bool IsMatch(Regex pattern, string input) => MatchWithRetry(pattern, regex => regex.IsMatch(input));
    private static Match Match(Regex pattern, string input, int start) => MatchWithRetry(pattern, regex => regex.Match(input, start));
    private static Match[] Matches(Regex pattern, string input) => MatchWithRetry(pattern, regex => regex.Matches(input).Cast<Match>().ToArray());
    private static string Replace(Regex pattern, string input, MatchEvaluator replacement) => MatchWithRetry(pattern, regex => regex.Replace(input, replacement));
    private static string Replace(Regex pattern, string input, string replacement) => MatchWithRetry(pattern, regex => regex.Replace(input, replacement));

    internal static T MatchWithRetry<T>(Regex pattern, Func<Regex, T> match)
    {
        try { return match(pattern); }
        catch (RegexMatchTimeoutException)
        {
            try { return match(new Regex(pattern.ToString(), pattern.Options, RetryLimit)); }
            catch (RegexMatchTimeoutException exception) { throw new AuditTimeoutException(exception); }
        }
    }

    // A regex already received its one retry; an enclosing command check must not repeat it again.
    private sealed class AuditTimeoutException(Exception inner) : TimeoutException("An invocation pattern check timed out after retrying.", inner);
}
