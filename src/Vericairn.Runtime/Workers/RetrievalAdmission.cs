// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Records.Types;
using Vericairn.Runtime.Providers;

namespace Vericairn.Runtime.Workers;

/// <summary>
/// Public retrieval for research runs (RUN-04; decision 01M6CKCRMAJEAJ9KF3P3A6KEC7, with the interim check of decision
/// 01M7J80BQRFM3J7CS9BZ82BK8J). A run gets its harness's web tools only when its assignment's brief permits retrieval, and then only
/// when the records show that nothing it could carry out is private: the workspace's repository resource and the estate's own
/// repository resource are both public, and none of the run's records (its assignment's sources and step inputs, its work order's and
/// its goal's inputs) names a credential. Unknown counts as private: an estate repository no resource records, or one the runtime
/// cannot observe, refuses. What the run's operating-system user can read on this machine, its same-user reach, is not checked here
/// until confined reads are qualified (RUN-07); CODE-82's reach shows it in the run's record.
/// </summary>
internal static class RetrievalAdmission
{
    /// <summary>The one retrieval a brief may permit.</summary>
    internal const string Public = AssignmentType.PublicRetrieval;

    /// <summary>The logical route of a retrieval run's web tools in its inventory.</summary>
    internal const string Destination = ExecutionRecord.PublicWebDestination;

    /// <summary>Every reason the interim check refuses retrieval for this run, empty when it admits it.</summary>
    internal static IReadOnlyList<string> Refusals(EstateModel model, RecordEnvelope assignment, RecordEnvelope? order, RecordEnvelope? goal, RecordEnvelope resource,
        RepositoryObservation? estateRepository)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(resource);
        var reasons = new List<string>();
        RecordEnvelope workspace = model.Latest(resource);
        if (EstateModel.BodyString(workspace, "visibility") != "public")
            reasons.Add($"the workspace's repository resource {Name(workspace)} is {Visibility(workspace)}, not public");

        if (estateRepository is null)
        {
            reasons.Add("the estate's own repository could not be observed here, so the records cannot show that its repository resource is public");
        }
        else
        {
            RecordEnvelope[] matches = EstateResources(model, estateRepository);
            if (matches.Length == 0)
                reasons.Add("no repository resource records the estate's own repository by its remote or its root commit, so its visibility is unknown");
            else if (matches.Length > 1)
                reasons.Add($"several repository resources record the estate's own repository ({string.Join(", ", matches.Select(Name))}), so its visibility is ambiguous");
            else if (EstateModel.BodyString(matches[0], "visibility") != "public")
                reasons.Add($"the estate's repository resource {Name(matches[0])} is {Visibility(matches[0])}, not public");
        }

        foreach ((string where, string id) in Credentials(model, assignment, order, goal))
            reasons.Add($"{where} names credential reference {id}");

        return reasons;
    }

    /// <summary>The repository resources whose location records the estate's own repository: its origin remote, or its root commit.</summary>
    internal static RecordEnvelope[] EstateResources(EstateModel model, RepositoryObservation estate)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(estate);
        string? remote = Address(estate.Remote);
        return [.. model.Current("resource").Where(r => EstateModel.BodyString(r, "kind") == "repository" && r.Body.TryGetProperty("location", out JsonElement location)
            && location.ValueKind == JsonValueKind.Object
            && ((remote is not null && ((Text(location, "github") is string github && "github.com/" + github.ToLowerInvariant() == remote) || Address(Text(location, "url")) == remote))
                || (Text(location, "rootCommit") is string root && root == estate.RootCommit)))
            .GroupBy(r => model.Chain(r)[0], StringComparer.Ordinal).Select(g => g.Last())];
    }

    /// <summary>
    /// Whether an address a web tool was asked for is a public one: an absolute http or https address with no user information, whose
    /// host is not this machine, a private, link-local or otherwise special address, or a name that only a local resolver answers.
    /// A public name that resolves to a private address is not caught here.
    /// </summary>
    internal static bool PublicAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 2000 || !Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || string.IsNullOrEmpty(uri.IdnHost))
        {
            return false;
        }

        string host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? ip) && PublicIp(ip);
        }

        return host.Contains('.', StringComparison.Ordinal) && host != "localhost" && !host.EndsWith(".localhost", StringComparison.Ordinal)
            && !host.EndsWith(".local", StringComparison.Ordinal) && !host.EndsWith(".internal", StringComparison.Ordinal)
            && !host.EndsWith(".lan", StringComparison.Ordinal) && !host.EndsWith(".home.arpa", StringComparison.Ordinal);
    }

    private static bool PublicIp(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal && !ip.IsIPv6UniqueLocal && !ip.IsIPv6Multicast;
        }

        byte[] b = ip.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 0 || b[0] >= 224 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] is >= 64 and <= 127) || (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2)
            || (b[0] == 198 && b[1] is 18 or 19) || (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113));
    }

    // Every record reference the run's records carry, judged by its declared type and by what it resolves to here.
    private static IEnumerable<(string Where, string Id)> Credentials(EstateModel model, RecordEnvelope assignment, RecordEnvelope? order, RecordEnvelope? goal)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string where, JsonElement reference) in References(assignment, order, goal))
        {
            string? id = Text(reference, "id");
            if (id is null) continue;
            bool credential = Text(reference, "type") == "credential-reference"
                || (model.ById.TryGetValue(id, out RecordEnvelope? target) && target.Type == "credential-reference");
            if (credential && named.Add(where + " " + id)) yield return (where, id);
        }
    }

    private static IEnumerable<(string Where, JsonElement Reference)> References(RecordEnvelope assignment, RecordEnvelope? order, RecordEnvelope? goal)
    {
        string brief = $"the brief of assignment {assignment.Id}";
        if (assignment.Body.TryGetProperty("brief", out JsonElement body) && body.ValueKind == JsonValueKind.Object)
            foreach (JsonElement source in Items(body, "sources")) yield return (brief, source);
        if (assignment.Body.TryGetProperty("step", out JsonElement step) && step.ValueKind == JsonValueKind.Object)
            foreach (JsonElement input in Items(step, "inputs")) yield return ($"the step inputs of assignment {assignment.Id}", input);
        if (order is not null)
            foreach (JsonElement input in Items(order.Body, "inputs")) yield return ($"work order {EstateModel.BodyString(order, "key") ?? order.Id}", input);
        if (goal is not null)
            foreach (JsonElement input in Items(goal.Body, "inputs")) yield return ($"goal {goal.Id}", input);
    }

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement items)
        && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object) : [];

    private static string Name(RecordEnvelope resource) => $"{EstateModel.BodyString(resource, "name") ?? "unnamed"} ({resource.Id})";

    private static string Visibility(RecordEnvelope resource) => EstateModel.BodyString(resource, "visibility") switch
    {
        "private" => "private",
        "n/a" => "of no stated visibility (n/a)",
        _ => "of unrecorded visibility",
    };

    private static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement found)
        && found.ValueKind == JsonValueKind.String ? found.GetString() : null;

    // A remote or location as host and path, lower-case, without scheme, user, port, trailing slash or .git: comparable across https and ssh forms.
    private static string? Address(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string text = value.Trim();
        string host;
        string path;
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && uri.Scheme is "https" or "http" or "ssh" or "git")
        {
            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else if (text.IndexOf(':', StringComparison.Ordinal) is int colon and > 0 && !text.Contains("://", StringComparison.Ordinal))
        {
            // The scp-like form: user@host:owner/name.git
            string left = text[..colon];
            host = left[(left.LastIndexOf('@') + 1)..];
            path = text[(colon + 1)..];
        }
        else
        {
            return null;
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return host.Length == 0 || path.Length == 0 ? null : (host + "/" + path).ToLowerInvariant();
    }
}
