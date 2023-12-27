using System.Collections.Frozen;
using ArcaneLibs.Extensions;
using Elastic.Apm;
using Elastic.Apm.Api;
using LibMatrix;
using LibMatrix.EventTypes.Spec.State;
using LibMatrix.EventTypes.Spec.State.RoomInfo;
using LibMatrix.Responses.ModAS;
using LibMatrix.RoomTypes;
using Microsoft.AspNetCore.Mvc;
using ModAS.Classes;
using ModAS.Server.Services;
using MxApiExtensions.Services;

namespace ModAS.Server.Controllers;

[ApiController]
public class RoomQueryController(UserProviderService ahsProvider, ModASConfiguration config, RoomStateCacheService stateCacheService, UserProviderService userProviderService) : ControllerBase {
    [HttpGet("/_matrix/_modas/room_query")]
    public async IAsyncEnumerable<ModASRoomQueryResult> RoomQuery() {
        var fetchRoomQueryDataSpan = currentTransaction.StartSpan("fetchRoomQueryData", ApiConstants.TypeApp);
        List<string> processedRooms = new();
        var getUsersSpan = currentTransaction.StartSpan("getUsers", ApiConstants.TypeApp);
        var validUsers = await ahsProvider.GetValidUsers();
        getUsersSpan.End();

        var collectRoomsSpan = currentTransaction.StartSpan("collectRooms", ApiConstants.TypeApp);
        // var userRoomLists = validUsers.Values.Select(ahs => ahs.GetJoinedRooms()).ToList();
        // await Task.WhenAll(userRoomLists);
        // var rooms = userRoomLists.SelectMany(r => r.Result).DistinctBy(x => x.RoomId).ToFrozenSet();
        var roomsTasks = validUsers.Values.Select(u => userProviderService.GetUserRoomsCached(u.WhoAmI.UserId)).ToList();
        await Task.WhenAll(roomsTasks);
        var rooms = roomsTasks.SelectMany(r => r.Result).DistinctBy(x => x.RoomId).ToFrozenSet();
        collectRoomsSpan.End();

        var collectRoomStateSpan = currentTransaction.StartSpan("collectRoomState", ApiConstants.TypeApp);
        //make sure we lock!!!!
        await stateCacheService.EnsureCachedFromRoomList(rooms);
        var roomStateTasks = rooms.Select(GetRoomState).ToAsyncEnumerable();
        var awaitRoomStateSpan = currentTransaction.StartSpan("fetchRoomState", ApiConstants.TypeApp);
        await foreach (var (room, roomState) in roomStateTasks) {
            awaitRoomStateSpan.Name = $"awaitRoomState {room.RoomId}";
            awaitRoomStateSpan.End();

            var filterStateEventsSpan = currentTransaction.StartSpan($"filterStateEvents {room.RoomId}", ApiConstants.TypeApp);
            var roomMembers = roomState.Where(r => r.Type == RoomMemberEventContent.EventId).ToFrozenSet();
            var localRoomMembers = roomMembers.Where(x => x.StateKey.EndsWith(':' + config.ServerName)).ToFrozenSet();
            var nonMemberState = roomState.Where(x => !roomMembers.Contains(x)).ToFrozenSet();
            filterStateEventsSpan.End();
            var buildResultSpan = currentTransaction.StartSpan($"buildResult {room.RoomId}", ApiConstants.TypeApp);

            // forgive me for raw json access... attempt at optimisation -emma
            yield return new ModASRoomQueryResult {
                RoomId = room.RoomId,
                StateEvents = roomState.Count,
                //members
                TotalMembers = roomMembers.Count,
                TotalLocalMembers = localRoomMembers.Count,
                JoinedMembers = roomMembers.Count(x => (x.TypedContent as RoomMemberEventContent)?.Membership == "join"),
                JoinedLocalMembers = localRoomMembers.Count(x => (x.TypedContent as RoomMemberEventContent)?.Membership == "join"),
                //-members
                //creation event
                Creator = nonMemberState.FirstOrDefault(r => r.Type == RoomCreateEventContent.EventId)?.Sender,
                Version = nonMemberState.FirstOrDefault(r => r.Type == RoomCreateEventContent.EventId)?.RawContent?["room_version"]?.GetValue<string>(),
                Type = nonMemberState.FirstOrDefault(r => r.Type == RoomCreateEventContent.EventId)?.RawContent?["type"]?.GetValue<string>(),
                Federatable = nonMemberState.FirstOrDefault(r => r.Type == RoomCreateEventContent.EventId)?.RawContent?["m.federate"]?.GetValue<bool>() ?? true,
                //-creation event
                Name = nonMemberState.FirstOrDefault(r => r.Type == RoomNameEventContent.EventId)?.RawContent?["name"]?.GetValue<string>(),
                CanonicalAlias = nonMemberState.FirstOrDefault(r => r.Type == RoomCanonicalAliasEventContent.EventId)?.RawContent?["alias"]?.GetValue<string>(),
                JoinRules = nonMemberState.FirstOrDefault(r => r.Type == RoomJoinRulesEventContent.EventId)?.RawContent?["join_rule"]?.GetValue<string>(),
                GuestAccess = nonMemberState.FirstOrDefault(r => r.Type == RoomGuestAccessEventContent.EventId)?.RawContent?["guest_access"]?.GetValue<string>(),
                HistoryVisibility =
                    nonMemberState.FirstOrDefault(r => r.Type == RoomHistoryVisibilityEventContent.EventId)?.RawContent?["history_visibility"]?.GetValue<string>(),
                Public = nonMemberState.FirstOrDefault(r => r.Type == RoomJoinRulesEventContent.EventId)?.RawContent?["join_rule"]?.GetValue<string>() == "public",
                Encryption = nonMemberState.FirstOrDefault(r => r.Type == RoomEncryptionEventContent.EventId)?.RawContent?["algorithm"]?.GetValue<string>(),
                AvatarUrl = nonMemberState.FirstOrDefault(r => r.Type == RoomAvatarEventContent.EventId)?.RawContent?["url"]?.GetValue<string>(),
                RoomTopic = nonMemberState.FirstOrDefault(r => r.Type == RoomTopicEventContent.EventId)?.RawContent?["topic"]?.GetValue<string>()
            };
            buildResultSpan.End();
            awaitRoomStateSpan = currentTransaction.StartSpan("fetchRoomState", ApiConstants.TypeApp);
        }

        collectRoomStateSpan.End();
        fetchRoomQueryDataSpan.End();
    }

    [HttpPost("/_matrix/_modas/room_query")]
    public async IAsyncEnumerable<ModASRoomQueryResult> RoomQuery([FromBody] RoomQueryFilter request) {
        await foreach (var room in RoomQuery()) {
            if (!string.IsNullOrWhiteSpace(request.RoomIdContains) && !room.RoomId.Contains(request.RoomIdContains)) continue;

            if (!string.IsNullOrWhiteSpace(request.NameContains) && room.Name?.Contains(request.NameContains) != true) continue;

            if (!string.IsNullOrWhiteSpace(request.CanonicalAliasContains) && room.CanonicalAlias?.Contains(request.CanonicalAliasContains) != true) continue;

            if (!string.IsNullOrWhiteSpace(request.VersionContains) && room.Version?.Contains(request.VersionContains) != true) continue;

            if (!string.IsNullOrWhiteSpace(request.CreatorContains) && room.Creator?.Contains(request.CreatorContains) != true) continue;

            if (!string.IsNullOrWhiteSpace(request.EncryptionContains) && room.Encryption?.Contains(request.EncryptionContains) != true) continue;

            if (!string.IsNullOrWhiteSpace(request.JoinRulesContains) && room.JoinRules?.Contains(request.JoinRulesContains) != true) continue;

            if (!string.IsNullOrWhiteSpace(request.GuestAccessContains) && room.GuestAccess?.Contains(request.GuestAccessContains) != true) continue;

            if (!string.IsNullOrWhiteSpace(request.HistoryVisibilityContains) && room.HistoryVisibility?.Contains(request.HistoryVisibilityContains) != true) continue;

            if (!string.IsNullOrWhiteSpace(request.AvatarUrlContains) && room.AvatarUrl?.Contains(request.AvatarUrlContains) != true) continue;

            if (!string.IsNullOrWhiteSpace(request.RoomTopicContains) && room.RoomTopic?.Contains(request.RoomTopicContains) != true) continue;

            if (request.JoinedMembersMin.HasValue && room.JoinedMembers < request.JoinedMembersMin.Value) continue;
            if (request.JoinedMembersMax.HasValue && room.JoinedMembers > request.JoinedMembersMax.Value) continue;

            if (request.JoinedLocalMembersMin.HasValue && room.JoinedLocalMembers < request.JoinedLocalMembersMin.Value) continue;
            if (request.JoinedLocalMembersMax.HasValue && room.JoinedLocalMembers > request.JoinedLocalMembersMax.Value) continue;

            if (request.StateEventsMin.HasValue && room.StateEvents < request.StateEventsMin.Value) continue;
            if (request.StateEventsMax.HasValue && room.StateEvents > request.StateEventsMax.Value) continue;

            if (request.IsFederatable.HasValue && room.Federatable != request.IsFederatable.Value) continue;

            if (request.IsPublic.HasValue && room.Public != request.IsPublic.Value) continue;

            yield return room;
        }
    }

    private async Task<KeyValuePair<GenericRoom, FrozenSet<StateEventResponse>>> GetRoomState(GenericRoom room) {
        var roomState = await stateCacheService.GetRoomState(room.RoomId, room);
        return new(room, roomState);
    }

    private static ITransaction currentTransaction => Agent.Tracer.CurrentTransaction;
}