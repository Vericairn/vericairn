// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Tests;
using Vericairn.Runtime.Reading;

namespace Vericairn.Runtime.Tests;

public class LocalCompanionGoldenTests
{
    private const string At = "2026-10-17T12:00:00Z";
    private const string Id = "01ARZ3NDEKTSV4RRFFQ69G5FAV";
    private static readonly string Hash = "sha256:" + new string('3', 64);

    [Fact]
    public void LocalImplementationHashDependsOnlyOnItsExplicitVersionAndNoSourceResources()
    {
        Assert.Empty(typeof(LocalCompanions).Assembly.GetManifestResourceNames());
        DocumentGeneratorDescriptor descriptor = Descriptor();
        Assert.Matches("^vericairn\\.runtime/local-readers/[1-9][0-9]*$", descriptor.Implementation);
        Assert.Equal(ContentHash.Sha256(Encoding.UTF8.GetBytes(descriptor.Implementation)), descriptor.ImplementationHash);
    }

    [Fact]
    public void EveryInspectorMatchesTheGoldenBytesForItsImplementationVersion() => CompanionGoldenFixtures.Check(Descriptor(), Cases());

    public static DocumentGeneratorDescriptor Descriptor()
    {
        LocalCompanions.EnsureRegistered();
        return DocumentGenerators.Find("vericairn.local/inspectors")!.Descriptor!;
    }

    /// <summary>Only synthetic allowlisted metadata; no ambient settings, credentials or machine bindings.</summary>
    public static IReadOnlyDictionary<string, DocumentRendition> Cases()
    {
        var cases = new Dictionary<string, DocumentRendition>(StringComparer.Ordinal);
        foreach ((string kind, ImmutableArray<LocalCompanionSchemas.Field> schema) in LocalCompanionSchemas.All)
        {
            JsonElement metadata = JsonSerializer.SerializeToElement(Example(schema));
            cases.Add(kind + "/list.md", LocalCompanions.Inspect(kind, metadata, At));
            cases.Add(kind + "/item.md", LocalCompanions.Inspect(kind, metadata, At, item: 1));
            cases.Add(kind + "/unknown.md", LocalCompanions.Inspect(kind, JsonSerializer.SerializeToElement(new JsonObject()), At));
            cases.Add(kind + "/empty.md", LocalCompanions.Inspect(kind, JsonSerializer.SerializeToElement(new JsonArray()), At));
            foreach (string collection in LocalCompanionSchemas.Collections(schema))
            {
                string name = kind + "/" + collection.Replace("[]", "-items", StringComparison.Ordinal).Replace('.', '-');
                cases.Add(name + "-list.md", LocalCompanions.Inspect(kind, metadata, At, field: collection));
                cases.Add(name + "-item.md", LocalCompanions.Inspect(kind, metadata, At, field: collection, item: 1));
            }
        }
        return cases;
    }

    private static JsonObject Example(ImmutableArray<LocalCompanionSchemas.Field> fields)
    {
        var result = new JsonObject();
        foreach (LocalCompanionSchemas.Field field in fields)
        {
            if (field.Kind == "object") result[field.Name] = Example(field.Children);
            else if (field.Kind == "list") result[field.Name] = new JsonArray(Example(field.Children));
            else if (field.Kind.EndsWith("[]", StringComparison.Ordinal)) result[field.Name] = new JsonArray(Scalar(field.Kind[..^2]));
            else result[field.Name] = Scalar(field.Kind);
        }
        return result;
    }

    private static JsonValue Scalar(string kind) => kind switch
    {
        "bool" => JsonValue.Create(true), "number" or "signed" => JsonValue.Create(12), "duration" => JsonValue.Create("0.5"),
        "id" or "actor" or "identity" => JsonValue.Create(Id), "hash" => JsonValue.Create(Hash), "time" => JsonValue.Create(At),
        "selector" => JsonValue.Create("fixture"), "repository" => JsonValue.Create("Example/example"), "record-type" => JsonValue.Create("result"),
        "operation" => JsonValue.Create("POST /v1/records"), _ => JsonValue.Create(kind.Split('|')[0]),
    };
}
