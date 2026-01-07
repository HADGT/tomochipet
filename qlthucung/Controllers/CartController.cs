using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using qlthucung.Helpers;
using qlthucung.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace qlthucung.Controllers
{
    public class CartController : Controller
    {

        private readonly AppDbContext _context;
        private ILogger<string> _logger;

        public CartController(AppDbContext context, ILogger<string> logger)
        {
            _context = context;
            _logger = logger;
        }

        [Authorize(Roles = "User")]
        [HttpGet]
        public IActionResult Index()
        {
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

            string username = HttpContext.Session.GetString("username");
            if (username == null)
            {
                return RedirectToAction("SignIn", "Security"); // Chuyển hướng đến trang đăng nhập
            }
            var user = _context.AspNetUsers
                   .Where(p => p.UserName == username)
                   .FirstOrDefault(); // Lấy 1 user thay vì List

            if (user != null)
            {
                ViewBag.fullName = user.FullName;
                ViewBag.email = user.Email;
                ViewBag.phoneNumber = user.PhoneNumber;
                ViewBag.birthDate = user.BirthDate;
                ViewBag.username = user.UserName;
            }

            return View();
        }

        // POST: Cập nhật số lượng từng sản phẩm
        [HttpPost]
        public JsonResult UpdateQuantity(int id, int quantity)
        {
            var cart = SessionHelper.GetObjectFromJson<List<Item>>(HttpContext.Session, "cart");
            if (cart == null)
            {
                return Json(new { success = false, message = "Giỏ hàng trống" });
            }

            var item = cart.FirstOrDefault(i => i.Product.Masp == id);
            if (item != null)
            {
                item.Quantity = quantity;
                // Cập nhật session
                SessionHelper.SetObjectAsJson(HttpContext.Session, "cart", cart);

                // Tính lại tổng tiền
                var total = cart.Sum(i => i.Product.Giakhuyenmai * i.Quantity);
                var itemTotal = item.Product.Giakhuyenmai * item.Quantity;

                return Json(new { success = true, total = total, itemTotal = itemTotal });
            }

            return Json(new { success = false, message = "Không tìm thấy sản phẩm trong giỏ hàng" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Index([Bind("Hoten,Tendangnhap,Matkhau,Email,Diachi,Dienthoai,Ngaysinh,RoleId,Status,Resetpasswordcode")] KhachHang kh, DonHang dh, IFormCollection form, CheckoutModel model, MoMoPayment momo)
        {
            model.Tinh = form["Tinh"];
            if (!string.IsNullOrEmpty(model.Tinh) && model.Tinh.Contains(","))
            {
                model.Tinh = model.Tinh.Split(',')[1].Trim(); // Lấy phần tên sau dấu phẩy
            }
            _logger.LogInformation("Dữ liệu tỉnh/thành phố: " + model.Tinh);
            string customerId = _context.AspNetUsers
                .Where(u => u.UserName == model.Username)
                .Select(u => u.Id)
                .FirstOrDefault();
            string Makh = _context.KhachHangs
                .Where(u => u.Makh == customerId)
                .Select(u => u.Makh)
                .FirstOrDefault();

            if (Makh == null)
            {
                kh.Makh = customerId;
                kh.Hoten = model.FullName;
                kh.Tendangnhap = model.Username;
                kh.Email = model.Email;
                kh.Diachi = model.SoNha + " " + model.Xa + " " + model.Tinh;
                kh.Dienthoai = model.PhoneNumber;
                kh.Ngaysinh = model.BirthDate;
                kh.RoleId = model.Username == "Admin" ? 1 : (model.Username == "Staff" ? 3 : 2);
                kh.Status = 1;
                kh.Resetpasswordcode = "123";

                _context.KhachHangs.Add(kh);
            }

            // Tạo đơn hàng mới
            dh.Makh = customerId;
            dh.Thanhtoan = model.PaymentMethod;
            dh.Giaohang = "chờ xử lý";
            dh.Ngaydat = DateTime.Now;
            _context.DonHangs.Add(dh);
            _context.SaveChanges();
            HttpContext.Session.SetInt32("lastOrderId", dh.Madon);

            var cart = SessionHelper.GetObjectFromJson<List<Item>>(HttpContext.Session, "cart");

            // Thêm chi tiết đơn hàng
            foreach (var item in cart)
            {
                var ctdh = new ChiTietDonHang
                {
                    Madon = dh.Madon,
                    Masp = item.Product.Masp,
                    Soluong = item.Quantity,
                    Gia = item.Product.Giakhuyenmai,
                    Tongsoluong = cart.Sum(i => i.Quantity),
                    Tonggia = cart.Sum(i => i.Product.Giakhuyenmai * i.Quantity),
                    Status = 0
                };

                _context.ChiTietDonHangs.Add(ctdh);

                // Cập nhật số lượng tồn của sản phẩm
                var product = _context.SanPhams.FirstOrDefault(p => p.Masp == item.Product.Masp);
                if (product != null)
                {
                    product.Soluongton -= item.Quantity;
                }
            }
            _context.SaveChanges();

            _logger.LogInformation("Mã đơn hàng vừa tạo: " + dh.Madon);
            HttpContext.Session.SetInt32("lastOrderId", dh.Madon);

            if (model.PaymentMethod == "vnpay")
            {
                HttpContext.Session.SetString("Ordermodel", JsonConvert.SerializeObject(model));
                return RedirectToAction("Payment", "Checkout");
            }
            else if (model.PaymentMethod == "cod")
            {
                // tạo đơn momo pay
                momo.PaymentId = Guid.NewGuid().ToString();
                momo.Madon = dh.Madon;
                momo.Magiaodich = "cod" + dh.Madon;
                momo.Amount = Convert.ToDecimal(cart.Sum(i => i.Product.Giakhuyenmai.GetValueOrDefault() * i.Quantity));
                momo.Trangthaithanhtoan = "Chưa thanh toán";
                momo.Tinnhantrave = "Chưa thanh toán";
                _logger.LogInformation("Payment method:" + momo);
                _context.MoMoPayments.Add(momo);
                _context.SaveChanges();
                return RedirectToAction("DatHangThanhCong");
            }
            return RedirectToAction("DatHangThanhCong");
        }


        [Authorize(Roles = "User")]
        public IActionResult DatHangThanhCong()
        {
            return View();
        }

        [Route("addtocart/{id}")]
        public IActionResult AddToCart(string id, IFormCollection form)
        {
            int productId = Convert.ToInt32(id);

            var produc = _context.SanPhams.Find(productId);

            int x = Convert.ToInt32(form["soluong"]);

            if (x < 1 || x > produc.Soluongton)
            {
                TempData["slError"] = "Số lượng ko được < 0 và > số lượng tồn";
            }
            else
            {
                string username = HttpContext.Session.GetString("username");
                if (username == null)
                {
                    return RedirectToAction("SignIn", "Security"); // Chuyển hướng đến trang đăng nhập
                }
                TempData["addSuccess"] = "Thêm vào giỏ hàng thành công!";
                if (SessionHelper.GetObjectFromJson<List<Item>>(HttpContext.Session, "cart") == null) //chua có sp trong giỏ
                {
                    List<Item> cart = new List<Item>();  ///tao new list
                    cart.Add(new Item { Product = produc, Quantity = x }); //them sp chưa có vào giỏ
                    SessionHelper.SetObjectAsJson(HttpContext.Session, "cart", cart);

                    //create session coiuntCart
                    HttpContext.Session.SetString("countCart", cart.Count.ToString());
                }
                else //có sp trong giỏ
                {
                    List<Item> cart = SessionHelper.GetObjectFromJson<List<Item>>(HttpContext.Session, "cart");

                    Item existingItem = cart.FirstOrDefault(i => i.Product.Masp == productId);
                    if (existingItem != null) // Nếu sản phẩm id{?} đã có trong giỏ hàng
                    {
                        existingItem.Quantity += x; // Tăng số lượng sản phẩm lên
                        if (existingItem.Quantity > existingItem.Product.Soluongton) //nếu sp vừa tăng lên > sl tồn thì xét về 1
                        {
                            existingItem.Quantity = 1;
                        }
                    }
                    else
                    {
                        cart.Add(new Item { Product = produc, Quantity = x }); //them sp thuôc id ? chưa có vào giỏ
                        SessionHelper.SetObjectAsJson(HttpContext.Session, "cart", cart);
                    }

                    SessionHelper.SetObjectAsJson(HttpContext.Session, "cart", cart); //cập nhập session cart

                    //create session coiuntCart
                    HttpContext.Session.SetString("countCart", cart.Count.ToString());
                }

            }

            return RedirectToAction("Details", "SanPham", new { id = id });
        }
        [Route("remove/{id}")]
        public IActionResult Remove(int id)
        {
            List<Item> cart = SessionHelper.GetObjectFromJson<List<Item>>(HttpContext.Session, "cart");

            // Find the item with the specified ID in the cart
            Item itemToRemove = cart.FirstOrDefault(item => item.Product.Masp == id);

            if (itemToRemove != null)
            {
                // Remove the item from the cart
                cart.Remove(itemToRemove);

                // Update the cart session
                SessionHelper.SetObjectAsJson(HttpContext.Session, "cart", cart);

                //create session coiuntCart
                HttpContext.Session.SetString("countCart", cart.Count.ToString());
            }

            if (cart.Count == 0)
            {
                HttpContext.Session.Remove("cart");
                HttpContext.Session.Remove("countCart");
            }

            return RedirectToAction("Index");
        }

        private int isExist(string id)
        {
            List<Item> cart = SessionHelper.GetObjectFromJson<List<Item>>(HttpContext.Session, "cart");
            for (int i = 0; i < cart.Count; i++)
            {
                if (cart[i].Product.Masp.Equals(id))
                {
                    return i;
                }
            }
            return -1;
        }

    }
}
