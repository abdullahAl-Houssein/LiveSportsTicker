using Microsoft.AspNetCore.Mvc;

namespace LiveSportsTicker.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [Route("/Home/Error")]
    public IActionResult Error()
    {
        return View();
    }
}
