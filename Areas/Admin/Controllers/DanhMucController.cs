using DoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace DoAn.Areas.Admin.Controllers;

[Area("Admin")]
public class DanhMucController(DataContext context, ILogger<DanhMucController> logger) : Controller
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext filterContext, ActionExecutionDelegate next)
    {
        var accountId = HttpContext.Session.GetInt32("TaiKhoanID");
        if (accountId == null)
        {
            filterContext.Result = RedirectToAction("Index", "DangNhap", new { area = "" });
            return;
        }
        if (!await context.tblTaiKhoan.AsNoTracking().AnyAsync(x => x.TK_ID == accountId && x.TK_TrangThai && x.TK_VaiTro == "Admin"))
        {
            filterContext.Result = StatusCode(StatusCodes.Status403Forbidden);
            return;
        }
        await next();
    }

    // 1. Danh sách danh mục (GET: Admin/DanhMuc)
    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, bool? trangThai)
    {
        var query = context.tblDanhMucTaiSan.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(x =>
                x.DMTS_MaDanhMuc.ToLower().Contains(kw) ||
                x.DMTS_TenDanhMuc.ToLower().Contains(kw));
        }

        if (trangThai.HasValue)
        {
            query = query.Where(x => x.DMTS_TrangThai == trangThai.Value);
        }

        ViewBag.Keyword = keyword;
        ViewBag.TrangThai = trangThai;

        var list = await query
            .OrderByDescending(x => x.DMTS_ID)
            .ToListAsync();

        return View(list);
    }

    // 2. Giao diện thêm mới (GET: Admin/DanhMuc/Create)
    [HttpGet]
    public IActionResult Create()
    {
        return View(new tblDanhMucTaiSan { DMTS_TrangThai = true });
    }

    // 3. Xử lý thêm mới (POST: Admin/DanhMuc/Create)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(tblDanhMucTaiSan model)
    {
        if (!string.IsNullOrWhiteSpace(model.DMTS_MaDanhMuc))
        {
            var ma = model.DMTS_MaDanhMuc.Trim();
            var exists = await context.tblDanhMucTaiSan.AnyAsync(x => x.DMTS_MaDanhMuc == ma);
            if (exists)
            {
                ModelState.AddModelError(nameof(model.DMTS_MaDanhMuc), "Mã danh mục này đã tồn tại.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.DMTS_MaDanhMuc = model.DMTS_MaDanhMuc.Trim();
        model.DMTS_TenDanhMuc = model.DMTS_TenDanhMuc.Trim();
        model.DMTS_MoTa = model.DMTS_MoTa?.Trim();
        model.DMTS_NgayTao = DateTime.Now;
        model.DMTS_TrangThai ??= true;

        context.tblDanhMucTaiSan.Add(model);
        await context.SaveChangesAsync();

        TempData["Success"] = "Thêm danh mục tài sản thành công.";
        return RedirectToAction(nameof(Index));
    }

    // 4. Giao diện cập nhật (GET: Admin/DanhMuc/Edit/5)
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await context.tblDanhMucTaiSan.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }
        return View(item);
    }

    // 5. Xử lý cập nhật (POST: Admin/DanhMuc/Edit/5)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, tblDanhMucTaiSan model)
    {
        if (id != model.DMTS_ID)
        {
            return BadRequest();
        }

        if (!string.IsNullOrWhiteSpace(model.DMTS_MaDanhMuc))
        {
            var ma = model.DMTS_MaDanhMuc.Trim();
            var exists = await context.tblDanhMucTaiSan
                .AnyAsync(x => x.DMTS_MaDanhMuc == ma && x.DMTS_ID != model.DMTS_ID);
            if (exists)
            {
                ModelState.AddModelError(nameof(model.DMTS_MaDanhMuc), "Mã danh mục này đã tồn tại.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existing = await context.tblDanhMucTaiSan.FindAsync(id);
        if (existing == null)
        {
            return NotFound();
        }

        existing.DMTS_MaDanhMuc = model.DMTS_MaDanhMuc.Trim();
        existing.DMTS_TenDanhMuc = model.DMTS_TenDanhMuc.Trim();
        existing.DMTS_MoTa = model.DMTS_MoTa?.Trim();
        existing.DMTS_TrangThai = model.DMTS_TrangThai;

        await context.SaveChangesAsync();

        TempData["Success"] = "Cập nhật danh mục tài sản thành công.";
        return RedirectToAction(nameof(Index));
    }

    // 6. Xử lý xóa (POST: Admin/DanhMuc/Delete/5)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var item = await context.tblDanhMucTaiSan.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }

        // Kiểm tra ràng buộc khóa ngoại với bảng Tài sản (DeleteBehavior.Restrict)
        var hasAssets = await context.tblTaiSan.AnyAsync(x => x.DMTS_ID == id);
        if (hasAssets)
        {
            TempData["Error"] = "Không thể xóa danh mục này vì đang có tài sản thuộc danh mục.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            context.tblDanhMucTaiSan.Remove(item);
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            context.ChangeTracker.Clear();
            logger.LogWarning(ex, "Danh mục {CategoryId} đã thay đổi khi xóa.", id);
            TempData["Error"] = "Danh mục đã được xử lý bởi người khác. Vui lòng tải lại danh sách.";
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException ex)
        {
            context.ChangeTracker.Clear();
            logger.LogWarning(ex, "Không thể xóa danh mục {CategoryId}.", id);
            var sql = ex.GetBaseException() as MySqlException;
            TempData["Error"] = sql?.Number switch
            {
                1451 => "Không thể xóa danh mục vì còn dữ liệu liên quan. Có thể chuyển sang ngừng hoạt động để giữ lịch sử.",
                1644 => sql.Message switch
                {
                    "Category has assets" => "Không thể xóa danh mục này vì đang có tài sản thuộc danh mục.",
                    "Category has child categories" => "Không thể xóa danh mục này vì còn danh mục con.",
                    "Category has attribute definitions" => "Không thể xóa danh mục này vì còn thuộc tính được cấu hình cho danh mục.",
                    "Category has audit history" => "Không thể xóa danh mục này vì đã có lịch sử cần lưu giữ. Có thể chuyển sang ngừng hoạt động.",
                    "Use Active/Inactive instead of deleting a category" => "Database hiện đang chặn xóa danh mục. Có thể chọn Sửa để chuyển sang ngừng hoạt động; cần cập nhật quy tắc xóa để xóa danh mục trống.",
                    _ => "Không thể xóa danh mục do ràng buộc dữ liệu. Vui lòng kiểm tra các dữ liệu liên quan."
                },
                1205 or 1213 => "Dữ liệu đang được xử lý đồng thời. Chưa xóa danh mục; hãy tải lại và thử lại.",
                _ => "Không thể xóa danh mục lúc này. Vui lòng thử lại sau."
            };
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = "Xóa danh mục tài sản thành công.";
        return RedirectToAction(nameof(Index));
    }
}
