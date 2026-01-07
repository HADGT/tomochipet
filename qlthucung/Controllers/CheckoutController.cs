using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using qlthucung.Helpers;
using qlthucung.Models;
using qlthucung.Models.vnpay;
using qlthucung.Services.vnpay;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace qlthucung.Controllers
{
    public class CheckoutController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IVnPayService _vnPayService;
        private ILogger<VnPayService> _logger;
        public CheckoutController(AppDbContext context, IVnPayService vnPayService, ILogger<VnPayService> logger)
        {
            _context = context;
            _vnPayService = vnPayService;
            _logger = logger;
        }

        [Authorize(Roles = "User")]
        public IActionResult Payment()
        {
            string orderJson = HttpContext.Session.GetString("Ordermodel");
            if (!string.IsNullOrEmpty(orderJson))
            {
                var model = JsonConvert.DeserializeObject<CheckoutModel>(orderJson);
                var cart = SessionHelper.GetObjectFromJson<List<Item>>(HttpContext.Session, "cart");
                if (cart != null)
                {
                    ViewBag.cart = cart;
                    ViewBag.total = cart.Sum(item => item.Product.Giakhuyenmai * item.Quantity);
                    if (ViewBag.total == null)
                    {
                        ViewBag.total = 0;
                    }

                }
                ViewBag.fullName = model.FullName;
                ViewBag.email = model.Email;
                ViewBag.phoneNumber = model.PhoneNumber;
                ViewBag.birthDate = model.BirthDate;
                ViewBag.username = model.Username;
                ViewBag.tinh = model.Tinh;
                ViewBag.xa = model.Xa;
                ViewBag.diachi = model.SoNha;
                return View();
            }
            return RedirectToAction("ErrorPage");
        }
        [HttpPost]
        [Route("OnlineCheckout/CreatePaymentUrlVnpay")]
        public IActionResult CreatePaymentUrlVnpay(PaymentInformationModel model, Decimal amount)
        {
            string ngayDatStr = HttpContext.Session.GetString("NgayDat");
            string username = HttpContext.Session.GetString("username");
            if (username == null)
            {
                return RedirectToAction("SignIn", "Security"); // Chuyển hướng đến trang đăng nhập
            }
            var user = _context.KhachHangs
                   .Where(p => p.Tendangnhap == username)
                   .Select(p => p.Hoten) // Chỉ lấy FullName
                   .FirstOrDefault(); // Trả về một giá trị thay vì danh sách
            if (user != null)
            {
                HttpContext.Session.SetString("FullName", user);
            }
            if (amount > 0)
            {
                HttpContext.Session.SetString("Amount", amount.ToString());
            }
            string fullname = HttpContext.Session.GetString("FullName");
            int? madon = HttpContext.Session.GetInt32("lastOrderId");
            // Tạo thông tin đơn hàng
            model.Madon = madon.ToString();
            model.OrderDescription = $"Thanh toan VnPay cho don hang tai Shoppet";
            model.Amount = amount;
            model.OrderType = "other";
            model.Name = username;
            model.FullName = fullname;
            _logger.LogInformation("model:" + model);
            var url = _vnPayService.CreatePaymentUrl(model, HttpContext);
            // Kiểm tra nếu PayUrl là null hoặc rỗng
            if (string.IsNullOrEmpty(url))
            {
                return BadRequest("Không thể tạo URL thanh toán. Vui lòng thử lại.");
            }

            // Nếu PayUrl hợp lệ, thực hiện chuyển hướng
            return Redirect(url);
        }

        [HttpGet]
        [Route("OnlineCheckout/PaymentCallbackVnpay")]
        public IActionResult PaymentCallbackVnpay()
        {
            var response = _vnPayService.PaymentExecute(Request.Query);
            _logger.LogInformation("PaymentCallBack response: " + JsonConvert.SerializeObject(response));

            // 🔹 Giải mã URL để lấy OrderDescription
            var decodedDescription = Uri.UnescapeDataString(response.OrderDescription);
            var madonMatch = Regex.Match(decodedDescription, @"Ma don:(\d+)");

            if (!madonMatch.Success)
            {
                _logger.LogError("Không tìm thấy mã đơn trong OrderDescription: " + response.OrderDescription);
                return View("PaymentResult", new { success = false, message = "Không tìm thấy mã đơn." });
            }

            int madon = int.Parse(madonMatch.Groups[1].Value);
            var donHang = _context.DonHangs.FirstOrDefault(o => o.Madon == madon);

            if (donHang == null)
            {
                _logger.LogError("Không tìm thấy đơn hàng: " + madon);
                return View("PaymentResult", new { success = false, message = "Không tìm thấy đơn hàng." });
            }

            // 🔹 Kiểm tra trạng thái thanh toán
            bool isSuccess = response.VnPayResponseCode == "00";
            var transactionStatus = isSuccess ? "Thanh toán thành công" : "Thanh toán thất bại";
            var chiTietDonHanglst = _context.ChiTietDonHangs
                    .Where(ct => ct.Madon == madon)
                    .ToList();

            decimal totalAmount = chiTietDonHanglst.Sum(ct => (ct.Gia ?? 0));
            // 🔹 Lưu giao dịch vào bảng MoMoPayments
            var momoPayment = new MoMoPayment
            {
                PaymentId = $"{response.TransactionId}-{response.OrderId}-{response.PaymentId}",
                Tinnhantrave = response.OrderDescription,
                Madon = madon,
                Magiaodich = response.PaymentMethod + madon,
                Trangthaithanhtoan = isSuccess ? "Đã thanh toán" : "Chưa thanh toán",
                Amount = totalAmount
            };

            _context.MoMoPayments.Add(momoPayment);
            _context.SaveChanges();
            ViewBag.madon = madon;
            ViewBag.Trangthai = response.Success ? "Thanh toán thành công" : "Thanh toán thất bại";

            // 🔹 Nếu thất bại, cập nhật đơn hàng thành "Đã hủy" và hoàn lại kho
            if (!isSuccess)
            {
                var chiTietDonHangs = _context.ChiTietDonHangs.Where(ct => ct.Madon == madon).ToList();

                foreach (var item in chiTietDonHangs)
                {
                    var sanPham = _context.SanPhams.FirstOrDefault(sp => sp.Masp == item.Masp);
                    if (sanPham != null)
                    {
                        sanPham.Soluongton += item.Soluong; // Hoàn lại số lượng kho
                    }
                }

                donHang.Giaohang = "Đã hủy";
                _context.DonHangs.Update(donHang);
                _context.SaveChanges();
                HttpContext.Session.Remove("cart");
                ViewBag.Trangthai = "Thanh toán thất bại";
            }
            else
            {
                HttpContext.Session.Remove("cart");
                ViewBag.Trangthai = "Thanh toán thành công";
            }

            return View(momoPayment);
        }

    }
}
