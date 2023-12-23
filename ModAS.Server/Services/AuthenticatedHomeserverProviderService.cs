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
    public Dictionary<string, AuthenticatedHomeserverGeneric> KnownUsers { get; set; } = new();

    public async Task<AuthenticatedHomeserverGeneric> GetImpersonatedHomeserver(string mxid) {
        if (KnownUsers.TryGetValue(mxid, out var homeserver)) return homeserver;
        var hs = await homeserverProviderService.GetAuthenticatedWithToken(config.ServerName, asRegistration.AsToken, config.HomeserverUrl);
        await hs.SetImpersonate(mxid);
        KnownUsers.TryAdd(mxid, hs);
        return hs;
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