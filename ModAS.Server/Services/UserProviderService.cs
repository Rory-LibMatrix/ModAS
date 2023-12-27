using System.Collections.Concurrent;
using ArcaneLibs.Extensions;
using Elastic.Apm;
using Elastic.Apm.Api;
using LibMatrix;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
using LibMatrix.Services;
using MxApiExtensions.Services;

namespace ModAS.Server.Services;

public class UserProviderService(
    AuthenticationService authenticationService,
    HomeserverProviderService homeserverProviderService,
    IHttpContextAccessor request,
    ModASConfiguration config,
    AppServiceRegistration asRegistration
) {
    public HttpContext? _context = request.HttpContext;

    private static ITransaction currentTransaction => Agent.Tracer.CurrentTransaction;

    private SemaphoreSlim updateLock = new(1,1);
    
    public ConcurrentDictionary<string, AuthenticatedHomeserverGeneric> KnownUsers { get; set; } = new();
    public ConcurrentDictionary<string, DateTime> UserValidationExpiry { get; set; } = new();
    public ConcurrentDictionary<string, List<string>> CachedUserRooms { get; set; } = new();
    public ConcurrentDictionary<string, DateTime> CachedUserRoomsExpiry { get; set; } = new();
    

    public async Task<AuthenticatedHomeserverGeneric> GetImpersonatedHomeserver(string mxid) {
        var span = currentTransaction.StartSpan("GetImpersonatedHomeserver", ApiConstants.TypeApp);
        if (!KnownUsers.TryGetValue(mxid, out var homeserver)) {
            var getUserSpan = currentTransaction.StartSpan($"GetUser - {mxid}", ApiConstants.TypeApp);
            homeserver = await homeserverProviderService.GetAuthenticatedWithToken(config.ServerName, asRegistration.AppServiceToken, config.HomeserverUrl, mxid);
            KnownUsers.TryAdd(mxid, homeserver);
            getUserSpan.End();
        }

        await homeserver.SetImpersonate(mxid);
        span.End();
        return homeserver;
    }

    public async Task<Dictionary<string, AuthenticatedHomeserverGeneric>> GetValidUsers() {
        var span = currentTransaction.StartSpan("GetValidUsers", ApiConstants.TypeApp);
        var tasks = KnownUsers.Select(kvp => ValidateUser(kvp.Key, kvp.Value)).ToList();
        var results = await Task.WhenAll(tasks);
        var validUsers = results.Where(r => r.Value is not null).ToDictionary(r => r.Key, r => r.Value!);
        span.End();
        return validUsers;
    }
    
    public async Task<List<GenericRoom>> GetUserRoomsCached(string mxid) {
        var span = currentTransaction.StartSpan($"GetUserRoomsCached - {mxid}", ApiConstants.TypeApp);
        var hs = await GetImpersonatedHomeserver(mxid);
        if (CachedUserRoomsExpiry.TryGetValue(mxid, out var expiry) && expiry > DateTime.Now) {
            if (CachedUserRooms.TryGetValue(mxid, out var rooms)) {
                span.End();
                return rooms.Select(hs.GetRoom).ToList();
            }
        }

        var userRooms = await hs.GetJoinedRooms();
        await updateLock.WaitAsync();
        CachedUserRooms[mxid] = userRooms.Select(r => r.RoomId).ToList();
        CachedUserRoomsExpiry[mxid] = DateTime.Now + TimeSpan.FromMinutes(5);
        updateLock.Release();
        span.End();
        return userRooms;
    }

    private async Task<KeyValuePair<string, AuthenticatedHomeserverGeneric?>> ValidateUser(string mxid, AuthenticatedHomeserverGeneric hs) {
        if(UserValidationExpiry.TryGetValue(mxid, out var expires))
            if(DateTime.Now < expires) return new KeyValuePair<string, AuthenticatedHomeserverGeneric?>(mxid, hs);
        var span = currentTransaction.StartSpan($"ValidateUser - {mxid}", ApiConstants.TypeApp);
        try {
            await hs.GetJoinedRooms();
            await updateLock.WaitAsync();
            UserValidationExpiry[mxid] = DateTime.Now + TimeSpan.FromMinutes(5);
            updateLock.Release();
            return new KeyValuePair<string, AuthenticatedHomeserverGeneric?>(mxid, hs);
        }
        catch (MatrixException e) {
            if (e.ErrorCode == "M_FORBIDDEN") {
                return new KeyValuePair<string, AuthenticatedHomeserverGeneric?>(mxid, null);
            }
            throw;
        }
        finally {
            span.End();
        }
    }
}