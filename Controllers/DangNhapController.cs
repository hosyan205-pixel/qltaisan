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

            if (taiKhoan == null || !PasswordMatches(model.MatKhau,taiKhoan.TK_MatKhau))
            {
                ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không đúng.");

                return View(model);
            }

            if (!taiKhoan.TK_TrangThai)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản đã bị khóa.");

                return View(model);
            }

            if (taiKhoan.TK_VaiTro is not ("Admin" or "NhanVien" or "QuanLyTaiSan"))
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

            if (taiKhoan.TK_VaiTro == "QuanLyTaiSan")
                return RedirectToAction("Index", "KiemKe", new { area = "Admin" });

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

        private static bool PasswordMatches(string supplied,string stored)
        {
            if(stored.Length==60&&(stored.StartsWith("$2a$")||stored.StartsWith("$2b$")||stored.StartsWith("$2y$")))
            {
                try{return BCrypt.Net.BCrypt.Verify(supplied,stored);}catch{return false;}
            }
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied),System.Text.Encoding.UTF8.GetBytes(stored));
        }
    }
}
