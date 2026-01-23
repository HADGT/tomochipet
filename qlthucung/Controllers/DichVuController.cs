using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using qlthucung.Models;
using qlthucung.Models.mail;
using qlthucung.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace qlthucung.Controllers
{
    public class DichVuController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly UserManager<AppIdentityUser> _userManager;

        public DichVuController(AppDbContext context, IEmailSender emailSender, UserManager<AppIdentityUser> userManager)
        {
            _context = context;
            _emailSender = emailSender;
            _userManager = userManager;
        }

        [Authorize(Roles = "User")]
        public IActionResult Index()
        {
            string khachHangName = HttpContext.Session.GetString("username");

            if (string.IsNullOrEmpty(khachHangName))
            {
                return RedirectToAction("SignIn", "Security");
            }
            ViewBag.KhachHangName = khachHangName;
            return View();
        }

        [Authorize(Roles = "User")]
        public IActionResult Datlich()
        {
            ViewBag.SPDV = _context.SPDichVu.ToList();
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> DatLich([Bind("Hoten,Email,Sdt,Diachi,Trangthai,Tendichvu,Ngaydat,Makh")] DichVu model)
        {
            ViewBag.SPDV = _context.SPDichVu.ToList();
            string khachHangName = HttpContext.Session.GetString("username");
            if (string.IsNullOrEmpty(khachHangName))
            {
                return RedirectToAction("SignIn", "Security");
            }

            var user = _context.AspNetUsers.FirstOrDefault(p => p.UserName == khachHangName);

            model.Makh = user.Id.ToString();
            model.Trangthai = "Đang chờ xử lý";

            DateTime start = model.Ngaydat;
            DateTime end = start.AddHours(1);

            try
            {
                // Dùng transaction với IsolationLevel.Serializable để tránh race condition
                using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

                // Kiểm tra trùng dịch vụ cùng ngày
                bool trungDichVuCungNgay = _context.DichVus.Any(d =>
                    d.Makh == model.Makh &&
                    d.Tendichvu == model.Tendichvu &&
                    d.Ngaydat.Date == model.Ngaydat.Date);

                if (trungDichVuCungNgay)
                {
                    ModelState.AddModelError("Ngaydat", "Bạn đã đặt dịch vụ này trong ngày này rồi. Vui lòng chọn thời gian khác.");
                    return View(model);
                }

                // Kiểm tra trùng khung giờ
                bool trungKhungGio = _context.DichVus.Any(d =>
                    d.Makh == model.Makh &&
                    d.Ngaydat < end &&
                    d.Ngaydat.AddHours(1) > start);

                if (trungKhungGio)
                {
                    ModelState.AddModelError("Ngaydat", "Bạn đã có lịch khác trong khung giờ này. Vui lòng chọn thời gian khác.");
                    return View(model);
                }

                // Kiểm tra trạng thái chờ xử lý / đã xác nhận
                bool trungTrangThai = _context.DichVus.Any(d =>
                    d.Makh == model.Makh &&
                    d.Tendichvu == model.Tendichvu &&
                    (d.Trangthai == "Đang chờ xử lý" || d.Trangthai == "Đã xác nhận") &&
                    d.Ngaydat.Date == model.Ngaydat.Date);

                if (trungTrangThai)
                {
                    ModelState.AddModelError("Ngaydat", "Bạn đang có dịch vụ này đang chờ hoặc đã xác nhận trong ngày hôm đó.");
                    return View(model);
                }

                // Lưu vào DB
                _context.DichVus.Add(model);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                // Nếu ràng buộc Unique ở DB bị vi phạm => trùng lịch
                ModelState.AddModelError("Ngaydat", "Khung giờ này đã có người khác đặt. Vui lòng chọn thời gian khác.");
                return View(model);
            }

            // Gửi email
            var users = _context.AspNetUsers.FirstOrDefault(p => p.UserName == khachHangName);
            if (user != null && !string.IsNullOrEmpty(model.Email))
            {
                string subject = "Thông báo đặt lịch dịch vụ thành công";
                string body = $@"
            <p>Xin chào {user.UserName},</p>
            <p>Bạn đã đặt lịch dịch vụ <strong>{model.Tendichvu}</strong> thành công.</p>
            <p>Ngày đặt: {model.Ngaydat.ToString("dd/MM/yyyy HH:mm")}</p>
            <p>Trạng thái: {model.Trangthai}</p>
            <p>Cảm ơn bạn đã sử dụng dịch vụ của chúng tôi!</p>";

                await _emailSender.SendEmailAsync(model.Email, subject, body);
            }

            TempData["Success"] = "Đặt lịch thành công! Vui lòng kiểm tra email để xác nhận.";
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        public async Task<IActionResult> SendUserInfoEmail()
        {
            string username = HttpContext.Session.GetString("username");
            if (string.IsNullOrEmpty(username))
                return RedirectToAction("SignIn", "Security");

            var user = _context.AspNetUsers.FirstOrDefault(p => p.UserName == username);
            if (user == null || string.IsNullOrEmpty(user.Email))
                return BadRequest("Không tìm thấy người dùng hoặc email.");

            // Tạo token xác nhận email
            var identityUser = await _userManager.FindByNameAsync(username);
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(identityUser);

            // Tạo link xác nhận
            var confirmationLink = Url.Action("ConfirmEmail", "DichVu", new
            {
                userId = identityUser.Id,
                token = token
            }, protocol: HttpContext.Request.Scheme);

            // Gửi email
            var email = new MailMess
            {
                To = user.Email,
                Subject = "Xác nhận email",
                Body = $"Xin chào {user.UserName},<br/>Vui lòng xác nhận email bằng cách <a href='{confirmationLink}'>bấm vào đây</a>."
            };

            await _emailSender.SendEmailAsync(email.To, email.Subject, email.Body);
            return Ok("Email xác nhận đã được gửi.");
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string userId, string token)
        {
            if (userId == null || token == null)
                return BadRequest("Yêu cầu không hợp lệ.");

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound("Không tìm thấy người dùng.");

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (result.Succeeded)
            {
                return View("ConfirmEmailSuccess"); // Tạo view này
            }

            return View("Error"); // Tạo view lỗi nếu xác thực thất bại
        }
    }
}
