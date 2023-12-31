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
        if (authAttribute is not null)
            logger.LogInformation($"{nameof(Route)} authorization: {authAttribute.ToJson()}");
        else if (string.IsNullOrWhiteSpace(accessToken)) {
            // auth is optional if auth attribute isnt set
            Console.WriteLine($"Allowing unauthenticated request, AuthAttribute is not set!");
            await next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
            if (authAttribute is not null) {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new MatrixException() {
                    ErrorCode = "M_UNAUTHORIZED",
                    Error = "Missing access token"
                }.GetAsObject());
                return;
            }

        try {
            switch (authAttribute.AuthType) {
                case AuthType.User:
                    var authUser = await GetAuthUser(accessToken);
                    context.Items.Add("AuthUser", authUser);
                    break;
                case AuthType.Server:
                    if (asr.HomeserverToken != accessToken)
                        throw new MatrixException() {
                            ErrorCode = "M_UNAUTHORIZED",
                            Error = "Invalid access token"
                        };

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (MatrixException e) {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(e.GetAsObject());
            return;
        }

        await next(context);
    }

    private async Task<AuthUser> GetAuthUser(string accessToken) {
        AuthenticatedHomeserverGeneric? homeserver;
        homeserver = await hsProvider.GetAuthenticatedWithToken(config.ServerName, accessToken, config.HomeserverUrl);

        return new AuthUser() {
            Homeserver = homeserver,
            AccessToken = accessToken,
            Roles = config.Roles.Where(r => r.Value.Contains(homeserver.WhoAmI.UserId)).Select(r => r.Key).ToList()
        };
    }
}