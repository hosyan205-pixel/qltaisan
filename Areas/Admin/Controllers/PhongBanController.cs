using DoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace DoAn.Areas.Admin.Controllers;

[Area("Admin")]
public class PhongBanController(DataContext context) : Controller
{
    public override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        if (HttpContext.Session.GetInt32("TaiKhoanID") == null)
            filterContext.Result = RedirectToAction("Index", "DangNhap", new { area = "" });
        else if (HttpContext.Session.GetString("VaiTro") != "Admin")
            filterContext.Result = StatusCode(StatusCodes.Status403Forbidden);
        base.OnActionExecuting(filterContext);
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, bool? trangThai)
    {
        var query = context.tblPhongBan.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(x => x.PB_MaPhongBan.ToLower().Contains(kw) || 
                x.PB_TenPhongBan.ToLower().Contains(kw) ||
                (x.PB_NguoiPhuTrach != null && x.PB_NguoiPhuTrach.ToLower().Contains(kw)));
        }

        if (trangThai.HasValue)
        {
            query = query.Where(x => x.PB_TrangThai == trangThai.Value);
        }

        ViewBag.Keyword = keyword;
        ViewBag.TrangThai = trangThai;

        var list = await query.OrderByDescending(x => x.PB_ID).ToListAsync();

        return View(list);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new tblPhongBan { PB_TrangThai = true });
    }

    [HttpPost]
    public async Task<IActionResult> Create(tblPhongBan model)
    {
        if (!string.IsNullOrWhiteSpace(model.PB_MaPhongBan))
        {
            var ma = model.PB_MaPhongBan.Trim();
            var exists = await context.tblPhongBan.AnyAsync(x => x.PB_MaPhongBan == ma);
            if (exists)
            {
                ModelState.AddModelError(nameof(model.PB_MaPhongBan), "Mã phòng ban này đã tồn tại.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.PB_MaPhongBan = model.PB_MaPhongBan.Trim();
        model.PB_TenPhongBan = model.PB_TenPhongBan.Trim();
        model.PB_NguoiPhuTrach = model.PB_NguoiPhuTrach?.Trim();
        model.PB_SoDienThoai = model.PB_SoDienThoai?.Trim();
        model.PB_DiaChi = model.PB_DiaChi?.Trim();
        model.PB_NgayTao = DateTime.Now;
        model.PB_TrangThai ??= true;

        context.tblPhongBan.Add(model);
        await context.SaveChangesAsync();

        TempData["Success"] = "Thêm phòng ban thành công.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await context.tblPhongBan.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }
        return View(item);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, tblPhongBan model)
    {
        if (id != model.PB_ID)
        {
            return BadRequest();
        }

        if (!string.IsNullOrWhiteSpace(model.PB_MaPhongBan))
        {
            var ma = model.PB_MaPhongBan.Trim();
            var exists = await context.tblPhongBan.AnyAsync(x => x.PB_MaPhongBan == ma && x.PB_ID != model.PB_ID);
            if (exists)
            {
                ModelState.AddModelError(nameof(model.PB_MaPhongBan), "Mã phòng ban này đã tồn tại.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existing = await context.tblPhongBan.FindAsync(id);
        if (existing == null)
        {
            return NotFound();
        }

        existing.PB_MaPhongBan = model.PB_MaPhongBan.Trim();
        existing.PB_TenPhongBan = model.PB_TenPhongBan.Trim();
        existing.PB_NguoiPhuTrach = model.PB_NguoiPhuTrach?.Trim();
        existing.PB_SoDienThoai = model.PB_SoDienThoai?.Trim();
        existing.PB_DiaChi = model.PB_DiaChi?.Trim();
        existing.PB_TrangThai = model.PB_TrangThai;

        await context.SaveChangesAsync();

        TempData["Success"] = "Cập nhật phòng ban thành công.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await context.tblPhongBan.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }

        var hasCapPhat = await context.tblCapPhatTaiSan.AnyAsync(x => x.PB_ID == id);
        var hasKiemKe = await context.tblKiemKeTaiSan.AnyAsync(x => x.PB_ID == id);

        if (hasCapPhat || hasKiemKe)
        {
            TempData["Error"] = "Không thể xóa phòng ban này vì đã phát sinh dữ liệu cấp phát hoặc kiểm kê tài sản.";
            return RedirectToAction(nameof(Index));
        }

        context.tblPhongBan.Remove(item);
        await context.SaveChangesAsync();

        TempData["Success"] = "Xóa phòng ban thành công.";
        return RedirectToAction(nameof(Index));
    }
}