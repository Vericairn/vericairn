// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;
using Vericairn.Records.Model;
using Vericairn.Records.Types;

namespace Vericairn.Records.Tests;

public class ExecutionPolicyTests
{
    [Theory]
    [InlineData("{\"choices\":{\"model\":\"model-a\",\"effort\":\"high\"}}", true)]
    [InlineData("{\"restrictions\":{\"requiredModel\":\"model-a\",\"exactRevision\":\"revision-1\",\"permittedProviders\":[\"provider\"],\"maxCurrency\":{\"amount\":100,\"unit\":\"USD\"}}}", true)]
    [InlineData("{\"choices\":{\"model\":\"\"}}", false)]
    [InlineData("{\"choices\":{\"model\":\"../binding\"}}", false)]
    [InlineData("{\"choices\":{\"effort\":5}}", false)]
    [InlineData("{\"choices\":{\"fallback\":\"cheap\"}}", false)]
    [InlineData("{\"restrictions\":{\"permittedProviders\":[]}}", false)]
    [InlineData("{\"restrictions\":{\"maxCurrency\":{\"amount\":-1,\"unit\":\"USD\"}}}", false)]
    public void PoliciesAreTypedAndRejectUnknownOrNonportableValues(string policy, bool valid)
    {
        using JsonDocument document = JsonDocument.Parse(policy);
        var findings = new FindingList();
        ExecutionPolicy.Validate(new BodyReader(document.RootElement, "execution", findings, 2));
        Assert.Equal(valid, !findings.HasErrors);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ExecutionIsAdditiveOnlyInFormatTwo(long format, bool valid)
    {
        using JsonDocument document = JsonDocument.Parse("""{"execution":{"choices":{"model":"model-a"}}}""");
        var findings = new FindingList();
        var reader = new BodyReader(document.RootElement, "body", findings, format);
        ExecutionPolicy.Read(reader);
        reader.NoOtherKeys();
        Assert.Equal(valid, !findings.HasErrors);
    }
}
