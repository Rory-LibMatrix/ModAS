using System.Net.Http.Headers;
using LibMatrix;
using LibMatrix.Services;
using MxApiExtensions.Extensions;
using MxApiExtensions.Services;

namespace ModAS.Server.Services;

public class AuthenticationService(
    ILogger<AuthenticationService> logger,
    ModASConfiguration config,
    IHttpContextAccessor request,
    HomeserverProviderService homeserverProviderService) {
    private readonly HttpRequest _request = request.HttpContext!.Request;

    private static Dictionary<string, string> _tokenMap = new();

    internal string? GetToken(bool fail = true) {
        //_request.GetTypedHeaders().Get<AuthenticationHeaderValue>("Authorization")?.Parameter != asr.HomeserverToken

        string? token = null;
        if (_request.GetTypedHeaders().TryGet<AuthenticationHeaderValue>("Authorization", out var authHeader) && !string.IsNullOrWhiteSpace(authHeader?.Parameter)) {
            token = authHeader.Parameter;
        }
        else if (_request.Query.ContainsKey("access_token")) {
            token = _request.Query["access_token"];
        }

        if (string.IsNullOrWhiteSpace(token) && fail) {
            throw new MatrixException() {
                ErrorCode = "M_MISSING_TOKEN",
                Error = "Missing access token"
            };
        }

        return token;
    }

    public async Task<string> GetMxidFromToken(string? token = null, bool fail = true) {
        token ??= GetToken(fail);
        if (string.IsNullOrWhiteSpace(token)) {
            if (fail) {
                throw new MatrixException() {
                    ErrorCode = "M_MISSING_TOKEN",
                    Error = "Missing access token"
                };
            }

            return "@anonymous:*";
        }

        if (_tokenMap is not { Count: > 0 } && File.Exists("token_map")) {
            _tokenMap = (await File.ReadAllLinesAsync("token_map"))
                .Select(l => l.Split('\t'))
                .ToDictionary(l => l[0], l => l[1]);
        }

        if (_tokenMap.TryGetValue(token, out var mxid)) return mxid;

        logger.LogInformation("Looking up mxid for token {}", token);
        var hs = await homeserverProviderService.GetAuthenticatedWithToken(config.ServerName, token, config.HomeserverUrl);
        try {
            var res = hs.WhoAmI.UserId;
            logger.LogInformation("Got mxid {} for token {}", res, token);

            return res;
        }
        catch (MatrixException e) {
            if (e.ErrorCode == "M_UNKNOWN_TOKEN") {
                return null;
            }

            throw;
        }
    }
}