using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.AspNetCore.Mvc;
using ModAS.Server.Controllers.AppService;
using MxApiExtensions.Services;

namespace ModAS.Server.Controllers;

/// <summary>
///    Manages the visual homepage.
/// </summary>
[ApiController]
public class HomeController(AppServiceRegistration asr, ModASConfiguration config) : Controller {
    private const string ValidChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <inheritdoc cref="HomeController"/>
    [HttpGet("/_matrix/_modas")]
    [ApiExplorerSettings(IgnoreApi = true)] //hide from swagger
    public IActionResult Index() {
        return LocalRedirect("/index.html");
    }

    [HttpGet("/_matrix/_modas/version")]
    public IActionResult Version() {
        return Ok(new {
            Version = Modas.Server.Version.VersionString
        });
    }

    [HttpGet("/_matrix/_modas/ping")]
    public async Task<IActionResult> Ping() {
        var txn = new PingController.TransactionIdContainer() {
            TransactionId = RandomNumberGenerator.GetString(ValidChars, 32)
        };
        var url = $"{config.HomeserverUrl}/_matrix/client/v1/appservice/{HttpUtility.UrlEncode(asr.Id)}/ping";
        var hrm = new HttpRequestMessage(HttpMethod.Post, url) {
            Content = new StringContent(JsonSerializer.Serialize(txn), Encoding.UTF8, "application/json"),
            Headers = {
                Authorization = new AuthenticationHeaderValue("Bearer", asr.AppServiceToken)
            }
        };
        var req = await new HttpClient().SendAsync(hrm);
        var resp = await req.Content.ReadFromJsonAsync<JsonObject>();
        resp!["tnxId"] = txn.TransactionId;
        return Ok(resp);
    }
}