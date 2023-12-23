using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.Homeservers;
using Microsoft.AspNetCore.Mvc;
using ModAS.Server.Services;
using MxApiExtensions.Services;

namespace WebApplication1.Controllers;

[ApiController]
public class DebugController(ModASConfiguration config, AuthenticatedHomeserverProviderService authHsProvider) : ControllerBase {
    [HttpGet("/_matrix/_modas/debug")]
    public IActionResult Index() {
        return Ok(new {
            Request = Request.Headers,
            Response = Response.Headers
        });
    }

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
        List<string> foundUsers = [startUser], processedRooms = new List<string>();
        var foundNew = true;
        while (foundNew) {
            foundNew = false;
            foreach (var user in foundUsers.ToList()) {
                AuthenticatedHomeserverGeneric? ahs = null;
                try {
                    ahs = await authHsProvider.GetImpersonatedHomeserver(user);
                    await ahs.GetJoinedRooms();
                }
                catch (MatrixException e) {
                    if(e is {ErrorCode: "M_FORBIDDEN"}) continue;
                    throw;
                }

                if(ahs is null) continue;
                var rooms = await ahs.GetJoinedRooms();
                Console.WriteLine($"Got {rooms.Count} rooms");
                rooms.RemoveAll(r => processedRooms.Contains(r.RoomId));
                processedRooms.AddRange(rooms.Select(r => r.RoomId));
                foundNew = rooms.Count > 0;
                Console.WriteLine($"Found {rooms.Count} new rooms");
            
                var roomMemberTasks = rooms.Select(r => r.GetMembersListAsync(false)).ToAsyncEnumerable();
                await foreach (var roomMembers in roomMemberTasks) {
                    Console.WriteLine($"Got {roomMembers.Count} members");
                    foreach (var member in roomMembers) {
                        if (!member.StateKey.EndsWith(':' + config.ServerName)) continue;
                        if (foundUsers.Contains(member.StateKey)) continue;
                        foundUsers.Add(member.StateKey);
                        yield return member.StateKey;
                    }
                }
            }
        }
    }
}