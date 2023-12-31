using System.Collections.Frozen;
using ArcaneLibs.Extensions;
using Elastic.Apm;
using Elastic.Apm.Api;
using LibMatrix.Homeservers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModAS.Server.Attributes;
using ModAS.Server.Services;
using MxApiExtensions.Services;

namespace ModAS.Server.Controllers.Debug;

/// <summary>
///   Provides debugging endpoints.
/// </summary>
/// <param name="config"><inheritdoc cref="ModASConfiguration"/></param>
/// <param name="authHsProvider"><inheritdoc cref="UserProviderService"/></param>
/// <param name="roomContextService"><inheritdoc cref="RoomContextService"/></param>
[ApiController]
[UserAuth(AnyRoles = AuthRoles.Developer | AuthRoles.Administrator)]
public class DebugController(ModASConfiguration config, UserProviderService authHsProvider, RoomContextService roomContextService) : ControllerBase {
    /// <summary>
    ///  Returns a JSON object containing the request and response headers.
    /// </summary>
    /// <returns>JSON object with request and partial response headers.</returns>
    [HttpGet("/_matrix/_modas/debug")]
    public IActionResult Index() {
        return Ok(new {
            Request = Request.Headers,
            Response = Response.Headers
        });
    }

    /// <summary>
    ///  Returns a JSON object containing the configuration.
    /// </summary>
    /// <returns></returns>
    [HttpGet("/_matrix/_modas/debug/config")]
    public IActionResult Config() {
        return Ok(config);
    }

    [HttpGet("/_matrix/_modas/debug/known_users")]
    public IActionResult KnownUsers() {
        return Ok(authHsProvider.KnownUsers.Keys);
    }

    [HttpGet("/_matrix/_modas/debug/test_locate_users")]
    public async IAsyncEnumerable<string> TestLocateUsers([FromQuery] string startUser) {
        List<AuthenticatedHomeserverGeneric> foundUsers = (await authHsProvider.GetValidUsers()).Select(x => x.Value).ToList();
        if (!foundUsers.Any(x => x.WhoAmI.UserId == startUser)) {
            foundUsers.Add(await authHsProvider.GetImpersonatedHomeserver(startUser));
        }

        List<string> processedRooms = [], processedUsers = [];
        var foundNew = true;
        while (foundNew) {
            var span1 = currentTransaction.StartSpan("iterateUsers", ApiConstants.TypeApp);
            foundNew = false;
            var usersToProcess = foundUsers.Where(x => !processedUsers.Any(y => x.WhoAmI.UserId == y)).ToFrozenSet();
            Console.WriteLine($"Got {usersToProcess.Count} users: {string.Join(", ", usersToProcess)}");

            var rooms = usersToProcess.Select(async x => await x.GetJoinedRooms());
            var roomLists = rooms.ToAsyncEnumerable();
            await foreach (var roomList in roomLists) {
                if (roomList is null) continue;
                foreach (var room in roomList) {
                    if (processedRooms.Contains(room.RoomId)) continue;
                    processedRooms.Add(room.RoomId);
                    var roomMembers = await room.GetMembersListAsync(false);
                    foreach (var roomMember in roomMembers) {
                        if (roomMember.StateKey.EndsWith(':' + config.ServerName) && !foundUsers.Any(x => x.WhoAmI.UserId == roomMember.StateKey)) {
                            foundUsers.Add(await authHsProvider.GetImpersonatedHomeserver(roomMember.StateKey));
                            foundNew = true;
                            yield return roomMember.StateKey;
                        }
                    }
                }
            }

            // await foreach (var task in tasks) {
            //     if (task is null) continue;
            //     foreach (var user in task) {
            //         if (foundUsers.Contains(user)) continue;
            //         foundUsers.Add(user);
            //         foundNew = true;
            //         yield return user;
            //     }
            // }

            span1.End();
        }
    }

    [HttpGet("/_matrix/_modas/debug/room_contexts")]
    public IActionResult RoomContexts() {
        return Ok(roomContextService.RoomContexts.Values);
    }

    [HttpGet("/_matrix/_modas/debug/room_contexts/{roomId}")]
    public async Task<IActionResult> RoomContext(string roomId) {
        var roomContext = await roomContextService.GetRoomContext(roomId);
        if (roomContext is null) return NotFound("Room not found");
        return Ok(roomContext);
    }

    [HttpGet("/_matrix/_modas/debug/room_contexts/by_user/{userId}")]
    public async IAsyncEnumerable<RoomContextService.RoomContext> RoomContextByUser(string userId) {
        var user = await authHsProvider.GetImpersonatedHomeserver(userId);
        var rooms = await user.GetJoinedRooms();
        var contexts = rooms.Select(x => roomContextService.GetRoomContext(x.RoomId)).ToAsyncEnumerable();
        await foreach (var context in contexts) {
            if (context is null) continue;
            yield return context;
        }
    }

    private static ITransaction currentTransaction => Agent.Tracer.CurrentTransaction;
}