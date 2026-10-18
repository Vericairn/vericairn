// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using Vericairn.Providers.GitHub;
using Vericairn.Records.Updates;
using Vericairn.Runtime.Providers;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;

namespace Vericairn.Runtime.Tests;

public sealed class FoundationProcessBoundaryTests
{
    [Fact]
    public void RuntimeConstructsOnlyTheLauncherProcessAndNeverStartsGitItself()
    {
        Instruction[] runtime = Instructions(typeof(ProviderRegistry).Assembly).ToArray();
        // The generic supervisor constructs the host-supplied launcher. Adapters may also use the bounded process primitive;
        // neither primitive chooses git, and no other runtime method may construct or retarget a process.
        Instruction construction = Assert.Single(runtime, i => i.Called is ConstructorInfo && i.Called.DeclaringType == typeof(ProcessStartInfo));
        Assert.Equal(typeof(RunSupervisor), construction.Method.DeclaringType);
        Assert.Equal(nameof(RunSupervisor.Start), construction.Method.Name);
        Assert.DoesNotContain(runtime, i => i.Called?.DeclaringType == typeof(ProcessStartInfo) && i.Called.Name is "set_FileName" or "set_Arguments");
        Assert.DoesNotContain(runtime, i => i.Called?.DeclaringType == typeof(Process) && i.Called.Name == nameof(Process.Start));
        Assert.All(runtime.Where(i => i.Called?.DeclaringType == typeof(ContainedProcess) && i.Called.Name == nameof(ContainedProcess.Start)),
            i => Assert.Contains(i.Method.DeclaringType, new[] { typeof(RunSupervisor), typeof(BoundedProcess) }));
        Assert.DoesNotContain(runtime.Where(i => i.Method.DeclaringType == typeof(RunSupervisor)
            || i.Method.DeclaringType == typeof(BoundedProcess) || i.Method.DeclaringType == typeof(ContainedProcess)),
            i => i.Text is not null && (i.Text.Equals("git", StringComparison.OrdinalIgnoreCase) || i.Text.Equals("git.exe", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void WorkerGitExecutionAndItsIsolatedObserverLiveInTheGitAdapter()
    {
        Assembly adapter = typeof(GitCommandAdapter).Assembly;
        foreach (Type implementation in new[]
        {
            typeof(GitWorktreeWorkspaces), typeof(WorkspaceGitCommand), typeof(WorkspaceGitScope),
            typeof(WorkspaceGitView), typeof(WorkspaceScopeProbe), typeof(WorkspaceScopeFiles), typeof(WorkspaceScopeAudit),
        }) Assert.Same(adapter, implementation.Assembly);
        Assert.IsType<GitWorktreeWorkspaces>(((ISourceAdapter)new GitCommandAdapter()).Workspaces);
    }

    [Fact]
    public void FoundationHasNoModuleReferenceOrGitHubRequestEndpointAndClientsRequireAReleaseSource()
    {
        foreach (Assembly foundation in new[]
        {
            typeof(ProviderRegistry).Assembly, typeof(PublishedRelease).Assembly,
            typeof(Vericairn.Protocol.IEstateApi).Assembly, typeof(ReleaseUpdates).Assembly,
        })
        {
            Assert.DoesNotContain(foundation.GetReferencedAssemblies(), reference => reference.Name is string name
                && (name.StartsWith("Vericairn.Adapters.", StringComparison.Ordinal)
                    || name.StartsWith("Vericairn.Providers.", StringComparison.Ordinal)
                    || name.StartsWith("Vericairn.Packs.", StringComparison.Ordinal)));
            Assert.DoesNotContain(Instructions(foundation), i => i.Text?.Contains("api.github.com", StringComparison.OrdinalIgnoreCase) == true);
        }
        Assert.Same(typeof(GitHubReleaseProvider).Assembly, typeof(GitHubReleaseSource).Assembly);
        Assert.Same(typeof(PublishedRelease).Assembly, typeof(IReleaseSource).Assembly);
        Assert.All(typeof(ReleaseUpdates).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            constructor => Assert.Contains(constructor.GetParameters(), parameter => parameter.ParameterType == typeof(IReleaseSource)));
    }

    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);

    private static IEnumerable<Instruction> Instructions(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        return assembly.GetTypes().SelectMany(type => type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            .SelectMany(Instructions);
    }

    // Decode operands rather than searching raw bytes: token bytes can look like opcodes, and generic calls need their context.
    private static IEnumerable<Instruction> Instructions(MethodBase method)
    {
        byte[] bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (int offset = 0; offset < bytes.Length;)
        {
            short value = bytes[offset++];
            if (value == 0xfe) value = (short)(0xfe00 | bytes[offset++]);
            OpCode code = Codes[value];
            int operand = offset;
            int size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * Integer(),
                _ => 4,
            };
            offset += size;
            if (code.OperandType == OperandType.InlineMethod)
                yield return new(method, method.Module.ResolveMethod(Integer(), method.DeclaringType?.GetGenericArguments(),
                    method.IsGenericMethod ? method.GetGenericArguments() : null), null);
            else if (code.OperandType == OperandType.InlineString)
                yield return new(method, null, method.Module.ResolveString(Integer()));

            int Integer() => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(operand));
        }
    }

    private sealed record Instruction(MethodBase Method, MethodBase? Called, string? Text);
}
