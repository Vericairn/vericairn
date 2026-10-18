// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Reflection;
using System.Text.Json;
using Vericairn.Mcp;
using Vericairn.Presentation;
using Vericairn.Protocol;
using Vericairn.Records.Estate;
using Vericairn.Records.Model;
using Vericairn.Runtime.Workers;
using Vericairn.Sdk;

namespace Vericairn.Workbench.Tests;

public sealed class SharedApiValuesTests
{
    [Fact]
    public void EverySharedStringFieldKeepsItsOriginalValuesAndOrderWithoutWritableStorage()
    {
        using Stream snapshot = typeof(SharedApiValuesTests).Assembly.GetManifestResourceStream("Vericairn.Workbench.Tests.shared-api-values.json")!;
        Dictionary<string, string[]> expected = JsonSerializer.Deserialize<Dictionary<string, string[]>>(snapshot)!;
        Assembly[] assemblies = [typeof(RecordParser).Assembly, typeof(WorkerOptions).Assembly, typeof(McpServer).Assembly];
        Dictionary<string, FieldInfo> fields = assemblies.SelectMany(a => a.GetExportedTypes())
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => !f.IsLiteral && typeof(IEnumerable<string>).IsAssignableFrom(f.FieldType))
            .ToDictionary(f => f.DeclaringType!.FullName + "." + f.Name, StringComparer.Ordinal);

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), fields.Keys.Order(StringComparer.Ordinal));
        foreach ((string name, string[] values) in expected)
        {
            object shared = fields[name].GetValue(null)!;
            Assert.True(fields[name].IsInitOnly, name);
            Assert.Equal(values, Assert.IsAssignableFrom<IEnumerable<string>>(shared));
            AssertReadOnly<string>(shared);
            Assert.Equal(values, Assert.IsAssignableFrom<IEnumerable<string>>(shared));
            Assert.Equal(JsonSerializer.Serialize(values), JsonSerializer.Serialize(shared, shared.GetType()));
        }
    }

    [Fact]
    public void SharedSetsKeepTheirOrdinalMembershipAndCannotBeChanged()
    {
        IReadOnlySet<string>[] sets = [RecordParser.RequiredKeys, RecordParser.OptionalKeys, RecordParser.HashExcludedKeys,
            EstateLayout.AllowedTopLevel, EstateLayout.IgnoredTopLevel];
        foreach (IReadOnlySet<string> set in sets)
        {
            string first = set.First();
            Assert.True(set.Contains(first));
            Assert.False(set.Contains(first.ToUpperInvariant()));
            if (set is ISet<string> mutable)
            {
                Assert.Throws<NotSupportedException>(() => mutable.UnionWith(["changed"]));
                Assert.Throws<NotSupportedException>(() => mutable.ExceptWith([first]));
                Assert.Throws<NotSupportedException>(() => mutable.IntersectWith([]));
                Assert.Throws<NotSupportedException>(() => mutable.SymmetricExceptWith([first]));
            }
        }
    }

    [Fact]
    public void SharedDictionariesProtectTheirContentsAndKeepTheirOrder()
    {
        Assert.Equal(["publisher", "decider"], Routing.ReservedRoles.Keys);
        Assert.Equal(["publication", "decision"], Routing.ReservedRoles.Values);
        Assert.False(Routing.ReservedRoles.ContainsKey("Publisher"));
        AssertReadOnly<KeyValuePair<string, string>>(Routing.ReservedRoles);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)Routing.ReservedRoles)["publisher"] = "changed");

        Assert.Equal(["ask", "living-document", "work-order"], GoalPredicates.States.Keys);
        Assert.Equal(["open", "notified", "withdrawn", "answered"], GoalPredicates.States["ask"]);
        Assert.Equal(["current", "due"], GoalPredicates.States["living-document"]);
        Assert.Equal(["planned", "active", "paused", "submitted", "retired", "accepted", "accepted (earlier revision)"], GoalPredicates.States["work-order"]);
        AssertReadOnly<KeyValuePair<string, IReadOnlyList<string>>>(GoalPredicates.States);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, IReadOnlyList<string>>)GoalPredicates.States)["ask"] = []);
        Assert.All(GoalPredicates.States.Values, values => AssertReadOnly<string>(values));
    }

    [Fact]
    public void ExistingReadOnlyVocabulariesProtectTheirNestedValues()
    {
        AssertReadOnly<StateLanguage.Vocabulary>(StateLanguage.Vocabularies);
        Assert.All(StateLanguage.Vocabularies, vocabulary => AssertReadOnly<StateLanguage.StateWord>(vocabulary.States));
        AssertReadOnly<(string Method, string Path)>(OpenApiDocument.Routes);
        AssertReadOnly<(string File, string Harness)>(Orientation.Pointers);
        AssertReadOnly<string>(CommunicationResolution.Frequencies);
        AssertReadOnly<string>(CommunicationResolution.Depths);
        AssertReadOnly<string>(CommunicationPresentation.MandatoryKinds);
        AssertReadOnly<string>(ConnectionStates.All);
        AssertReadOnly<string>(ReviewWorkspace.Outcomes);
    }

    [Fact]
    public void JsonDefaultsAreReadOnlyAndPrivateCopiesKeepTheWireConventions()
    {
        Assert.True(ProtocolJson.Options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => ProtocolJson.Options.WriteIndented = true);
        var privateOptions = new JsonSerializerOptions(ProtocolJson.Options) { WriteIndented = true };
        Assert.False(ProtocolJson.Options.WriteIndented);
        Assert.True(privateOptions.WriteIndented);
        string[] values = ["first", "second"];
        Assert.Equal("{\"displayName\":\"sample\",\"values\":[\"first\",\"second\"]}",
            JsonSerializer.Serialize(new { DisplayName = "sample", Values = values }, ProtocolJson.Options));
    }

    private static void AssertReadOnly<T>(object value)
    {
        Assert.False(value is T[]);
        if (value is ICollection<T> collection)
        {
            Assert.True(collection.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => collection.Add(default!));
            Assert.Throws<NotSupportedException>(() => collection.Clear());
            Assert.Throws<NotSupportedException>(() => collection.Remove(default!));
        }
        if (value is IList<T> list && list.Count > 0)
        {
            Assert.Throws<NotSupportedException>(() => list[0] = list[0]);
            Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => list.Insert(0, list[0]));
        }
    }
}
