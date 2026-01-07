using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using qlthucung.Models;
using qlthucung.Security;
using qlthucung.Services.email;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace qlthucung.Controllers
{
    public class RoleAdminController : Controller
    {
        private readonly UserManager<AppIdentityUser> _userManager;
        private readonly RoleManager<AppIdentityRole> _roleManager;
        private readonly IEmailSender _emailSender;
        public RoleAdminController(UserManager<AppIdentityUser> userManager, IEmailSender emailSender, RoleManager<AppIdentityRole> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _emailSender = emailSender;
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index(int? pageNumber, string searchTerm)
        {
            int pageSize = 5;
            var users = await _userManager.Users.ToListAsync();
            var userList = new List<NguoiDungViewModel>();
            if (!string.IsNullOrEmpty(searchTerm))
            {
                // Lọc người dùng theo FullName hoặc UserName chứa từ khóa (không phân biệt hoa thường)
                users = users
                    .Where(u => u.FullName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                                u.UserName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userList.Add(new NguoiDungViewModel
                {
                    FullName = user.FullName,
                    UserName = user.UserName,
                    BirthDate = user.BirthDate,
                    Email = user.Email,
                    EmailConfirmed = user.EmailConfirmed,
                    LockoutEnd = user.LockoutEnd,
                    LockoutEnabled = user.LockoutEnabled,
                    AccessFailedCount = user.AccessFailedCount,
                    Role = string.Join(", ", roles)
                });
            }
            // Truyền lại searchTerm để giữ giá trị tìm kiếm khi phân trang
            ViewBag.SearchTerm = searchTerm;
            var sortedList = userList.OrderBy(u => u.UserName).ToList(); // Không dùng AsQueryable
            var paginatedUsers = PaginatedList<NguoiDungViewModel>.Create(sortedList, pageNumber ?? 1, pageSize);
            return View(paginatedUsers);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateRole(string userName, string newRole, int pageNumber)
        {
            var user = await _userManager.FindByNameAsync(userName);
            if (user != null)
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, newRole);
            }
            return RedirectToAction("Index", new { pageNumber });
        }

        [HttpPost]
        public async Task<IActionResult> LockUser(string userName, int pageNumber)
        {
            var user = await _userManager.FindByNameAsync(userName);
            if (user != null)
            {
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            }
            return RedirectToAction("Index", new { pageNumber });
        }

        [HttpPost]
        public async Task<IActionResult> UnlockUser(string userName, int pageNumber)
        {
            var user = await _userManager.FindByNameAsync(userName);
            if (user != null)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                await _userManager.ResetAccessFailedCountAsync(user);
            }
            return RedirectToAction("Index", new { pageNumber });
        }

        [HttpGet]
        public IActionResult ChangePassword(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                TempData["Error"] = "Link không hợp lệ";
                return RedirectToAction("Login", "Account");
            }

            var model = new ChangePasswordViewModel
            {
                Email = email,
                Token = token
            };

            return View(model);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Tìm user theo email (đã lấy từ link reset)
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                TempData["Error"] = "Tài khoản không tồn tại.";
                return RedirectToAction("SignIn", "Security");
            }

            // Dùng token nhận từ email (model.Token) để reset mật khẩu
            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);

            if (result.Succeeded)
            {
                // Nếu có custom flag như ForceChangePassword thì cập nhật
                user.ForceChangePassword = false;
                await _userManager.UpdateAsync(user);

                TempData["Success"] = "Đổi mật khẩu thành công. Hãy đăng nhập lại.";
                return RedirectToAction("SignIn", "Security");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error.Description);
            }

            return View(model);
        }


        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        public IActionResult ForgotPasswordSuccess()
        {
            return View();
        }

        [HttpGet]
        public IActionResult ForgotPasswordConfirmation()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.FindByNameAsync(model.UserName);
            if (user == null || user.Email != model.Email)
            {
                // Không tiết lộ rõ lý do để tránh lộ thông tin
                TempData["Error"] = "Thông tin không chính xác.";
                return View(model);
            }

            // Sinh token reset mật khẩu
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            var callbackUrl = Url.Action(
                "ChangePassword",
                "RoleAdmin",
                new { email = user.Email, token = token },
                protocol: HttpContext.Request.Scheme);

            // Nội dung email
            string subject = "Đặt lại mật khẩu";
            string body = $@"
        <p>Xin chào {user.UserName},</p>
        <p>Bạn vừa yêu cầu đặt lại mật khẩu cho tài khoản.</p>
        <p>Vui lòng click vào link dưới đây để đặt lại mật khẩu:</p>
        <p><a href='{callbackUrl}'>Đặt lại mật khẩu</a></p>
        <p>Nếu bạn không yêu cầu, vui lòng bỏ qua email này.</p>
        <p>Trân trọng,<br/>Đội ngũ hỗ trợ</p>
    ";

            await _emailSender.SendEmailAsync(user.Email, subject, body);

            return RedirectToAction("ForgotPasswordConfirmation");
        }

        private string GenerateRandomPassword(int length = 15)
        {
            const string lowercase = "abcdefghijklmnopqrstuvwxyz";
            const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string digits = "1234567890";
            const string special = "!@#$%&*";
            const string valid = lowercase + uppercase + digits + special;

            var res = new StringBuilder();
            var rnd = new Random();

            // Đảm bảo ít nhất 1 ký tự mỗi loại
            res.Append(lowercase[rnd.Next(lowercase.Length)]);
            res.Append(uppercase[rnd.Next(uppercase.Length)]);
            res.Append(digits[rnd.Next(digits.Length)]);
            res.Append(special[rnd.Next(special.Length)]);

            // Thêm các ký tự còn lại từ valid
            for (int i = 4; i < length; i++)
            {
                res.Append(valid[rnd.Next(valid.Length)]);
            }

            // Trộn ngẫu nhiên các ký tự
            var passwordArray = res.ToString().ToCharArray();
            var shuffledPassword = new string(passwordArray.OrderBy(x => rnd.Next()).ToArray());

            return shuffledPassword;
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public IActionResult CreateUser()
        {
            // Trả về form tạo tài khoản
            return View(new CreateUserViewModel());
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> CreateUser(CreateUserViewModel model)
        {
            // Kiểm tra email hoặc username đã tồn tại
            var existingUser = await _userManager.FindByNameAsync(model.UserName);
            if (existingUser != null)
            {
                ModelState.AddModelError("", "Tên đăng nhập đã tồn tại");
                return View(model);
            }

            existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
            {
                ModelState.AddModelError("", "Email đã được sử dụng");
                return View(model);
            }

            // Sinh mật khẩu ngẫu nhiên
            string randomPassword = GenerateRandomPassword(10);

            var newUser = new AppIdentityUser
            {
                UserName = model.UserName,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                FullName = model.FullName,
                BirthDate = model.BirthDate,
                EmailConfirmed = false,
                PhoneNumberConfirmed = false,
                LockoutEnabled = true,
                ForceChangePassword = false
            };

            // Tạo user trong hệ thống
            var result = await _userManager.CreateAsync(newUser, randomPassword);

            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(newUser, "Staff");

                string subject = "Tài khoản mới của bạn tại Shoppet";
                string body = $@"
            <p>Xin chào {newUser.FullName},</p>
            <p>Tài khoản của bạn đã được tạo thành công:</p>
            <p><b>Tên đăng nhập:</b> {newUser.UserName}</p>
            <p><b>Mật khẩu:</b> {randomPassword}</p>
            <p>Vui lòng đổi mật khẩu sau lần đăng nhập đầu tiên.</p>
            <p>Trân trọng,<br/>Ban quản trị hệ thống</p>";

                await _emailSender.SendEmailAsync(newUser.Email, subject, body);

                TempData["Success"] = "Tạo tài khoản thành công và đã gửi email thông báo.";
                return RedirectToAction("Index");
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);

            return RedirectToAction("Index");
        }
    }
}


