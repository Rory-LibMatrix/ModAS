using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ModAS.Server.Controllers;

/// <summary>
///    Manages the visual homepage.
/// </summary>
[ApiController]
public class HomeController : Controller {
    /// <inheritdoc cref="HomeController"/>
    [HttpGet("/_matrix/_modas")]
    public IActionResult Index() {
        return LocalRedirect("/index.html");
    }
}