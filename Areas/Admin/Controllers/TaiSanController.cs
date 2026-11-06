using DoAn.Areas.Admin.Models;
using DoAn.Models;
using DoAn.Services.Assets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace DoAn.Areas.Admin.Controllers;

[Area("Admin")]
public class TaiSanController(DataContext context, ILogger<TaiSanController> logger) : Controller
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
    public async Task<IActionResult> Index(string? keyword, int? danhMucId, string? trangThai,
        string? tinhTrang = null, string sort = "created_desc", int page = 1, int pageSize = 10)
    {
        keyword = keyword?.Trim();
        pageSize = pageSize is 10 or 20 or 50 ? pageSize : 10;
        sort = AssetQueries.Sorts.ContainsKey(sort ?? "") ? sort! : "created_desc";
        ModelState.Remove(nameof(pageSize));
        ModelState.Remove(nameof(sort));
        var query = AssetQueries.Filter(context.tblTaiSan.AsNoTracking(), keyword, danhMucId, tinhTrang, trangThai);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        return View(new TaiSanIndex
        {
            Items = await AssetQueries.Sort(query.Include(x => x.tblDanhMucTaiSan), sort)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(),
            Keyword = keyword, DanhMucId = danhMucId, TrangThai = trangThai, TinhTrang = tinhTrang,
            Sort = sort, Page = page, PageSize = pageSize, Total = total,
            TongNguyenGia = await query.SumAsync(x => (decimal?)x.TS_NguyenGia) ?? 0,
            DanhMucs = await Categories()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id)
    {
        var asset = await context.tblTaiSan.AsNoTracking().Include(x => x.tblDanhMucTaiSan).FirstOrDefaultAsync(x => x.TS_ID == id);
        return asset == null ? NotFound() : View(asset);
    }

    [HttpGet]
    public async Task<IActionResult> Create() => View(new AssetCreateViewModel { DanhMucs = await Categories(activeOnly: true) });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AssetCreateViewModel model)
    {
        model.Ma = model.Ma?.Trim() ?? "";
        if (model.Ma.Length == 0) ModelState.AddModelError(nameof(model.Ma), "Vui lòng nhập mã tài sản.");
        if (await context.tblTaiSan.AnyAsync(x => x.TS_MaTaiSan == model.Ma))
            ModelState.AddModelError(nameof(model.Ma), $"Mã tài sản {model.Ma} đã tồn tại.");
        await ValidateFields(model);
        if (ModelState.IsValid)
        {
            var asset = AssetProfile.Create(model);
            context.tblTaiSan.Add(asset);
            if (await Save(model.Ma))
            {
                TempData["Success"] = "Thêm tài sản thành công.";
                return RedirectToAction(nameof(Details), new { id = asset.TS_ID });
            }
        }
        model.DanhMucs = await Categories(activeOnly: true);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id)
    {
        var asset = await context.tblTaiSan.FindAsync(id);
        if (asset == null) return NotFound();
        var model = new AssetEditViewModel
        {
            Ten = asset.TS_TenTaiSan, DanhMucId = asset.DMTS_ID, NgayMua = asset.TS_NgayMua,
            NguyenGia = asset.TS_NguyenGia, HanBaoHanh = asset.TS_HanBaoHanh,
            TinhTrang = AssetConditions.Normalize(asset.TS_TinhTrang), MoTa = asset.TS_MoTa, GhiChu = asset.TS_GhiChu
        };
        await PrepareEdit(model, asset);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromRoute] int id, AssetEditViewModel model)
    {
        var asset = await context.tblTaiSan.FindAsync(id);
        if (asset == null) return NotFound();
        await ValidateFields(model);
        if (ModelState.IsValid)
        {
            AssetProfile.Update(model, asset);
            if (await Save())
            {
                TempData["Success"] = "Cập nhật tài sản thành công.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }
        await PrepareEdit(model, asset);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Delete([FromRoute] int id)
    {
        var asset = await context.tblTaiSan.AsNoTracking().Include(x => x.tblDanhMucTaiSan).FirstOrDefaultAsync(x => x.TS_ID == id);
        if (asset == null) return NotFound();
        ViewBag.HasHistory = await AssetHistory.HasHistory(context, id);
        return View(asset);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed([FromRoute] int id)
    {
        var asset = await context.tblTaiSan.Include(x => x.tblDanhMucTaiSan).FirstOrDefaultAsync(x => x.TS_ID == id);
        if (asset == null) return NotFound();
        var hasHistory = await AssetHistory.HasHistory(context, id);
        ViewBag.HasHistory = hasHistory;
        if (hasHistory)
        {
            ModelState.AddModelError("", "Tài sản đã phát sinh nghiệp vụ nên không thể xóa.");
            return View("Delete", asset);
        }
        context.tblTaiSan.Remove(asset);
        if (!await Save()) return View("Delete", asset);
        TempData["Success"] = "Xóa tài sản thành công.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<SelectListItem>> Categories(bool activeOnly = false, int? currentId = null) =>
        await context.tblDanhMucTaiSan.AsNoTracking()
            .Where(x => !activeOnly || x.DMTS_TrangThai == true || x.DMTS_ID == currentId)
            .OrderBy(x => x.DMTS_TenDanhMuc)
            .Select(x => new SelectListItem(x.DMTS_TenDanhMuc + (x.DMTS_TrangThai == true ? "" : " (ngừng sử dụng)"),
                x.DMTS_ID.ToString())).ToListAsync();

    private async Task ValidateFields(AssetFieldsViewModel model)
    {
        model.Ten = model.Ten?.Trim() ?? "";
        if (model.Ten.Length == 0) ModelState.AddModelError(nameof(model.Ten), "Vui lòng nhập tên tài sản.");
        if (!await context.tblDanhMucTaiSan.AnyAsync(x => x.DMTS_ID == model.DanhMucId && x.DMTS_TrangThai == true))
            ModelState.AddModelError(nameof(model.DanhMucId), "Danh mục không tồn tại hoặc đã ngừng sử dụng. Vui lòng chọn danh mục đang hoạt động.");
    }

    private async Task PrepareEdit(AssetEditViewModel model, tblTaiSan asset)
    {
        model.AssetId = asset.TS_ID;
        model.Ma = asset.TS_MaTaiSan;
        model.TrangThai = asset.TS_TrangThai;
        model.DanhMucs = await Categories(activeOnly: true, currentId: asset.DMTS_ID);
        // Không hiển thị dữ liệu giả được gửi lên thay cho thông tin từ database.
        ModelState.Remove(nameof(model.AssetId));
        ModelState.Remove(nameof(model.Ma));
        ModelState.Remove(nameof(model.TrangThai));
    }

    private async Task<bool> Save(string? createCode = null)
    {
        try { await context.SaveChangesAsync(); return true; }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Tài sản đã bị xóa hoặc đổi trạng thái trong lúc lưu.");
            ModelState.AddModelError("", "Dữ liệu đã thay đổi hoặc bị xóa. Vui lòng tải lại trang.");
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Không thể lưu thay đổi tài sản.");
            if (createCode != null && ex.InnerException is MySqlException { Number: 1062 })
                ModelState.AddModelError("Ma", $"Mã tài sản {createCode} đã tồn tại.");
            else
                ModelState.AddModelError("", "Không thể lưu do dữ liệu liên quan đã thay đổi. Vui lòng tải lại trang và thử lại.");
        }
        return false;
    }
}
