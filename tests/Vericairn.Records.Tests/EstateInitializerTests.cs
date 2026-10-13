// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using Vericairn.Records.Authoring;
using Vericairn.Records.Estate;
using Vericairn.Records.Identity;
using Vericairn.Records.Model;

namespace Vericairn.Records.Tests;

public class EstateInitializerTests
{
    [Fact]
    public void InitializesAValidEstateAndRefusesToDoItTwice()
    {
        string root = Path.Combine(Path.GetTempPath(), "vericairn-init-" + Ulid.NewUlid().Value);
        Directory.CreateDirectory(root);
        try
        {
            var initialization = new EstateInitialization("Example", "example", "Prove initialization.", "Example Operator", "Example Org", "example", "Example/vericairn", new string('a', 40), new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.Zero));
            (RecordEnvelope operatorRecord, RecordEnvelope estateRecord) = EstateInitializer.Initialize(root, initialization);
            Assert.Equal(operatorRecord.Id, operatorRecord.By);
            Assert.Equal(operatorRecord.Id, estateRecord.By);
            EstateModel model = EstateLoader.Load(root);
            Assert.False(model.Findings.HasErrors, string.Join("\n", model.Findings));
            Assert.Equal(estateRecord.Id, model.Manifest!.Estate);
            Assert.Equal("source", model.Manifest.Mode);
            Assert.False(EstateChecks.Run(model).HasErrors);
            Assert.Throws<InvalidOperationException>(() => EstateInitializer.Initialize(root, initialization));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
