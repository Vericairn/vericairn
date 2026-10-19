// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Text;
using Vericairn.Records.Hashing;
using Vericairn.Runtime.Policies;

namespace Vericairn.Runtime.Tests;

/// <summary>An installed policy implementation is named without reading its assembly's file, which a single-file application lacks.</summary>
public sealed class PolicyImplementationDigestTests
{
    [Fact]
    public void AnImplementationIsNamedByItsAssemblyAndDeterministicModuleIdentity()
    {
        Assembly runtime = typeof(PolicySources).Assembly;
        string expected = ContentHash.Sha256(Encoding.UTF8.GetBytes(
            "vericairn.policy-implementation/1|" + runtime.GetName().Name + "|" + runtime.ManifestModule.ModuleVersionId.ToString("N")));
        Assert.Equal(expected, PolicySources.Digest(runtime));
        Assert.Equal(expected, PolicySources.Digest(runtime));
        Assert.NotEqual(expected, PolicySources.Digest(typeof(ContentHash).Assembly));
        Assert.Throws<ArgumentNullException>(() => PolicySources.Digest(null!));
    }
}
