using DoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace DoAn.Areas.Admin.Controllers;

[Area("Admin")]
public class BaoTriController(DataContext context) : Controller
{
    public static readonly IReadOnlyDictionary<string, string> TrangThaiLabels = new Dictionary<string, string>
    {
        ["ChoXuLy"] = "Chờ xử lý",
        ["DangBaoTri"] = "Đang bảo trì",
        ["HoanThanh"] = "Hoàn thành",
        ["Huy"] = "Đã hủy"
    };

    public static string BadgeClass(string? status) => status switch
    {
        "ChoXuLy" => "bg-warning text-dark",
        "DangBaoTri" => "bg-info text-dark",
        "HoanThanh" => "bg-success",
        "Huy" => "bg-secondary",
        _ => "bg-secondary"
    };

    public static string StatusLabel(string? status) =>
        status != null && TrangThaiLabels.TryGetValue(status, out var label) ? label : (status ?? "Không rõ");

    public override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        if (HttpContext.Session.GetInt32("TaiKhoanID") == null)
            filterContext.Result = RedirectToAction("Index", "DangNhap", new { area = "" });
        else if (HttpContext.Session.GetString("VaiTro") != "Admin")
            filterContext.Result = StatusCode(StatusCodes.Status403Forbidden);
        base.OnActionExecuting(filterContext);
    }

    private async Task LoadDropdownsAsync(int? selectedTaiSanId = null)
    {
        var assets = await context.tblTaiSan
            .AsNoTracking()
            .OrderBy(x => x.TS_MaTaiSan)
            .Select(x => new
            {
                x.TS_ID,
                DisplayText = x.TS_MaTaiSan + " - " + x.TS_TenTaiSan
            })
            .ToListAsync();

        ViewBag.TaiSans = new SelectList(assets, "TS_ID", "DisplayText", selectedTaiSanId);
    }

    // 1. Danh sách phiếu bảo trì (GET: Admin/BaoTri)
    [HttpGet]
    public async Task<IActionResult> Index(string? keyword, int? taiSanId, string? trangThai)
    {
        var query = context.tblBaoTriTaiSan
            .AsNoTracking()
            .Include(x => x.TaiSan)
            .Include(x => x.NguoiLap)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim().ToLower();
            query = query.Where(x =>
                x.BT_MaPhieu.ToLower().Contains(kw) ||
                (x.BT_DonViBaoTri != null && x.BT_DonViBaoTri.ToLower().Contains(kw)) ||
                (x.TaiSan != null && (x.TaiSan.TS_MaTaiSan.ToLower().Contains(kw) || x.TaiSan.TS_TenTaiSan.ToLower().Contains(kw))));
        }

        if (taiSanId.HasValue)
        {
            query = query.Where(x => x.TS_ID == taiSanId.Value);
        }

        if (!string.IsNullOrWhiteSpace(trangThai))
        {
            query = query.Where(x => x.BT_TrangThai == trangThai);
        }

        ViewBag.Keyword = keyword;
        ViewBag.TaiSanId = taiSanId;
        ViewBag.TrangThai = trangThai;
        ViewBag.TongChiPhi = await query.SumAsync(x => x.BT_ChiPhi ?? 0);

        await LoadDropdownsAsync(taiSanId);

        var items = await query
            .OrderByDescending(x => x.BT_ID)
            .ToListAsync();

        return View(items);
    }

    // 2. Xem chi tiết phiếu bảo trì (GET: Admin/BaoTri/Details/5)
    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var item = await context.tblBaoTriTaiSan
            .AsNoTracking()
            .Include(x => x.TaiSan)
            .Include(x => x.NguoiLap)
            .FirstOrDefaultAsync(x => x.BT_ID == id);

        if (item == null)
        {
            return NotFound();
        }

        return View(item);
    }

    // 3. Giao diện thêm mới (GET: Admin/BaoTri/Create)
    [HttpGet]
    public async Task<IActionResult> Create(int? taiSanId = null)
    {
        await LoadDropdownsAsync(taiSanId);
        return View(new tblBaoTriTaiSan
        {
            TS_ID = taiSanId ?? 0,
            BT_NgayBaoTri = DateOnly.FromDateTime(DateTime.Now),
            BT_TrangThai = "ChoXuLy",
            BT_ChiPhi = 0
        });
    }

    // 4. Xử lý thêm mới (POST: Admin/BaoTri/Create)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(tblBaoTriTaiSan model)
    {
        var currentUserId = HttpContext.Session.GetInt32("TaiKhoanID") ?? 1;
        model.BT_NguoiLapID = currentUserId;
        ModelState.Remove(nameof(model.BT_NguoiLapID));

        if (!string.IsNullOrWhiteSpace(model.BT_MaPhieu))
        {
            var ma = model.BT_MaPhieu.Trim();
            var exists = await context.tblBaoTriTaiSan.AnyAsync(x => x.BT_MaPhieu == ma);
            if (exists)
            {
                ModelState.AddModelError(nameof(model.BT_MaPhieu), "Mã phiếu bảo trì này đã tồn tại.");
            }
        }

        if (model.TS_ID <= 0)
        {
            ModelState.AddModelError(nameof(model.TS_ID), "Vui lòng chọn tài sản cần bảo trì.");
        }

        if (model.BT_NgayBaoTri.HasValue && model.BT_NgayHoanThanh.HasValue &&
            model.BT_NgayHoanThanh.Value < model.BT_NgayBaoTri.Value)
        {
            ModelState.AddModelError(nameof(model.BT_NgayHoanThanh), "Ngày hoàn thành không được nhỏ hơn ngày bảo trì.");
        }

        if (!ModelState.IsValid)
        {
            await LoadDropdownsAsync(model.TS_ID);
            return View(model);
        }

        model.BT_MaPhieu = model.BT_MaPhieu.Trim();
        model.BT_DonViBaoTri = model.BT_DonViBaoTri?.Trim();
        model.BT_LyDo = model.BT_LyDo?.Trim();
        model.BT_NoiDung = model.BT_NoiDung?.Trim();
        model.BT_GhiChu = model.BT_GhiChu?.Trim();
        model.BT_ChiPhi ??= 0;

        context.tblBaoTriTaiSan.Add(model);
        await context.SaveChangesAsync();

        TempData["Success"] = "Tạo phiếu bảo trì tài sản thành công.";
        return RedirectToAction(nameof(Index));
    }

    // 5. Giao diện cập nhật (GET: Admin/BaoTri/Edit/5)
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var item = await context.tblBaoTriTaiSan.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }

        await LoadDropdownsAsync(item.TS_ID);
        return View(item);
    }

    // 6. Xử lý cập nhật (POST: Admin/BaoTri/Edit/5)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, tblBaoTriTaiSan model)
    {
        if (id != model.BT_ID)
        {
            return BadRequest();
        }

        ModelState.Remove(nameof(model.BT_NguoiLapID));

        if (!string.IsNullOrWhiteSpace(model.BT_MaPhieu))
        {
            var ma = model.BT_MaPhieu.Trim();
            var exists = await context.tblBaoTriTaiSan
                .AnyAsync(x => x.BT_MaPhieu == ma && x.BT_ID != model.BT_ID);
            if (exists)
            {
                ModelState.AddModelError(nameof(model.BT_MaPhieu), "Mã phiếu bảo trì này đã tồn tại.");
            }
        }

        if (model.BT_NgayBaoTri.HasValue && model.BT_NgayHoanThanh.HasValue &&
            model.BT_NgayHoanThanh.Value < model.BT_NgayBaoTri.Value)
        {
            ModelState.AddModelError(nameof(model.BT_NgayHoanThanh), "Ngày hoàn thành không được nhỏ hơn ngày bảo trì.");
        }

        if (!ModelState.IsValid)
        {
            await LoadDropdownsAsync(model.TS_ID);
            return View(model);
        }

        var existing = await context.tblBaoTriTaiSan.FindAsync(id);
        if (existing == null)
        {
            return NotFound();
        }

        existing.TS_ID = model.TS_ID;
        existing.BT_MaPhieu = model.BT_MaPhieu.Trim();
        existing.BT_NgayBaoTri = model.BT_NgayBaoTri;
        existing.BT_NgayHoanThanh = model.BT_NgayHoanThanh;
        existing.BT_DonViBaoTri = model.BT_DonViBaoTri?.Trim();
        existing.BT_ChiPhi = model.BT_ChiPhi ?? 0;
        existing.BT_LyDo = model.BT_LyDo?.Trim();
        existing.BT_NoiDung = model.BT_NoiDung?.Trim();
        existing.BT_GhiChu = model.BT_GhiChu?.Trim();
        existing.BT_TrangThai = model.BT_TrangThai;

        if (existing.BT_TrangThai == "HoanThanh" && !existing.BT_NgayHoanThanh.HasValue)
        {
            existing.BT_NgayHoanThanh = DateOnly.FromDateTime(DateTime.Now);
        }

        await context.SaveChangesAsync();

        TempData["Success"] = "Cập nhật phiếu bảo trì thành công.";
        return RedirectToAction(nameof(Index));
    }

    // 7. Xử lý xóa (POST: Admin/BaoTri/Delete/5)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await context.tblBaoTriTaiSan.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }

        context.tblBaoTriTaiSan.Remove(item);
        await context.SaveChangesAsync();

        TempData["Success"] = "Đã xóa phiếu bảo trì thành công.";
        return RedirectToAction(nameof(Index));
    }
}