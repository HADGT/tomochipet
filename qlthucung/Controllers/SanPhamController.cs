using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using qlthucung.Models;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace qlthucung.Controllers
{
    public class SanPhamController : Controller
    {
        private readonly AppDbContext _context;

        public SanPhamController(AppDbContext context)
        {
            _context = context;
        }

        [Authorize(Roles = "User")]
        // GET: All SanPham
        public async Task<IActionResult> Index(int Id)
        {
            var model = new HomeVm();

            // Sản phẩm nổi bật
            model.SanPhamNoiBat = await getSPNoiBat();

            // Sản phẩm chung (12 sản phẩm mới nhất)
            model.AllProducts = await _context.SanPhams
                .ToListAsync();

            // Root categories L1
            model.RootCategoriesl1 = await _context.DanhMucs
                .Where(dm => dm.ParentID == null)
                .Select(dm => new CategoryVm { Id = dm.IdDanhmuc, Ten = dm.Tendanhmuc })
                .ToListAsync();

            // Nếu chưa chọn thì mặc định lấy thằng đầu tiên
            if (Id == 0)
            {
                Id = model.RootCategoriesl1.First().Id;
            }

            // Lấy Level 2 theo Tên L1 đã chọn
            model.RootCategoriesl2 = await GetMenulev2(Id);

            // Lấy sản phẩm cho từng L2
            foreach (var cat in model.RootCategoriesl2)
            {
                model.ProductsByRoot[cat.Id] = await GetProductsByRootName(cat.Id);
            }

            return View(model);
        }

        /// <summary>
        /// API Ajax: lấy L2 + sản phẩm theo tên L1
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLevel2AndProducts(int l1Id)
        {
            var l2s = await GetMenulev2(l1Id);

            var productsByL2 = new Dictionary<int, List<SanPham>>();
            foreach (var cat in l2s)
            {
                // Lấy tên danh mục L2
                string l2Name = await _context.DanhMucs
                    .Where(dm => dm.IdDanhmuc == cat.Id)
                    .Select(dm => dm.Tendanhmuc)
                    .FirstOrDefaultAsync();

                // Lấy tất cả Id con của L2
                List<int> childIds = await _context.DanhMucs
                    .Where(dm => dm.ParentID != null && dm.ParentID == l2Name)
                    .Select(dm => dm.IdDanhmuc)
                    .ToListAsync();

                // Thêm chính L2 vào danh sách
                childIds.Add(cat.Id);

                // Lấy tất cả sản phẩm của L2 + các danh mục con
                var products = await _context.SanPhams
                    .Where(sp => childIds.Contains((int)sp.IdDanhmuc))
                    .Select(sp => new SanPham
                    {
                        Masp = sp.Masp,
                        Tensp = sp.Tensp,
                        Hinh = sp.Hinh,
                        Giaban = sp.Giaban ?? 0,
                        Giakhuyenmai = sp.Giakhuyenmai ?? 0,
                        Giamgia = sp.Giamgia
                    })
                    .ToListAsync();

                productsByL2[cat.Id] = products;
            }

            return Json(new { level2 = l2s, products = productsByL2 });
        }

        /// <summary>
        /// Lấy menu L2 theo tên L1
        /// </summary>
        private async Task<List<CategoryVm>> GetMenulev2(int l1id)
        {
            var catName = await _context.DanhMucs
                .Where(dm => dm.IdDanhmuc == l1id)
                .Select(dm => dm.Tendanhmuc)
                .FirstOrDefaultAsync();

            return await _context.DanhMucs
                .Where(dm => dm.ParentID == catName) // ParentID lưu tên cha
                .Select(dm => new CategoryVm
                {
                    Id = dm.IdDanhmuc,
                    Ten = dm.Tendanhmuc
                })
                .ToListAsync();
        }

        /// <summary>
        /// Lấy sản phẩm theo tên danh mục
        /// </summary>
        private async Task<List<SanPham>> GetProductsByRootName(int Id)
        {
            // Lấy sản phẩm theo IdDanhmuc
            return await _context.SanPhams
                .Where(sp => sp.IdDanhmuc == Id)
                .ToListAsync();
        }

        [HttpGet]
        public async Task<IActionResult> GetProductsByLevel2(int l2Id, int l1Id)
        {
            // Lấy tất cả các Id con của L2 (nếu có)
            List<int> childCategoryIds = await _context.DanhMucs
                .Where(dm => dm.ParentID != null && dm.ParentID == _context.DanhMucs
                                                             .Where(p => p.IdDanhmuc == l2Id)
                                                             .Select(p => p.Tendanhmuc)
                                                             .FirstOrDefault())
                .Select(dm => dm.IdDanhmuc)
                .ToListAsync();

            // Thêm chính L2 vào danh sách
            childCategoryIds.Add(l2Id);

            // Lấy tất cả sản phẩm thuộc L2 và các con
            var products = await _context.SanPhams
                .Where(sp => childCategoryIds.Contains((int)sp.IdDanhmuc))
                .Select(sp => new {
                    sp.Masp,
                    sp.Tensp,
                    sp.Hinh,
                    Giaban = sp.Giaban ?? 0,
                    Giakhuyenmai = sp.Giakhuyenmai ?? 0,
                    sp.Giamgia
                })
                .ToListAsync();

            return Json(products);
        }

        public async Task<List<SanPham>> ViewAllSanPham()
        {
            return await _context.SanPhams
                .ToListAsync();
        }

        //cac ham lay ra san pham
        #region
        //lay san pham noi bat
        private async Task<List<SanPham>> getSPNoiBat()
        {
            return await _context.SanPhams
                .OrderByDescending(sp => sp.Masp)
                .Take(10)
                .ToListAsync();
        }
        #endregion

        [Authorize(Roles = "User")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }
            var sanpham = await _context.SanPhams.FirstOrDefaultAsync(m => m.Masp == id);
            if (sanpham == null)
            {
                return NotFound();
            }

            //random san pham
            List<SanPham> products = _context.SanPhams.OrderBy(x => Guid.NewGuid()).Skip(5).Take(5).ToList();
            ViewBag.getSPRanDom = products;

            getThuVienAnhList(id);

            return View(sanpham);
        }

        private void getThuVienAnhList(int? id)
        {

            //sp vs thu vien anh
            List<SanPham> sanpham = _context.SanPhams.ToList();
            List<ThuVienAnh> thuvienanh = _context.ThuVienAnhs.ToList();
            var thu = from sp in sanpham
                      join tv in thuvienanh
                              on sp.Idthuvien equals tv.Idthuvien
                      where (sp.Masp == id && sp.Idthuvien == tv.Idthuvien)
                      select new ViewModel
                      {
                          sanpham = sp,
                          thuvienanh = tv
                      };

            ViewBag.getthuvienanh = thu;

        }

        [Authorize(Roles = "User")]
        [HttpGet]
        public async Task<IActionResult> Search(string search)
        {
            var searchProduct = from m in _context.SanPhams
                                select m;

            if (!String.IsNullOrEmpty(search))
            {
                searchProduct = searchProduct.Where(s => s.Tensp.Contains(search));
                if (!searchProduct.Any())
                {
                    TempData["nameProduct"] = search;
                    return RedirectToAction("NotFoundProduct", "SanPham");
                }
            }
            else
            {
                return RedirectToAction("NotFoundProduct", "SanPham");
            }

            TempData["nameProduct"] = search;
            return View(await searchProduct.ToListAsync());
        }

        public IActionResult NotFoundProduct()
        {
            return View();
        }

        public async Task<IActionResult> TatCaSanPham(int? pageNumber, string maLoai)
        {
            const int pageSize = 20;

            // Lấy danh sách sản phẩm
            var products = _context.SanPhams.AsNoTracking();

            // Nếu có mã loại và maLoai là số, lọc theo loại
            if (!string.IsNullOrEmpty(maLoai) && maLoai != "all")
            {
                if (int.TryParse(maLoai, out int loaiId))
                {
                    products = products.Where(p => p.IdDanhmuc == loaiId);
                }
                else
                {
                    // Nếu maLoai không hợp lệ, có thể trả về tất cả hoặc 404
                    // products = products; // giữ nguyên tất cả
                    // Hoặc: return NotFound();
                }
            }

            // Phân trang
            var paginatedProducts = await PaginatedList<SanPham>.CreateAsync(products, pageNumber ?? 1, pageSize);

            // Giữ lại mã loại để view render filter/active state
            ViewBag.MaLoai = maLoai;

            return View(paginatedProducts);
        }

    }
}
