using DoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoAn.Controllers
{
    public class DangNhapController : Controller
    {
        private readonly DataContext _context;

        public DangNhapController(DataContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View(new DangNhap());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(DangNhap model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var taiKhoan = await _context.tblTaiKhoan.FirstOrDefaultAsync(t => t.TK_TenDangNhap == model.TenDangNhap);

            if (taiKhoan == null || taiKhoan.TK_MatKhau != model.MatKhau)
            {
                ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không đúng.");

                return View(model);
            }

            if (!taiKhoan.TK_TrangThai)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản đã bị khóa.");

                return View(model);
            }

            if (taiKhoan.TK_VaiTro != "Admin" && taiKhoan.TK_VaiTro != "NhanVien")
            {
                ModelState.AddModelError(string.Empty, "Vai trò tài khoản không hợp lệ.");

                return View(model);
            }

            HttpContext.Session.Clear();

            HttpContext.Session.SetInt32("TaiKhoanID", taiKhoan.TK_ID);

            HttpContext.Session.SetString("TenDangNhap", taiKhoan.TK_TenDangNhap ?? "");

            HttpContext.Session.SetString("HoTen", taiKhoan.TK_HoTen ?? "");

            HttpContext.Session.SetString("VaiTro", taiKhoan.TK_VaiTro ?? "");

            Console.WriteLine("Tên đăng nhập: " + HttpContext.Session.GetString("TenDangNhap"));

            Console.WriteLine("Họ tên: " + HttpContext.Session.GetString("HoTen"));

            if (taiKhoan.TK_VaiTro == "Admin")
            {
                return RedirectToAction(
                    "Index", "Home", new { area = "Admin" });
            }

            return RedirectToAction(
                "Index", "Home", new { area = "" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DangXuat()
        {
            HttpContext.Session.Clear();
            return Redirect("/DangNhap/Index");
        }
    }
}
