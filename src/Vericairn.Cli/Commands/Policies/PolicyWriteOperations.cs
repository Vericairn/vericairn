// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using Vericairn.Protocol;
using Vericairn.Records.Canonical;
using Vericairn.Records.Hashing;

namespace Vericairn.Cli;

internal sealed class PolicyWriteOperations(CommandContext context)
{
    internal int Declare(string root, string by, string body, bool json) => Change(root, by, body, null, json, (api, request) => api.PolicyDeclare(request));
    internal int Revise(string root, string key, string expected, string by, string body, bool json) => Change(root, by, body, expected, json, (api, request) => api.PolicyRevise(key, request));
    internal int Retire(string root, string key, string expected, string by, string body, bool json) => Change(root, by, body, expected, json, (api, request) => api.PolicyRetire(key, request));
    internal int Except(string root, string key, string expected, string by, string body, bool json) => Change(root, by, body, expected, json, (api, request) => api.PolicyExcept(key, request));
    internal int RevokeException(string root, string key, string id, string expected, string by, string body, bool json) => Change(root, by, body, expected, json, (api, request) => api.PolicyRevokeException(key, id, request));
    internal int WritingAdd(string root, string by, string body, bool json) => Change(root, by, body, null, json, (api, request) => api.WritingExceptionAdd(request));
    internal int WritingRemove(string root, string id, string by, string expected, string reason, string? basis, bool json) => basis is null
        ? context.WithApi(root, api => context.ReportOutcome(api.WritingExceptionRemove(id, new(by, expected, reason)), json))
        : context.WithBody(basis, body => context.WithApi(root, api => context.ReportOutcome(api.WritingExceptionRemove(id, new(by, expected, reason, body)), json)));
    internal int Place(string root, string key, string by, string expected, string expectedSelection, string? releasePlan, string? backlog, bool json) =>
        context.WithApi(root, api => context.ReportOutcome(api.WorkPlace(key, new(by, expected, expectedSelection, releasePlan, backlog)), json));

    private int Change(string root, string by, string path, string? expected, bool json, Func<IEstateApi, PolicyChangeRequest, WriteOutcome> submit) =>
        context.WithBody(path, body => context.WithApi(root, api => context.ReportOutcome(submit(api,
            new(by, body, expected ?? ContentHash.Sha256(Encoding.UTF8.GetBytes(CanonicalJson.Serialize(body))))), json)));
}
