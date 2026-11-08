// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Vericairn.Protocol;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>
/// The API operations a network MCP session's tools reach, and how each is run (MCP-03): in this process, through the very route the
/// API maps, with its binding, its request scope and its service call, as a request of the device that signed the session's HTTP
/// request. Nothing here decides what a device may do: the route's scope rule and the service do, as for the same request sent to
/// the API. What this adds is narrower: only the operations section 8's tools use, never the machine-local ones, and every record
/// signed by the requesting device alone.
/// </summary>
internal sealed class McpOperations
{
    /// <summary>
    /// The operations that carry their input in a POST body that a network tool sends: the reads among them, and section 8's contributor
    /// writes. Every other POST is refused before its route runs; reads by GET pass, and of the event routes only the long poll of
    /// <c>GET /v1/events</c>, which events_read reads (MCP-05): a tool never opens a stream or reads an event by key.
    /// </summary>
    internal static readonly IReadOnlySet<string> Posts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "POST /v1/resources/read",
        "POST /v1/releases/check",
        "POST /v1/templates/render",
        "POST /v1/work/attempt",
        "POST /v1/work/progress",
        "POST /v1/work/result",
        "POST /v1/work/amend",
        "POST /v1/documents/{id}/reviews",
    };

    private readonly Route[] routes;

    internal McpOperations(IEnumerable<RouteEndpoint> endpoints)
    {
        var table = new List<Route>();
        foreach (RouteEndpoint endpoint in endpoints)
        {
            IReadOnlyList<string> methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
            Segment[]? segments = Segments(endpoint.RoutePattern);
            if (segments is not null && methods.Count > 0)
            {
                table.Add(new Route(endpoint, methods, segments));
            }
        }

        routes = [.. table];
    }

    /// <summary>Whether a network tool may reach an operation, named as the request scope names it.</summary>
    internal static bool Offered(string operation) =>
        operation.StartsWith("GET ", StringComparison.OrdinalIgnoreCase)
            && (!operation.StartsWith("GET /v1/events", StringComparison.OrdinalIgnoreCase) || string.Equals(operation, EventsOperation, StringComparison.OrdinalIgnoreCase))
            ? !RequestAuthenticator.MachineLocal.Contains(operation)
            : Posts.Contains(operation) && !RequestAuthenticator.MachineLocal.Contains(operation);

    /// <summary>The event journal's read, which a network tool reaches as a long poll only.</summary>
    internal const string EventsOperation = "GET /v1/events";

    /// <summary>The route a request reaches, preferring literal segments to parameters from the left, with its values; null when none.</summary>
    internal (RouteEndpoint Endpoint, RouteValueDictionary Values)? Match(string method, string path)
    {
        string[] parts = path.Trim('/').Split('/');
        Route? best = null;
        foreach (Route route in routes)
        {
            if (route.Segments.Length != parts.Length || !route.Methods.Contains(method, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            bool matches = true;
            for (int i = 0; i < parts.Length && matches; i++)
            {
                matches = route.Segments[i].Literal is not string literal
                    ? parts[i].Length > 0
                    : string.Equals(literal, parts[i], StringComparison.OrdinalIgnoreCase);
            }

            if (matches && (best is null || MoreSpecific(route, best)))
            {
                best = route;
            }
        }

        if (best is null)
        {
            return null;
        }

        var values = new RouteValueDictionary();
        for (int i = 0; i < parts.Length; i++)
        {
            if (best.Segments[i].Parameter is string name)
            {
                values[name] = Uri.UnescapeDataString(parts[i]);
            }
        }

        return (best.Endpoint, values);
    }

    /// <summary>
    /// An HTTP handler that runs the API's routes in this process for one network MCP request, as its authenticated device. Each
    /// request it carries is judged by the route's request scope for that device, under the session's signing rule for the call.
    /// </summary>
    internal HttpMessageHandler Handler(DaemonHost host, HttpContext outer, AuthenticatedDevice device, Func<DeviceSigning> signing, CancellationToken observing) =>
        new InProcess(this, host, outer, device, signing, observing);

    private static bool MoreSpecific(Route candidate, Route current)
    {
        for (int i = 0; i < candidate.Segments.Length; i++)
        {
            bool literal = candidate.Segments[i].Literal is not null;
            bool other = current.Segments[i].Literal is not null;
            if (literal != other)
            {
                return literal;
            }
        }

        return false;
    }

    private static Segment[]? Segments(RoutePattern pattern)
    {
        var segments = new List<Segment>();
        foreach (RoutePatternPathSegment segment in pattern.PathSegments)
        {
            if (segment.Parts.Count != 1)
            {
                return null;
            }

            switch (segment.Parts[0])
            {
                case RoutePatternLiteralPart literal:
                    segments.Add(new Segment(literal.Content, null));
                    break;
                case RoutePatternParameterPart parameter when !parameter.IsCatchAll && !parameter.IsOptional && parameter.Default is null && parameter.ParameterPolicies.Count == 0:
                    segments.Add(new Segment(null, parameter.Name));
                    break;
                default:
                    return null;
            }
        }

        return [.. segments];
    }

    private sealed record Segment(string? Literal, string? Parameter);

    private sealed record Route(RouteEndpoint Endpoint, IReadOnlyList<string> Methods, Segment[] Segments);

    private sealed class InProcess(McpOperations operations, DaemonHost host, HttpContext outer, AuthenticatedDevice device, Func<DeviceSigning> signing, CancellationToken observing)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Uri uri = request.RequestUri ?? throw new InvalidOperationException("A request names its target.");
            string method = request.Method.Method;
            if (operations.Match(method, uri.AbsolutePath) is not { } matched)
            {
                return Refused(HttpStatusCode.NotFound, new Problem("not-found", $"No operation is served at {method} {uri.AbsolutePath}.", []));
            }

            (RouteEndpoint endpoint, RouteValueDictionary values) = matched;

            string operation = method.ToUpperInvariant() + " /" + (endpoint.RoutePattern.RawText ?? string.Empty).Trim('/');
            if (!Offered(operation))
            {
                return Refused(HttpStatusCode.Unauthorized, new Problem("denied", $"{operation} is not offered over network MCP.", []));
            }

            // events_read reads the journal as a long poll, which answers once; a stream never ends, so no tool may open one.
            if (string.Equals(operation, EventsOperation, StringComparison.OrdinalIgnoreCase)
                && request.Headers.Accept.Any(a => string.Equals(a.MediaType, EventVocabulary.StreamMediaType, StringComparison.OrdinalIgnoreCase)))
            {
                return Refused(HttpStatusCode.NotAcceptable, new Problem("refused", "A network tool reads events as a long poll, never as a stream.", []));
            }

            byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            using var input = new MemoryStream(body, writable: false);
            using var output = new MemoryStream();
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(observing, cancellationToken);
            await using AsyncServiceScope services = host.Services.CreateAsyncScope();

            var features = new FeatureCollection();
            // The connection's facts are the network request's own, so a route sees the profile and the peer it came from.
            if (outer.Features.Get<IHttpConnectionFeature>() is { } connection) features.Set(connection);
            if (outer.Features.Get<ITlsConnectionFeature>() is { } tls) features.Set(tls);
            if (outer.Features.Get<IConnectionItemsFeature>() is { } items) features.Set(items);
            var headers = new HeaderDictionary();
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }

            if (request.Content is not null)
            {
                foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
                {
                    headers[header.Key] = header.Value.ToArray();
                }

                headers.ContentLength = body.Length;
            }

            features.Set<IHttpRequestFeature>(new HttpRequestFeature
            {
                Protocol = "HTTP/1.1",
                Scheme = outer.Request.Scheme,
                Method = method,
                PathBase = string.Empty,
                Path = Uri.UnescapeDataString(uri.AbsolutePath),
                QueryString = uri.Query,
                RawTarget = uri.PathAndQuery,
                Headers = headers,
                Body = input,
            });
            features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection(request.Content is not null));
            features.Set<IHttpResponseFeature>(new HttpResponseFeature());
            var responseBody = new StreamResponseBodyFeature(output);
            features.Set<IHttpResponseBodyFeature>(responseBody);
            features.Set<IHttpRequestLifetimeFeature>(new HttpRequestLifetimeFeature { RequestAborted = stop.Token });
            features.Set<IItemsFeature>(new ItemsFeature());
            features.Set<IRouteValuesFeature>(new RouteValuesFeature { RouteValues = values });
            var context = new DefaultHttpContext(features) { RequestServices = services.ServiceProvider };
            context.SetEndpoint(endpoint);

            // The request is the device's, as the network request that carried it was authenticated; the route's scope holds it there.
            context.Items[RequestAuthenticator.DeviceItem] = device;
            context.Items[RequestScope.SigningItem] = signing();
            try
            {
                await endpoint.RequestDelegate!(context).ConfigureAwait(false);
                await responseBody.CompleteAsync().ConfigureAwait(false);
            }
            catch (BadHttpRequestException exception)
            {
                return Refused((HttpStatusCode)exception.StatusCode, new Problem("refused", exception.Message, []));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or JsonException or ArgumentException
                && !stop.IsCancellationRequested)
            {
                // What failed stays in the runtime's log: its message may name paths on this machine, which a network client never sees.
                host.Log($"network MCP: {operation} failed for device {device.Identity}: {exception.GetType().Name}: {exception.Message}");
                return Refused(HttpStatusCode.InternalServerError, new Problem("unavailable", $"{operation} failed on the runtime; its log says why.", []));
            }

            var response = new HttpResponseMessage((HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()), RequestMessage = request };
            if (context.Response.ContentType is string type && MediaTypeHeaderValue.TryParse(type, out MediaTypeHeaderValue? media))
            {
                response.Content.Headers.ContentType = media;
            }

            return response;
        }

        private static HttpResponseMessage Refused(HttpStatusCode status, Problem problem) =>
            new(status) { Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(problem, ProtocolJson.Options)) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } } };
    }

    private sealed class BodyDetection(bool canHaveBody) : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => canHaveBody;
    }
}
