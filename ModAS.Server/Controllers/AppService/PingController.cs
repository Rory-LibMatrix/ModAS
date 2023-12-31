using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Validations.Rules;
using ModAS.Server.Attributes;

namespace ModAS.Server.Controllers.AppService;

[ApiController]
[ApiExplorerSettings(IgnoreApi = true)] //hide from swagger
public class PingController : ControllerBase {
    [HttpPost("/_matrix/app/v1/ping")]
    [UserAuth(AuthType = AuthType.Server)]
    public IActionResult PutTransactions([FromBody] TransactionIdContainer data) {
        return Ok(new { });
    }

    public class TransactionIdContainer {
        [JsonPropertyName("transaction_id")]
        public string TransactionId { get; set; }
    }
}