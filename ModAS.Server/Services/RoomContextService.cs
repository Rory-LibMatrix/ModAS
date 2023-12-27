using ArcaneLibs.Extensions;
using Elastic.Apm;
using Elastic.Apm.Api;
using LibMatrix;
using LibMatrix.EventTypes.Spec.State;
using LibMatrix.RoomTypes;
using MxApiExtensions.Services;

namespace ModAS.Server.Services;

public class RoomContextService(UserProviderService userProvider, ModASConfiguration config) {
    public Dictionary<string, RoomContext> RoomContexts { get; } = new();
    public Dictionary<string, List<string>> LocalUsersByRoom { get; private set; } = new();

    public async Task<RoomContext?> GetRoomContext(GenericRoom room) {
        if (RoomContexts.TryGetValue(room.RoomId, out var roomContext) && !roomContext.NeedsUpdate) return roomContext;

        var newRoomContext = await FetchRoomContext(room.RoomId);
        if (newRoomContext is not null) RoomContexts[room.RoomId] = newRoomContext;
        return roomContext;
    }

    public async Task<RoomContext?> GetRoomContext(string roomId) {
        if (RoomContexts.TryGetValue(roomId, out var roomContext) && !roomContext.NeedsUpdate) return roomContext;

        var newRoomContext = await FetchRoomContext(roomId);
        if (newRoomContext is not null) RoomContexts[roomId] = newRoomContext;
        return roomContext;
    }

    public async Task<RoomContext?> FetchRoomContext(string roomId) {
        var span = currentTransaction.StartSpan($"FetchRoomContext - {roomId}", ApiConstants.TypeApp);
        if (!LocalUsersByRoom.ContainsKey(roomId))
            await UpdateLocalUserRoomLists();
        if (!LocalUsersByRoom.TryGetValue(roomId, out var localUsers)) return null;
        if (localUsers.Count == 0) return null;

        var roomContext = new RoomContext {
            RoomId = roomId
        };

        var room = (await userProvider.GetImpersonatedHomeserver(localUsers.First())).GetRoom(roomId);
        var roomMembers = await room.GetMembersListAsync(false);
        
        roomContext.UserCountByMembership = roomMembers.GroupBy(x => (x.TypedContent as RoomMemberEventContent)?.Membership)
            .ToDictionary(x => x.Key, x => x.Count());
        roomContext.LocalUsers = roomMembers.Select(x => x.StateKey).Where(x => x.EndsWith(':' + config.ServerName)).ToList();
        roomContext.CurrentLocalUsers = roomMembers.Where(x => x.StateKey.EndsWith(':' + config.ServerName) && (x.TypedContent as RoomMemberEventContent)?.Membership == "join")
            .Select(x => x.StateKey).ToList();

        var powerLevels = await room.GetPowerLevelsAsync();
        roomContext.LocalUsersByStatePermission = powerLevels?.Events?
            .Select(@event => (@event.Key, roomContext.CurrentLocalUsers.Where(clu => powerLevels.UserHasStatePermission(clu, @event.Key))))
            .Where(x => x.Item2.Any())
            .ToDictionary(x => x.Key, x => x.Item2.ToList());

        roomContext.LastUpdate = DateTime.Now;
        
        span.End();
        return roomContext;
    }

    private async Task UpdateLocalUserRoomLists() {
        var span = currentTransaction.StartSpan("UpdateLocalUserRoomLists", ApiConstants.TypeApp);
        var newLocalUsersByRoom = new Dictionary<string, List<string>>();
        var users = await userProvider.GetValidUsers();
        var getRoomsSpan = currentTransaction.StartSpan("GetRooms", ApiConstants.TypeApp);
        var userRoomLists = users.Values.Select(ahs => (ahs, ahs.GetJoinedRooms())).ToList();
        await Task.WhenAll(userRoomLists.Select(x => x.Item2));
        getRoomsSpan.End();
        foreach (var (ahs, rooms) in userRoomLists) {
            foreach (var room in rooms.Result) {
                newLocalUsersByRoom.TryAdd(room.RoomId, new List<string>());
                newLocalUsersByRoom[room.RoomId].Add(ahs.UserId);
            }
        }
        span.End();
        LocalUsersByRoom = newLocalUsersByRoom;
    }

    public class RoomContext {
        public string RoomId { get; set; }
        public List<string> LocalUsers { get; set; }
        public List<string> CurrentLocalUsers { get; set; }
        public Dictionary<string, int> UserCountByMembership { get; set; }
        public Dictionary<string, List<string>> LocalUsersByStatePermission { get; set; }

        public DateTime LastUpdate { get; set; }
        public bool NeedsUpdate => DateTime.Now - LastUpdate > TimeSpan.FromMinutes(5);
    }

    public async Task<GenericRoom?> GetRoomReferenceById(string roomId) {
        var roomContext = await GetRoomContext(roomId);
        var localUsers = roomContext.LocalUsers.Select(userProvider.GetImpersonatedHomeserver).ToAsyncEnumerable();
        await foreach (var localUser in localUsers) {
            var room = localUser.GetRoom(roomId);
            try {
                if (await room.GetCreateEventAsync() is not null)
                    return room;
            }
            catch (MatrixException e) {
                if (e is { ErrorCode: "M_UNAUTHORIZED" }) continue;
                throw;
            }
        }

        return null;
    }
    
    private static ITransaction currentTransaction => Agent.Tracer.CurrentTransaction;
}