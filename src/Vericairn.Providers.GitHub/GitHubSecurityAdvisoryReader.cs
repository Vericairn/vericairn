// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Providers.GitHub;

/// <summary>
/// Reads a repository's security advisories, drafts and private reports included, through bounded gh processes using the
/// operator's own login, read-only (OPS-11). It never posts, never reads or stores a credential, never passes GitHub's text on,
/// and keeps no diagnostic output: each advisory leaves as its identifier, state, severity and the hash of its response.
/// </summary>
public sealed class GitHubSecurityAdvisoryReader : ISecurityAdvisoryReader
{
    /// <summary>The provider id a product's security-reports declaration names.</summary>
    public const string ProviderId = "github-security-advisories";

    /// <summary>The share of the core allowance kept for the person's own use: a read starts only above a fifth of the limit.</summary>
    internal const int ReserveDivisor = 5;

    /// <summary>The fewest core requests a read leaves for the person's own use, whatever the limit.</summary>
    internal const int MinimumReserve = 100;

    /// <summary>Advisories asked for in one page.</summary>
    internal const int PageSize = 100;

    /// <summary>The most pages one read follows: 3,000 advisories, beyond which the listing is refused as too large to be complete.</summary>
    internal const int MaxPages = 30;

    /// <summary>The most characters one page's response may hold, headers included; a larger one fails rather than being truncated.</summary>
    internal const int PageCharacters = 16 * 1024 * 1024;

    /// <summary>The most characters all of one read's pages may hold together.</summary>
    internal const long ListingCharacters = 96L * 1024 * 1024;

    /// <summary>The classic token scopes that let a login see a repository's draft and private advisories.</summary>
    internal static readonly string[] AdvisoryScopes = ["repo", "repository_advisories:read", "repository_advisories:write"];

    private readonly Func<ProcessStartInfo, ProcessResult> run;

    /// <summary>Each gh invocation has a one-minute deadline and reads at most one page's bounded output.</summary>
    public GitHubSecurityAdvisoryReader() : this(info => BoundedProcess.Run(info, TimeSpan.FromMinutes(1), PageCharacters)) { }

    internal GitHubSecurityAdvisoryReader(Func<ProcessStartInfo, ProcessResult> run, long listingCharacters = ListingCharacters)
    {
        this.run = run;
        listingLimit = listingCharacters;
    }

    private readonly long listingLimit;

    /// <inheritdoc />
    public string Provider => ProviderId;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new("github-security-advisories-gh", ISecurityAdvisoryReader.Contract, "implemented", ["read"],
        ["Reads with the operator's own gh login, read-only; Vericairn never sees or stores the token, and posts nothing.",
         "Drafts and private reports are visible only to a repository's administrators through a token that carries the repo or repository_advisories:read scope, so another login, or a token whose scopes cannot be confirmed (a fine-grained or app token), is refused rather than reported as complete.",
         "Reads only while GitHub's core rate allowance keeps at least a fifth of its limit, and at least 100 requests, for the person's own use; each read costs one repository request and one per page of 100 advisories, at most 30 pages.",
         "Keeps each advisory's identifier, state, severity and response hash only, never its text; the estate's App replaces this reader once it exists."],
        Locations: ["github"]);

    /// <inheritdoc />
    public SecurityAdvisoryListing Read(string repository)
    {
        // A dot segment would let the API path name another endpoint; GitHub has no repository named so.
        if (repository is null || !Patterns.GitHubRepository.IsMatch(repository) || repository.Split('/')[1] is "." or "..")
            throw new ProviderRefusedException("The repository must be a GitHub owner/name.");
        long reserve = CheckLogin();
        using (JsonDocument described = Json(Body(Invoke("api", "repos/" + repository)).Body))
        {
            JsonElement root = described.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !string.Equals(Text(root, "full_name"), repository, StringComparison.OrdinalIgnoreCase))
                throw new ProviderRefusedException($"GitHub answered for {repository} under another name; it may have moved. Correct its repository resource, then read again.");
            if (!root.TryGetProperty("permissions", out JsonElement permissions) || permissions.ValueKind != JsonValueKind.Object
                || !permissions.TryGetProperty("admin", out JsonElement admin) || admin.ValueKind != JsonValueKind.True)
                throw new ProviderRefusedException($"The gh login is not an administrator of {repository}, so its drafts and private reports are invisible to it; log in with gh auth login as an administrator, then read again.");
        }

        // Each page is read and bounded on its own and followed by its cursor, so one large report cannot overflow the rest.
        var advisories = new Dictionary<string, SecurityAdvisory>(StringComparer.Ordinal);
        long characters = 0;
        string? after = null;
        for (int page = 1; ; page++)
        {
            if (page > MaxPages)
                throw new ProviderRefusedException($"{repository} lists more than {MaxPages * PageSize} advisories, more than one read follows, so no listing is reported.");
            ProcessResult result = Invoke("api", "-i", $"repos/{repository}/security-advisories?per_page={PageSize}" + (after is null ? string.Empty : "&after=" + after));
            characters += result.StandardOutput.Length;
            if (characters > listingLimit)
                throw new ProviderRefusedException($"{repository}'s advisories are larger than one read holds, so no listing is reported.");
            (Dictionary<string, string> headers, string body) = Body(result);
            using JsonDocument listed = Json(body);
            if (listed.RootElement.ValueKind != JsonValueKind.Array)
                throw new ProviderRefusedException("A page of GitHub's advisory listing was not a list; no listing is reported.");
            foreach (JsonElement item in listed.RootElement.EnumerateArray())
            {
                SecurityAdvisory advisory = Advisory(item);
                if (advisories.TryGetValue(advisory.Identifier, out SecurityAdvisory? seen) && seen != advisory)
                    throw new ProviderRefusedException("An advisory changed while the listing was read; read again.");
                advisories[advisory.Identifier] = advisory;
            }

            after = Next(headers);
            if (after is null) break;
            if (headers.TryGetValue("x-ratelimit-remaining", out string? left) && long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out long remaining) && remaining <= reserve)
                throw new ProviderRefusedException("GitHub's rate allowance reached the share kept for the person's own use before the listing ended, so no listing is reported; read again after it resets.");
        }

        return new(repository, advisories.Values.OrderBy(a => a.Identifier, StringComparer.Ordinal).ToArray());
    }

    // Only the identifier, two closed enumerations and a hash leave this method; every other field stays in the response.
    private static SecurityAdvisory Advisory(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new ProviderRefusedException("GitHub's advisory listing was incomplete; no listing is reported.");
        string identifier = Text(item, "ghsa_id");
        if (!Patterns.GitHubAdvisory.IsMatch(identifier))
            throw new ProviderRefusedException("GitHub listed an advisory without a valid identifier, so the listing cannot be reported as complete.");
        string state = Text(item, "state");
        string severity = Text(item, "severity");
        var advisory = new SecurityAdvisory(identifier, SecurityAdvisory.States.Contains(state, StringComparer.Ordinal) ? state : "unknown",
            SecurityAdvisory.Severities.Contains(severity, StringComparer.Ordinal) ? severity : "unknown", Hash(item));
        advisory.Validate();
        return advisory;
    }

    // The token behind the gh login must carry a scope that shows drafts and private reports, and the core allowance must keep its
    // share; the answer is the number of requests a read leaves untouched.
    private long CheckLogin()
    {
        (Dictionary<string, string> headers, string body) = Body(Invoke("api", "-i", "rate_limit"));
        if (!headers.TryGetValue("x-oauth-scopes", out string? granted))
            throw new ProviderRefusedException("The gh login's token scopes cannot be confirmed, as with a fine-grained or app token, including one in GH_TOKEN or GITHUB_TOKEN; log in with gh auth login, whose token carries the repo scope, or use a classic token with repo or repository_advisories:read, then read again.");
        string[] scopes = [.. granted.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
        if (!scopes.Any(scope => AdvisoryScopes.Contains(scope, StringComparer.Ordinal)))
            throw new ProviderRefusedException("The gh login's token carries neither the repo nor the repository_advisories:read scope, so drafts and private reports are invisible to it; run gh auth refresh --scopes repo, or log in with gh auth login, then read again.");
        using JsonDocument limits = Json(body);
        if (!limits.RootElement.TryGetProperty("resources", out JsonElement resources) || resources.ValueKind != JsonValueKind.Object
            || !resources.TryGetProperty("core", out JsonElement core) || core.ValueKind != JsonValueKind.Object
            || !core.TryGetProperty("limit", out JsonElement limitValue) || !limitValue.TryGetInt64(out long limit) || limit <= 0
            || !core.TryGetProperty("remaining", out JsonElement remainingValue) || !remainingValue.TryGetInt64(out long remaining) || remaining < 0)
            throw new ProviderRefusedException("GitHub's rate allowance could not be read, so no read was started.");
        long reserve = Math.Max(MinimumReserve, limit / ReserveDivisor);
        if (remaining <= reserve)
        {
            string reset = core.TryGetProperty("reset", out JsonElement resetValue) && resetValue.TryGetInt64(out long seconds)
                ? " after " + DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : " after it resets";
            throw new ProviderRefusedException($"GitHub's rate allowance is within the share kept for the person's own use ({remaining} of {limit} left), so no read was started; read again{reset}.");
        }

        return reserve;
    }

    // The next page's cursor from a Link header, or null on the last page. Only GitHub's own advisory listing, with one cursor of
    // base64 characters, is followed; anything else fails closed. The cursor is decoded, checked and encoded again, so it can carry
    // no separator, placeholder, flag or space into the route.
    private static string? Next(Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("link", out string? link)) return null;
        string[] nexts = [.. link.Split(',').Where(part => part.Contains("rel=\"next\"", StringComparison.Ordinal))];
        if (nexts.Length == 0) return null;
        if (nexts.Length > 1) throw new ProviderRefusedException("GitHub named more than one next page; no listing is reported.");
        string part = nexts[0];
        int open = part.IndexOf('<', StringComparison.Ordinal), close = part.IndexOf('>', StringComparison.Ordinal);
        if (open < 0 || close < open || !Uri.TryCreate(part[(open + 1)..close], UriKind.Absolute, out Uri? next) || next.Scheme != Uri.UriSchemeHttps
            || !string.Equals(next.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) || !next.AbsolutePath.EndsWith("/security-advisories", StringComparison.Ordinal))
            throw new ProviderRefusedException("GitHub named its next page somewhere other than its own advisory listing; no listing is reported.");
        string[] cursors = [.. next.Query.TrimStart('?').Split('&').Where(p => p.StartsWith("after=", StringComparison.Ordinal)).Select(p => p[6..])];
        if (cursors.Length != 1)
            throw new ProviderRefusedException($"GitHub named its next page with {(cursors.Length == 0 ? "no" : "more than one")} cursor; no listing is reported.");
        string cursor;
        try { cursor = Uri.UnescapeDataString(cursors[0]); }
        catch (UriFormatException) { throw new ProviderRefusedException("GitHub named its next page with an unreadable cursor; no listing is reported."); }
        if (cursor.Length is 0 or > 512 || cursor[0] == '-' || !cursor.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=' or '_' or '-'))
            throw new ProviderRefusedException("GitHub named its next page with a cursor this reader does not follow; no listing is reported.");
        return Uri.EscapeDataString(cursor);
    }

    private ProcessResult Invoke(params string[] arguments)
    {
        var info = new ProcessStartInfo("gh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["GH_PROMPT_DISABLED"] = "1";
        return run(info);
    }

    // GitHub's and gh's diagnostics are classified, never repeated: they may carry text from outside the estate.
    private static (Dictionary<string, string> Headers, string Body) Body(ProcessResult result)
    {
        if (!result.Started || result.Outcome == ProcessOutcome.NotStarted)
            throw new ProviderRefusedException("The GitHub CLI could not be started; install gh and log in as an administrator of the repository.");
        if (result.Outcome == ProcessOutcome.OutputLimitExceeded)
            throw new ProviderRefusedException("A page of GitHub's answer was larger than one page may be, so no listing is reported.");
        if (result.Outcome != ProcessOutcome.Exited)
            throw new ProviderRefusedException($"The GitHub CLI {result.Description}; GitHub could not be read completely.");
        if (result.ExitCode == 4)
            throw new ProviderRefusedException("The GitHub CLI is not logged in; run gh auth login yourself as an administrator of the repository. Vericairn stores no token.");
        if (result.ExitCode != 0)
        {
            string diagnostics = result.StandardOutput + "\n" + result.StandardError;
            if (diagnostics.Contains("rate limit", StringComparison.OrdinalIgnoreCase) || diagnostics.Contains("HTTP 429", StringComparison.Ordinal))
                throw new ProviderRefusedException("GitHub's rate limit stopped the read; read again once it resets.");
            if (diagnostics.Contains("HTTP 404", StringComparison.Ordinal) || diagnostics.Contains("HTTP 403", StringComparison.Ordinal) || diagnostics.Contains("HTTP 401", StringComparison.Ordinal))
                throw new ProviderRefusedException("GitHub refused the read for this login; the repository or its advisories are not readable by it.");
            throw new ProviderRefusedException("GitHub could not be read completely; check the network and the gh login, then read again.");
        }

        string output = result.StandardOutput;
        if (!output.StartsWith("HTTP/", StringComparison.Ordinal)) return ([], output);
        int split = output.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        int skip = 4;
        if (split < 0) { split = output.IndexOf("\n\n", StringComparison.Ordinal); skip = 2; }
        if (split < 0) throw new ProviderRefusedException("GitHub returned an unreadable response; no listing is reported.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in output[..split].Split('\n').Skip(1))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        return (headers, output[(split + skip)..]);
    }

    private static JsonDocument Json(string body)
    {
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { throw new ProviderRefusedException("GitHub returned an unreadable response; no listing is reported."); }
    }

    private static string Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    /// <summary>
    /// sha256 over one canonical form of the advisory's JSON: members by ordinal name, no insignificant whitespace, integers as
    /// themselves and other numbers in their shortest round-trip form, so gh's formatting never makes a new revision.
    /// </summary>
    internal static string Hash(JsonElement element)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false, SkipValidation = false }))
            Write(writer, element);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray()) Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String: writer.WriteStringValue(element.GetString()); break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out long integer)) writer.WriteNumberValue(integer);
                else if (element.TryGetDouble(out double number) && double.IsFinite(number)) writer.WriteNumberValue(number);
                else throw new ProviderRefusedException("GitHub returned a number this reader cannot represent; no listing is reported.");
                break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            default: writer.WriteNullValue(); break;
        }
    }
}
