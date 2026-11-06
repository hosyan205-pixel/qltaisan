using DoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoAn.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class TaiKhoanController : Controller
    {
        private readonly DataContext _context;

        public TaiKhoanController(DataContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? keyword, string? vaiTro, bool? trangThai)
        {
            var query = _context.tblTaiKhoan.AsQueryable();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(x => x.TK_TenDangNhap.Contains(keyword) || x.TK_HoTen.Contains(keyword) || 
                (x.TK_Email != null && x.TK_Email.Contains(keyword)));
            }

            if (!string.IsNullOrWhiteSpace(vaiTro))
            {
                query = query.Where(x => x.TK_VaiTro == vaiTro);
            }

            if (trangThai.HasValue)
            {
                query = query.Where(x => x.TK_TrangThai == trangThai.Value);
            }

            var taiKhoanList = await query.OrderBy(x => x.TK_ID).ToListAsync();

            ViewBag.Keyword = keyword;
            ViewBag.VaiTro = vaiTro;
            ViewBag.TrangThai = trangThai;

            return View(taiKhoanList);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(tblTaiKhoan tk)
        {
            if (!ModelState.IsValid)
            {
                return View(tk);
            }

            bool usernameExists = await _context.tblTaiKhoan.AnyAsync(x => x.TK_TenDangNhap == tk.TK_TenDangNhap);

            if (usernameExists)
            {
                ModelState.AddModelError("TK_TenDangNhap", "Tên đăng nhập đã tồn tại!");
                return View(tk);
            }

            if (!string.IsNullOrWhiteSpace(tk.TK_Email))
            {
                bool emailExists = await _context.tblTaiKhoan.AnyAsync(x => x.TK_Email == tk.TK_Email);

                if (emailExists)
                {
                    ModelState.AddModelError("TK_Email", "Email đã tồn tại!");
                    return View(tk);
                }
            }

            tk.TK_NgayTao = DateTime.Now;
            tk.TK_MatKhau = BCrypt.Net.BCrypt.HashPassword(tk.TK_MatKhau);

            _context.tblTaiKhoan.Add(tk);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var tk = await _context.tblTaiKhoan.FindAsync(id);

            if (tk == null)
                return NotFound();

            return View(tk);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(int id, tblTaiKhoan tk)
        {
            if (id != tk.TK_ID)
                return NotFound();

            var existing = await _context.tblTaiKhoan.FirstOrDefaultAsync(x => x.TK_ID == id);

            if (existing == null)
                return NotFound();

            bool usernameExists = await _context.tblTaiKhoan
                .AnyAsync(x => x.TK_TenDangNhap == tk.TK_TenDangNhap && x.TK_ID != id);

            if (usernameExists)
            {
                ModelState.AddModelError("TK_TenDangNhap", "Tên đăng nhập đã tồn tại!");
            }

            bool emailExists = await _context.tblTaiKhoan
                .AnyAsync(x => x.TK_Email == tk.TK_Email && x.TK_ID != id);

            if (emailExists)
            {
                ModelState.AddModelError("TK_Email", "Email đã tồn tại!");
            }

            ModelState.Remove(nameof(tk.TK_MatKhau));

            if (!ModelState.IsValid)
            {
                tk.TK_NgayTao = existing.TK_NgayTao;
                return View(tk);
            }

            existing.TK_TenDangNhap = tk.TK_TenDangNhap;
            existing.TK_HoTen = tk.TK_HoTen;
            existing.TK_Email = tk.TK_Email;
            existing.TK_SoDienThoai = tk.TK_SoDienThoai;
            existing.TK_VaiTro = tk.TK_VaiTro;
            existing.TK_TrangThai = tk.TK_TrangThai;

            if (!string.IsNullOrWhiteSpace(tk.TK_MatKhau))
            {
                existing.TK_MatKhau = tk.TK_MatKhau;
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var tk = await _context.tblTaiKhoan.FirstOrDefaultAsync(x => x.TK_ID == id);

            if (tk == null)
                return NotFound();

            return View(tk);
        }

        [HttpPost, ActionName("Delete")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var tk = await _context.tblTaiKhoan.FindAsync(id);

            if (tk == null)
                return NotFound();

            _context.tblTaiKhoan.Remove(tk);

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
