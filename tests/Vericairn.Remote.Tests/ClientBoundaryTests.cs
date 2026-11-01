// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using Vericairn.Remote;

namespace Vericairn.Remote.Tests;

/// <summary>Remote is a client of a runtime, never a host: its assembly closure holds no runtime, provider, adapter or store.</summary>
public sealed class ClientBoundaryTests
{
    [Fact]
    public void RemotesAssemblyClosureHoldsNoRuntimeProviderAdapterOrSqlite()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<Assembly>([typeof(RemoteViewModel).Assembly]);
        while (pending.Count > 0)
        {
            foreach (AssemblyName reference in pending.Pop().GetReferencedAssemblies())
            {
                if (reference.Name is not string name || !name.StartsWith("Vericairn", StringComparison.Ordinal) && !name.Contains("sqlite", StringComparison.OrdinalIgnoreCase) || !seen.Add(name)) continue;
                pending.Push(Assembly.Load(reference));
            }
        }

        Assert.Equal(["Vericairn.Presentation", "Vericairn.Protocol", "Vericairn.Records", "Vericairn.Sdk"], seen.Order(StringComparer.Ordinal));
    }
}
