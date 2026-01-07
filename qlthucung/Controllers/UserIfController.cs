using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using qlthucung.Models;
using qlthucung.Security;
using System.Linq;

namespace qlthucung.Controllers
{
    public class UserIfController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly UserManager<AppIdentityUser> _userManager;

        public UserIfController(AppDbContext context, IEmailSender emailSender, UserManager<AppIdentityUser> userManager)
        {
            _context = context;
            _emailSender = emailSender;
            _userManager = userManager;
        }
        [HttpGet]
        public IActionResult UserInfo()
        {
            string username = HttpContext.Session.GetString("username");
            if (string.IsNullOrEmpty(username))
            {
                return RedirectToAction("SignIn", "Security");
            }
            var user = _context.AspNetUsers
                        .Where(u => u.UserName == username)
                        .Select(u => new UserInfoViewModel
                        {
                            FullName = u.FullName,
                            UserName = u.UserName,
                            BirthDate = u.BirthDate,
                            Email = u.Email,
                            EmailConfirmed = u.EmailConfirmed,
                            PhoneNumber = u.PhoneNumber,
                            PhoneNumberConfirmed = u.PhoneNumberConfirmed
                        })
                        .FirstOrDefault();

            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }
    }
}
