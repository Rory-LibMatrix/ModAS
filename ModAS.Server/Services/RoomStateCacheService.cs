using System.Collections.Frozen;
using ArcaneLibs.Extensions;
using Elastic.Apm;
using Elastic.Apm.Api;
using LibMatrix;
using LibMatrix.RoomTypes;

namespace ModAS.Server.Services;

public class RoomStateCacheService(RoomContextService roomContextService) {
    public FrozenDictionary<string, FrozenSet<StateEventResponse>> RoomStateCache { get; private set; } = FrozenDictionary<string, FrozenSet<StateEventResponse>>.Empty;
    private SemaphoreSlim updateLock = new(1, 1);
    public async Task<FrozenSet<StateEventResponse>> GetRoomState(string roomId, GenericRoom? roomReference = null) {
        if (RoomStateCache.TryGetValue(roomId, out var roomState)) return roomState;
        return await InvalidateRoomState(roomId, roomReference);
    }

    public async Task<FrozenSet<StateEventResponse>> InvalidateRoomState(string roomId, GenericRoom? roomReference = null) {
        var invalidateSpan = currentTransaction.StartSpan($"invalidateRoomState - {roomId}", ApiConstants.TypeApp);
        var getRoomReferenceSpan = currentTransaction.StartSpan($"getRoomReference - {roomId}", ApiConstants.TypeApp);
        if (roomReference is null) {
            var rc = await roomContextService.GetRoomContext(roomId);
            if (rc is null) return FrozenSet<StateEventResponse>.Empty;
            roomReference = await roomContextService.GetRoomReferenceById(roomId);
        }

        if (roomReference is null) {
            currentTransaction.CaptureException(new Exception("Could not get room reference for room state invalidation"), roomId, true);
            return FrozenSet<StateEventResponse>.Empty;
        }

        getRoomReferenceSpan.End();

        var updateSpan = currentTransaction.StartSpan($"updateRoomState - {roomId}", ApiConstants.TypeApp);
        await updateLock.WaitAsync();
        var unfrozen = RoomStateCache.ToDictionary();
        unfrozen[roomId] = (await roomReference.GetFullStateAsListAsync()).ToFrozenSet();
        RoomStateCache = unfrozen.ToFrozenDictionary();
        updateSpan.End();
        updateLock.Release();

        invalidateSpan.End();
        if (!RoomStateCache.ContainsKey(roomId)) {
            currentTransaction.CaptureException(new Exception("Room state cache does not contain room after invalidation"), roomId, false);
            if (!unfrozen.ContainsKey(roomId))
                currentTransaction.CaptureException(new Exception("Unfrozen room state cache does not contain room after invalidation either..."), roomId, false);
        }

        return RoomStateCache[roomId];
    }

    public async Task EnsureCachedFromRoomList(IEnumerable<GenericRoom> rooms) {
        await updateLock.WaitAsync();
        var unfrozen = RoomStateCache.ToDictionary();
        
        var tasks = rooms.Select(async room => {
            if (RoomStateCache.ContainsKey(room.RoomId)) return;
            unfrozen[room.RoomId] = (await room.GetFullStateAsListAsync()).ToFrozenSet();
        }).ToList();
        await Task.WhenAll(tasks);
        RoomStateCache = unfrozen.ToFrozenDictionary();
        updateLock.Release();
    }

    private static ITransaction currentTransaction => Agent.Tracer.CurrentTransaction;
}