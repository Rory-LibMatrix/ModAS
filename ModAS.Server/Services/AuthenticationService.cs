using LibMatrix;
using LibMatrix.Services;
using MxApiExtensions.Services;

namespace ModAS.Server.Services;

public class AuthenticationService(ILogger<AuthenticationService> logger, ModASConfiguration config, IHttpContextAccessor request, HomeserverProviderService homeserverProviderService) {
    private readonly HttpRequest _request = request.HttpContext!.Request;

    private static Dictionary<string, string> _tokenMap = new();

    internal string? GetToken(bool fail = true) {
        string? token;
        if (_request.Headers.TryGetValue("Authorization", out var tokens)) {
            token = tokens.FirstOrDefault()?[7..];
        }
        else {
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

        var lookupTasks = new Dictionary<string, Task<string?>>();
        
        
        logger.LogInformation("Looking up mxid for token {}", token);
        var hs = await homeserverProviderService.GetAuthenticatedWithToken(config.ServerName, token, config.HomeserverUrl);
        try {
            var res = hs.WhoAmI.UserId;
            logger.LogInformation("Got mxid {} for token {}", res, token);
            await SaveMxidForToken(token, mxid);

            return res;
        }
        catch (MatrixException e) {
            if (e.ErrorCode == "M_UNKNOWN_TOKEN") {
                return null;
            }

            throw;
        }
    }


    public async Task SaveMxidForToken(string token, string mxid) {
        _tokenMap.Add(token, mxid);
        await File.AppendAllLinesAsync("token_map", new[] { $"{token}\t{mxid}" });
    }
}
