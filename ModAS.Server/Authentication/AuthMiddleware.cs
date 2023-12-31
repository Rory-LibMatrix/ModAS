using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net.Http.Headers;
using System.Text.Json;
using LibMatrix;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using ModAS.Server.Attributes;
using MxApiExtensions.Services;

namespace ModAS.Server.Authentication;

public class AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger, ModASConfiguration config, HomeserverProviderService hsProvider, AppServiceRegistration asr) {
    public async Task InvokeAsync(HttpContext context) {
        context.Request.Query.TryGetValue("access_token", out var queryAccessToken);
        var accessToken = queryAccessToken.FirstOrDefault();
        accessToken ??= context.Request.GetTypedHeaders().Get<AuthenticationHeaderValue>("Authorization")?.Parameter;

        //get UserAuth custom attribute
        var endpoint = context.GetEndpoint();
        if (endpoint is null) {
            Console.WriteLine($"Ignoring authentication, endpoint is null!");
            await next(context);
            return;
        }

        var authAttribute = endpoint?.Metadata.GetMetadata<UserAuthAttribute>();
        if (authAttribute is null) {
            if (string.IsNullOrWhiteSpace(accessToken)) {
                // auth is optional if auth attribute isnt set
                Console.WriteLine($"Allowing unauthenticated request, AuthAttribute is not set!");
                await next(context);
                return;
            }
        }
        else
            logger.LogInformation($"{nameof(Route)} authorization: {authAttribute.ToJson()}");

        if (string.IsNullOrWhiteSpace(accessToken))
            if (authAttribute is not null) {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new MatrixException() {
                    ErrorCode = "M_UNAUTHORIZED",
                    Error = "Missing access token"
                }.GetAsObject());
                return;
            }

        if (await ValidateAuth(authAttribute, context, accessToken))
            await next(context);
    }

    private async Task<bool> ValidateAuth(UserAuthAttribute? authAttribute, HttpContext context, string? accessToken) {
        try {
            switch (authAttribute?.AuthType) {
                case null:
                case AuthType.User:
                    if (string.IsNullOrWhiteSpace(accessToken) && authAttribute is null)
                        return true; //we dont care in this case
                    var authUser = await GetAuthUser(accessToken!);
                    context.Items.Add("AuthUser", authUser);
                    return true;
                case AuthType.Server:
                    if (asr.HomeserverToken != accessToken)
                        throw new MatrixException() {
                            ErrorCode = "M_UNAUTHORIZED",
                            Error = "Invalid access token"
                        };
                    return true;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (MatrixException e) {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(e.GetAsObject());
            return false;
        }
    }

    private static readonly Dictionary<string, AuthUser> AuthCache = new();

    private async Task<AuthUser> GetAuthUser(string accessToken) {
        if (AuthCache.TryGetValue(accessToken, out var authUser)) return authUser;
        var homeserver = await hsProvider.GetAuthenticatedWithToken(config.ServerName, accessToken, config.HomeserverUrl);

        return AuthCache[accessToken] = new AuthUser() {
            Homeserver = homeserver,
            AccessToken = accessToken,
            Roles = config.Roles.Where(r => r.Value.Contains(homeserver.WhoAmI.UserId)).Select(r => r.Key).ToList()
        };
    }
}