using System.Data;
using System.Data.Common;
using DoAn.Areas.Admin.Models;
using DoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace DoAn.Areas.Admin.Controllers;

[Area("Admin")]
public class ThanhLyController(DataContext context, ILogger<ThanhLyController> logger) : Controller
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext filterContext, ActionExecutionDelegate next)
    {
        var accountId = HttpContext.Session.GetInt32("TaiKhoanID");
        if (accountId == null)
        {
            filterContext.Result = RedirectToAction("Index", "DangNhap", new { area = "" });
            return;
        }

        try
        {
            var account = await context.tblTaiKhoan.AsNoTracking()
                .Where(x => x.TK_ID == accountId)
                .Select(x => new { x.TK_TrangThai, x.TK_VaiTro })
                .SingleOrDefaultAsync();

            if (account?.TK_TrangThai != true || account.TK_VaiTro != "Admin")
            {
                filterContext.Result = StatusCode(StatusCodes.Status403Forbidden);
                return;
            }

            HttpContext.Items["ThanhLyActorId"] = accountId.Value;
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            logger.LogError(ex, "Không thể kiểm tra quyền truy cập module thanh lý.");
            filterContext.Result = DatabaseUnavailable();
            return;
        }

        var executed = await next();
        if (executed.Exception is Exception dbError && IsDatabaseError(dbError))
        {
            logger.LogError(dbError, "Lỗi CSDL tại {Action}.", filterContext.ActionDescriptor.DisplayName);
            executed.ExceptionHandled = true;
            executed.Result = DatabaseUnavailable();
        }
    }

    private int ActorId => HttpContext.Items["ThanhLyActorId"] is int id ? id : 0;

    [HttpGet]
    public async Task<IActionResult> Index(
        string? keyword = null,
        string? trangThai = null,
        DateOnly? tuNgay = null,
        DateOnly? denNgay = null,
        string sort = "date_desc",
        int page = 1,
        int pageSize = 10)
    {
        keyword = keyword?.Trim();
        trangThai = trangThai?.Trim();
        pageSize = pageSize is 10 or 20 or 50 ? pageSize : 10;
        sort = sort is "date_asc" or "code_asc" or "code_desc" or "value_desc" ? sort : "date_desc";

        if (tuNgay.HasValue && denNgay.HasValue && tuNgay > denNgay)
            ModelState.AddModelError(nameof(denNgay), "Đến ngày phải bằng hoặc sau từ ngày.");

        var allReceipts = context.tblThanhLyTaiSan.AsNoTracking();
        var query = allReceipts;

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(x =>
                x.TL_MaPhieu.Contains(keyword) ||
                (x.TL_LyDo != null && x.TL_LyDo.Contains(keyword)) ||
                (x.TL_PhuongThuc != null && x.TL_PhuongThuc.Contains(keyword)) ||
                x.tblChiTietThanhLies.Any(d => d.TaiSan != null &&
                    (d.TaiSan.TS_MaTaiSan.Contains(keyword) || d.TaiSan.TS_TenTaiSan.Contains(keyword))));
        }

        if (!string.IsNullOrEmpty(trangThai))
            query = query.Where(x => x.TL_TrangThai == trangThai);

        if (tuNgay.HasValue)
            query = query.Where(x => x.TL_NgayThanhLy >= tuNgay);

        if (denNgay.HasValue)
            query = query.Where(x => x.TL_NgayThanhLy <= denNgay);

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling((double)total / pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var ordered = sort switch
        {
            "date_asc" => query.OrderBy(x => x.TL_NgayThanhLy).ThenBy(x => x.TL_ID),
            "code_asc" => query.OrderBy(x => x.TL_MaPhieu).ThenBy(x => x.TL_ID),
            "code_desc" => query.OrderByDescending(x => x.TL_MaPhieu).ThenBy(x => x.TL_ID),
            "value_desc" => query.OrderByDescending(x => x.TL_TongGiaTri).ThenByDescending(x => x.TL_ID),
            _ => query.OrderByDescending(x => x.TL_NgayThanhLy).ThenByDescending(x => x.TL_ID)
        };

        var model = new ThanhLyIndexViewModel
        {
            Keyword = keyword,
            TrangThai = trangThai,
            TuNgay = tuNgay,
            DenNgay = denNgay,
            Sort = sort,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TongPhieu = await allReceipts.CountAsync(),
            SoChoDuyet = await allReceipts.CountAsync(x => x.TL_TrangThai == ThanhLyStates.ChoDuyet),
            SoDaDuyet = await allReceipts.CountAsync(x => x.TL_TrangThai == ThanhLyStates.DaDuyet),
            TongGiaTriDaDuyet = await allReceipts
                .Where(x => x.TL_TrangThai == ThanhLyStates.DaDuyet)
                .SumAsync(x => x.TL_TongGiaTri ?? 0m),
            Items = await ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new ThanhLyRowViewModel
                {
                    Id = x.TL_ID,
                    MaPhieu = x.TL_MaPhieu,
                    NgayThanhLy = x.TL_NgayThanhLy,
                    NguoiLap = x.NguoiLap != null ? x.NguoiLap.TK_HoTen : null,
                    PhuongThuc = x.TL_PhuongThuc,
                    LyDo = x.TL_LyDo,
                    TongGiaTri = x.TL_TongGiaTri ?? 0m,
                    TrangThai = x.TL_TrangThai,
                    GhiChu = x.TL_GhiChu,
                    SoTaiSan = x.tblChiTietThanhLies.Count()
                })
                .ToListAsync()
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id)
    {
        var model = await LoadDetailsAsync(id);
        return model == null ? NotFound() : View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Print([FromRoute] int id)
    {
        var model = await LoadDetailsAsync(id);
        return model == null ? NotFound() : View("InPhieu", model);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new ThanhLyFormViewModel
        {
            MaPhieu = await GenerateNextCodeAsync(),
            NgayThanhLy = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),
            PhuongThuc = "Bán thanh lý",
            AvailableAssets = await EligibleAssetsQuery().ToListAsync()
        };
        return View("Form", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ThanhLyFormViewModel model)
    {
        NormalizeInput(model);
        ValidateFormLines(model);

        if (!ModelState.IsValid)
        {
            model.AvailableAssets = await EligibleAssetsQuery().ToListAsync();
            return View("Form", model);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var codeExists = await context.tblThanhLyTaiSan.AsNoTracking()
                .AnyAsync(x => x.TL_MaPhieu == model.MaPhieu);
            Ensure(!codeExists, $"Mã phiếu '{model.MaPhieu}' đã tồn tại.");

            foreach (var assetId in model.Lines.Select(x => x.TaiSanId).Distinct().Order())
            {
                await LockRow("asset", assetId);
                await ValidateAssetEligibleAsync(assetId, null);
            }

            var doc = new tblThanhLyTaiSan
            {
                TL_MaPhieu = model.MaPhieu,
                TL_NgayThanhLy = model.NgayThanhLy ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),
                TL_NguoiLapID = ActorId,
                TL_PhuongThuc = model.PhuongThuc,
                TL_LyDo = model.LyDo,
                TL_GhiChu = model.GhiChu,
                TL_TrangThai = ThanhLyStates.ChoDuyet,
                TL_TongGiaTri = model.Lines.Sum(x => x.GiaTriThanhLy),
                tblChiTietThanhLies = model.Lines.Select(x => new tblChiTietThanhLy
                {
                    TS_ID = x.TaiSanId,
                    CTTL_GiaTriThanhLy = x.GiaTriThanhLy,
                    CTTL_TinhTrang = x.TinhTrang,
                    CTTL_GhiChu = x.GhiChu
                }).ToList()
            };

            context.tblThanhLyTaiSan.Add(doc);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = $"Đã tạo phiếu thanh lý {doc.TL_MaPhieu}.";
            return RedirectToAction(nameof(Details), new { id = doc.TL_ID });
        }
        catch (WorkflowFailure ex)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", ex.Message);
            model.AvailableAssets = await EligibleAssetsQuery().ToListAsync();
            return View("Form", model);
        }
        catch (Exception ex) when (SqlError(ex) == 1062)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", "Mã phiếu hoặc tài sản trong phiếu bị trùng do thao tác đồng thời. Vui lòng thử lại.");
            model.AvailableAssets = await EligibleAssetsQuery().ToListAsync();
            return View("Form", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id)
    {
        var doc = await context.tblThanhLyTaiSan.AsNoTracking()
            .Include(x => x.tblChiTietThanhLies)
            .SingleOrDefaultAsync(x => x.TL_ID == id);

        if (doc == null) return NotFound();
        if (doc.TL_TrangThai != ThanhLyStates.ChoDuyet)
        {
            TempData["Error"] = "Chỉ được chỉnh sửa phiếu đang ở trạng thái Chờ duyệt.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var model = new ThanhLyFormViewModel
        {
            Id = doc.TL_ID,
            MaPhieu = doc.TL_MaPhieu,
            NgayThanhLy = doc.TL_NgayThanhLy,
            PhuongThuc = doc.TL_PhuongThuc,
            LyDo = doc.TL_LyDo ?? "",
            GhiChu = doc.TL_GhiChu,
            Lines = doc.tblChiTietThanhLies.OrderBy(x => x.CTTL_ID).Select(x => new ThanhLyLineInput
            {
                TaiSanId = x.TS_ID,
                GiaTriThanhLy = x.CTTL_GiaTriThanhLy ?? 0m,
                TinhTrang = x.CTTL_TinhTrang,
                GhiChu = x.CTTL_GhiChu
            }).ToList(),
            AvailableAssets = await LoadEditAssetsAsync(id, doc.tblChiTietThanhLies.Select(x => x.TS_ID))
        };

        return View("Form", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromRoute] int id, ThanhLyFormViewModel model)
    {
        model.Id = id;
        NormalizeInput(model);
        ValidateFormLines(model);

        if (!ModelState.IsValid)
        {
            model.AvailableAssets = await LoadEditAssetsAsync(id, model.Lines.Select(x => x.TaiSanId));
            return View("Form", model);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await LockRow("disposal", id);
            var doc = await context.tblThanhLyTaiSan
                .Include(x => x.tblChiTietThanhLies)
                .SingleOrDefaultAsync(x => x.TL_ID == id);

            Ensure(doc != null, "Không tìm thấy phiếu thanh lý.");
            Ensure(doc!.TL_TrangThai == ThanhLyStates.ChoDuyet, "Chỉ được sửa phiếu đang ở trạng thái Chờ duyệt.");

            var duplicateCode = await context.tblThanhLyTaiSan.AsNoTracking()
                .AnyAsync(x => x.TL_MaPhieu == model.MaPhieu && x.TL_ID != id);
            Ensure(!duplicateCode, $"Mã phiếu '{model.MaPhieu}' đã tồn tại.");

            var allAssetIds = doc.tblChiTietThanhLies.Select(x => x.TS_ID)
                .Concat(model.Lines.Select(x => x.TaiSanId))
                .Distinct().Order();
            foreach (var assetId in allAssetIds)
                await LockRow("asset", assetId);

            foreach (var line in model.Lines)
                await ValidateAssetEligibleAsync(line.TaiSanId, id);

            doc.TL_MaPhieu = model.MaPhieu;
            doc.TL_NgayThanhLy = model.NgayThanhLy ?? doc.TL_NgayThanhLy ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
            doc.TL_PhuongThuc = model.PhuongThuc;
            doc.TL_LyDo = model.LyDo;
            doc.TL_GhiChu = model.GhiChu;
            doc.TL_TongGiaTri = model.Lines.Sum(x => x.GiaTriThanhLy);

            // Xóa dòng cũ và lưu trước trong transaction để tránh đụng UNIQUE KEY (TL_ID, TS_ID)
            context.tblChiTietThanhLy.RemoveRange(doc.tblChiTietThanhLies);
            await context.SaveChangesAsync();

            foreach (var line in model.Lines)
            {
                context.tblChiTietThanhLy.Add(new tblChiTietThanhLy
                {
                    TL_ID = doc.TL_ID,
                    TS_ID = line.TaiSanId,
                    CTTL_GiaTriThanhLy = line.GiaTriThanhLy,
                    CTTL_TinhTrang = line.TinhTrang,
                    CTTL_GhiChu = line.GhiChu
                });
            }

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = $"Đã cập nhật phiếu thanh lý {doc.TL_MaPhieu}.";
            return RedirectToAction(nameof(Details), new { id = doc.TL_ID });
        }
        catch (WorkflowFailure ex)
        {
            await transaction.RollbackAsync();
            ModelState.AddModelError("", ex.Message);
            model.AvailableAssets = await LoadEditAssetsAsync(id, model.Lines.Select(x => x.TaiSanId));
            return View("Form", model);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve([FromRoute] int id)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await LockRow("disposal", id);
            var doc = await context.tblThanhLyTaiSan
                .Include(x => x.tblChiTietThanhLies)
                .SingleOrDefaultAsync(x => x.TL_ID == id);

            Ensure(doc != null, "Không tìm thấy phiếu thanh lý.");
            Ensure(doc!.TL_TrangThai == ThanhLyStates.ChoDuyet, "Phiếu không ở trạng thái Chờ duyệt.");
            Ensure(doc.tblChiTietThanhLies.Count > 0, "Phiếu thanh lý phải có ít nhất 1 tài sản.");

            var assetIds = doc.tblChiTietThanhLies.Select(x => x.TS_ID).Distinct().Order().ToList();
            foreach (var assetId in assetIds)
            {
                await LockRow("asset", assetId);
                await ValidateAssetEligibleAsync(assetId, id);
            }

            var assets = await context.tblTaiSan
                .Where(x => assetIds.Contains(x.TS_ID))
                .ToDictionaryAsync(x => x.TS_ID);

            foreach (var line in doc.tblChiTietThanhLies)
            {
                var asset = assets[line.TS_ID];
                asset.TS_TrangThai = "DaThanhLy";
                asset.TS_DangQuanLy = false;
            }

            doc.TL_TrangThai = ThanhLyStates.DaDuyet;
            doc.TL_TongGiaTri = doc.tblChiTietThanhLies.Sum(x => x.CTTL_GiaTriThanhLy ?? 0m);

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = $"Đã duyệt phiếu thanh lý {doc.TL_MaPhieu} và chuyển trạng thái {assetIds.Count} tài sản sang Đã thanh lý.";
        }
        catch (WorkflowFailure ex)
        {
            await transaction.RollbackAsync();
            TempData["Error"] = ex.Message;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogWarning(ex, "Lỗi khi duyệt phiếu thanh lý {Id}.", id);
            TempData["Error"] = "Không thể duyệt phiếu thanh lý. Vui lòng tải lại và thử lại.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel([FromRoute] int id, string? lyDoHuy)
    {
        lyDoHuy = lyDoHuy?.Trim();
        if (string.IsNullOrWhiteSpace(lyDoHuy) || lyDoHuy.Length > 1000)
        {
            TempData["Error"] = "Vui lòng nhập lý do hủy phiếu (tối đa 1000 ký tự).";
            return RedirectToAction(nameof(Details), new { id });
        }

        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            await LockRow("disposal", id);
            var doc = await context.tblThanhLyTaiSan.SingleOrDefaultAsync(x => x.TL_ID == id);
            Ensure(doc != null, "Không tìm thấy phiếu thanh lý.");
            Ensure(doc!.TL_TrangThai == ThanhLyStates.ChoDuyet, "Chỉ được hủy phiếu đang ở trạng thái Chờ duyệt.");

            doc.TL_TrangThai = ThanhLyStates.Huy;
            var cancelNote = $"[Hủy phiếu: {lyDoHuy}]";
            doc.TL_GhiChu = string.IsNullOrWhiteSpace(doc.TL_GhiChu)
                ? cancelNote
                : $"{doc.TL_GhiChu}\n{cancelNote}";

            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = $"Đã hủy phiếu thanh lý {doc.TL_MaPhieu}.";
        }
        catch (WorkflowFailure ex)
        {
            await transaction.RollbackAsync();
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<ThanhLyDetailsViewModel?> LoadDetailsAsync(int id)
    {
        var receipt = await context.tblThanhLyTaiSan.AsNoTracking()
            .Where(x => x.TL_ID == id)
            .Select(x => new ThanhLyRowViewModel
            {
                Id = x.TL_ID,
                MaPhieu = x.TL_MaPhieu,
                NgayThanhLy = x.TL_NgayThanhLy,
                NguoiLap = x.NguoiLap != null ? x.NguoiLap.TK_HoTen : null,
                PhuongThuc = x.TL_PhuongThuc,
                LyDo = x.TL_LyDo,
                TongGiaTri = x.TL_TongGiaTri ?? 0m,
                TrangThai = x.TL_TrangThai,
                GhiChu = x.TL_GhiChu,
                SoTaiSan = x.tblChiTietThanhLies.Count()
            })
            .SingleOrDefaultAsync();

        if (receipt == null) return null;

        var items = await context.tblChiTietThanhLy.AsNoTracking()
            .Where(x => x.TL_ID == id)
            .OrderBy(x => x.CTTL_ID)
            .Select(x => new ThanhLyLineViewModel
            {
                Id = x.CTTL_ID,
                TaiSanId = x.TS_ID,
                MaTaiSan = x.TaiSan != null ? x.TaiSan.TS_MaTaiSan : "#" + x.TS_ID,
                TenTaiSan = x.TaiSan != null ? x.TaiSan.TS_TenTaiSan : "Không tìm thấy tài sản",
                DanhMuc = x.TaiSan != null && x.TaiSan.tblDanhMucTaiSan != null ? x.TaiSan.tblDanhMucTaiSan.DMTS_TenDanhMuc : null,
                TinhTrangHienTai = x.TaiSan != null ? x.TaiSan.TS_TinhTrang : null,
                TrangThaiHienTai = x.TaiSan != null ? x.TaiSan.TS_TrangThai : null,
                GiaTriThanhLy = x.CTTL_GiaTriThanhLy ?? 0m,
                TinhTrangThanhLy = x.CTTL_TinhTrang,
                GhiChu = x.CTTL_GhiChu
            })
            .ToListAsync();

        return new ThanhLyDetailsViewModel { Phieu = receipt, Items = items };
    }

    private IQueryable<ThanhLyAssetOption> EligibleAssetsQuery(int? excludeReceiptId = null)
    {
        var excludeId = excludeReceiptId ?? 0;
        return context.tblTaiSan.AsNoTracking()
            .Where(x => x.TS_DangQuanLy
                && x.TS_TrangThai != "DaCapPhat"
                && x.TS_TrangThai != "DaThanhLy"
                && !x.tblChiTietCapPhats.Any(d => d.CTCP_TrangThai == null || d.CTCP_TrangThai == "DangHieuLuc")
                && !x.tblBaoTriTaiSans.Any(d => d.BT_TrangThai != "DaHoanThanh")
                && !x.tblChiTietKiemKes.Any(d => d.KiemKe != null && d.KiemKe.KK_TrangThai == "DangKiemKe")
                && !x.tblChiTietThanhLies.Any(d => d.TL_ID != excludeId && d.ThanhLy != null && d.ThanhLy.TL_TrangThai != ThanhLyStates.Huy))
            .OrderBy(x => x.TS_MaTaiSan)
            .Select(x => new ThanhLyAssetOption
            {
                Id = x.TS_ID,
                MaTaiSan = x.TS_MaTaiSan,
                TenTaiSan = x.TS_TenTaiSan,
                DanhMuc = x.tblDanhMucTaiSan != null ? x.tblDanhMucTaiSan.DMTS_TenDanhMuc : null,
                TinhTrang = x.TS_TinhTrang,
                TrangThai = x.TS_TrangThai
            });
    }

    private async Task<List<ThanhLyAssetOption>> LoadEditAssetsAsync(int receiptId, IEnumerable<int> selectedIds)
    {
        var eligible = await EligibleAssetsQuery(receiptId).ToListAsync();
        var eligibleSet = eligible.Select(x => x.Id).ToHashSet();
        var missingIds = selectedIds.Distinct().Where(x => !eligibleSet.Contains(x)).ToList();

        if (missingIds.Count > 0)
        {
            var currentSelected = await context.tblTaiSan.AsNoTracking()
                .Where(x => missingIds.Contains(x.TS_ID))
                .Select(x => new ThanhLyAssetOption
                {
                    Id = x.TS_ID,
                    MaTaiSan = x.TS_MaTaiSan,
                    TenTaiSan = x.TS_TenTaiSan,
                    DanhMuc = x.tblDanhMucTaiSan != null ? x.tblDanhMucTaiSan.DMTS_TenDanhMuc : null,
                    TinhTrang = x.TS_TinhTrang,
                    TrangThai = x.TS_TrangThai
                })
                .ToListAsync();
            eligible.AddRange(currentSelected);
        }

        return eligible.OrderBy(x => x.MaTaiSan).ToList();
    }

    private async Task ValidateAssetEligibleAsync(int assetId, int? excludeReceiptId)
    {
        var excludeId = excludeReceiptId ?? 0;
        var asset = await context.tblTaiSan.AsNoTracking().SingleOrDefaultAsync(x => x.TS_ID == assetId);

        Ensure(asset != null, $"Không tìm thấy tài sản #{assetId}.");
        Ensure(asset!.TS_DangQuanLy && asset.TS_TrangThai != "DaThanhLy",
            $"Tài sản {asset.TS_MaTaiSan} đã thanh lý hoặc ngừng quản lý.");
        Ensure(asset.TS_TrangThai != "DaCapPhat"
            && !await context.tblChiTietCapPhat.AnyAsync(d => d.TS_ID == assetId && (d.CTCP_TrangThai == null || d.CTCP_TrangThai == "DangHieuLuc")),
            $"Tài sản {asset.TS_MaTaiSan} đang được cấp phát hoặc chưa đối soát; cần thu hồi trước khi thanh lý.");
        Ensure(!await context.tblBaoTriTaiSan.AnyAsync(d => d.TS_ID == assetId && d.BT_TrangThai != "DaHoanThanh"),
            $"Tài sản {asset.TS_MaTaiSan} đang có phiếu bảo trì chưa hoàn thành.");
        Ensure(!await context.tblChiTietKiemKe.AnyAsync(d => d.TS_ID == assetId && d.KiemKe != null && d.KiemKe.KK_TrangThai == "DangKiemKe"),
            $"Tài sản {asset.TS_MaTaiSan} đang thuộc đợt kiểm kê chưa hoàn tất.");
        Ensure(!await context.tblChiTietThanhLy.AnyAsync(d => d.TS_ID == assetId && d.TL_ID != excludeId && d.ThanhLy != null && d.ThanhLy.TL_TrangThai != ThanhLyStates.Huy),
            $"Tài sản {asset.TS_MaTaiSan} đã nằm trong một phiếu thanh lý khác.");
    }

    private async Task<string> GenerateNextCodeAsync()
    {
        var maxId = await context.tblThanhLyTaiSan.AsNoTracking()
            .Select(x => (int?)x.TL_ID).MaxAsync() ?? 0;
        var next = maxId + 1;
        string code;
        do
        {
            code = $"TL{next:D3}";
            next++;
        } while (await context.tblThanhLyTaiSan.AsNoTracking().AnyAsync(x => x.TL_MaPhieu == code));
        return code;
    }

    private static void NormalizeInput(ThanhLyFormViewModel model)
    {
        model.MaPhieu = (model.MaPhieu ?? "").Trim().ToUpperInvariant();
        model.PhuongThuc = model.PhuongThuc?.Trim();
        model.LyDo = (model.LyDo ?? "").Trim();
        model.GhiChu = model.GhiChu?.Trim();
        model.Lines = (model.Lines ?? [])
            .Where(x => x != null && x.TaiSanId > 0)
            .Select(x => new ThanhLyLineInput
            {
                TaiSanId = x.TaiSanId,
                GiaTriThanhLy = x.GiaTriThanhLy,
                TinhTrang = x.TinhTrang?.Trim(),
                GhiChu = x.GhiChu?.Trim()
            })
            .ToList();
    }

    private void ValidateFormLines(ThanhLyFormViewModel model)
    {
        if (model.Lines.Count == 0)
            ModelState.AddModelError("", "Vui lòng chọn ít nhất 1 tài sản để thanh lý.");
        else if (model.Lines.Count > 100)
            ModelState.AddModelError("", "Mỗi phiếu thanh lý tối đa 100 tài sản.");
        else if (model.Lines.Select(x => x.TaiSanId).Distinct().Count() != model.Lines.Count)
            ModelState.AddModelError("", "Không được chọn trùng tài sản trong cùng một phiếu.");
    }

    private async Task LockRow(string table, int id)
    {
        FormattableString sql = table switch
        {
            "asset" => $"SELECT TS_ID AS Value FROM tbltaisan WHERE TS_ID={id} FOR UPDATE",
            "disposal" => $"SELECT TL_ID AS Value FROM tblthanhlytaisan WHERE TL_ID={id} FOR UPDATE",
            _ => throw new InvalidOperationException("Unknown lock target")
        };
        Ensure((await context.Database.SqlQuery<int>(sql).ToListAsync()).Count == 1,
            "Dữ liệu liên quan không tồn tại. Hãy tải lại trang.");
    }

    private static bool IsDatabaseError(Exception error) =>
        error is DbException or InvalidOperationException { InnerException: DbException };

    private static int? SqlError(Exception error)
    {
        for (Exception? cur = error; cur != null; cur = cur.InnerException)
            if (cur is MySqlException mysql) return mysql.Number;
        return null;
    }

    private ObjectResult DatabaseUnavailable() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        "Không thể kết nối cơ sở dữ liệu. Vui lòng thử lại sau.");

    private static void Ensure(bool valid, string message)
    {
        if (!valid) throw new WorkflowFailure(message);
    }

    private sealed class WorkflowFailure(string message) : Exception(message);
}