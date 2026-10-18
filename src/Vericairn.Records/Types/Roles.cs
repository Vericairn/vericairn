// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;

namespace Vericairn.Records.Types;

/// <summary>Role names a principal can hold.</summary>
public static class Roles
{
    /// <summary>The accountable human who owns the estate and accepts releases.</summary>
    public const string Operator = "operator";

    /// <summary>A principal that produces work (code, documents, records).</summary>
    public const string Builder = "builder";

    /// <summary>A principal that reviews other principals' work.</summary>
    public const string Reviewer = "reviewer";

    /// <summary>A principal admitted to contribute bounded work.</summary>
    public const string Contributor = "contributor";

    /// <summary>An automated service principal.</summary>
    public const string Service = "service";

    /// <summary>All role names.</summary>
    public static readonly ImmutableArray<string> All = [Operator, Builder, Reviewer, Contributor, Service];
}

/// <summary>Authority scope names with a defined meaning.</summary>
public static class Scopes
{
    /// <summary>Engineering inside the estate's repositories: code, tests, documents, records.</summary>
    public const string LocalEngineering = "local-engineering";

    /// <summary>Commit and push to the estate's repositories on non-protected branches, open pull requests.</summary>
    public const string RepositoryWrite = "repository-write";

    /// <summary>Record self-reviews and self-acceptances for ordinary (non-release, non-milestone) work orders.</summary>
    public const string SelfAcceptOrdinaryWork = "self-accept-ordinary-work";

    /// <summary>Accept other principals' work orders.</summary>
    public const string AcceptWork = "accept-work";

    /// <summary>Record principals, authority and admissions.</summary>
    public const string Administer = "administer";

    /// <summary>Spend: grant a goal's budget by activating or resuming a goal the grantee sponsors; the operator holds it without a grant.</summary>
    public const string Spending = "spending";

    /// <summary>Qualify and revoke exact harness versions from probes; only human grantees can exercise it.</summary>
    public const string Qualify = "qualify";

    /// <summary>Manage approved estate-policy definitions within explicit typed bounds.</summary>
    public const string ManageEstatePolicies = "manage-estate-policies";

    /// <summary>Grant and revoke finite policy exceptions within explicit typed bounds.</summary>
    public const string GrantPolicyExceptions = "grant-policy-exceptions";

    /// <summary>Add and remove exact spacing tokens within explicit typed bounds.</summary>
    public const string ManageWritingExceptions = "manage-writing-exceptions";

    /// <summary>All scope names with defined meaning.</summary>
    public static readonly ImmutableArray<string> Known = [LocalEngineering, RepositoryWrite, SelfAcceptOrdinaryWork, AcceptWork, Administer, Spending, Qualify, ManageEstatePolicies, GrantPolicyExceptions, ManageWritingExceptions];
}
