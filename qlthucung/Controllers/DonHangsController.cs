using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using qlthucung.Models;

namespace qlthucung.Controllers
{
    public class DonHangsController : Controller
    {
        private readonly AppDbContext _context;

        public DonHangsController(AppDbContext context)
        {
            _context = context;
        }


        // GET: DonHangs/Details/5

        [Authorize(Roles = "User")]
        public IActionResult Details()
        {
            var donHang = (from dh in _context.DonHangs
                           join kh in _context.KhachHangs on dh.Makh.ToString() equals kh.Makh.ToString()
                           where kh.Tendangnhap == HttpContext.Session.GetString("username")
                           select dh).Distinct().ToList();
            return View(donHang);
        }

        public ActionResult ChiTietDonHang(int id)
        {
            var donHangData = (from dh in _context.DonHangs
                               join kh in _context.KhachHangs on dh.Makh equals kh.Makh
                               join mo in _context.MoMoPayments on dh.Madon equals mo.Madon into payments
                               from mo in payments.DefaultIfEmpty()
                               where dh.Madon == id
                               select new
                               {
                                   Madon = dh.Madon,
                                   Makh = dh.Makh,
                                   Thanhtoan = dh.Thanhtoan,
                                   Giaohang = dh.Giaohang,
                                   Ngaydat = dh.Ngaydat,
                                   Ngaygiao = dh.Ngaygiao,
                                   TenDangNhap = kh.Tendangnhap,
                                   TenKhachHang = kh.Hoten,
                                   MaGiaoDich = mo != null ? mo.Magiaodich : null,
                                   Amount = mo != null ? mo.Amount : 0,
                                   TrangThaiThanhToan = mo != null ? mo.Trangthaithanhtoan : null,
                                   TinNhanTraVe = mo != null ? mo.Tinnhantrave : null
                               }).FirstOrDefault();

            // Lưu thông tin vào ViewBag
            ViewBag.Madon = donHangData.Madon;
            ViewBag.Thanhtoan = donHangData.Thanhtoan;
            ViewBag.Giaohang = donHangData.Giaohang;
            ViewBag.Ngaydat = donHangData.Ngaydat;
            ViewBag.Ngaygiao = donHangData.Ngaygiao;
            ViewBag.TenDangNhap = donHangData.TenDangNhap;
            ViewBag.TenKhachHang = donHangData.TenKhachHang;
            ViewBag.MaGiaoDich = donHangData.MaGiaoDich;
            ViewBag.TrangThaiThanhToan = donHangData.TrangThaiThanhToan;
            ViewBag.TinNhanTraVe = donHangData.TinNhanTraVe;
            var results = (from t1 in _context.ChiTietDonHangs
                           join t2 in _context.DonHangs
                           on new { t1.Madon } equals
                               new { t2.Madon }
                           where t2.Madon == id
                           select t1).ToList();

            List<KhachHang> khachhang = _context.KhachHangs.ToList();
            List<DonHang> donhang = _context.DonHangs.ToList();
            List<ChiTietDonHang> ctdh = _context.ChiTietDonHangs.ToList();
            List<SanPham> sanpham = _context.SanPhams.ToList();

            var ViewKH2 = (from kh in khachhang
                           join dh in donhang on kh.Makh equals dh.Makh
                           where dh.Madon == id
                           select new
                           {
                               khachhang = kh,
                               donhang = dh
                           }).FirstOrDefault();
            if (ViewKH2 != null)
            {
                ViewBag.TenKhachHang = ViewKH2.khachhang.Hoten;
                ViewBag.TenDangNhap = ViewKH2.khachhang.Tendangnhap;
                string DiaChi = ViewKH2.khachhang.Diachi;
                string diachiChuan = Regex.Replace(DiaChi, @"\s*\d+$", "").Trim();
                ViewBag.Diachi = diachiChuan;
                ViewBag.SoDienThoai = ViewKH2.khachhang.Dienthoai;
                ViewBag.Email = ViewKH2.khachhang.Email;
            }
            var ViewSP = from ct in ctdh
                         join sp in sanpham on ct.Masp equals sp.Masp
                         join dh in donhang on ct.Madon equals dh.Madon
                         where ct.Madon == id && sp.Masp == ct.Masp && ct.Madon == dh.Madon

                         select new ViewModel
                         {
                             sanpham = sp,
                             ctdh = ct,
                             donhang = dh
                         };

            ViewBag.ViewChiTietDH2 = ViewKH2;
            ViewBag.ViewSP = ViewSP;
            return View(results);
        }
        public IActionResult HuyDon(int id)
        {
            // Tìm đơn hàng theo ID
            var donHang = _context.DonHangs.FirstOrDefault(dh => dh.Madon == id);

            if (donHang == null)
            {
                return NotFound(); // Trả về lỗi nếu không tìm thấy đơn hàng
            }

            if (donHang.Giaohang == "Đã hủy")
            {
                TempData["ErrorMessage"] = "Đơn hàng này đã bị hủy trước đó!";
                return RedirectToAction("Details");
            }

            // Lấy danh sách chi tiết đơn hàng
            var chiTietDonHangs = _context.ChiTietDonHangs.Where(ct => ct.Madon == id).ToList();

            if (chiTietDonHangs.Any())
            {
                foreach (var item in chiTietDonHangs)
                {
                    // Tìm sản phẩm tương ứng
                    var sanPham = _context.SanPhams.FirstOrDefault(sp => sp.Masp == item.Masp);
                    if (sanPham != null)
                    {
                        sanPham.Soluongton += item.Soluong; // Cộng lại số lượng tồn kho
                    }
                }
            }

            // Cập nhật trạng thái đơn hàng thành "Đã hủy"
            donHang.Giaohang = "Đã hủy";

            // Lưu thay đổi vào database
            _context.SaveChanges();

            TempData["SuccessMessage"] = "Đơn hàng đã được hủy thành công!";
            return RedirectToAction("Details");
        }

    }
}
