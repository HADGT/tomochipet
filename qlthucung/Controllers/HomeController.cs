using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using qlthucung.Models;
using qlthucung.Security;
using System.Linq;

namespace qlthucung.Controllers
{
    [AllowAnonymous]
    public class HomeController : Controller
    {
        private readonly UserManager<AppIdentityUser> userManager;
        private readonly AppDbContext _context;

        public HomeController(UserManager<AppIdentityUser> userManager, AppDbContext context)
        {
            this.userManager = userManager;
            _context = context;
        }

        [Authorize(Roles = "User")]
        public IActionResult Index()
        {
            return View();
        }
    }
}
