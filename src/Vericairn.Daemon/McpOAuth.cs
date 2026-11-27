// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Vericairn.Protocol;
using Vericairn.Records.Model;
using Vericairn.Records.Signing;
using Vericairn.Runtime.Service;

namespace Vericairn.Daemon;

/// <summary>The runtime's own authorization server, restricted to its separately marked public TLS listener.</summary>
internal sealed class McpOAuth
{
    internal const string Profile = "token";
    internal const string ResourceMetadata = "/.well-known/oauth-protected-resource/mcp";
    private const string ServerMetadata = "/.well-known/oauth-authorization-server";
    private const string AuthorizePath = "/oauth/authorize";
    private const string TokenPath = "/oauth/token";
    private const string RegisterPath = "/oauth/register";
    internal const int BodyLimit = 16 * 1024;
    private static readonly string[] Scopes = ["read", "contribute"];
    private static readonly string[] BearerMethods = ["header"];
    private static readonly string[] ResponseTypes = ["code"];
    private static readonly string[] GrantTypes = ["authorization_code", "refresh_token"];
    private static readonly string[] ChallengeMethods = ["S256"];
    private static readonly string[] ClientAuthentication = ["none"];
    private readonly DaemonHost host;
    private readonly McpPublicEndpoint endpoint;
    private readonly IDeviceKeyStore? custody;
    private readonly McpTokenStore tokens;
    private readonly McpApprovalCodes approvals;
    private readonly McpBrowserAuthorization browser;
    private readonly McpClientRegistry registrations;
    private readonly McpPublicThrottle throttle;
    private readonly Func<string, CancellationToken, Task<McpClientMetadata?>> fetchMetadata;

    internal McpOAuth(DaemonHost host, McpPublicEndpoint endpoint, string directory, IDeviceKeyStore? custody,
        Func<string, CancellationToken, Task<McpClientMetadata?>>? fetchMetadata = null)
    {
        this.host = host;
        this.endpoint = endpoint;
        this.custody = custody;
        this.fetchMetadata = fetchMetadata ?? McpClientMetadata.FetchAsync;
        tokens = new McpTokenStore(directory, host.Time);
        approvals = new McpApprovalCodes(host.Time);
        browser = new McpBrowserAuthorization(host.Time, endpoint.Issuer);
        registrations = new McpClientRegistry(directory, host.Time);
        throttle = new McpPublicThrottle(host.Time);
    }

    internal string Issuer => endpoint.Issuer;
    internal string Resource => Issuer + McpEndpoint.Path;

    internal (McpApprovalIssued? Issued, string? Problem) Issue(string? principal, string? by, string? scope, int days, string? renew)
    {
        if (days is < 1 or > 90 || !Scopes.Contains(scope, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(by)
            || (string.IsNullOrWhiteSpace(principal) && string.IsNullOrWhiteSpace(renew)))
        {
            return (null, "Name an administrator, an agent or service principal or a token device to renew, scope read or contribute, and a duration of 1 to 90 days.");
        }

        McpTokenDevice? renewal = null;
        if (renew is not null)
        {
            renewal = host.Service.CurrentMcpTokenDevice(renew);
            if (renewal is null || principal is not null && principal != renewal.Principal || HeldKey(renewal) is null)
                return (null, "Renewal needs the key of a currently admitted token device of the named principal on this runtime.");
            principal = renewal.Principal;
        }

        (PairingAuthority? authority, string? refused) = host.Service.McpTokenIssuer(principal!, by);
        if (authority is null) return (null, refused);
        var approval = new McpApproval(principal!, authority, scope!, host.Time.GetUtcNow().AddDays(days), renewal);
        McpApprovalIssued? issued = approvals.Issue(approval, Resource);
        return (issued, issued is null ? "The runtime already holds as many pending MCP approvals as it can. Wait for one to expire or be used." : null);
    }

    internal static bool ServesPath(string? path) => path
        is McpEndpoint.Path or ResourceMetadata or ServerMetadata or AuthorizePath or TokenPath or RegisterPath or "/.well-known/oauth-protected-resource";

    internal async Task InvokeAsync(HttpContext http, RequestDelegate mcp)
    {
        string? path = http.Request.Path.Value;
        if (!ServesPath(path))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        http.Response.Headers.CacheControl = "no-store";
        HostString hostName = http.Request.Host;
        if (!hostName.HasValue || !endpoint.Matches(hostName.Host, hostName.Port))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        StringValues origin = http.Request.Headers.Origin;
        if (origin.Count != 0 && (path == AuthorizePath
            ? origin.Count != 1 || !string.Equals(origin[0], Issuer, StringComparison.Ordinal)
            : path != McpEndpoint.Path || !host.Mcp.OriginAllowed(origin)))
        {
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (!throttle.Admit(http.Connection.RemoteIpAddress?.ToString() ?? "unknown", path == McpEndpoint.Path))
        {
            http.Response.Headers.RetryAfter = "60";
            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        string method = http.Request.Method;
        try
        {
            switch (path)
            {
                case McpEndpoint.Path:
                    await HandleMcpAsync(http, mcp).ConfigureAwait(false);
                    break;
                case ResourceMetadata or "/.well-known/oauth-protected-resource" when HttpMethods.IsGet(method):
                    await http.Response.WriteAsJsonAsync(new { resource = Resource, authorization_servers = new[] { Issuer }, scopes_supported = Scopes,
                        bearer_methods_supported = BearerMethods }, http.RequestAborted).ConfigureAwait(false);
                    break;
                case ServerMetadata when HttpMethods.IsGet(method):
                    await http.Response.WriteAsJsonAsync(new
                    {
                        issuer = Issuer, authorization_endpoint = Issuer + AuthorizePath, token_endpoint = Issuer + TokenPath,
                        registration_endpoint = Issuer + RegisterPath, response_types_supported = ResponseTypes,
                        grant_types_supported = GrantTypes, scopes_supported = Scopes,
                        code_challenge_methods_supported = ChallengeMethods, token_endpoint_auth_methods_supported = ClientAuthentication,
                        client_id_metadata_document_supported = true, authorization_response_iss_parameter_supported = true,
                    }, http.RequestAborted).ConfigureAwait(false);
                    break;
                case RegisterPath when HttpMethods.IsPost(method):
                    await RegisterAsync(http).ConfigureAwait(false);
                    break;
                case AuthorizePath when HttpMethods.IsGet(method):
                    await BeginAsync(http).ConfigureAwait(false);
                    break;
                case AuthorizePath when HttpMethods.IsPost(method):
                    await ApproveAsync(http).ConfigureAwait(false);
                    break;
                case TokenPath when HttpMethods.IsPost(method):
                    await TokenAsync(http).ConfigureAwait(false);
                    break;
                default:
                    http.Response.Headers.Allow = path == AuthorizePath ? "GET, POST" : path is TokenPath or RegisterPath ? "POST" : "GET";
                    http.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                    break;
            }
        }
        catch (Exception exception) when (!http.Response.HasStarted &&
            exception is not Microsoft.AspNetCore.Http.BadHttpRequestException &&
            exception is (InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException))
        {
            host.Log("MCP public endpoint could not complete a request: " + exception.GetType().Name);
            await ErrorAsync(http, StatusCodes.Status503ServiceUnavailable, "temporarily_unavailable").ConfigureAwait(false);
        }
    }

    internal void Challenge(HttpContext http, string? error = null)
    {
        string value = "Bearer resource_metadata=\"" + Issuer + ResourceMetadata + "\"";
        if (error is not null) value += ", error=\"" + error + "\"";
        http.Response.Headers.WWWAuthenticate = value;
    }

    private async Task HandleMcpAsync(HttpContext http, RequestDelegate mcp)
    {
        StringValues credentials = http.Request.Headers.Authorization;
        if (credentials.Count == 1 && AuthenticationHeaderValue.TryParse(credentials[0], out AuthenticationHeaderValue? auth)
            && auth.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) && auth.Parameter is string secret
            && tokens.Authenticate(secret, Resource) is { } access)
        {
            McpTokenDevice? device = Standing(access.Grant);
            IDeviceKeyStore? store = device is null ? null : HeldKey(device);
            if (device is not null && store is not null && tokens.Current(access))
            {
                http.Items[RequestAuthenticator.DeviceItem] = new AuthenticatedDevice(device.Device, device.Principal, device.PublicKey, LocalNetwork: false)
                {
                    Token = new McpTokenRequest(access.Grant.Scope, access.ExpiresAt, () => tokens.Current(access) && Standing(access.Grant) is not null,
                        hash => Base64Url.EncodeToString(store.Sign(device.Device, RecordSigning.Digest(hash)))),
                };
                await mcp(http).ConfigureAwait(false);
                return;
            }

            if (device is null)
            {
                host.Mcp.Sessions.EndDevice(access.Grant.Device);
                // Remove old material only if this is still its family; a renewed family may already be current.
                tokens.EndFamily(access.Family);
            }
        }

        Challenge(http, credentials.Count == 0 ? null : "invalid_token");
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
    }

    private McpTokenDevice? Standing(McpTokenGrant grant)
    {
        McpTokenDevice? current = host.Service.CurrentMcpTokenDevice(grant.Device, grant.Admission);
        return current?.PublicKey == grant.PublicKey && current.Principal == grant.Principal ? current : null;
    }

    private IDeviceKeyStore? HeldKey(McpTokenDevice device)
    {
        IDeviceKeyStore store = custody is not null && custody.Kind == device.Custody ? custody : DeviceKeyStores.Named(device.Custody, presence: false);
        return store.PublicKey(device.Device) is byte[] key && RecordSigning.EncodePublicKey(key) == device.PublicKey ? store : null;
    }

    private async Task RegisterAsync(HttpContext http)
    {
        if (http.Request.ContentType?.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            await ErrorAsync(http, StatusCodes.Status400BadRequest, "invalid_client_metadata").ConfigureAwait(false);
            return;
        }

        McpClientMetadata? client;
        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted).ConfigureAwait(false);
            client = McpClientMetadata.Parse(document.RootElement, McpTokenStore.RandomValue(24), dynamic: true);
        }
        catch (JsonException)
        {
            client = null;
        }

        if (client is null)
        {
            await ErrorAsync(http, StatusCodes.Status400BadRequest, "invalid_client_metadata").ConfigureAwait(false);
            return;
        }

        if (!registrations.Add(client))
        {
            await ErrorAsync(http, StatusCodes.Status429TooManyRequests, "temporarily_unavailable").ConfigureAwait(false);
            return;
        }

        http.Response.StatusCode = StatusCodes.Status201Created;
        await http.Response.WriteAsJsonAsync(new { client_id = client.Id, client_name = client.Name, redirect_uris = client.Redirects,
            application_type = client.ApplicationType, token_endpoint_auth_method = "none", grant_types = GrantTypes,
            response_types = ResponseTypes }, http.RequestAborted).ConfigureAwait(false);
    }

    private async Task BeginAsync(HttpContext http)
    {
        IQueryCollection query = http.Request.Query;
        string? id = One(query["client_id"]);
        string? redirect = One(query["redirect_uri"]);
        McpClientMetadata? client = id is null || redirect is null ? null : registrations.Find(id);
        if (client is null && id is not null && redirect is not null && McpClientMetadata.IsDocumentId(id))
            client = await fetchMetadata(id, http.RequestAborted).ConfigureAwait(false);

        if (client is null || client.Id != id || redirect is null || !client.Redirects.Contains(redirect, StringComparer.Ordinal))
        {
            await ErrorAsync(http, StatusCodes.Status400BadRequest, "invalid_request").ConfigureAwait(false);
            return;
        }

        string? state = One(query["state"]);
        string? challenge = One(query["code_challenge"]);
        string? scope = ParseScope(One(query["scope"]));
        var request = new McpAuthorizationRequest(client, redirect, challenge ?? string.Empty, scope, state is { Length: <= 1024 } ? state : null);
        string? error = query.Any(p => p.Value.Count != 1) || challenge is not { Length: 43 } || !McpTokenStore.IsUrlValue(challenge)
            || One(query["code_challenge_method"]) != "S256" || One(query["resource"]) != Resource || state is { Length: > 1024 }
            ? "invalid_request" : One(query["response_type"]) != "code" ? "unsupported_response_type" : scope == "invalid" ? "invalid_scope" : null;
        if (error is not null)
        {
            await browser.RedirectAsync(http, request, null, error).ConfigureAwait(false);
            return;
        }

        string? transaction = browser.Begin(http, request);
        if (transaction is null)
        {
            await browser.RedirectAsync(http, request, null, "temporarily_unavailable").ConfigureAwait(false);
            return;
        }

        await McpBrowserAuthorization.PageAsync(http, transaction).ConfigureAwait(false);
    }

    private async Task ApproveAsync(HttpContext http)
    {
        IFormCollection? form = await FormAsync(http).ConfigureAwait(false);
        string? transaction = form is null ? null : One(form["transaction"]);
        string? action = form is null ? null : One(form["action"]);
        if (transaction is null || action is not ("prove" or "approve" or "deny")
            || browser.Flow(http, transaction, consume: action != "prove") is not { } flow)
        {
            await ErrorAsync(http, StatusCodes.Status400BadRequest, "invalid_request").ConfigureAwait(false);
            return;
        }

        if (action == "prove")
        {
            McpApproval? approval = flow.Approval ?? approvals.Prove(One(form!["approval_code"]) ?? string.Empty);
            if (approval is null || host.Service.PairingAuthorityRefusal(approval.Authority) is not null)
            {
                await McpBrowserAuthorization.PageAsync(http, transaction, denied: true).ConfigureAwait(false);
                return;
            }

            if (flow.Request.Scope == "contribute" && approval.Scope != "contribute")
            {
                await browser.RedirectAsync(http, flow.Request, null, "insufficient_scope").ConfigureAwait(false);
                return;
            }

            if (flow.Request.Scope == "read") approval = approval with { Scope = "read" };
            if (flow.Approval is not null || browser.Confirm(transaction, flow, approval))
                await McpBrowserAuthorization.PageAsync(http, transaction, host.Service.PairingPlan(approval.Principal), approval, flow.Request.Client, flow.Request.Redirect).ConfigureAwait(false);
            else
                await ErrorAsync(http, StatusCodes.Status400BadRequest, "invalid_request").ConfigureAwait(false);
            return;
        }

        if (action == "deny" || flow.Approval is not { } accepted || host.Service.PairingAuthorityRefusal(accepted.Authority) is not null)
        {
            await browser.RedirectAsync(http, flow.Request, null, "access_denied").ConfigureAwait(false);
            return;
        }

        McpTokenDevice? device;
        if (accepted.Renew is { } renew)
        {
            device = host.Service.WithMcpTokenDevice(renew.Device, renew.Admission, renew.PublicKey,
                current => HeldKey(current) is null ? null : current);
        }
        else
        {
            (device, _) = host.Service.CreateMcpTokenDevice(accepted.Principal, accepted.Authority, custody);
            if (device is not null) host.Service.NoteMcpTokenClient(device.Device, accepted.Authority.Principal, reissued: false);
        }

        if (device is null || device.Principal != accepted.Principal)
        {
            await browser.RedirectAsync(http, flow.Request, null, "access_denied").ConfigureAwait(false);
            return;
        }

        var grant = new McpTokenGrant(device.Device, device.Principal, device.PublicKey, device.Admission, accepted.Authority.Principal,
            flow.Request.Client.Id, Resource, accepted.Scope, accepted.ExpiresAt);
        registrations.Use(flow.Request.Client.Id);
        string? code = browser.Complete(flow.Request, grant);
        await browser.RedirectAsync(http, flow.Request, code, code is null ? "temporarily_unavailable" : null).ConfigureAwait(false);
    }

    private async Task TokenAsync(HttpContext http)
    {
        IFormCollection? form = await FormAsync(http).ConfigureAwait(false);
        if (form is null || One(form["client_id"]) is not string client || One(form["resource"]) != Resource
            || form.ContainsKey("client_secret") || http.Request.Headers.ContainsKey("Authorization"))
        {
            await ErrorAsync(http, StatusCodes.Status400BadRequest, "invalid_request").ConfigureAwait(false);
            return;
        }

        McpTokenResponse? response = null;
        string error = "invalid_grant";
        switch (One(form["grant_type"]))
        {
            case "authorization_code":
                McpTokenGrant? grant = browser.Exchange(One(form["code"]) ?? string.Empty, client, One(form["redirect_uri"]) ?? string.Empty,
                    Resource, One(form["code_verifier"]) ?? string.Empty);
                if (grant is not null)
                {
                    response = host.Service.WithMcpTokenDevice(grant.Device, grant.Admission, grant.PublicKey,
                        device => HeldKey(device) is null ? null : tokens.Create(grant));
                    if (response is not null) host.Mcp.Sessions.EndDevice(grant.Device);
                }
                break;
            case "refresh_token":
                McpTokenRefresh? refresh = tokens.RefreshGrant(One(form["refresh_token"]) ?? string.Empty, client, Resource);
                string? requestedScope = ParseScope(One(form["scope"]));
                if (refresh is not null && (requestedScope is null || requestedScope == "read" || requestedScope == refresh.Grant.Scope))
                {
                    (response, bool reused) = host.Service.WithMcpTokenDevice(refresh.Grant.Device, refresh.Grant.Admission, refresh.Grant.PublicKey,
                        device => HeldKey(device) is null ? default : tokens.Rotate(refresh, requestedScope));
                    if (reused)
                    {
                        host.Mcp.Sessions.EndDevice(refresh.Grant.Device);
                        host.Service.NoteMcpTokenClient(refresh.Grant.Device, refresh.Grant.By, reissued: true);
                    }
                }

                break;
            default:
                error = "unsupported_grant_type";
                break;
        }

        if (response is null)
        {
            await ErrorAsync(http, StatusCodes.Status400BadRequest, error).ConfigureAwait(false);
            return;
        }

        http.Response.Headers.Pragma = "no-cache";
        await http.Response.WriteAsJsonAsync(new { access_token = response.AccessToken, refresh_token = response.RefreshToken,
            token_type = "Bearer", expires_in = response.ExpiresIn, scope = response.Scope }, http.RequestAborted).ConfigureAwait(false);
    }

    private static string? One(StringValues values) => values.Count == 1 && !string.IsNullOrEmpty(values[0]) ? values[0] : null;

    private static string? ParseScope(string? scope)
    {
        if (scope is null) return null;
        string[] scopes = scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return scopes.Length == 0 || scopes.Any(s => !Scopes.Contains(s, StringComparer.Ordinal)) ? "invalid"
            : scopes.Contains("contribute", StringComparer.Ordinal) ? "contribute" : "read";
    }

    private static async Task<IFormCollection?> FormAsync(HttpContext http)
    {
        if (http.Request.ContentType?.Split(';')[0].Trim().Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) != true) return null;
        IFormCollection form = await http.Request.ReadFormAsync(http.RequestAborted).ConfigureAwait(false);
        return form.Any(p => p.Value.Count != 1) ? null : form;
    }

    private static Task ErrorAsync(HttpContext http, int status, string error)
    {
        http.Response.StatusCode = status;
        return http.Response.WriteAsJsonAsync(new { error }, http.RequestAborted);
    }
}

internal sealed record McpTokenRequest(string Scope, DateTimeOffset ExpiresAt, Func<bool> Current, Func<string, string> Sign);
