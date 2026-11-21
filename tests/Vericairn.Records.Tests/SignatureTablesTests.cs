// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Security.Cryptography;
using Vericairn.Records.Estate;
using Vericairn.Records.Hashing;
using Vericairn.Records.Signing;

namespace Vericairn.Records.Tests;

/// <summary>
/// From 0.9.4 (FIX-148; design/performance.md § 3.1): the process's signature verdicts and imported keys. Only a verdict that verifies and
/// a key whose import succeeded are kept; a false and a refusal are asked again; the tables answer exactly as the platform does under
/// concurrent judgments, each key verifying under its own lock and never disposed while a verification holds it; and a bound below what
/// the judgments use evicts the least recently used, counting any verdict a judgment in progress had used. Each test reads fresh tables.
/// </summary>
public sealed class SignatureTablesTests
{
    [Fact]
    public void OnlyAVerdictThatVerifiesIsKeptAndAFalseIsAskedAgain()
    {
        (string key, string hash, byte[] digest, string signature) = Signed("kept");
        var tables = new SignatureTables();
        var context = new JudgmentContext(tables);
        using SignatureTables.Judgment judgment = tables.Begin(2, 1, context);
        string other = Signed("another").Signature;

        Assert.False(tables.Verifies(key, hash, digest, other, judgment));
        Assert.False(tables.Verifies(key, hash, digest, other, judgment));
        Assert.Equal(2, context.Verifications);
        Assert.Equal(0, tables.Verdicts);

        Assert.True(tables.Verifies(key, hash, digest, signature, judgment));
        Assert.True(tables.Verifies(key, hash, digest, signature, judgment));
        Assert.Equal(3, context.Verifications);
        Assert.Equal((1, 1, 1L), (tables.Verdicts, tables.Keys, context.KeyImports));
        // The verdict is for exactly these three strings: another content hash under the same key and signature is verified anew.
        string otherHash = ContentHash.Sha256("another content"u8.ToArray());
        Assert.False(tables.Verifies(key, otherHash, RecordSigning.Digest(otherHash), signature, judgment));
        Assert.Equal(4, context.Verifications);
    }

    [Fact]
    public void ARefusedVerificationOrImportIsNotKept()
    {
        (string key, string hash, byte[] digest, string signature) = Signed("refused");
        var tables = new SignatureTables();
        var context = new JudgmentContext(tables) { RefuseImport = _ => true };
        using SignatureTables.Judgment judgment = tables.Begin(1, 1, context);

        Assert.False(tables.Verifies(key, hash, digest, signature, judgment));
        Assert.Equal((0, 0, 1L, 0L), (tables.Keys, tables.Verdicts, context.KeyImports, context.Verifications));
        context.RefuseImport = null;
        context.RefuseVerification = s => s == signature;
        Assert.False(tables.Verifies(key, hash, digest, signature, judgment));
        Assert.Equal((1, 0, 2L, 1L), (tables.Keys, tables.Verdicts, context.KeyImports, context.Verifications));
        context.RefuseVerification = null;
        Assert.True(tables.Verifies(key, hash, digest, signature, judgment));
        Assert.Equal((1, 1, 2L, 2L), (tables.Keys, tables.Verdicts, context.KeyImports, context.Verifications));
    }

    [Fact]
    public void ConcurrentJudgmentsAnswerAsThePlatformDoesUnderASmallBound()
    {
        var signed = Enumerable.Range(0, 64).Select(i => Signed("concurrent " + i)).ToArray();
        string wrong = Signed("wrong").Signature;
        var tables = new SignatureTables(startingVerdicts: 4, startingKeys: 2);
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 8 }, worker =>
        {
            var context = new JudgmentContext(tables);
            for (int round = 0; round < 8; round++)
            {
                using SignatureTables.Judgment judgment = tables.Begin(1, 1, context);
                foreach ((string key, string hash, byte[] digest, string signature) in signed)
                {
                    if (!tables.Verifies(key, hash, digest, signature, judgment)) failures.Add("a valid signature did not verify");
                    if (tables.Verifies(key, hash, digest, wrong, judgment)) failures.Add("a wrong signature verified");
                }
            }
        });

        Assert.Empty(failures);
        Assert.True(tables.Evictions > 0);
        Assert.True(tables.Verdicts <= signed.Length);
        Assert.True(tables.Keys <= signed.Length);
    }

    [Fact]
    public void EachJudgmentReservesTwiceItsSignaturesSoOneEstateEvictsNothing()
    {
        var signed = Enumerable.Range(0, 40).Select(i => Signed("reserved " + i)).ToArray();
        var tables = new SignatureTables(startingVerdicts: 4, startingKeys: 1);
        var context = new JudgmentContext(tables);
        for (int judgmentNumber = 0; judgmentNumber < 2; judgmentNumber++)
        {
            using SignatureTables.Judgment judgment = tables.Begin(signed.Length, signed.Length, context);
            foreach ((string key, string hash, byte[] digest, string signature) in signed) Assert.True(tables.Verifies(key, hash, digest, signature, judgment));
        }

        Assert.Equal((80, 80), (tables.VerdictBound, tables.KeyBound));
        Assert.Equal((40L, 40L), (context.Verifications, context.KeyImports));
        Assert.Equal((0L, 0L, 0L), (tables.Evictions, tables.EvictedInUse, context.EvictionsInUse));
    }

    [Fact]
    public void AnEvictionOfAVerdictAJudgmentInProgressUsedIsCounted()
    {
        var signed = Enumerable.Range(0, 12).Select(i => Signed("in use " + i)).ToArray();
        var tables = new SignatureTables(startingVerdicts: 4, startingKeys: 16);
        var context = new JudgmentContext(tables);
        // A judgment that reserves for fewer signatures than it verifies: its own verdicts leave while it is in progress.
        using SignatureTables.Judgment judgment = tables.Begin(1, 1, context);
        foreach ((string key, string hash, byte[] digest, string signature) in signed) Assert.True(tables.Verifies(key, hash, digest, signature, judgment));
        Assert.True(tables.EvictedInUse > 0);
        Assert.Equal(tables.EvictedInUse, context.EvictionsInUse);
    }

    private static (string Key, string Hash, byte[] Digest, string Signature) Signed(string content)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string hash = ContentHash.Sha256(System.Text.Encoding.UTF8.GetBytes(content));
        byte[] digest = RecordSigning.Digest(hash);
        byte[] signature = key.SignHash(digest, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return (RecordSigning.EncodePublicKey(key.ExportSubjectPublicKeyInfo()), hash, digest, System.Buffers.Text.Base64Url.EncodeToString(signature));
    }
}
