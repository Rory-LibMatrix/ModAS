using System.IO.Pipelines;
using Microsoft.AspNetCore.Mvc;
using ModAS.Server;

namespace WebApplication1.Controllers;

[ApiController]
public class TransactionsController(AppServiceRegistration asr) : ControllerBase {
    [HttpPut(" /_matrix/app/v1/transactions/{txnId}")]
    public async Task<IActionResult> PutTransactions(string txnId) {
        if(!Request.Headers.ContainsKey("Authorization") || Request.Headers["Authorization"] != asr.HomeserverToken) return Unauthorized();
        await Request.Body.CopyToAsync(Console.OpenStandardOutput());
        return Ok(new{});
    }
}