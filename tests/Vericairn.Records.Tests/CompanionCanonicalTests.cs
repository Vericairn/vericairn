// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;
using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;

namespace Vericairn.Records.Tests;

[CollectionDefinition("Companion cultures", DisableParallelization = true)]
public sealed class CompanionCultureScope;

[Collection("Companion cultures")]
public class CompanionCanonicalTests
{
    [Fact]
    public void OnDemandCollectionsAndManifestRetainCanonicalBytesAcrossCulturesZonesAndOrders()
    {
        using var fixture = new PolicyFixture();
        var original = fixture.Estate.Load();
        var reversed = new EstateModel(original.Root, original.Records.Reverse().ToArray(), original.ById, original.SupersededBy, original.Findings) { Manifest = original.Manifest };
        byte[] manifest = File.ReadAllBytes(Path.Combine(fixture.Estate.Root, EstateLayout.ManifestPath));
        CultureInfo oldCulture = CultureInfo.CurrentCulture, oldUi = CultureInfo.CurrentUICulture;
        string? oldZone = Environment.GetEnvironmentVariable("TZ");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (string locale in new[] { "", "en-US", "tr-TR", "ar-SA", "ja-JP" })
            {
                CultureInfo culture;
                try { culture = new CultureInfo(locale); }
                catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                foreach (string zone in new[] { "UTC", "Pacific/Kiritimati", "America/Los_Angeles" })
                {
                    Environment.SetEnvironmentVariable("TZ", zone);
                    TimeZoneInfo.ClearCachedData();
                    foreach (EstateModel model in new[] { original, reversed })
                    {
                        foreach (var register in RevisionCompanionGenerator.Registers)
                            foreach (string selection in new[] { "current", "all-revisions" })
                                Check(register.Id + selection, DocumentOnDemand.Records(model, register.Id, DocumentOnDemand.RecordSnapshot(model), selection));
                        Check("manifest", DocumentOnDemand.Manifest(model, manifest, DocumentOnDemand.ManifestSnapshot(model, manifest)));
                    }
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUi;
            Environment.SetEnvironmentVariable("TZ", oldZone);
            TimeZoneInfo.ClearCachedData();
        }

        void Check(string key, DocumentRendition rendition)
        {
            Assert.Equal(DocumentGenerationStatus.Success, rendition.Status);
            Assert.NotNull(rendition.ContentHash);
            hashes.TryAdd(key, rendition.ContentHash);
            Assert.Equal(hashes[key], rendition.ContentHash);
        }
    }

    [Fact]
    public void AllRegistersAreByteIdenticalAcrossCulturesZonesAndEnumerationOrders()
    {
        CultureInfo oldCulture = CultureInfo.CurrentCulture;
        CultureInfo oldUi = CultureInfo.CurrentUICulture;
        string? oldZone = Environment.GetEnvironmentVariable("TZ");
        try
        {
            foreach (var register in RevisionCompanionGenerator.Registers)
            {
                string[]? hashes = null;
                foreach (string locale in new[] { "", "en-US", "tr-TR", "ar-SA", "ja-JP" })
                {
                    CultureInfo culture;
                    try { culture = new CultureInfo(locale); }
                    catch (CultureNotFoundException)
                    {
                        // The default build uses invariant globalization. Separate validation runs enable the named cultures.
                        culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                        culture.DateTimeFormat.DateSeparator = "!";
                        culture.NumberFormat.NegativeSign = "minus";
                    }
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = culture;
                    foreach (string zone in new[] { "UTC", "Pacific/Kiritimati", "America/Los_Angeles" })
                    {
                        Environment.SetEnvironmentVariable("TZ", zone);
                        TimeZoneInfo.ClearCachedData();
                        string titleField = register.Type switch { "writing-exception" => "token", "release" => "version", "policy-exception" => "rule", _ => "title" };
                        var body = new JsonObject { [titleField] = "TITLE 東京 | &" };
                        var first = RecordCompanionTests.Source(register.Type, body);
                        var nextRecord = RecordAuthor.Create(new RecordDraft(register.Type, first.Author!, first.Body!.Value,
                            Supersedes: first.Identity, At: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), Id: "01ARZ3NDEKTSV4RRFFQ69G5FAX", Format: 3));
                        var second = DocumentSourceSnapshot.Record(nextRecord.Envelope);
                        foreach (ImmutableArray<DocumentSourceSnapshot> order in new ImmutableArray<DocumentSourceSnapshot>[] { [first, second], [second, first] })
                        {
                            DocumentMemberSelection members = DocumentGeneration.Members(RevisionCompanionTests.Carrier(register.Id), new(DocumentGenerationStatus.Success, order, null, null));
                            Assert.Equal(DocumentGenerationStatus.Success, members.Status);
                            string[] rendered = members.Members.Select(m =>
                            {
                                DocumentRendition result = DocumentRendering.Render(DocumentGenerators.Find(register.Id)!, m.Context);
                                Assert.Equal(DocumentGenerationStatus.Success, result.Status);
                                Assert.DoesNotContain((byte)'\r', result.Bytes);
                                Assert.Equal((byte)'\n', result.Bytes[^1]);
                                return m.Path + ":" + result.ContentHash + ":" + result.InputDigest;
                            }).ToArray();
                            hashes ??= rendered;
                            Assert.Equal(hashes, rendered);
                        }
                    }
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUi;
            Environment.SetEnvironmentVariable("TZ", oldZone);
            TimeZoneInfo.ClearCachedData();
        }
    }
}
