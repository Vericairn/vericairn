// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Daemon.Tests;

public sealed class McpClientMetadataTests
{
    private static readonly string[] Redirects = ["https://client.example/callback"];

    [Fact]
    public void DynamicRegistrationDefaultsToWebAndAppliesTheWebRedirectRules()
    {
        const string client = "dynamic-client";
        const string loopback = "http://localhost:3000/callback";
        static JsonElement OmittedApplicationType(string redirect) => JsonSerializer.SerializeToElement(new
        {
            client_name = "Test client",
            redirect_uris = new[] { redirect },
            token_endpoint_auth_method = "none",
        });

        McpClientMetadata registration = Assert.IsType<McpClientMetadata>(
            McpClientMetadata.Parse(OmittedApplicationType("https://client.example/callback"), client, dynamic: true));
        Assert.Equal("web", registration.ApplicationType);
        Assert.Null(McpClientMetadata.Parse(OmittedApplicationType(loopback), client, dynamic: true));

        JsonElement native = JsonSerializer.SerializeToElement(new
        {
            client_name = "Test client",
            redirect_uris = new[] { loopback },
            application_type = "native",
        });
        Assert.Equal("native", McpClientMetadata.Parse(native, client, dynamic: true)?.ApplicationType);
    }

    [Theory]
    [InlineData('\u061c')]
    [InlineData('\u200f')]
    [InlineData('\u202e')]
    [InlineData('\u2067')]
    public void ClientNamesRejectBidirectionalControls(char control)
    {
        const string id = "https://client.example/registration.json";
        static JsonElement Document(string name) => JsonSerializer.SerializeToElement(new
        {
            client_id = id,
            client_name = name,
            redirect_uris = Redirects,
        });

        Assert.Null(McpClientMetadata.Parse(Document("Example " + control + " client"), id, dynamic: false));
        Assert.NotNull(McpClientMetadata.Parse(Document("مثال"), id, dynamic: false));
    }
}
