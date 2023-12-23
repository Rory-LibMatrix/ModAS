using System.Collections.Concurrent;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.Homeservers;
using LibMatrix.Services;
using MxApiExtensions.Services;

namespace ModAS.Server.Services;

public class AuthenticatedHomeserverProviderService(
    AuthenticationService authenticationService,
    HomeserverProviderService homeserverProviderService,
    IHttpContextAccessor request,
    ModASConfiguration config,
    AppServiceRegistration asRegistration
    ) {
    public HttpContext? _context = request.HttpContext;
    public ConcurrentDictionary<string, AuthenticatedHomeserverGeneric> KnownUsers { get; set; } = new();

    public async Task<AuthenticatedHomeserverGeneric> GetImpersonatedHomeserver(string mxid) {
        if (!KnownUsers.TryGetValue(mxid, out var homeserver)) {
            homeserver = await homeserverProviderService.GetAuthenticatedWithToken(config.ServerName, asRegistration.AppServiceToken, config.HomeserverUrl);
            KnownUsers.TryAdd(mxid, homeserver);
        }
        //var hs = await homeserverProviderService.GetAuthenticatedWithToken(config.ServerName, asRegistration.AsToken, config.HomeserverUrl);
        await homeserver.SetImpersonate(mxid);
        // KnownUsers.TryAdd(mxid, homeserver);
        return homeserver;
    }
    
    public async Task<AuthenticatedHomeserverGeneric> GetHomeserver() {
        var token = authenticationService.GetToken();
        if (token == null) {
            throw new MatrixException {
                ErrorCode = "M_MISSING_TOKEN",
                Error = "Missing access token"
            };
        }

        var mxid = await authenticationService.GetMxidFromToken(token);
        if (mxid == "@anonymous:*") {
            throw new MatrixException {
                ErrorCode = "M_MISSING_TOKEN",
                Error = "Missing access token"
            };
        }

        var hsCanonical = string.Join(":", mxid.Split(':').Skip(1));
        return await homeserverProviderService.GetAuthenticatedWithToken(hsCanonical, token);
    }
}