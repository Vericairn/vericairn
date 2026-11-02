// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text;
using System.Text.Json;
using Vericairn.Runtime.Guides;

namespace Vericairn.Runtime.Tests;

// Only synthetic inputs are used. File-based cases create and remove their own directories.
public sealed class CatalogFixture
{
    public GuideFixture Guides { get; } = GuideFixture.Create();
    public CatalogExportPlan Plan { get; set; }

    public CatalogFixture() => Plan = Selection(Guides.Index);

    public static CatalogExportPlan Selection(GuideSourceIndex index) => new()
    {
        IndexIdentity = GuideGenerator.Hash(Json(index)),
        Allowlist = index.Sources,
        Entries = index.Entries.Where(e => e.State is "planned" or "remainder").Select(e => e.Key).ToArray(),
    };

    public static byte[] Json<T>(T value) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, GuideJson.Options) + "\n");

    public static string CreateTemporaryDirectory(string prefix)
    {
        // Resolve system temporary-directory aliases before using the deliberately link-free file boundary.
        string created = Directory.CreateTempSubdirectory(prefix).FullName;
        string current = Path.GetPathRoot(created)!;
        foreach (string part in created[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(current, part));
            current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
        }
        return current;
    }

    public CatalogBundle Generate(params CatalogTrackingIssue[] issues) => CatalogExporter.Generate(Guides.Index, Guides.Sources, Plan, issues);
    public static string Text(CatalogBundle bundle, string file) => Encoding.UTF8.GetString(bundle.Files[file]);

    public void WriteInputs(string root)
    {
        Guides.WriteInputs(root);
        File.WriteAllBytes(Path.Combine(root, "plan.json"), Json(Plan));
    }

    public static CatalogInventory Inventory => new("vericairn.catalog-inventory/1",
    [
        new("fixture.active", "Active", "adapter", null, "planned", "guides/adapter/fixture.active.md", true, null),
        new("fixture.remainder", "Remainder", "adapter", null, "remainder", "guides/adapter/fixture.remainder.md", true, null),
        new("fixture.retired", "Retired", "adapter", null, "retired", "guides/adapter/fixture.retired.md", false, "https://example.org/replacement"),
        new("fixture.available", "Available", "adapter", null, "available", "guides/adapter/fixture.available.md", false, "https://example.org/replacement"),
    ]);

    public static DateTimeOffset Time(int minute) => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(minute);

    public static CatalogRequestObservation Issue(string id, string account, int minute = 1, string key = "fixture.active") => new()
    {
        Kind = "issue", ObjectId = id, IssueId = id, AccountId = account, AccountKind = "human", Verified = true,
        Revision = 1, IssueCreatedAt = Time(minute), CreatedAt = Time(minute), UpdatedAt = Time(minute),
        Fields = new(key, true),
    };

    public static CatalogRequestObservation Comment(string id, string account, CatalogRequestObservation issue, int minute = 2) =>
        Issue(id, account, minute, issue.Fields!.EntryKey) with
        { Kind = "comment", IssueId = issue.IssueId, IssueCreatedAt = issue.IssueCreatedAt };

    public static CatalogRequestSnapshot Snapshot(params CatalogRequestObservation[] observations) =>
        new("fixture/catalog", true, Time(0), Time(60), Time(59), observations);
}
