// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

namespace Vericairn.Protocol;

/// <summary>Requests one administrator approval code from the local runtime.</summary>
public sealed record McpAuthorizeRequest(string? Principal, string By, string Scope, int Days = 30, string? Renew = null);

/// <summary>The one-time approval returned only over the local socket when issued.</summary>
public sealed record McpApprovalIssued(string Code, string Endpoint, string Principal, string Scope, string ExpiresAt, int Attempts);
