using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ArcaneLibs;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration.Json;
using ModAS.Server;
using ModAS.Server.Services;
using MxApiExtensions.Services;

namespace ModAS.Server.Controllers.AppService;

[ApiController]
public class TransactionsController(
    AppServiceRegistration asr,
    ModASConfiguration config,
    UserProviderService userProvider,
    RoomContextService roomContextService,
    RoomStateCacheService stateCacheService) : ControllerBase {
    private static List<string> _ignoredInvalidationEvents { get; set; } = [
        RoomMessageEventContent.EventId,
        RoomMessageReactionEventContent.EventId
    ];

    [HttpPut("/_matrix/app/v1/transactions/{txnId}")]
    public async Task<IActionResult> PutTransactions(string txnId) {
        if (!Request.Headers.ContainsKey("Authorization")) {
            Console.WriteLine("PutTransaction: missing authorization header");
            return Unauthorized();
        }

        if (Request.GetTypedHeaders().Get<AuthenticationHeaderValue>("Authorization")?.Parameter != asr.HomeserverToken) {
            Console.WriteLine($"PutTransaction: invalid authorization header: {Request.Headers["Authorization"]}");
            return Unauthorized();
        }

        var data = await JsonSerializer.DeserializeAsync<EventList>(Request.Body);
        Console.WriteLine(
            $"PutTransaction: {txnId}: {data.Events.Count} events, {Util.BytesToString(Request.Headers.ContentLength ?? Request.ContentLength ?? Request.Body.Length)}");

        if (!Directory.Exists("data"))
            Directory.CreateDirectory("data");
        Directory.CreateDirectory($"data/{txnId}");
        // var pipe = PipeReader.Create(Request.Body);
        // await using var file = System.IO.File.OpenWrite($"data/{txnId}");
        // await pipe.CopyToAsync(file);
        // await pipe.CompleteAsync();
        //
        // Console.WriteLine($"PutTransaction: {txnId}: {Util.BytesToString(file.Length)}");
        for (var i = 0; i < data.Events.Count; i++) {
            var evt = data.Events[i];
            Console.WriteLine($"PutTransaction: {txnId}/{i}: {evt.Type} {evt.StateKey} {evt.Sender}");
            await System.IO.File.WriteAllTextAsync($"data/{txnId}/{i}-{evt.Type}-{evt.StateKey}-{evt.Sender}.json", JsonSerializer.Serialize(evt));

            if (evt.Sender.EndsWith(':' + config.ServerName)) {
                Console.WriteLine("PutTransaction: sender is local user, updating data...");
                try {
                    var user = await userProvider.GetImpersonatedHomeserver(evt.Sender);
                    var rooms = await user.GetJoinedRooms();
                    foreach (var room in rooms) {
                        await roomContextService.GetRoomContext(room);
                    }
                }
                catch (Exception e) {
                    Console.WriteLine($"PutTransaction: failed to update data: {e}");
                }
            }
            else
                Console.WriteLine("PutTransaction: sender is remote user");

            if (!string.IsNullOrWhiteSpace(evt.RoomId) && !_ignoredInvalidationEvents.Contains(evt.Type))
                await stateCacheService.InvalidateRoomState(evt.RoomId);
        }

        return Ok(new { });
    }
}