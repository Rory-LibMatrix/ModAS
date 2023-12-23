using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WebApplication1.Controllers;

[ApiController]
public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    [HttpGet("/_matrix/_modas")]
    public IActionResult Index() {
        //return wwwroot/index.html
        return LocalRedirect("/index.html");
    }
}