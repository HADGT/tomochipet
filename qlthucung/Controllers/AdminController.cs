using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using qlthucung.Models;
using RestSharp;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace qlthucung.Controllers
{
    [Authorize(Roles = "Manager,Admin")]
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly string spreadsheetId = "1EHUAnDAHI6iXnvj4YPrYPh6vZUkRdQnR1bU_aG2t08s";

        public AdminController(AppDbContext context, IWebHostEnvironment hostingEnvironment)
        {
            _context = context;
            _hostingEnvironment = hostingEnvironment;
        }

        //Show drop list danh muc
        private void showDropList()
        {
            List<SelectListItem> list = (from c in _context.DanhMucs
                                         select new SelectListItem()
                                         {
                                             Text = c.Tendanhmuc,
                                             Value = c.IdDanhmuc.ToString()
                                         }).Distinct().ToList();
            ViewBag.ShowDropList = list;
        }

        // GET: Admin

        [Authorize(Roles = "Admin")]
        public IActionResult Index()
        {
            //Dashboard
            ViewBag.ThongKeSL = ThongKeSL();
            ViewBag.ThongKeDonHang = ThongKeDonHang();
            ViewBag.ThongKeKH = ThongKeKhachHang();
            ViewBag.TongDoanhThu = _context.ChiTietDonHangs
                .Where(m => m.Status == 1)
                .Sum(
                n => n.Soluong * n.Gia
            ).Value;
            return View();
        }
        public async Task<IActionResult> ListDanhMuc(int? pageNumber, string searchTerm)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }

            const int pageSize = 5;

            // Query sản phẩm
            var query = _context.SanPhams
                .Include(s => s.IdDanhmucNavigation)
                .Include(s => s.IdthuvienNavigation)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(sp => sp.Tensp.Contains(searchTerm));
                ViewBag.SearchTerm = searchTerm;
            }

            // Phân trang
            var paginatedProducts = await PaginatedList<SanPham>.CreateAsync(
                query.OrderBy(m => m.Masp),
                pageNumber ?? 1,
                pageSize
            );

            // Lấy toàn bộ sản phẩm để so sánh với Google Sheet
            var sanPhams = await query.OrderBy(m => m.Masp).ToListAsync();
            var tongSanPham = sanPhams.Count; // số bản ghi sản phẩm

            // Lấy dữ liệu Google Sheet
            var service = GetSheetsService();
            var request = service.Spreadsheets.Values.Get(spreadsheetId, "SP!A2:G");
            var response = request.Execute();

            // Lọc sheet bỏ hàng trống
            var sheetValues = (response.Values ?? new List<IList<object>>())
                .Where(r => r.Count > 0 && !string.IsNullOrWhiteSpace(r[0]?.ToString()))
                .ToList();

            bool isDifferent = false; // mặc định coi như giống
            for (int i = 0; i < tongSanPham; i++)
            {
                var sp = sanPhams[i];
                var row = sheetValues[i];

                if (row.Count < 7) // thiếu cột => khác
                {
                    isDifferent = true;
                    break;
                }

                // nếu có 1 cột khác => khác
                if (sp.Masp.ToString() != row[0].ToString() ||
                    sp.Tensp != row[1].ToString() ||
                    sp.Giaban.ToString() != row[2].ToString() ||
                    sp.Giamgia.ToString() != row[5].ToString() ||
                    sp.Giakhuyenmai.ToString() != row[6].ToString())
                {
                    isDifferent = true;
                    break;
                }
            }

            // Gửi flag + link Sheet sang View
            ViewBag.IsDifferent = isDifferent;
            ViewBag.SheetUrl = $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit?usp=sharing";

            return View(paginatedProducts);
        }

        /// <summary>
        /// Hàm làm sạch dữ liệu số từ Google Sheet
        /// </summary>
        private string CleanNumber(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "0";

            // Bỏ ký tự không phải số hoặc dấu .
            var cleaned = new string(input.Where(c => char.IsDigit(c)).ToArray());

            return string.IsNullOrEmpty(cleaned) ? "0" : cleaned;
        }

        [HttpPost]
        public IActionResult UpdateDB()
        {
            var service = GetSheetsService();
            var request = service.Spreadsheets.Values.Get(spreadsheetId, "SP!A2:G");
            var response = request.Execute();
            var sheetValues = response.Values ?? new List<IList<object>>();

            foreach (var row in sheetValues)
            {
                int masp = int.Parse(row[0].ToString());
                var sp = _context.SanPhams.FirstOrDefault(x => x.Masp == masp);
                if (sp != null)
                {
                    sp.Tensp = row[1].ToString();
                    // Giá bán
                    if (decimal.TryParse(CleanNumber(row[2]?.ToString()), out var giaban))
                        sp.Giaban = giaban;
                    if (int.TryParse(row[3]?.ToString(), out var sl))
                        sp.Soluongton = sp.Soluongton + sl;
                    sp.Mota = row.Count > 4 ? row[4].ToString() : "";
                    // Giảm giá
                    if (int.TryParse(CleanNumber(row[5]?.ToString()), out var giamgia))
                        sp.Giamgia = giamgia;
                    sp.Giakhuyenmai = sp.Giaban - (sp.Giaban * sp.Giamgia / 100);
                    sp.Ngaycapnhat = DateTime.Now;
                }
            }
            try
            {
                var changed = _context.SaveChanges();

                // 🔹 Reset lại dữ liệu Sheet (Soluongton = 0)
                // Ghi dữ liệu
                var values = new List<IList<object>>();
                values.Add(new List<object> { "Masp", "Tensp", "Giaban", "Soluongton", "Giamgia", "Giakhuyenmai" });
                var sanPhams = _context.SanPhams.ToList();
                foreach (var sp in sanPhams)
                {
                    values.Add(new List<object> { sp.Masp, sp.Tensp, sp.Giaban, 0, sp.Giamgia, sp.Giakhuyenmai });
                }

                var updateRequest = service.Spreadsheets.Values.Update(
                    new Google.Apis.Sheets.v4.Data.ValueRange
                    {
                        Values = values
                    },
                    spreadsheetId,
                    "SP!A2:G" // Range ghi đè lại
                );
                updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.RAW;
                updateRequest.Execute();

                TempData["Message"] = $"Cập nhật thành công {changed} bản ghi và reset Sheet!";
            }
            catch (Exception ex)
            {
                TempData["Message"] = "Lỗi cập nhật: " + ex.Message;
            }

            return RedirectToAction("ListDanhMuc", new { pageNumber = 1 });
        }

        public ActionResult ListDanhMucSP(string selectedParentName, string searchTerm, int page = 1)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }
            // Lấy danh sách ParentName không trùng lặp
            var parentNames = _context.DanhMucs
                                      .Select(d => d.ParentID)
                                      .Distinct()
                                      .ToList();

            // Lọc danh sách con theo ParentName và tìm kiếm
            var query = _context.DanhMucs.AsQueryable();

            if (!string.IsNullOrEmpty(selectedParentName))
            {
                query = query.Where(d => d.ParentID == selectedParentName);
            }

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(d => d.Tendanhmuc.Contains(searchTerm));
            }

            // Phân trang
            int pageSize = 10;
            int totalItems = query.Count();
            var danhMucs = query
                            .OrderBy(d => d.ParentID) // Sắp xếp theo ParentName
                            .Skip((page - 1) * pageSize)
                            .Take(pageSize)
                            .ToList();

            var model = new DanhMucViewModel
            {
                ParentNames = parentNames,
                DanhMucs = danhMucs,
                SelectedParentName = selectedParentName,
                SearchTerm = searchTerm,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
            };

            return View(model);
        }

        // API doanh thu (AJAX)
        [HttpGet]
        public JsonResult DoanhThu()
        {
            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            decimal revenueToday = (from ct in _context.ChiTietDonHangs
                                    join dh in _context.DonHangs on ct.Madon equals dh.Madon
                                    where dh.Ngaydat.Value.Date == today
                                    select ct.Tonggia.Value).Sum();

            decimal revenueWeek = (from ct in _context.ChiTietDonHangs
                                   join dh in _context.DonHangs on ct.Madon equals dh.Madon
                                   where dh.Ngaydat.Value.Date >= startOfWeek && dh.Ngaydat.Value.Date <= today
                                   select ct.Tonggia.Value).Sum();

            decimal revenueMonth = (from ct in _context.ChiTietDonHangs
                                    join dh in _context.DonHangs on ct.Madon equals dh.Madon
                                    where dh.Ngaydat.Value.Date >= startOfMonth && dh.Ngaydat.Value.Date <= today
                                    select ct.Tonggia.Value).Sum();

            return Json(new { revenueToday, revenueWeek, revenueMonth });
        }

        public JsonResult ThongKeSP()
        {
            var data = (from sp in _context.SanPhams
                        join dm in _context.DanhMucs on sp.IdDanhmuc equals dm.IdDanhmuc
                        group sp by dm.Tendanhmuc into g
                        select new
                        {
                            Label = g.Key,
                            Value = g.Count()
                        })
                       .ToList();

            return Json(new
            {
                labels = data.Select(d => d.Label).ToArray(),
                values = data.Select(d => d.Value).ToArray()
            });
        }

        public decimal ThongKeSL()
        {
            decimal TongDoanhThu = _context.ChiTietDonHangs
                .Where(m => m.Status == 1)
                .Sum(
                n => (n.Soluong)
            ).Value;
            return TongDoanhThu;
        }

        public double ThongKeDonHang()
        {
            double slddh = _context.DonHangs.Count();
            return slddh;
        }
        public double ThongKeKhachHang()
        {
            double slkh = _context.KhachHangs.Count();
            return slkh;
        }

        [Authorize(Roles = "Admin")]
        // GET: Admin/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }
            if (id == null)
            {
                return NotFound();
            }

            var sanPham = await _context.SanPhams
                .Include(s => s.IdDanhmucNavigation)
                .Include(s => s.IdthuvienNavigation)
                .FirstOrDefaultAsync(m => m.Masp == id);
            if (sanPham == null)
            {
                return NotFound();
            }

            return View(sanPham);
        }

        private SheetsService GetSheetsService()
        {
            var json = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_JSON");

            if (string.IsNullOrEmpty(json))
            {
                throw new Exception("GOOGLE_CREDENTIALS_JSON is missing");
            }

            using var stream = new MemoryStream(
                System.Text.Encoding.UTF8.GetBytes(json));

            var credential = GoogleCredential
                .FromStream(stream)
                .CreateScoped(SheetsService.Scope.Spreadsheets);

            return new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "QLThuCung"
            });
        }

        // GET: Admin/Create
        public IActionResult Create()
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }
            showDropList();
            return View();
        }

        public IActionResult ExcelManage()
        {
            var sanPhams = _context.SanPhams.ToList();
            var json = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS_JSON");

            if (string.IsNullOrEmpty(json))
            {
                throw new Exception("GOOGLE_CREDENTIALS_JSON is missing");
            }

            using var stream = new MemoryStream(
                System.Text.Encoding.UTF8.GetBytes(json));

            var credential = GoogleCredential
                .FromStream(stream)
                .CreateScoped(SheetsService.Scope.Spreadsheets);

            var service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "QLThuCung"
            });

            // Ghi dữ liệu
            var values = new List<IList<object>>();
            values.Add(new List<object> { "Masp", "Tensp", "Giaban", "Soluongton", "Giamgia", "Giakhuyenmai" });
            foreach (var sp in sanPhams)
            {
                values.Add(new List<object> { sp.Masp, sp.Tensp, sp.Giaban, 0, sp.Giamgia, sp.Giakhuyenmai });
            }

            var valueRange = new ValueRange { Values = values };
            var updateRequest = service.Spreadsheets.Values.Update(valueRange, spreadsheetId, "SP!A1");
            updateRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
            updateRequest.Execute();

            return Redirect($"https://docs.google.com/spreadsheets/d/{spreadsheetId}/edit?usp=sharing");
        }

        public ActionResult CreateDm(DanhMuc model)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }

            // Lấy tất cả danh mục hiện có để chọn làm danh mục cha theo TÊN
            ViewBag.ShowDropList = new SelectList(
                _context.DanhMucs.Where(dm => dm.IdDanhmuc != model.IdDanhmuc),
                "Tendanhmuc", // VALUE là tên
                "Tendanhmuc"  // TEXT hiển thị cũng là tên
            );

            return View();
        }

        [HttpPost]
        public ActionResult CreateDanhMucSP(DanhMuc model)
        {
            if (ModelState.IsValid)
            {
                _context.DanhMucs.Add(model);
                _context.SaveChanges();
                return RedirectToAction("ListDanhMucSP", "Admin");
            }

            // Load lại danh mục cha nếu có lỗi
            ViewBag.ShowDropList = new SelectList(
                _context.DanhMucs,
                "Tendanhmuc", // Dựa vào tên chứ không phải ID
                "Tendanhmuc"
            );

            return View(model);
        }

        // POST: Admin/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Masp,Idthuvien,IdDanhmuc,Tensp,Hinh,Giaban,Ngaycapnhat,Soluongton,Mota,Giamgia,Giakhuyenmai")] SanPham sanPham, List<IFormFile> files)
        {
            if (ModelState.IsValid)
            {
                if (files != null && files.Count > 0)
                {
                    // code for handling uploaded image file(s)
                    var filePaths = new List<string>();
                    string tempname = "";
                    foreach (var formFile in files)
                    {
                        if (formFile.Length > 0)
                        {
                            var fileName = Path.GetFileName(formFile.FileName);
                            var filePath = Path.Combine(_hostingEnvironment.WebRootPath, "Content/uploads", fileName);
                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await formFile.CopyToAsync(stream);
                            }
                            tempname = fileName;
                            filePaths.Add(fileName);
                        }
                    }
                    sanPham.Hinh = "/Content/uploads/" + tempname;


                }

                sanPham.Giakhuyenmai = sanPham.Giaban - (sanPham.Giaban * sanPham.Giamgia / 100);

                _context.Add(sanPham);
                await _context.SaveChangesAsync();
                return RedirectToAction("ListDanhMuc", "Admin");
            }

            showDropList();
            return View(sanPham);
        }

        // GET: Admin/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }
            if (id == null)
            {
                return NotFound();
            }

            var sanPham = await _context.SanPhams.FindAsync(id);
            if (sanPham == null)
            {
                return NotFound();
            }

            showDropList();

            return View(sanPham);
        }

        // POST: Admin/Edit/5

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Masp,IdDanhmuc,Idthuvien,Tensp,Hinh,Giaban,Ngaycapnhat,Soluongton,Mota,Giamgia,Giakhuyenmai")] SanPham sanPham, List<IFormFile> files, IFormCollection form)
        {
            if (id != sanPham.Masp)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {

                    if (files != null && files.Count > 0)
                    {
                        // code for handling uploaded image file(s)
                        var filePaths = new List<string>();
                        string tempname = "";
                        foreach (var formFile in files)
                        {
                            if (formFile.Length > 0)
                            {
                                var fileName = Path.GetFileName(formFile.FileName);
                                var filePath = Path.Combine(_hostingEnvironment.WebRootPath, "Content/uploads", fileName);
                                using (var stream = new FileStream(filePath, FileMode.Create))
                                {
                                    await formFile.CopyToAsync(stream);
                                }
                                tempname = fileName;
                                filePaths.Add(fileName);
                            }

                            sanPham.Hinh = "/Conent/uploads/" + form["PathHinh"];
                        }
                    }
                    sanPham.Hinh = form["PathHinh"];

                    sanPham.Giakhuyenmai = sanPham.Giaban - (sanPham.Giaban * sanPham.Giamgia / 100);

                    _context.Update(sanPham);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SanPhamExists(sanPham.Masp))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction("ListDanhMuc", "Admin");
            }
            ViewData["IdDanhmuc"] = new SelectList(_context.DanhMucs, "IdDanhmuc", "IdDanhmuc", sanPham.IdDanhmuc);
            ViewData["Idthuvien"] = new SelectList(_context.ThuVienAnhs, "Idthuvien", "Idthuvien", sanPham.Idthuvien);
            return View(sanPham);
        }

        // GET: Admin/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }
            if (id == null)
            {
                return NotFound();
            }

            var sanPham = await _context.SanPhams
                .Include(s => s.IdDanhmucNavigation)
                .Include(s => s.IdthuvienNavigation)
                .FirstOrDefaultAsync(m => m.Masp == id);
            if (sanPham == null)
            {
                return NotFound();
            }

            return View(sanPham);
        }

        // POST: Admin/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            // 1. Tìm sản phẩm
            var sanPham = await _context.SanPhams
                .FirstOrDefaultAsync(sp => sp.Masp == id);

            if (sanPham == null)
            {
                return NotFound();
            }

            // 2. Lấy tất cả các chi tiết đơn hàng có chứa sản phẩm này
            var chiTietDonHangs = await _context.ChiTietDonHangs
                .Where(ct => ct.Masp == id)
                .ToListAsync();

            // Lấy danh sách đơn hàng liên quan
            var donHangIds = chiTietDonHangs
                .Select(ct => ct.Madon)
                .Distinct()
                .ToList();

            // 3. Xóa chi tiết đơn hàng chứa sản phẩm
            _context.ChiTietDonHangs.RemoveRange(chiTietDonHangs);

            // 4. Kiểm tra từng đơn hàng — nếu không còn chi tiết thì xóa
            foreach (var donhangId in donHangIds)
            {
                bool conChiTiet = await _context.ChiTietDonHangs
                    .AnyAsync(ct => ct.Madon == donhangId);

                if (!conChiTiet)
                {
                    var donhang = await _context.DonHangs
                        .FirstOrDefaultAsync(d => d.Madon == donhangId);

                    if (donhang != null)
                    {
                        _context.DonHangs.Remove(donhang);
                    }
                }
            }
            _context.SanPhams.Remove(sanPham);
            await _context.SaveChangesAsync();
            return RedirectToAction("ListDanhMuc", "Admin");
        }

        private bool SanPhamExists(int id)
        {
            return _context.SanPhams.Any(e => e.Masp == id);
        }

        //---------------------------------------Quản lý đơn hàng------------------------------------

        public async Task<IActionResult> QLDonHang(int? pageNumber)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }

            const int pageSize = 5;

            var query = from dh in _context.DonHangs
                        join kh in _context.KhachHangs
                            on dh.Makh equals kh.Makh
                        join gd in _context.MoMoPayments
                            on dh.Madon equals gd.Madon into gj
                        from subGd in gj.DefaultIfEmpty() // left join momo
                        select new DonHangGiaoDichViewModel
                        {
                            DonHang = dh,
                            GiaoDich = subGd,
                            KhachHang = kh
                        };

            var paginated = await PaginatedList<DonHangGiaoDichViewModel>.CreateAsync(query.AsNoTracking(), pageNumber ?? 1, pageSize);

            return View(paginated);
        }
        public async Task<IActionResult> QLGiaoDich(int? pageNumber)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }
            const int pageSize = 5;
            var query = _context.MoMoPayments
                .AsNoTracking()
                .OrderByDescending(p => p.CreatedAt);
            var paginated = await PaginatedList<MoMoPayment>.CreateAsync(query, pageNumber ?? 1, pageSize);
            return View(paginated);
        }

        public async Task<IActionResult> DetailsGD(string id)
        {
            if (id == null) return NotFound();

            var payment = await _context.MoMoPayments.FirstOrDefaultAsync(p => p.PaymentId == id);
            if (payment == null) return NotFound();

            return View(payment);
        }

        [HttpGet]
        public IActionResult QLChiTietDonHang(int id)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }
            var donhang = _context.DonHangs.FirstOrDefault(m => m.Madon == id);
            if (donhang == null)
            {
                return NotFound();
            }
            var khachhang = _context.KhachHangs.FirstOrDefault(kh => kh.Makh == donhang.Makh);
            var giaodich = _context.MoMoPayments.FirstOrDefault(gd => gd.Madon == id);
            // Join để lấy Chi tiết + thông tin Sản phẩm
            var chiTietWithSP = (from ct in _context.ChiTietDonHangs
                                 where ct.Madon == id
                                 join sp in _context.SanPhams
                                 on ct.Masp equals sp.Masp
                                 select new ChiTietDonHangItemViewModel
                                 {
                                     ChiTiet = ct,
                                     SanPham = sp
                                 }).ToList();
            var model = new ChiTietDonHangViewModel
            {
                DonHang = donhang,
                KhachHang = khachhang,
                GiaoDich = giaodich,
                ChiTietDonHangs = chiTietWithSP
            };

            ViewBag.TrangThaiGiaoHang = new List<SelectListItem>
                                        {
                                            new SelectListItem { Value = "chờ xử lý", Text = "1 - chờ xử lý" },
                                            new SelectListItem { Value = "đang xử lý", Text = "2 - đang xử lý" },
                                            new SelectListItem { Value = "đang giao", Text = "3 - đang giao" },
                                            new SelectListItem { Value = "giao thành công", Text = "4 - giao thành công" },
                                            new SelectListItem { Value = "hủy", Text = "5 - hủy" }
                                        };
            ViewBag.TrangThaiThanhToan = new List<SelectListItem>
                                        {
                                            new SelectListItem { Value = "Chưa thanh toán", Text = "1 - Chưa thanh toán" },
                                            new SelectListItem { Value = "Đã thanh toán", Text = "2 - Đã thanh toán" }
                                        };
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult QLChiTietDonHang(ChiTietDonHangViewModel model)
        {
            var donhang = _context.DonHangs.FirstOrDefault(m => m.Madon == model.DonHang.Madon);
            var momo = _context.MoMoPayments.FirstOrDefault(m => m.Madon == model.DonHang.Madon);
            if (donhang == null)
            {
                return NotFound();
            }
            // Tính tổng tiền đơn hàng
            var chiTietDonHangs = _context.ChiTietDonHangs
                .Where(ct => ct.Madon == donhang.Madon)
                .ToList();
            decimal totalAmount = chiTietDonHangs.Sum(ct => (ct.Gia ?? 0));
            if (momo == null)
            {
                momo = new MoMoPayment
                {
                    Madon = model.DonHang.Madon,
                    Tinnhantrave = "Thanh toan VnPay cho don hang tai Shoppet",
                    Amount = totalAmount,
                    Trangthaithanhtoan = model.GiaoDich?.Trangthaithanhtoan ?? "Chưa thanh toán",
                    Magiaodich = "cod" + model.DonHang.Madon.ToString(),
                    PaymentId = Guid.NewGuid().ToString()
                };
                _context.MoMoPayments.Add(momo); // thêm mới nếu chưa tồn tại
            }
            else
            {
                momo.Trangthaithanhtoan = model.GiaoDich?.Trangthaithanhtoan ?? momo.Trangthaithanhtoan;
                momo.Amount = totalAmount; // Cập nhật lại số tiền nếu muốn
                _context.MoMoPayments.Update(momo); // cập nhật nếu đã tồn tại
            }
            if (!string.IsNullOrEmpty(model.DonHang.Giaohang) || model.DonHang.Ngaygiao != null)
            {
                donhang.Giaohang = model.DonHang.Giaohang;
                donhang.Ngaygiao = model.DonHang.Ngaygiao;
                _context.Update(donhang);
                var ctdh = _context.ChiTietDonHangs.Where(m => m.Madon == donhang.Madon).ToList();
                int status = model.DonHang.Giaohang == "giao thành công" ? 1 : 0;

                foreach (var item in ctdh)
                {
                    item.Status = status;
                    _context.Update(item);
                }

                _context.SaveChanges();
                return RedirectToAction("QLDonHang");
            }

            // Load lại dữ liệu nếu không hợp lệ
            model.KhachHang = _context.KhachHangs.FirstOrDefault(kh => kh.Makh == donhang.Makh);
            model.GiaoDich = _context.MoMoPayments.FirstOrDefault(gd => gd.Madon == donhang.Madon);
            model.ChiTietDonHangs = (from ct in _context.ChiTietDonHangs
                                     where ct.Madon == donhang.Madon
                                     join sp in _context.SanPhams on ct.Masp equals sp.Masp
                                     select new ChiTietDonHangItemViewModel
                                     {
                                         ChiTiet = ct,
                                         SanPham = sp
                                     }).ToList();

            model.DonHang = donhang;
            return View(model);
        }

        public async Task<IActionResult> ListKhachHang(int? pageNumber, string searchTerm)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }
            const int pageSize = 10;
            var query = _context.KhachHangs.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(kh => kh.Tendangnhap.Contains(searchTerm) || kh.Hoten.Contains(searchTerm) || kh.Dienthoai.Contains(searchTerm));
                ViewBag.SearchTerm = searchTerm;
            }

            var paginatedCustomers = await PaginatedList<KhachHang>.CreateAsync(query.OrderBy(kh => kh.Makh), pageNumber ?? 1, pageSize);
            return View(paginatedCustomers);
        }

        public async Task<IActionResult> Dichvu(int? pageNumber, string searchTerm, DateTime? ngaydat)
        {
            int pageSize = 5;
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }

            var query = _context.DichVus.AsQueryable();

            // Lấy từ TempData nếu không có trong tham số
            if (string.IsNullOrEmpty(searchTerm) && TempData["searchTerm"] != null)
            {
                searchTerm = TempData["searchTerm"].ToString();
            }

            if (!ngaydat.HasValue && TempData["ngaydat"] != null)
            {
                ngaydat = DateTime.Parse(TempData["ngaydat"].ToString());
            }

            if (TempData["EditId"] != null)
            {
                ViewBag.EditId = Convert.ToInt32(TempData["EditId"]);
            }

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(kh => kh.Hoten.Contains(searchTerm) || kh.Tendichvu.Contains(searchTerm));
                ViewBag.SearchTerm = searchTerm;
            }

            if (ngaydat.HasValue)
            {
                query = query.Where(kh => kh.Ngaydat == ngaydat.Value.Date);
                ViewBag.NgayDat = ngaydat.Value.ToString("yyyy-MM-dd");
            }

            var paginatedCustomers = await PaginatedList<DichVu>.CreateAsync(query.OrderByDescending(kh => kh.Ngaydat), pageNumber ?? 1, pageSize);
            return View(paginatedCustomers);
        }

        [HttpGet]
        public IActionResult EditTrangThaiDichvu(int id, string searchTerm, DateTime? ngaydat, int? pageNumber)
        {
            TempData["EditId"] = id;
            TempData["searchTerm"] = searchTerm;
            TempData["ngaydat"] = ngaydat?.ToString("yyyy-MM-dd");
            TempData["pageNumber"] = pageNumber;
            return RedirectToAction(nameof(Dichvu));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CapNhatTrangThai(int id, string trangthai)
        {
            var dichVu = await _context.DichVus.FindAsync(id);
            if (dichVu == null)
            {
                return NotFound();
            }

            dichVu.Trangthai = trangthai;
            _context.Update(dichVu);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Dichvu));
        }

        public IActionResult Editkh(string id)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var kh = _context.KhachHangs.Find(id);
            if (kh == null)
            {
                return NotFound();
            }

            return View(kh);
        }

        [HttpPost]
        public async Task<IActionResult> Editkh(string id, [Bind("Makh,Hoten,Tendangnhap,Email,Diachi,Dienthoai,Ngaysinh,Status")] KhachHang khachHang)
        {
            if (id != khachHang.Makh)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(khachHang);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.KhachHangs.Any(e => e.Makh == khachHang.Makh))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction("ListKhachHang", "Admin", 1);
            }
            return RedirectToAction("ListKhachHang", "Admin", 1);
        }
        public IActionResult LichDat(int? month, int? year)
        {
            if (!User.IsInRole("Admin") && !User.IsInRole("Manager"))
            {
                return RedirectToAction("SignIn", "Security");
            }

            int currentMonth = month ?? DateTime.Now.Month;
            int currentYear = year ?? DateTime.Now.Year;

            var lichDatList = _context.DichVus
                .Where(d => d.Ngaydat.Month == currentMonth && d.Ngaydat.Year == currentYear)
                .AsEnumerable()
                .GroupBy(d => d.Ngaydat.Date)
                .Select(g => new LichDatViewModel
                {
                    Ngay = g.Key,
                    SoLuong = g.Count(),
                    Dv = g.ToList()
                })
                .ToList();

            ViewBag.Month = currentMonth;
            ViewBag.Year = currentYear;

            return View(lichDatList);
        }

    }
}
