// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    /// <inheritdoc />
    public IReadOnlyList<string> PolicyAccess(string principal) => Get<List<string>>("/v1/estate-policies/access?principal=" + Uri.EscapeDataString(principal))!;

    /// <inheritdoc />
    public WriteOutcome PolicyDeclare(PolicyChangeRequest request) => Post<PolicyChangeRequest, WriteOutcome>("/v1/estate-policies", request)!;

    /// <inheritdoc />
    public WriteOutcome PolicyRevise(string key, PolicyChangeRequest request) => Post<PolicyChangeRequest, WriteOutcome>("/v1/estate-policies/" + Uri.EscapeDataString(key) + "/revisions", request)!;

    /// <inheritdoc />
    public WriteOutcome PolicyRetire(string key, PolicyChangeRequest request) => Post<PolicyChangeRequest, WriteOutcome>("/v1/estate-policies/" + Uri.EscapeDataString(key) + "/retire", request)!;

    /// <inheritdoc />
    public WriteOutcome PolicyExcept(string key, PolicyChangeRequest request) => Post<PolicyChangeRequest, WriteOutcome>("/v1/estate-policies/" + Uri.EscapeDataString(key) + "/exceptions", request)!;

    /// <inheritdoc />
    public WriteOutcome PolicyRevokeException(string key, string id, PolicyChangeRequest request) => Post<PolicyChangeRequest, WriteOutcome>("/v1/estate-policies/" + Uri.EscapeDataString(key) + "/exceptions/" + Uri.EscapeDataString(id) + "/revoke", request)!;

    /// <inheritdoc />
    public WriteOutcome WritingExceptionAdd(PolicyChangeRequest request) => Post<PolicyChangeRequest, WriteOutcome>("/v1/writing-exceptions", request)!;

    /// <inheritdoc />
    public WriteOutcome WritingExceptionRemove(string id, WritingExceptionRemoveRequest request) => Post<WritingExceptionRemoveRequest, WriteOutcome>("/v1/writing-exceptions/" + Uri.EscapeDataString(id) + "/remove", request)!;

    /// <inheritdoc />
    public WriteOutcome WorkPlace(string key, WorkPlaceRequest request) => Post<WorkPlaceRequest, WriteOutcome>("/v1/work/" + Uri.EscapeDataString(key) + "/placement", request)!;

    /// <inheritdoc />
    public PolicyResponse Policies() => Get<PolicyResponse>("/v1/estate-policies")!;

    /// <inheritdoc />
    public PolicyResponse? Policy(string key) => Get<PolicyResponse>("/v1/estate-policies/" + Uri.EscapeDataString(key) + "/view", allowNotFound: true);

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
