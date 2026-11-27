// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Runtime.Service;

internal static class AndroidAttestationStatus
{
    private static readonly Uri Source = new("https://android.googleapis.com/attestation/status");
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(5),
        MaxResponseContentBufferSize = 8 * 1024 * 1024,
    };
    private static readonly object Gate = new();
    private static IReadOnlySet<string>? cached;
    private static DateTimeOffset nextRequest;

    internal static IReadOnlySet<string>? Get()
    {
        lock (Gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (now < nextRequest) return cached;
            cached = null;
            nextRequest = now.AddSeconds(20);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, Source);
                using HttpResponseMessage response = Client.Send(request, HttpCompletionOption.ResponseContentRead);
                if (!response.IsSuccessStatusCode) return null;
                using Stream content = response.Content.ReadAsStream();
                using JsonDocument document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 6 });
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("entries", out JsonElement entries) || entries.ValueKind != JsonValueKind.Object) return null;
                // The list contains certificates with any status other than normal valid status.
                var serials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty entry in entries.EnumerateObject())
                {
                    if (!serials.Add(entry.Name.TrimStart('0'))) return null;
                }
                TimeSpan advertised = response.Headers.CacheControl?.MaxAge ?? TimeSpan.Zero;
                TimeSpan age = response.Headers.Age ?? TimeSpan.Zero;
                TimeSpan freshness = advertised - age;
                if (age > TimeSpan.Zero && freshness <= TimeSpan.Zero) return null;
                if (response.Headers.CacheControl is { NoStore: true } or { NoCache: true } || freshness <= TimeSpan.Zero)
                {
                    nextRequest = now;
                    return serials;
                }
                nextRequest = now + (freshness < TimeSpan.FromDays(1) ? freshness : TimeSpan.FromDays(1));
                return cached = serials;
            }
            catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException)
            {
                return null;
            }
        }
    }
}
