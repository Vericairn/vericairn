// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public PolicyResponse Policies() => Get<PolicyResponse>("/v1/estate-policies")!;

    /// <inheritdoc />
    public PolicyResponse? Policy(string key) => Get<PolicyResponse>("/v1/estate-policies/" + Uri.EscapeDataString(key), allowNotFound: true);

    /// <inheritdoc />
    public IReadOnlyList<PolicyKindDto> PolicyKinds() => Get<List<PolicyKindDto>>("/v1/estate-policies/kinds")!;

    /// <inheritdoc />
    public PolicyResponse PolicyEvaluate(string? asOf = null, string? release = null) => Get<PolicyResponse>(PolicyEvaluationPath(asOf, release))!;

    /// <inheritdoc />
    public WritingExceptionsResponse WritingExceptions() => Get<WritingExceptionsResponse>("/v1/writing-exceptions")!;

    /// <inheritdoc />
    public WritingExceptionsResponse? WritingException(string id) => Get<WritingExceptionsResponse>("/v1/writing-exceptions/" + Uri.EscapeDataString(id), allowNotFound: true);

    private static string PolicyEvaluationPath(string? asOf, string? release)
    {
        var query = new Dictionary<string, string?> { ["asOf"] = asOf, ["release"] = release };
        string suffix = string.Join('&', query.Where(p => p.Value is not null).Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value!)));
        return "/v1/estate-policies/evaluate" + (suffix.Length == 0 ? "" : "?" + suffix);
    }
}
