// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Vericairn.Protocol;

namespace Vericairn.Sdk;

public sealed partial class VericairnClient
{
    private HealthResponse? daemonHealth;
    private LookupRoutes? lookupRoutes;
    private sealed record LookupRoutes(HealthResponse Health, string[] Paths);
    private static string ClientRelease => Release(typeof(VericairnClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");

    private const string RestartInstructions = "On the daemon's machine, from this estate's directory, use the installed release to run 'vericairn runtime stop', then 'vericairn runtime start'.";

    /// <summary>Both releases and restart instructions when the latest health response differs from this client; build metadata is ignored.</summary>
    public string? DaemonVersionWarning => Volatile.Read(ref daemonHealth) is { } health && Release(health.Vericairn) != ClientRelease
        ? $"This client is Vericairn {ClientRelease}, but the serving daemon is Vericairn {Release(health.Vericairn)}. {RestartInstructions}"
        : null;

    private HealthResponse? ObserveHealth(HealthResponse? health)
    {
        Volatile.Write(ref daemonHealth, health);
        return health;
    }

    private static string Release(string? version) => version?.Split('+')[0] ?? "unknown";

    private async Task<ClientRefusedException> MissingOperationAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // A failed operation is never retried. Health is only a read to identify the daemon answering it.
        // Keep the definite 404 even when that diagnostic read cannot finish.
        if (Volatile.Read(ref daemonHealth) is null && response.RequestMessage?.RequestUri?.AbsolutePath != "/v1/health")
        {
            try
            {
                using var deadline = new CancellationTokenSource(ClientOperation.Deadline(nameof(Health)), OperationClock);
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                using HttpResponseMessage health = await asyncHttp.GetAsync(new Uri("/v1/health", UriKind.Relative), stop.Token).ConfigureAwait(false);
                if (health.IsSuccessStatusCode)
                    ObserveHealth(await health.Content.ReadFromJsonAsync<HealthResponse>(ProtocolJson.Options, stop.Token).ConfigureAwait(false));
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or OperationCanceledException)
            {
                // The original response already establishes that the operation was refused.
            }
        }

        string daemon = Volatile.Read(ref daemonHealth) is { } known ? $"Vericairn {Release(known.Vericairn)}" : "of unknown release";
        string operation = response.RequestMessage is { } request ? $"{request.Method} {request.RequestUri?.AbsolutePath}" : "requested operation";
        return new ClientRefusedException($"The serving daemon {daemon} does not provide {operation} (HTTP 404). This client is Vericairn {ClientRelease}. {RestartInstructions}", statusCode: HttpStatusCode.NotFound);
    }

    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw MissingOperationAsync(response, CancellationToken.None).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
    }

    private async Task CheckLookupRouteAsync(string path, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // A lookup's empty 404 also means an absent record. Only the differing daemon's own
        // route description can establish that it lacks this operation instead.
        if (DaemonVersionWarning is null || Volatile.Read(ref daemonHealth) is not { } health) return;
        LookupRoutes? routes = Volatile.Read(ref lookupRoutes);
        if (routes?.Health != health)
        {
            try
            {
                using var deadline = new CancellationTokenSource(ClientOperation.Deadline(nameof(Health)), OperationClock);
                using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
                using HttpResponseMessage description = await asyncHttp.GetAsync(new Uri(OpenApiDocument.Path, UriKind.Relative), stop.Token).ConfigureAwait(false);
                if (!description.IsSuccessStatusCode) return;
                using JsonDocument document = await JsonDocument.ParseAsync(await description.Content.ReadAsStreamAsync(stop.Token).ConfigureAwait(false), cancellationToken: stop.Token).ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("openapi", out JsonElement format) || format.ValueKind != JsonValueKind.String ||
                    !document.RootElement.TryGetProperty("paths", out JsonElement paths) || paths.ValueKind != JsonValueKind.Object) return;
                routes = new LookupRoutes(health, paths.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object && p.Value.TryGetProperty("get", out _)).Select(p => p.Name).ToArray());
                Volatile.Write(ref lookupRoutes, routes);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or OperationCanceledException)
            {
                // An unavailable description cannot turn a missing resource into a missing operation.
                return;
            }
        }

        string requestPath = path.Split('?')[0];
        if (!routes.Paths.Any(route => MatchesRoute(route, requestPath)))
            throw await MissingOperationAsync(response, cancellationToken).ConfigureAwait(false);
    }

    private static bool MatchesRoute(string template, string path)
    {
        string[] pattern = template.Split('/');
        string[] actual = path.Split('/');
        return pattern.Length == actual.Length && pattern.Zip(actual).All(pair =>
            pair.First == pair.Second || pair.First.StartsWith('{') && pair.First.EndsWith('}') && pair.Second.Length > 0);
    }
}
