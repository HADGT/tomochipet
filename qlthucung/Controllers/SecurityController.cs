using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using qlthucung.Models;
using qlthucung.Security;
using Microsoft.AspNetCore.Http;
using System.Net.Http;

namespace qlthucung.Controllers
{
    public class SecurityController : Controller
    {
        private readonly UserManager<AppIdentityUser> userManager;
        private readonly RoleManager<AppIdentityRole> roleManager;
        private readonly SignInManager<AppIdentityUser> signInManager;

        public SecurityController(UserManager<AppIdentityUser> userManager,
            RoleManager<AppIdentityRole> roleManager,
            SignInManager<AppIdentityUser> signInManager)
        {
            this.userManager = userManager;
            this.roleManager = roleManager;
            this.signInManager = signInManager;
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(Register register)
        {
            if (ModelState.IsValid)
            {
                // Kiểm tra xem username đã tồn tại chưa
                var existingUser = userManager.FindByNameAsync(register.UserName).Result;
                if (existingUser != null)
                {
                    ModelState.AddModelError("", "Tên đăng nhập đã tồn tại. Vui lòng chọn tên khác.");
                    return View(register); // Trả về ngay nếu username đã tồn tại
                }
                // Tạo role User nếu chưa có
                if (!roleManager.RoleExistsAsync("User").Result)
                {
                    var role = new AppIdentityRole
                    {
                        Name = "User",
                        Description = "Standard user with limited access"
                    };
                    var roleResult = roleManager.CreateAsync(role).Result;
                }

                // Tạo user mới
                var user = new AppIdentityUser
                {
                    UserName = register.UserName,
                    Email = register.Email,
                    FullName = register.FullName,
                    BirthDate = register.BirthDate
                };

                var result = userManager.CreateAsync(user, register.Password).Result;
                if (result.Succeeded)
                {
                    // Gán quyền User mặc định
                    if (!roleManager.RoleExistsAsync("User").Result)
                    {
                        var role = new AppIdentityRole
                        {
                            Name = "User",
                            Description = "Standard user"
                        };
                        var roleResult = roleManager.CreateAsync(role).Result;
                    }

                    userManager.AddToRoleAsync(user, "User").Wait();
                    return RedirectToAction("SignIn", "Security");
                }
                else
                {
                    // Thêm từng lỗi vào ModelState để hiển thị
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                    return View(register);
                }
            }
            return NotFound();
        }


        [HttpGet]
        public IActionResult SignIn()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SignInAsync(SignIn signIn)
        {
            if (ModelState.IsValid)
            {
                // Tìm user trước để kiểm tra trạng thái khóa
                var user = await userManager.FindByNameAsync(signIn.UserName);
                if (user != null)
                {
                    // Kiểm tra xem có phải Admin không
                    var isAdmin = await userManager.IsInRoleAsync(user, "Admin");
                    var isStaff = await userManager.IsInRoleAsync(user, "Staff");
                    // Nếu bị khóa rồi (có thể là vĩnh viễn), không cho đăng nhập nữa
                    if (await userManager.IsLockedOutAsync(user))
                    {
                        TempData["LoginErr"] = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ quản trị viên để được mở khóa.";
                        return View(signIn);
                    }
                    // Chỉ chuyển hướng nếu KHÔNG phải admin và cần đổi mật khẩu
                    if (user.ForceChangePassword && !isAdmin)
                    {
                        // Lưu userName tạm vào session để xử lý đổi mật khẩu
                        HttpContext.Session.SetString("usernameToChangePwd", signIn.UserName);
                        return RedirectToAction("ChangePassword", "RoleAdmin");
                    }
                    // Tiến hành đăng nhập, bật lockoutOnFailure để tự tăng số lần sai
                    var result = await signInManager.PasswordSignInAsync(
                        signIn.UserName,
                        signIn.Password,
                        signIn.RememberMe,
                        lockoutOnFailure: !isAdmin // Nếu là admin thì không tăng số lần sai
                    );

                    if (result.Succeeded)
                    {
                        HttpContext.Session.SetString("username", signIn.UserName);
                        HttpContext.Session.SetString("userId", user.Id);
                        // Nếu là Admin thì chuyển sang giao diện Admin
                        // Phân hướng theo vai trò
                        if (isAdmin)
                        {
                            return RedirectToAction("Index", "Admin");
                        }
                        else if (isStaff)
                        {
                            return RedirectToAction("Index", "Staff");
                        }
                        else
                        {
                            return RedirectToAction("Index", "Home");
                        }
                    }
                    else if (result.IsLockedOut && !isAdmin)
                    {
                        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                        TempData["LoginErr"] = "Tài khoản của bạn đã bị khóa vĩnh viễn do đăng nhập sai quá nhiều lần.";
                    }
                    else
                    {
                        TempData["LoginErr"] = "Tên tài khoản hoặc mật khẩu không chính xác!";
                    }
                }
                else
                {
                    TempData["LoginErr"] = "Tài khoản không tồn tại!";
                }
            }

            return View(signIn);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear(); // Xóa toàn bộ session
            Response.Cookies.Delete(".AspNetCore.Identity.Application"); // Nếu bạn dùng Identity

            return RedirectToAction("Index", "Home");
        }

        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
