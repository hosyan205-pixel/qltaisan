using DoAn.Models;
using MySqlConnector;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DoAn.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class TaiKhoanController : Controller
    {
        private readonly DataContext _context;
        private readonly ILogger<TaiKhoanController> _logger;

        public TaiKhoanController(DataContext context, ILogger<TaiKhoanController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext action, ActionExecutionDelegate next)
        {
            var id = HttpContext.Session.GetInt32("TaiKhoanID");
            if (id == null) { action.Result = RedirectToAction("Index", "DangNhap", new { area = "" }); return; }
            if (!await _context.tblTaiKhoan.AsNoTracking().AnyAsync(x => x.TK_ID == id && x.TK_TrangThai && x.TK_VaiTro == "Admin")) { action.Result = StatusCode(403); return; }
            await next();
        }

        private async Task Departments(int? current = null) => ViewBag.Departments = await _context.tblPhongBan.AsNoTracking()
            .Where(x => x.PB_TrangThai == true || x.PB_ID == current).OrderBy(x => x.PB_TenPhongBan)
            .Select(x => new SelectListItem(x.PB_TenPhongBan + (x.PB_TrangThai == true ? "" : " (ngừng hoạt động)"), x.PB_ID.ToString())).ToListAsync();

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

            var taiKhoanList = await query.OrderByDescending(x => x.TK_ID).ToListAsync();

            ViewBag.Keyword = keyword;
            ViewBag.VaiTro = vaiTro;
            ViewBag.TrangThai = trangThai;

            return View(taiKhoanList);
        }

        public async Task<IActionResult> Create()
        {
            await Departments(); return View(new tblTaiKhoan());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("TK_ID,TK_TenDangNhap,TK_MatKhau,TK_HoTen,TK_Email,TK_SoDienThoai,TK_VaiTro,TK_TrangThai,PB_ID")] tblTaiKhoan tk)
        {
            await Departments(tk.PB_ID);
            if (tk.PB_ID.HasValue && !await _context.tblPhongBan.AnyAsync(x => x.PB_ID == tk.PB_ID && x.PB_TrangThai == true)) ModelState.AddModelError(nameof(tk.PB_ID), "Chọn phòng ban đang hoạt động.");
            if (tk.TK_VaiTro is not ("Admin" or "NhanVien" or "QuanLyTaiSan")) ModelState.AddModelError(nameof(tk.TK_VaiTro), "Vai trò không hợp lệ.");
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
            tk.TK_ID = 0;
            tk.TK_MatKhau = BCrypt.Net.BCrypt.HashPassword(tk.TK_MatKhau);

            _context.tblTaiKhoan.Add(tk);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Thêm tài khoản thành công!";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit([FromRoute] int? id)
        {
            if (id == null || id == 0)
                return NotFound();

            var tk = await _context.tblTaiKhoan.FindAsync(id);

            if (tk == null)
                return NotFound();

            await Departments(tk.PB_ID); return View(tk);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([FromRoute] int id, [Bind("TK_ID,TK_TenDangNhap,TK_MatKhau,TK_HoTen,TK_Email,TK_SoDienThoai,TK_VaiTro,TK_TrangThai,PB_ID")] tblTaiKhoan tk)
        {
            if (id != tk.TK_ID)
                return NotFound();

            var existing = await _context.tblTaiKhoan.FirstOrDefaultAsync(x => x.TK_ID == id);

            if (existing == null)
                return NotFound();

            await Departments(tk.PB_ID ?? existing.PB_ID);
            if (tk.PB_ID != existing.PB_ID && tk.PB_ID.HasValue && !await _context.tblPhongBan.AnyAsync(x => x.PB_ID == tk.PB_ID && x.PB_TrangThai == true)) ModelState.AddModelError(nameof(tk.PB_ID), "Chọn phòng ban đang hoạt động.");
            if (tk.TK_VaiTro is not ("Admin" or "NhanVien" or "QuanLyTaiSan")) ModelState.AddModelError(nameof(tk.TK_VaiTro), "Vai trò không hợp lệ.");

            bool usernameExists = await _context.tblTaiKhoan
                .AnyAsync(x => x.TK_TenDangNhap == tk.TK_TenDangNhap && x.TK_ID != id);

            if (usernameExists)
            {
                ModelState.AddModelError("TK_TenDangNhap", "Tên đăng nhập đã tồn tại!");
            }

            bool emailExists = !string.IsNullOrWhiteSpace(tk.TK_Email) && await _context.tblTaiKhoan
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
            if (Request.HasFormContentType && Request.Form.ContainsKey(nameof(tk.PB_ID)))
            {
                _logger.LogInformation("Tài khoản {AccountId}: phòng ban {OldDepartment} -> {NewDepartment}, người sửa {ActorId}.", id, existing.PB_ID, tk.PB_ID, HttpContext.Session.GetInt32("TaiKhoanID"));
                existing.PB_ID = tk.PB_ID;
            }

            if (!string.IsNullOrWhiteSpace(tk.TK_MatKhau))
            {
                existing.TK_MatKhau = BCrypt.Net.BCrypt.HashPassword(tk.TK_MatKhau);
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Cập nhật tài khoản thành công.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete([FromRoute] int id)
        {
            var currentUserId = HttpContext.Session.GetInt32("TaiKhoanID");
            if (currentUserId == id)
            {
                TempData["Error"] = "Không thể tự xóa tài khoản đang đăng nhập.";
                return RedirectToAction(nameof(Index));
            }

            var tk = await _context.tblTaiKhoan.FindAsync(id);
            if (tk == null)
            {
                return NotFound();
            }

            try
            {
                _context.tblTaiKhoan.Remove(tk);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _context.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Tài khoản {AccountId} đã thay đổi khi xóa.", id);
                TempData["Error"] = "Tài khoản đã được xử lý bởi người khác. Vui lòng tải lại danh sách.";
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                _context.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Không thể xóa tài khoản {AccountId}.", id);
                var sql = ex.GetBaseException() as MySqlException;
                TempData["Error"] = sql?.Number switch
                {
                    1451 => "Không thể xóa tài khoản vì còn dữ liệu liên quan. Có thể sửa trạng thái sang Bị khóa để giữ lịch sử.",
                    1644 => !string.IsNullOrWhiteSpace(sql.Message)
                        ? sql.Message
                        : "Không thể xóa tài khoản do ràng buộc dữ liệu. Có thể khóa tài khoản để giữ lịch sử.",
                    1205 or 1213 => "Dữ liệu đang được xử lý đồng thời. Chưa xóa tài khoản; hãy tải lại và thử lại.",
                    _ => "Tài khoản có lịch sử liên quan nên không thể xóa. Có thể khóa tài khoản để giữ lịch sử."
                };
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = "Xóa tài khoản thành công.";
            return RedirectToAction(nameof(Index));
        }
    }
}
