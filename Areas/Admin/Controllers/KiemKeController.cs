using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoAn.Areas.Admin.Models;
using DoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace DoAn.Areas.Admin.Controllers;

[Area("Admin")]
public class KiemKeController(DataContext db, ILogger<KiemKeController> logger) : Controller
{
    private sealed record Actor(int Id, string Name, string Role)
    {
        public bool Manager => Role is "Admin" or "QuanLyTaiSan";
    }
    private sealed class InventoryFailure(string message, int status = 409) : Exception(message)
    {
        public int Status { get; } = status;
    }
    private Actor? viewer;
    private bool Html => Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    private static string Key() => Guid.NewGuid().ToString("D");
    private static void Require(bool condition, string message, int status = 409) { if (!condition) throw new InventoryFailure(message, status); }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Normal(string? value) => Regex.Replace((value ?? "").Normalize().Trim(), @"\s+", " ").ToUpperInvariant();
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool DatabaseError(Exception ex) => ex is DbException or DbUpdateException || ex.InnerException is DbException;

    #region Quyền truy cập và giao dịch
    public override async Task OnActionExecutionAsync(ActionExecutingContext filter, ActionExecutionDelegate next)
    {
        try
        {
            viewer = await ReadActor();
            if (viewer == null)
            {
                filter.Result = HttpContext.Session.GetInt32("TaiKhoanID") == null
                    ? RedirectToAction("Index", "DangNhap", new { area = "" }) : StatusCode(403);
                return;
            }
            HttpContext.Items["KiemKeRestricted"] = viewer.Role != "Admin";
            ViewData["KiemKeCanManage"] = viewer.Manager;
            if (!await SchemaReady()) { filter.Result = Unavailable(); return; }
            var executed = await next();
            if (executed.Exception is Exception error && DatabaseError(error))
            {
                logger.LogError(error, "Không thể thực hiện kiểm kê.");
                executed.ExceptionHandled = true; executed.Result = Unavailable(databaseFailure: true);
            }
        }
        catch (Exception ex) when (DatabaseError(ex))
        {
            logger.LogError(ex, "Không thể xác minh quyền/schema kiểm kê."); filter.Result = Unavailable(databaseFailure: true);
        }
    }
    private async Task<Actor?> ReadActor()
    {
        var id = HttpContext.Session.GetInt32("TaiKhoanID");
        if (!id.HasValue) return null;
        return await db.tblTaiKhoan.AsNoTracking().Where(x => x.TK_ID == id && x.TK_TrangThai &&
            (x.TK_VaiTro == "Admin" || x.TK_VaiTro == "QuanLyTaiSan" || x.TK_VaiTro == "NhanVien"))
            .Select(x => new Actor(x.TK_ID, x.TK_HoTen, x.TK_VaiTro)).SingleOrDefaultAsync();
    }
    private async Task<bool> SchemaReady()
    {
        if (!db.Database.IsRelational()) return true;
        var count = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND ((TABLE_NAME='tblkiemketaisan' AND COLUMN_NAME IN ('KK_LaDuLieuCu','KK_PhienBan','KK_DotGocID')) OR (TABLE_NAME='tblchitietkiemke' AND COLUMN_NAME IN ('CTKK_PhienBan','CTKK_DauVetNguon','CTKK_BangChung','CTKK_XacNhanCanCu')) OR (TABLE_NAME='tblsukienkiemke' AND COLUMN_NAME='SKK_ID'))").SingleAsync();
        return count == 8;
    }
    private IActionResult Unavailable(bool databaseFailure = false)
    {
        // Không để component menu truy vấn lại database vừa mất kết nối khi render trang lỗi.
        if (databaseFailure) HttpContext.Items["KiemKeRestricted"] = true;
        Response.StatusCode = 503;
        return View("ChuaSanSang", viewer?.Manager == true);
    }
    private IQueryable<tblKiemKeTaiSan> Scope(IQueryable<tblKiemKeTaiSan> query, Actor actor) =>
        actor.Manager ? query : query.Where(x => x.KK_NguoiKiemKeID == actor.Id);
    private async Task<tblKiemKeTaiSan?> Visible(int id)
    {
        var actor = await ReadActor(); if (actor == null) return null;
        return await Scope(db.tblKiemKeTaiSan.AsNoTracking(), actor).Include(x => x.PhongBan).Include(x => x.NguoiKiemKe)
            .Include(x => x.NguoiTao).Include(x => x.NguoiDuyet).Include(x => x.DotGoc).SingleOrDefaultAsync(x => x.KK_ID == id);
    }
    private static string Snapshot(object value) => JsonSerializer.Serialize(value.GetType().GetProperties()
        .Where(p => p.PropertyType == typeof(string) || p.PropertyType.IsValueType).ToDictionary(p => p.Name, p => p.GetValue(value)));
    private async Task Lock(string table, int id)
    {
        FormattableString sql = table switch
        {
            "dot" => $"SELECT KK_ID AS Value FROM tblkiemketaisan WHERE KK_ID={id} FOR UPDATE",
            "dong" => $"SELECT CTKK_ID AS Value FROM tblchitietkiemke WHERE CTKK_ID={id} FOR UPDATE",
            "account" => $"SELECT TK_ID AS Value FROM tbltaikhoan WHERE TK_ID={id} FOR UPDATE",
            "department" => $"SELECT PB_ID AS Value FROM tblphongban WHERE PB_ID={id} FOR UPDATE",
            _ => throw new InvalidOperationException("Unknown inventory lock")
        };
        Require((await db.Database.SqlQuery<int>(sql).ToListAsync()).Count == 1, "Dữ liệu không tồn tại hoặc đã thay đổi.", 404);
    }
    private async Task<IActionResult> Write(string action, int? id, int? lineId, KiemKeRequest input,
        Func<Actor, tblKiemKeTaiSan?, tblChiTietKiemKe?, Task<(tblKiemKeTaiSan Doc, tblChiTietKiemKe? Line)>> operation, bool starting = false)
    {
        var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(input, new(input), errors, true) || !ModelState.IsValid)
            return BadRequest(string.Join(" ", errors.Select(x => x.ErrorMessage).Append("Kiểm tra lại dữ liệu biểu mẫu.")));
        if (!Guid.TryParseExact(input.RequestKey, "D", out var requestGuid)) return BadRequest("Mã yêu cầu không hợp lệ.");
        input.RequestKey = requestGuid.ToString("D");
        var signature = Digest(JsonSerializer.Serialize(input, input.GetType()));
        if (!db.Database.IsRelational()) return StatusCode(503, "Thao tác kiểm kê yêu cầu database hỗ trợ transaction và khóa.");
        var actor = await ReadActor(); if (actor == null) return StatusCode(403);
        if (action is "Tao" or "Sua" or "BatDau" or "Huy" or "Duyet" or "TraLai" or "CapNhatXuLy" or "DoiNguoiKiemKe")
            if (!actor.Manager) return StatusCode(403);
        string? namedLock = null;
        IDbContextTransaction? transaction = null;
        var commitStarted = false;
        try
        {
            await db.Database.OpenConnectionAsync();
            if (starting)
            {
                namedLock = "qlts_kiemke_start_" + Digest(db.Database.GetDbConnection().Database)[..24];
                var acquired = await db.Database.SqlQuery<int?>($"SELECT GET_LOCK({namedLock},5) AS Value").SingleAsync();
                Require(acquired == 1, "Có người đang bắt đầu một đợt kiểm kê. Hãy tải lại và thử lại.");
            }
            transaction = await db.Database.BeginTransactionAsync(starting ? IsolationLevel.RepeatableRead : IsolationLevel.ReadCommitted);
            await Lock("account", actor.Id);
            actor = await ReadActor(); Require(actor != null, "Tài khoản không còn quyền hoạt động.", 403);
            if (action is "Tao" or "Sua" or "BatDau" or "Huy" or "Duyet" or "TraLai" or "CapNhatXuLy" or "DoiNguoiKiemKe") Require(actor!.Manager, "Không đủ quyền.", 403);
            if (lineId.HasValue)
                id = await db.tblChiTietKiemKe.AsNoTracking().Where(x => x.CTKK_ID == lineId).Select(x => (int?)x.KK_ID).SingleOrDefaultAsync();
            tblKiemKeTaiSan? doc = null; tblChiTietKiemKe? line = null;
            if (id.HasValue)
            {
                await Lock("dot", id.Value);
                doc = await db.tblKiemKeTaiSan.SingleAsync(x => x.KK_ID == id);
                Require(actor!.Manager || doc.KK_NguoiKiemKeID == actor.Id, "Không tìm thấy đợt được phân công.", 404);
            }
            else Require(action == "Tao", "Không tìm thấy dữ liệu kiểm kê.", 404);
            var prior = await db.tblSuKienKiemKe.AsNoTracking().SingleOrDefaultAsync(x => x.SKK_RequestKey == input.RequestKey);
            if (prior != null)
            {
                Require(prior.SKK_NguoiThucHienID == actor!.Id && prior.SKK_HanhDong == action && (!id.HasValue || prior.KK_ID == id) &&
                    (lineId.HasValue ? prior.CTKK_ID == lineId : action == "PhatHien" || prior.CTKK_ID == null), "Mã yêu cầu đã được dùng cho thao tác khác.");
                using var saved = JsonDocument.Parse(prior.SKK_Sau!);
                Require(saved.RootElement.GetProperty("DauVetYeuCau").GetString() == signature, "Yêu cầu lặp có nội dung khác. Hãy tải lại.");
                await transaction.CommitAsync(); return RedirectToAction(nameof(Details), new { id = prior.KK_ID });
            }
            if (doc != null)
            {
                Require(!doc.KK_LaDuLieuCu, "Phiếu trước nâng cấp chỉ được xem; không tạo snapshot hoặc sửa lịch sử cũ.");
                if (lineId.HasValue)
                {
                    await Lock("dong", lineId.Value);
                    line = await db.tblChiTietKiemKe.SingleAsync(x => x.CTKK_ID == lineId && x.KK_ID == doc.KK_ID);
                    Require(line.CTKK_PhienBan == input.PhienBan, "Dòng đã được người khác cập nhật. Hãy tải lại.");
                }
                else Require(doc.KK_PhienBan == input.PhienBan, "Phiếu đã thay đổi. Hãy tải lại trước khi thao tác.");
            }
            var before = line != null ? Snapshot(line) : doc == null ? null : Snapshot(doc);
            var result = await operation(actor!, doc, line);
            if (result.Line != null) result.Line.CTKK_PhienBan++;
            if (!lineId.HasValue) result.Doc.KK_PhienBan++;
            Require(!db.ChangeTracker.Entries().Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                e.Entity is not (tblKiemKeTaiSan or tblChiTietKiemKe or tblSuKienKiemKe)), "Kiểm kê không được thay đổi dữ liệu phân hệ khác.");
            await db.SaveChangesAsync();
            db.tblSuKienKiemKe.Add(new()
            {
                KiemKe = result.Doc, ChiTiet = result.Line, SKK_HanhDong = action, SKK_NguoiThucHienID = actor!.Id,
                SKK_NguoiThucHienSnapshot = actor.Name, SKK_ThoiDiemUTC = DateTime.UtcNow, SKK_LyDo = Clean(input.LyDo),
                SKK_Truoc = before, SKK_Sau = JsonSerializer.Serialize(new { DauVetYeuCau = signature, DuLieu = Snapshot((object?)result.Line ?? result.Doc) }), SKK_RequestKey = input.RequestKey
            });
            await db.SaveChangesAsync(); commitStarted = true; await transaction.CommitAsync();
            TempData["KiemKeSuccess"] = "Đã ghi nhận thao tác kiểm kê.";
            return RedirectToAction(nameof(Details), new { id = result.Doc.KK_ID });
        }
        catch (InventoryFailure ex)
        {
            var rolledBack = await Rollback(transaction); db.ChangeTracker.Clear();
            return StatusCode(rolledBack ? ex.Status : 503, rolledBack ? ex.Message : "Chưa xác định được kết quả. Tra cứu nhật ký theo mã yêu cầu trước khi thử lại.");
        }
        catch (Exception ex)
        {
            var rolledBack = await Rollback(transaction); db.ChangeTracker.Clear();
            logger.LogWarning(ex, "Giao dịch kiểm kê {Action} không hoàn tất.", action);
            if (commitStarted || !rolledBack) return StatusCode(503, "Chưa xác định được kết quả commit. Tra cứu nhật ký trước khi gửi lại cùng mã yêu cầu.");
            var sql = ex.GetBaseException() as MySqlException;
            return StatusCode(sql?.Number is 1062 or 1205 or 1213 || ex is DbUpdateConcurrencyException ? 409 : 500,
                sql?.Number switch { 1062 => "Tài sản, mã nhận diện hoặc yêu cầu đã được ghi đồng thời. Hãy tải lại; không tạo dòng trùng.", 1205 or 1213 => "Xung đột khóa; giao dịch đã hoàn tác. Hãy tải lại và thử lại.", _ => "Không thể hoàn tất; giao dịch đã được hoàn tác." });
        }
        finally
        {
            if (transaction != null) await transaction.DisposeAsync();
            if (namedLock != null)
            {
                try { await db.Database.SqlQuery<int?>($"SELECT RELEASE_LOCK({namedLock}) AS Value").SingleAsync(); }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Không thể giải phóng khóa bắt đầu kiểm kê.");
                    if (db.Database.GetDbConnection() is MySqlConnection connection) MySqlConnection.ClearPool(connection);
                }
            }
            await db.Database.CloseConnectionAsync();
        }
    }
    private async Task<bool> Rollback(IDbContextTransaction? transaction)
    {
        if (transaction == null) return true;
        try { await transaction.RollbackAsync(); return true; }
        catch (Exception ex) { logger.LogError(ex, "Không thể xác minh rollback kiểm kê."); return false; }
    }
    private IActionResult TransitionResponse(IActionResult result, int id)
    {
        if (Html && result is ObjectResult { StatusCode: 400 or 409 or 500 or 503 } error)
        { TempData["KiemKeError"] = error.Value?.ToString(); return RedirectToAction(nameof(Details), new { id }); }
        return result;
    }
    private async Task<IActionResult> FormResponse(IActionResult result, string view, Func<Task<object?>> form)
    {
        if (Html && result is ObjectResult { StatusCode: 400 or 409 or 500 or 503 } error)
        {
            ModelState.AddModelError("", error.Value?.ToString() ?? "Dữ liệu không hợp lệ.");
            var model = await form(); if (model == null) return NotFound();
            var response = View(view, model); response.StatusCode = error.StatusCode; return response;
        }
        return result;
    }
    #endregion

    #region Sổ tài sản và nguồn trách nhiệm, chỉ đọc các phân hệ khác
    private async Task<List<KiemKeSach>> ReadBooks(int[]? ids = null)
    {
        var assets = await db.tblTaiSan.AsNoTracking().Where(x => ids == null || ids.Contains(x.TS_ID)).Include(x => x.tblDanhMucTaiSan).OrderBy(x => x.TS_ID).ToListAsync();
        var assetIds = assets.Select(x => x.TS_ID).ToArray();
        var assignments = await db.tblChiTietCapPhat.AsNoTracking().Where(x => assetIds.Contains(x.TS_ID) && (x.CTCP_TrangThai == "DangHieuLuc" || (x.CTCP_TrangThai == null && x.CTCP_NgayThuHoi == null)))
            .Include(x => x.CapPhat).ThenInclude(x => x!.PhongBan).Include(x => x.CapPhat).ThenInclude(x => x!.NguoiNhan).OrderBy(x => x.CTCP_ID).ToListAsync();
        var repairs = await db.tblBaoTriTaiSan.AsNoTracking().Where(x => assetIds.Contains(x.TS_ID)).OrderBy(x => x.BT_ID).ToListAsync();
        var disposals = await db.tblChiTietThanhLy.AsNoTracking().Where(x => assetIds.Contains(x.TS_ID)).Include(x => x.ThanhLy).OrderBy(x => x.CTTL_ID).ToListAsync();
        var result = new List<KiemKeSach>();
        foreach (var asset in assets)
        {
            var lines = assignments.Where(x => x.TS_ID == asset.TS_ID).ToList();
            var active = lines.Where(x => x.CTCP_TrangThai == "DangHieuLuc").ToList();
            var legacy = lines.Where(x => x.CTCP_TrangThai == null && x.CapPhat!.CP_TrangThai == "DaCapPhat" && x.CapPhat.CP_LoaiPhieu == null).ToList();
            var book = new KiemKeSach { Id = asset.TS_ID, Ma = asset.TS_MaTaiSan, Ten = asset.TS_TenTaiSan, Serial = asset.TS_Serial, DanhMuc = asset.tblDanhMucTaiSan?.DMTS_TenDanhMuc,
                NguyenGia = asset.TS_NguyenGia, ViTri = asset.TS_ViTri, TinhTrang = asset.TS_TinhTrang, TrangThai = asset.TS_TrangThai, DangQuanLy = asset.TS_DangQuanLy,
                PhongBanLienQuan = active.Concat(legacy).Select(x => x.CapPhat!.PB_ID).Distinct().ToList() };
            var validActive = active.Count == 1 && active[0].CTCP_BatDauUTC.HasValue && active[0].CTCP_KetThucUTC == null &&
                ((active[0].CapPhat!.CP_TrangThai == "DaBanGiao" && active[0].CapPhat!.CP_LoaiPhieu is "Giao" or "Chuyen") ||
                 (active[0].CapPhat!.CP_TrangThai == "DaCapPhat" && active[0].CTCP_DoiSoatUTC.HasValue)) && active[0].CapPhat!.CP_NguoiNhanID.HasValue;
            tblChiTietCapPhat? source = null;
            if (active.Count > 0)
            {
                if (!validActive || legacy.Count > 0 || asset.TS_TrangThai != "DaCapPhat") book.XungDot = true;
                else source = active[0];
            }
            else if (legacy.Count > 0)
            {
                if (legacy.Count != 1 || asset.TS_TrangThai != "DaCapPhat") book.XungDot = true;
                else source = legacy[0];
            }
            else if (asset.TS_TrangThai == "DaCapPhat") book.XungDot = true;
            if (source != null)
            {
                book.PhongBanId = source.CapPhat!.PB_ID; book.PhongBan = source.CapPhat.PhongBan?.PB_TenPhongBan;
                book.NguoiNhanId = source.CapPhat.CP_NguoiNhanID; book.NguoiNhan = source.CapPhat.NguoiNhan?.TK_HoTen ?? source.CapPhat.CP_NguoiNhan;
                if (!book.NguoiNhanId.HasValue) book.CanhBao = "Nguồn bàn giao cũ duy nhất; danh tính người nhận chưa gắn tài khoản. Cần biên bản khi đối chiếu người giữ.";
            }
            if (book.XungDot) book.CanhBao = "Dữ liệu bàn giao chưa đối soát: nguồn trách nhiệm chồng chéo, thiếu nguồn hợp lệ hoặc mâu thuẫn trạng thái tài sản.";
            var maintenance = repairs.Where(x => x.TS_ID == asset.TS_ID).ToList();
            book.DangBaoTri = maintenance.Any(x => x.BT_TrangThai is not ("HoanThanh" or "DaHoanThanh" or "Huy")) || asset.TS_TrangThai is "DangBaoTri" or "ChoBaoTri";
            var liquidation = disposals.Where(x => x.TS_ID == asset.TS_ID).ToList();
            var completed = liquidation.Any(x => x.ThanhLy!.TL_TrangThai is "HoanThanh" or "DaHoanThanh" or "DaThanhLy" && x.ThanhLy.TL_NgayThanhLy.HasValue && x.ThanhLy.TL_NgayThanhLy <= Today);
            book.DaThanhLyThucTe = asset.TS_TrangThai == "DaThanhLy" && completed;
            if ((asset.TS_TrangThai == "DaThanhLy") != completed)
            { book.XungDot = true; book.CanhBao = (book.CanhBao + " Trạng thái thanh lý và hồ sơ hoàn tất chưa nhất quán.").Trim(); }
            book.DauVet = Digest(JsonSerializer.Serialize(new
            {
                book.Id, book.Ma, book.Ten, book.Serial, book.DanhMuc, book.NguyenGia, book.ViTri, book.TinhTrang, book.TrangThai, book.DangQuanLy,
                book.PhongBanId, book.PhongBan, book.NguoiNhanId, book.NguoiNhan, book.XungDot,
                BanGiao = lines.Select(x => new { x.CTCP_ID, x.CTCP_TrangThai, x.CTCP_BatDauUTC, x.CTCP_KetThucUTC, x.CapPhat!.CP_PhienBan, x.CapPhat.CP_TrangThai }),
                BaoTri = maintenance.Select(x => new { x.BT_ID, x.BT_TrangThai, x.BT_NgayHoanThanh }),
                ThanhLy = liquidation.Select(x => new { x.TL_ID, x.ThanhLy!.TL_TrangThai, x.ThanhLy.TL_NgayThanhLy })
            }));
            result.Add(book);
        }
        return result;
    }
    private static tblChiTietKiemKe NewSnapshot(int id, KiemKeSach book, string kind = "TrongSo") => new()
    {
        KK_ID = id, TS_ID = book.Id, CTKK_LoaiDong = kind, CTKK_KetQua = "ChuaKiem", CTKK_MaTaiSanSnapshot = book.Ma, CTKK_TenTaiSanSnapshot = book.Ten,
        CTKK_DanhMucSnapshot = book.DanhMuc, CTKK_NguyenGiaDuKien = book.NguyenGia, CTKK_SerialDuKien = book.Serial,
        CTKK_PBDuKienID = book.PhongBanId, CTKK_PhongBanDuKienSnapshot = book.PhongBan, CTKK_NguoiNhanDuKienID = book.NguoiNhanId,
        CTKK_NguoiNhanDuKienSnapshot = book.NguoiNhan, CTKK_ViTriDuKien = book.ViTri, CTKK_TinhTrangDuKien = book.TinhTrang, CTKK_TrangThaiDuKien = book.TrangThai,
        CTKK_CanhBaoNguon = book.CanhBao, CTKK_DauVetNguon = book.DauVet
    };
    #endregion

    #region Lập đợt, chốt danh sách, gửi duyệt, phê duyệt
    [HttpGet]
    public async Task<IActionResult> Create(int? dotGocId = null)
    {
        var actor = await ReadActor(); if (actor?.Manager != true) return StatusCode(403);
        var input = new KiemKePhieuInput { RequestKey = Key(), NgayKiemKe = Today, DotGocId = dotGocId };
        if (dotGocId.HasValue)
        {
            var original = await Visible(dotGocId.Value); if (original == null) return NotFound();
            if (original.KK_TrangThai != "HoanThanh") return Conflict("Chỉ lập đợt bổ sung tham chiếu phiếu đã hoàn thành.");
            input.TenDot = "Bổ sung " + original.KK_MaPhieu; input.LoaiPhamVi = original.KK_LoaiPhamVi; input.PhongBanId = original.PB_ID;
        }
        return View("Form", await PreparePhieu(input));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(KiemKePhieuInput input)
    {
        var result = await Write("Tao", null, null, input, async (actor, _, _) =>
        {
            await ValidatePhieu(input);
            if (input.DotGocId.HasValue) Require(await db.tblKiemKeTaiSan.AnyAsync(x => x.KK_ID == input.DotGocId && x.KK_TrangThai == "HoanThanh"), "Đợt gốc phải là phiếu đã hoàn thành.");
            var doc = new tblKiemKeTaiSan { KK_MaPhieu = "KK-" + Today.ToString("yyyyMMdd") + "-" + Guid.NewGuid().ToString("N")[..12], KK_NguoiTaoID = actor.Id,
                KK_NgayTaoUTC = DateTime.UtcNow, KK_TrangThai = "Nhap", KK_RequestKey = input.RequestKey, KK_DotGocID = input.DotGocId };
            SetPhieu(doc, input); db.tblKiemKeTaiSan.Add(doc); return (doc, null);
        });
        return await FormResponse(result, "Form", async () => await PreparePhieu(input));
    }
    [HttpGet]
    public async Task<IActionResult> Edit([FromRoute] int id)
    {
        var actor = await ReadActor(); if (actor?.Manager != true) return StatusCode(403);
        var doc = await Visible(id); if (doc == null) return NotFound();
        if (doc.KK_LaDuLieuCu || doc.KK_TrangThai != "Nhap") return Conflict("Chỉ sửa được phiếu nháp chưa chốt.");
        return View("Form", await PreparePhieu(new KiemKePhieuInput { RequestKey = Key(), PhienBan = doc.KK_PhienBan, TenDot = doc.KK_TenDot ?? "", LoaiPhamVi = doc.KK_LoaiPhamVi,
            PhongBanId = doc.PB_ID, NgayKiemKe = doc.KK_NgayKiemKe, NgayKetThucDuKien = doc.KK_NgayKetThucDuKien, NguoiKiemKeId = doc.KK_NguoiKiemKeID, GhiChu = doc.KK_GhiChu, DotGocId = doc.KK_DotGocID }, id));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit([FromRoute] int id, KiemKePhieuInput input)
    {
        var result = await Write("Sua", id, null, input, async (_, doc, _) =>
        {
            Require(doc!.KK_TrangThai == "Nhap" && !doc.KK_ThoiDiemChotUTC.HasValue, "Phạm vi và dữ liệu đã chốt không được sửa.");
            Require(doc.KK_DotGocID == input.DotGocId, "Không đổi liên kết đợt gốc."); await ValidatePhieu(input); SetPhieu(doc, input); return (doc, null);
        });
        return await FormResponse(result, "Form", async () => await PreparePhieu(input, id));
    }
    private static void SetPhieu(tblKiemKeTaiSan doc, KiemKePhieuInput input)
    {
        doc.KK_TenDot = input.TenDot.Trim(); doc.KK_LoaiPhamVi = input.LoaiPhamVi; doc.PB_ID = input.PhongBanId;
        doc.KK_NgayKiemKe = input.NgayKiemKe; doc.KK_NgayKetThucDuKien = input.NgayKetThucDuKien; doc.KK_NguoiKiemKeID = input.NguoiKiemKeId; doc.KK_GhiChu = Clean(input.GhiChu);
    }
    private async Task ValidatePhieu(KiemKePhieuInput input)
    {
        Require(!string.IsNullOrWhiteSpace(input.TenDot) && input.TenDot.Trim().Length <= 200, "Nhập tên đợt hợp lệ.", 400);
        Require(input.LoaiPhamVi is "ToanBo" or "PhongBan", "Phạm vi không hợp lệ.", 400);
        Require((input.LoaiPhamVi == "ToanBo" && input.PhongBanId == null) || (input.LoaiPhamVi == "PhongBan" && input.PhongBanId > 0), "Toàn bộ phải để trống phòng ban; theo phòng ban phải chọn đơn vị.", 400);
        if (input.PhongBanId.HasValue)
        { await Lock("department", input.PhongBanId.Value); Require(await db.tblPhongBan.AnyAsync(x => x.PB_ID == input.PhongBanId && x.PB_TrangThai == true), "Phòng ban không tồn tại hoặc đã ngừng hoạt động."); }
        await Lock("account", input.NguoiKiemKeId);
        Require(await db.tblTaiKhoan.AnyAsync(x => x.TK_ID == input.NguoiKiemKeId && x.TK_TrangThai && (x.TK_VaiTro == "Admin" || x.TK_VaiTro == "NhanVien" || x.TK_VaiTro == "QuanLyTaiSan")), "Người kiểm kê phải là tài khoản đang hoạt động.");
        Require(input.NgayKiemKe >= new DateOnly(2000, 1, 1) && input.NgayKiemKe <= Today.AddYears(1), "Ngày kiểm kê phải hợp lệ, không quá một năm trong tương lai.", 400);
        Require(input.NgayKetThucDuKien == null || input.NgayKetThucDuKien >= input.NgayKiemKe && input.NgayKetThucDuKien <= input.NgayKiemKe.Value.AddYears(1), "Ngày kết thúc không trước ngày kiểm kê và không kéo dài quá một năm.", 400);
    }
    private async Task<KiemKePhieuForm> PreparePhieu(KiemKePhieuInput input, int? id = null) => new()
    {
        Id = id, RequestKey = input.RequestKey, PhienBan = input.PhienBan, LyDo = input.LyDo, TenDot = input.TenDot, LoaiPhamVi = input.LoaiPhamVi, PhongBanId = input.PhongBanId,
        NgayKiemKe = input.NgayKiemKe, NgayKetThucDuKien = input.NgayKetThucDuKien, NguoiKiemKeId = input.NguoiKiemKeId, GhiChu = input.GhiChu, DotGocId = input.DotGocId,
        PhongBans = await Departments(), NguoiKiemKes = await People()
    };
    private Task<List<SelectListItem>> Departments() => db.tblPhongBan.AsNoTracking().OrderBy(x => x.PB_TenPhongBan).ThenBy(x => x.PB_ID)
        .Select(x => new SelectListItem(x.PB_TenPhongBan + (x.PB_TrangThai == true ? "" : " (ngừng hoạt động)"), x.PB_ID.ToString())).ToListAsync();
    private Task<List<SelectListItem>> People(bool managers = false) => db.tblTaiKhoan.AsNoTracking().Where(x => x.TK_TrangThai &&
        (x.TK_VaiTro == "Admin" || x.TK_VaiTro == "QuanLyTaiSan" || !managers && x.TK_VaiTro == "NhanVien")).OrderBy(x => x.TK_HoTen).ThenBy(x => x.TK_ID)
        .Select(x => new SelectListItem(x.TK_HoTen + " (#" + x.TK_ID + ")", x.TK_ID.ToString())).ToListAsync();
    private Task<List<SelectListItem>> ActualPeople() => db.tblTaiKhoan.AsNoTracking().OrderBy(x => x.TK_HoTen).ThenBy(x => x.TK_ID)
        .Select(x => new SelectListItem(x.TK_HoTen + " (#" + x.TK_ID + ")" + (x.TK_TrangThai ? "" : " — tài khoản đã khóa"), x.TK_ID.ToString())).ToListAsync();

    [HttpGet]
    public async Task<IActionResult> NguonDuLieu()
    {
        if ((await ReadActor())?.Manager != true) return StatusCode(403);
        return View((await ReadBooks()).Where(x => x.XungDot || x.PhongBanId == null || x.DangBaoTri).ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DoiNguoiKiemKe([FromRoute] int id, KiemKePhanCongInput input) => TransitionResponse(await Write("DoiNguoiKiemKe", id, null, input, async (_, doc, _) =>
    {
        Require(doc!.KK_TrangThai == "DangKiemKe", "Chỉ phân công lại khi đợt đang mở; phiếu chờ duyệt cần được trả lại trước.");
        Require(Clean(input.LyDo) != null, "Cần lý do và căn cứ phân công lại.", 400);
        Require(input.NguoiKiemKeId != doc.KK_NguoiKiemKeID, "Chọn người phụ trách khác.", 400);
        await Lock("account", input.NguoiKiemKeId);
        Require(await db.tblTaiKhoan.AnyAsync(x => x.TK_ID == input.NguoiKiemKeId && x.TK_TrangThai &&
            (x.TK_VaiTro == "Admin" || x.TK_VaiTro == "QuanLyTaiSan" || x.TK_VaiTro == "NhanVien")), "Tài khoản được giao không còn hoạt động hoặc không có quyền kiểm kê.");
        doc.KK_NguoiKiemKeID = input.NguoiKiemKeId;
        return (doc, null);
    }), id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> BatDau([FromRoute] int id, KiemKeRequest input) => TransitionResponse(await Write("BatDau", id, null, input, async (_, doc, _) =>
    {
        Require(doc!.KK_TrangThai == "Nhap" && doc.KK_ThoiDiemChotUTC == null, "Đợt đã bắt đầu hoặc không còn là nháp; không chốt lại.");
        Require(doc.KK_NgayKiemKe <= Today, "Chưa đến ngày kiểm kê dự kiến.");
        await ValidatePhieu(new() { TenDot = doc.KK_TenDot!, LoaiPhamVi = doc.KK_LoaiPhamVi, PhongBanId = doc.PB_ID, NguoiKiemKeId = doc.KK_NguoiKiemKeID, NgayKiemKe = doc.KK_NgayKiemKe, NgayKetThucDuKien = doc.KK_NgayKetThucDuKien });
        Require(!await db.tblKiemKeTaiSan.AnyAsync(x => x.KK_ID != id && !x.KK_LaDuLieuCu && (x.KK_TrangThai == "DangKiemKe" || x.KK_TrangThai == "ChoDuyet") &&
            (doc.KK_LoaiPhamVi == "ToanBo" || x.KK_LoaiPhamVi == "ToanBo" || x.PB_ID == doc.PB_ID)), "Phạm vi giao nhau với một đợt đang kiểm kê hoặc chờ duyệt.");
        Require(!await db.tblChiTietKiemKe.AnyAsync(x => x.KK_ID == id), "Phiếu đã có chi tiết; không tạo lại snapshot.");
        var books = (await ReadBooks()).Where(x => x.DangQuanLy && !x.DaThanhLyThucTe).ToList();
        if (doc.KK_LoaiPhamVi == "PhongBan")
        {
            var uncertain = books.Where(x => x.XungDot && (x.PhongBanLienQuan.Count == 0 || x.PhongBanLienQuan.Contains(doc.PB_ID!.Value))).ToList();
            Require(uncertain.Count == 0, "Chưa thể chốt phòng ban: dữ liệu bàn giao chưa đối soát ở " + string.Join(", ", uncertain.Take(15).Select(x => x.Ma)) + ". Xử lý tại phân hệ bàn giao trước.");
            books = books.Where(x => x.PhongBanId == doc.PB_ID).ToList();
        }
        var now = DateTime.UtcNow;
        db.tblChiTietKiemKe.AddRange(books.Select(x => NewSnapshot(id, x)));
        doc.KK_ThoiDiemChotUTC = now; doc.KK_TrangThai = "DangKiemKe"; return (doc, null);
    }, starting: true), id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GuiDuyet([FromRoute] int id, KiemKeRequest input) => TransitionResponse(await Write("GuiDuyet", id, null, input, async (_, doc, _) =>
    {
        Require(doc!.KK_TrangThai == "DangKiemKe", "Chỉ gửi duyệt đợt đang kiểm kê.");
        await ReadyForApproval(doc); doc.KK_TrangThai = "ChoDuyet"; doc.KK_NgayGuiDuyetUTC = DateTime.UtcNow; return (doc, null);
    }), id);
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Duyet([FromRoute] int id, KiemKeRequest input) => TransitionResponse(await Write("Duyet", id, null, input, async (actor, doc, _) =>
    {
        Require(doc!.KK_TrangThai == "ChoDuyet", "Phiếu không ở trạng thái chờ duyệt.");
        Require(doc.KK_NguoiKiemKeID != actor.Id, "Người trực tiếp được giao kiểm kê không được tự phê duyệt.", 403);
        Require(Clean(input.LyDo) != null, "Nhập nhận xét/kết luận phê duyệt.", 400);
        await ReadyForApproval(doc);
        var touched = await db.tblSuKienKiemKe.AsNoTracking().Where(x => x.KK_ID == id && x.SKK_NguoiThucHienID == actor.Id && x.CTKK_ID != null &&
            (x.SKK_HanhDong == "GhiNhan" || x.SKK_HanhDong == "PhatHien")).Select(x => x.CTKK_ID!.Value).Distinct().ToArrayAsync();
        Require(!await db.tblChiTietKiemKe.AnyAsync(x => x.KK_ID == id && touched.Contains(x.CTKK_ID) &&
            (x.CTKK_TrangThaiDoiSoat != "DaDoiSoat" || x.CTKK_NguoiDoiSoatID == null || x.CTKK_NguoiDoiSoatID == actor.Id)), "Kết quả do người duyệt từng trực tiếp ghi nhận cần người khác đối soát độc lập.", 403);
        doc.KK_TrangThai = "HoanThanh"; doc.KK_NguoiDuyetID = actor.Id; doc.KK_NgayDuyetUTC = DateTime.UtcNow; doc.KK_KetLuan = input.LyDo!.Trim(); return (doc, null);
    }), id);
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> TraLai([FromRoute] int id, KiemKeRequest input) => TransitionResponse(await Write("TraLai", id, null, input, (_, doc, _) =>
    {
        Require(doc!.KK_TrangThai == "ChoDuyet", "Chỉ trả lại phiếu chờ duyệt."); Require(Clean(input.LyDo) != null, "Cần lý do trả lại.", 400);
        doc.KK_TrangThai = "DangKiemKe"; doc.KK_LyDoTraLai = input.LyDo!.Trim(); return Task.FromResult((doc, (tblChiTietKiemKe?)null));
    }), id);
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Huy([FromRoute] int id, KiemKeRequest input) => TransitionResponse(await Write("Huy", id, null, input, (_, doc, _) =>
    {
        Require(doc!.KK_TrangThai is "Nhap" or "DangKiemKe" or "ChoDuyet", "Không hủy phiếu hoàn thành/đã hủy."); Require(Clean(input.LyDo) != null, "Cần lý do hủy.", 400);
        doc.KK_TrangThai = "DaHuy"; doc.KK_LyDoHuy = input.LyDo!.Trim(); doc.KK_NgayHuyUTC = DateTime.UtcNow; return Task.FromResult((doc, (tblChiTietKiemKe?)null));
    }), id);
    private async Task ReadyForApproval(tblKiemKeTaiSan doc)
    {
        var rows = await db.tblChiTietKiemKe.AsNoTracking().Where(x => x.KK_ID == doc.KK_ID).ToListAsync();
        Require(rows.Count > 0 && rows.Any(x => x.CTKK_LoaiDong == "TrongSo"), "Phạm vi không có tài sản trong sổ; chưa có quy trình phê chuẩn phạm vi rỗng nên không gửi duyệt.");
        var books = (await ReadBooks(rows.Where(x => x.TS_ID.HasValue).Select(x => x.TS_ID!.Value).ToArray())).ToDictionary(x => x.Id);
        var pending = new List<string>();
        foreach (var line in rows)
        {
            var invalid = !KiemKeUi.Results.Contains(line.CTKK_KetQua) || line.CTKK_CanXacMinh || line.CTKK_KetQua is "ChuaKiem" or "CanXacMinh" || line.CTKK_TrangThaiDoiSoat == "CanKiemKeLai" ||
                line.CTKK_LoaiDong == "TrongSo" && line.CTKK_DaTimThay == null ||
                KiemKeUi.IsDifference(line.CTKK_KetQua) && (Clean(line.CTKK_LyDoChenhLech) == null || Clean(line.CTKK_DeXuatXuLy) == null || !KiemKeUi.Directions.Contains(line.CTKK_HuongXuLy) || line.CTKK_TrangThaiDoiSoat != "DaDoiSoat" || line.CTKK_NguoiDoiSoatID == null) ||
                line.CTKK_LoaiDong != "TrongSo" && (Clean(line.CTKK_ViTriThucTe) == null || Clean(line.CTKK_TenThucTe) == null || Clean(line.CTKK_MaNhanDienThucTe) == null && Clean(line.CTKK_SerialThucTe) == null);
            if (line.TS_ID.HasValue && books.TryGetValue(line.TS_ID.Value, out var book))
                invalid |= book.XungDot || line.CTKK_DauVetNguon != book.DauVet && (!line.CTKK_XacNhanCanCu || Clean(line.CTKK_BangChung) == null || line.CTKK_TrangThaiDoiSoat != "DaDoiSoat");
            else if (line.CTKK_LoaiDong != "NgoaiSo") invalid = true;
            if (invalid) pending.Add(line.CTKK_MaTaiSanSnapshot ?? line.CTKK_MaPhatHien ?? "#" + line.CTKK_ID);
        }
        Require(pending.Count == 0, "Chưa đủ điều kiện gửi/duyệt. Hoàn tất ghi nhận, xác minh và đối soát các dòng: " + string.Join(", ", pending.Take(20)) + (pending.Count > 20 ? $"… ({pending.Count} dòng)" : ""));
    }
    #endregion

    #region Ghi nhận từng tài sản, phát hiện và đối soát
    private async Task<KiemKeDongRow?> VisibleLine(int id)
    {
        var line = await db.tblChiTietKiemKe.AsNoTracking().SingleOrDefaultAsync(x => x.CTKK_ID == id);
        if (line == null || await Visible(line.KK_ID) == null) return null;
        return new() { Dong = line, HienTai = line.TS_ID.HasValue ? (await ReadBooks([line.TS_ID.Value])).SingleOrDefault() : null };
    }
    [HttpGet]
    public async Task<IActionResult> GhiNhan([FromRoute] int id)
    {
        var row = await VisibleLine(id); if (row == null) return NotFound();
        var doc = await Visible(row.Dong.KK_ID);
        if (doc!.KK_LaDuLieuCu || doc.KK_TrangThai != "DangKiemKe") return Conflict("Chỉ ghi nhận khi đợt đang kiểm kê.");
        return View(await PrepareObservation(row, new()
        {
            RequestKey = Key(), PhienBan = row.Dong.CTKK_PhienBan, CachGhiNhan = row.Dong.CTKK_CanXacMinh ? "CanXacMinh" : row.Dong.CTKK_DaTimThay == false ? "KhongThay" : "CoMat",
            TinhTrang = row.Dong.CTKK_TinhTrangThucTe ?? row.Dong.CTKK_TinhTrangDuKien,
            ViTri = row.Dong.CTKK_ViTriThucTe ?? row.Dong.CTKK_ViTriDuKien, PhongBanId = row.Dong.CTKK_PBThucTeID ?? row.Dong.CTKK_PBDuKienID,
            NguoiSuDungId = row.Dong.CTKK_NguoiSuDungThucTeID ?? row.Dong.CTKK_NguoiNhanDuKienID,
            NguoiSuDung = row.Dong.CTKK_NguoiSuDungThucTe, MaNhanDien = row.Dong.CTKK_MaNhanDienThucTe ?? row.Dong.CTKK_MaTaiSanSnapshot,
            Serial = row.Dong.CTKK_SerialThucTe ?? row.Dong.CTKK_SerialDuKien, TenTaiSanThucTe = row.Dong.CTKK_TenThucTe,
            CoHuHong = row.Dong.CTKK_CoHuHong, ThieuPhuKien = row.Dong.CTKK_ThieuPhuKien, GhiChu = row.Dong.CTKK_GhiChu, BangChung = row.Dong.CTKK_BangChung
        }));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GhiNhan([FromRoute] int id, KiemKeGhiNhanInput input)
    {
        var result = await Write("GhiNhan", null, id, input, async (actor, doc, line) =>
        {
            Require(doc!.KK_TrangThai == "DangKiemKe", "Kết quả đã đóng băng hoặc phiếu không còn mở.");
            Require(line!.CTKK_TrangThaiDoiSoat != "DaDoiSoat" || Clean(input.LyDo) != null, "Cần lý do khi sửa kết quả đã đối soát.", 400);
            await Observe(doc, line, input, actor); return (doc, line);
        });
        return await FormResponse(result, "GhiNhan", async () => { var row = await VisibleLine(id); return row == null ? null : await PrepareObservation(row, input); });
    }
    private async Task<KiemKeGhiNhanForm> PrepareObservation(KiemKeDongRow row, KiemKeGhiNhanInput input) => new()
    {
        Dong = row, RequestKey = input.RequestKey, PhienBan = input.PhienBan, LyDo = input.LyDo, CachGhiNhan = input.CachGhiNhan,
        TinhTrang = input.TinhTrang, ViTri = input.ViTri, PhongBanId = input.PhongBanId, NguoiSuDungId = input.NguoiSuDungId, NguoiSuDung = input.NguoiSuDung,
        MaNhanDien = input.MaNhanDien, Serial = input.Serial, TenTaiSanThucTe = input.TenTaiSanThucTe, CoHuHong = input.CoHuHong, ThieuPhuKien = input.ThieuPhuKien,
        KhacNguoiSuDung = input.KhacNguoiSuDung, XacNhanCanCu = input.XacNhanCanCu, DaKiemTraThucTe = input.DaKiemTraThucTe,
        KhongApDungViTri = input.KhongApDungViTri, BangChung = input.BangChung, GhiChu = input.GhiChu, PhongBans = await Departments(), NguoiDungs = await ActualPeople()
    };
    private async Task Observe(tblKiemKeTaiSan doc, tblChiTietKiemKe line, KiemKeGhiNhanInput input, Actor actor)
    {
        Require(input.CachGhiNhan is "CoMat" or "KhongThay" or "CanXacMinh", "Chọn kết quả kiểm tra thực tế.", 400);
        Require(input.CachGhiNhan == "CanXacMinh" || input.DaKiemTraThucTe, "Cần xác nhận đã kiểm tra thực tế từng tài sản.", 400);
        Require(input.CachGhiNhan == "CoMat" || line.CTKK_LoaiDong == "TrongSo", "Dòng phát hiện ngoài danh sách phải là hiện vật đã tìm thấy.", 400);
        var book = line.TS_ID.HasValue ? (await ReadBooks([line.TS_ID.Value])).SingleOrDefault() : null;
        line.CTKK_CoSaiPhongBan = false; line.CTKK_CoSaiViTri = false; line.CTKK_CoSaiNguoiSuDung = false; line.CTKK_CoHuHong = false; line.CTKK_ThieuPhuKien = false;
        line.CTKK_CanXacMinh = input.CachGhiNhan == "CanXacMinh";
        if (input.CachGhiNhan != "CoMat")
        {
            Require(Clean(input.GhiChu) != null, "Nêu cách kiểm tra/lý do không tìm thấy hoặc cần xác minh.", 400);
            line.CTKK_DaTimThay = input.CachGhiNhan == "KhongThay" ? false : null;
            line.CTKK_TinhTrangThucTe = null; line.CTKK_ViTriThucTe = null; line.CTKK_PBThucTeID = null;
            line.CTKK_NguoiSuDungThucTeID = null; line.CTKK_NguoiSuDungThucTe = null; line.CTKK_SerialThucTe = null; line.CTKK_MaNhanDienThucTe = null;
            if (book?.DangBaoTri == true && !(input.XacNhanCanCu && Clean(input.BangChung) != null))
            { line.CTKK_DaTimThay = null; line.CTKK_CanXacMinh = true; }
        }
        else
        {
            Require(KiemKeUi.Conditions.Contains(input.TinhTrang), "Chọn tình trạng thực tế hợp lệ.", 400);
            Require(Clean(input.ViTri) != null || input.KhongApDungViTri && Clean(input.GhiChu) != null, "Nhập vị trí thực tế hoặc giải trình vì sao không áp dụng.", 400);
            if (input.PhongBanId.HasValue) Require(await db.tblPhongBan.AnyAsync(x => x.PB_ID == input.PhongBanId), "Phòng ban thực tế không tồn tại.", 400);
            tblTaiKhoan? user = null;
            if (input.NguoiSuDungId.HasValue)
            { user = await db.tblTaiKhoan.AsNoTracking().SingleOrDefaultAsync(x => x.TK_ID == input.NguoiSuDungId); Require(user != null, "Người sử dụng không tồn tại.", 400); }
            await CheckObservedIdentity(doc.KK_ID, line.CTKK_ID, input.MaNhanDien, input.Serial, line.TS_ID);
            line.CTKK_DaTimThay = true; line.CTKK_TinhTrangThucTe = input.TinhTrang; line.CTKK_ViTriThucTe = Clean(input.ViTri);
            line.CTKK_PBThucTeID = input.PhongBanId; line.CTKK_NguoiSuDungThucTeID = input.NguoiSuDungId; line.CTKK_NguoiSuDungThucTe = user?.TK_HoTen ?? Clean(input.NguoiSuDung);
            line.CTKK_MaNhanDienThucTe = Clean(input.MaNhanDien); line.CTKK_SerialThucTe = Clean(input.Serial);
            if (Clean(input.TenTaiSanThucTe) != null) line.CTKK_TenThucTe = input.TenTaiSanThucTe!.Trim();
            line.CTKK_CoSaiPhongBan = line.CTKK_PBDuKienID.HasValue && input.PhongBanId.HasValue && line.CTKK_PBDuKienID != input.PhongBanId;
            line.CTKK_CoSaiViTri = Clean(line.CTKK_ViTriDuKien) != null && Clean(input.ViTri) != null && Normal(line.CTKK_ViTriDuKien) != Normal(input.ViTri);
            line.CTKK_CoSaiNguoiSuDung = line.CTKK_NguoiNhanDuKienID.HasValue && input.NguoiSuDungId.HasValue ? line.CTKK_NguoiNhanDuKienID != input.NguoiSuDungId : input.KhacNguoiSuDung;
            line.CTKK_CoHuHong = input.CoHuHong || input.TinhTrang == "HuHong"; line.CTKK_ThieuPhuKien = input.ThieuPhuKien || input.TinhTrang == "ThieuPhuKien";
            if (line.CTKK_CoHuHong || line.CTKK_ThieuPhuKien || input.KhacNguoiSuDung)
                Require(Clean(input.GhiChu) != null && Clean(input.BangChung) != null, "Ghi mô tả và bằng chứng cho hỏng, thiếu phụ kiện hoặc khác người sử dụng.", 400);
            var evidence = input.XacNhanCanCu && Clean(input.BangChung) != null;
            line.CTKK_CanXacMinh = book?.XungDot == true || (book?.DangBaoTri == true || line.CTKK_TrangThaiDuKien is "DangBaoTri" or "ChoBaoTri") && !evidence ||
                line.CTKK_DauVetNguon != null && book != null && line.CTKK_DauVetNguon != book.DauVet && !evidence ||
                line.CTKK_PBDuKienID.HasValue && !input.PhongBanId.HasValue ||
                line.CTKK_NguoiNhanDuKienID.HasValue && !input.NguoiSuDungId.HasValue && !(input.KhacNguoiSuDung && evidence) ||
                !line.CTKK_NguoiNhanDuKienID.HasValue && Clean(line.CTKK_NguoiNhanDuKienSnapshot) != null && !evidence;
            if (Clean(line.CTKK_SerialDuKien) != null && Clean(input.Serial) != null && Normal(line.CTKK_SerialDuKien) != Normal(input.Serial))
                line.CTKK_CanXacMinh |= !(evidence && book != null && Normal(book.Serial) == Normal(input.Serial) && line.CTKK_DauVetNguon != book.DauVet);
        }
        line.CTKK_BangChung = Clean(input.BangChung); line.CTKK_XacNhanCanCu = input.XacNhanCanCu;
        line.CTKK_GhiChu = Clean(input.GhiChu); line.CTKK_NguoiGhiNhanID = actor.Id; line.CTKK_ThoiDiemGhiNhanUTC = DateTime.UtcNow;
        line.CTKK_TrangThaiDoiSoat = "ChuaDoiSoat"; line.CTKK_NguoiDoiSoatID = null; line.CTKK_ThoiDiemDoiSoatUTC = null;
        line.CTKK_KetQua = Classify(line);
    }
    private static string Classify(tblChiTietKiemKe line)
    {
        if (line.CTKK_LoaiDong == "NgoaiSo") return "Thua";
        if (line.CTKK_LoaiDong == "NgoaiPhamVi") return "NgoaiPhamVi";
        if (!line.CTKK_DaTimThay.HasValue) return line.CTKK_CanXacMinh ? "CanXacMinh" : "ChuaKiem";
        if (!line.CTKK_DaTimThay.Value) return "Thieu";
        if (line.CTKK_CanXacMinh) return "CanXacMinh";
        var flags = new[] { (line.CTKK_CoSaiPhongBan, "SaiPhongBan"), (line.CTKK_CoSaiViTri, "SaiViTri"), (line.CTKK_CoSaiNguoiSuDung, "SaiNguoiSuDung"), (line.CTKK_CoHuHong, "Hong"), (line.CTKK_ThieuPhuKien, "ThieuPhuKien") }.Where(x => x.Item1).ToArray();
        return flags.Length > 1 ? "NhieuSaiLech" : flags.Length == 1 ? flags[0].Item2 : "Khop";
    }
    private async Task CheckObservedIdentity(int docId, int lineId, string? code, string? serial, int? assetId)
    {
        var c = Normal(code); var s = Normal(serial);
        var others = await db.tblChiTietKiemKe.AsNoTracking().Where(x => x.KK_ID == docId && x.CTKK_ID != lineId)
            .Select(x => new { x.CTKK_MaNhanDienThucTe, x.CTKK_SerialThucTe }).ToListAsync();
        Require(!others.Any(x => c.Length > 0 && Normal(x.CTKK_MaNhanDienThucTe) == c || s.Length > 0 && Normal(x.CTKK_SerialThucTe) == s), "Mã/serial quan sát đã được ghi ở dòng khác trong đợt.");
        if (assetId.HasValue && (c.Length > 0 || s.Length > 0))
        {
            var known = await db.tblTaiSan.AsNoTracking().Where(x => x.TS_ID != assetId && (c != "" && x.TS_MaTaiSan.Trim().ToUpper() == c || s != "" && x.TS_Serial != null && x.TS_Serial.Trim().ToUpper() == s))
                .Select(x => x.TS_MaTaiSan).Take(3).ToListAsync();
            Require(known.Count == 0, "Mã/serial thuộc tài sản khác: " + string.Join(", ", known) + ". Kiểm tra đúng dòng hoặc ghi phát hiện ngoài danh sách.");
        }
    }
    [HttpGet]
    public async Task<IActionResult> PhatHienNgoaiDanhSach([FromRoute] int id, string? keyword = null)
    {
        if (keyword?.Length > 150) return BadRequest("Từ khóa tối đa 150 ký tự.");
        var doc = await Visible(id); if (doc == null) return NotFound();
        if (doc.KK_LaDuLieuCu || doc.KK_TrangThai != "DangKiemKe") return Conflict("Chỉ thêm phát hiện khi đợt đang kiểm kê.");
        return View("PhatHien", await PrepareDiscovery(id, new() { RequestKey = Key(), PhienBan = doc.KK_PhienBan, CachGhiNhan = "CoMat", PhongBanId = doc.PB_ID }, keyword));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PhatHienNgoaiDanhSach([FromRoute] int id, KiemKePhatHienInput input)
    {
        var result = await Write("PhatHien", id, null, input, async (actor, doc, _) =>
        {
            Require(doc!.KK_TrangThai == "DangKiemKe", "Đợt không còn mở.");
            Require(Clean(input.TenThucTe) != null && (Clean(input.MaNhanDien) != null || Clean(input.Serial) != null) && Clean(input.ViTri) != null && Clean(input.BangChung) != null, "Phát hiện phải có tên, mã/serial quan sát, vị trí và bằng chứng.", 400);
            tblChiTietKiemKe line;
            if (input.TaiSanId.HasValue)
            {
                Require(!await db.tblChiTietKiemKe.AnyAsync(x => x.KK_ID == id && x.TS_ID == input.TaiSanId), "Tài sản đã nằm trong danh sách; ghi nhận trên dòng gốc.");
                var book = (await ReadBooks([input.TaiSanId.Value])).SingleOrDefault(); Require(book != null, "Tài sản đã biết không tồn tại.", 400);
                line = NewSnapshot(id, book!, "NgoaiPhamVi");
            }
            else
            {
                var c = Normal(input.MaNhanDien); var s = Normal(input.Serial);
                var existing = await db.tblTaiSan.AsNoTracking().Where(x => c != "" && x.TS_MaTaiSan.Trim().ToUpper() == c || s != "" && x.TS_Serial != null && x.TS_Serial.Trim().ToUpper() == s).Select(x => x.TS_MaTaiSan).Take(5).ToListAsync();
                Require(existing.Count == 0, "Mã/serial đã có trong hệ thống: " + string.Join(", ", existing) + ". Chọn tài sản đã biết; không tạo dòng ngoài sổ.");
                var discovery = Clean(input.MaPhatHien) ?? "PH-" + Guid.NewGuid().ToString("N")[..20];
                Require(Regex.IsMatch(discovery, "^[A-Za-z0-9][A-Za-z0-9._-]{0,49}$"), "Mã phát hiện chỉ dùng chữ không dấu, số, dấu chấm, gạch dưới/gạch ngang.", 400);
                line = new() { KK_ID = id, TS_ID = null, CTKK_LoaiDong = "NgoaiSo", CTKK_MaPhatHien = discovery };
            }
            line.CTKK_TenThucTe = input.TenThucTe.Trim();
            await Observe(doc, line, input, actor); db.tblChiTietKiemKe.Add(line); return (doc, line);
        });
        return await FormResponse(result, "PhatHien", async () => await PrepareDiscovery(id, input));
    }
    private async Task<KiemKePhatHienForm> PrepareDiscovery(int id, KiemKePhatHienInput input, string? keyword = null)
    {
        var query = db.tblTaiSan.AsNoTracking(); keyword = Clean(keyword);
        if (keyword != null) query = query.Where(x => x.TS_MaTaiSan.Contains(keyword) || x.TS_TenTaiSan.Contains(keyword) || x.TS_Serial != null && x.TS_Serial.Contains(keyword));
        return new()
        {
            DotId = id, RequestKey = input.RequestKey, PhienBan = input.PhienBan, TaiSanId = input.TaiSanId, TenThucTe = input.TenThucTe, MaPhatHien = input.MaPhatHien,
            CachGhiNhan = input.CachGhiNhan, TinhTrang = input.TinhTrang, PhongBanId = input.PhongBanId, NguoiSuDungId = input.NguoiSuDungId, NguoiSuDung = input.NguoiSuDung,
            MaNhanDien = input.MaNhanDien, Serial = input.Serial, ViTri = input.ViTri, GhiChu = input.GhiChu, BangChung = input.BangChung, LyDo = input.LyDo,
            CoHuHong = input.CoHuHong, ThieuPhuKien = input.ThieuPhuKien, XacNhanCanCu = input.XacNhanCanCu, DaKiemTraThucTe = input.DaKiemTraThucTe,
            KhacNguoiSuDung = input.KhacNguoiSuDung, KhongApDungViTri = input.KhongApDungViTri, TenTaiSanThucTe = input.TenTaiSanThucTe,
            PhongBans = await Departments(), NguoiDungs = await ActualPeople(), Keyword = keyword,
            TaiSans = await query.OrderBy(x => x.TS_MaTaiSan).ThenBy(x => x.TS_ID).Take(50).Select(x => new KiemKeSach { Id = x.TS_ID, Ma = x.TS_MaTaiSan, Ten = x.TS_TenTaiSan, Serial = x.TS_Serial }).ToListAsync()
        };
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> KiemKeLai([FromRoute] int id, KiemKeRequest input)
    {
        var parent = await db.tblChiTietKiemKe.AsNoTracking().Where(x => x.CTKK_ID == id).Select(x => x.KK_ID).SingleOrDefaultAsync();
        return TransitionResponse(await Write("KiemKeLai", null, id, input, (_, doc, line) =>
        {
            Require(doc!.KK_TrangThai == "DangKiemKe", "Đợt không còn mở.");
            Require(line!.CTKK_KetQua == "CanXacMinh" || line.CTKK_TrangThaiDoiSoat == "CanKiemKeLai", "Chỉ kiểm kê lại dòng cần xác minh/đã yêu cầu kiểm kê lại.");
            Require(Clean(input.LyDo) != null, "Cần lý do kiểm kê lại.", 400);
            line.CTKK_DaTimThay = null; line.CTKK_KetQua = "ChuaKiem"; line.CTKK_CanXacMinh = false; line.CTKK_XacNhanCanCu = false;
            line.CTKK_TinhTrangThucTe = null; line.CTKK_ViTriThucTe = null; line.CTKK_PBThucTeID = null; line.CTKK_NguoiSuDungThucTeID = null; line.CTKK_NguoiSuDungThucTe = null;
            line.CTKK_SerialThucTe = null; line.CTKK_MaNhanDienThucTe = null; line.CTKK_CoHuHong = false; line.CTKK_ThieuPhuKien = false;
            line.CTKK_CoSaiPhongBan = false; line.CTKK_CoSaiViTri = false; line.CTKK_CoSaiNguoiSuDung = false;
            line.CTKK_NguoiGhiNhanID = null; line.CTKK_ThoiDiemGhiNhanUTC = null; line.CTKK_TenThucTe = null;
            line.CTKK_BangChung = null; line.CTKK_GhiChu = null;
            line.CTKK_TrangThaiDoiSoat = "CanKiemKeLai"; line.CTKK_ThoiDiemDoiSoatUTC = null; line.CTKK_NguoiDoiSoatID = null; return Task.FromResult((doc, (tblChiTietKiemKe?)line));
        }), parent);
    }
    [HttpGet]
    public async Task<IActionResult> DoiSoat([FromRoute] int id)
    {
        var row = await VisibleLine(id); if (row == null) return NotFound();
        var doc = await Visible(row.Dong.KK_ID); if (doc!.KK_LaDuLieuCu || doc.KK_TrangThai != "DangKiemKe") return Conflict("Chỉ đối soát khi đợt đang kiểm kê.");
        return View(await PrepareReconcile(row, new()
        {
            RequestKey = Key(), PhienBan = row.Dong.CTKK_PhienBan, NguyenNhan = row.Dong.CTKK_LyDoChenhLech ?? "", HuongXuLy = row.Dong.CTKK_HuongXuLy ?? "",
            DeXuat = row.Dong.CTKK_DeXuatXuLy ?? "", TrangThai = row.Dong.CTKK_TrangThaiDoiSoat, NguoiDoiSoatId = row.Dong.CTKK_NguoiDoiSoatID
        }));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DoiSoat([FromRoute] int id, KiemKeDoiSoatInput input)
    {
        var result = await Write("DoiSoat", null, id, input, async (actor, doc, line) =>
        {
            Require(doc!.KK_TrangThai == "DangKiemKe", "Phiếu đã đóng băng.");
            Require(line!.CTKK_KetQua != "ChuaKiem", "Ghi nhận thực tế trước khi đối soát.");
            Require(line.CTKK_TrangThaiDoiSoat != "DaDoiSoat" || Clean(input.LyDo) != null, "Cần lý do khi sửa giải trình đã đối soát.", 400);
            Require(Clean(input.NguyenNhan) != null && Clean(input.DeXuat) != null && KiemKeUi.Directions.Contains(input.HuongXuLy), "Cần nguyên nhân, hướng xử lý và đề xuất hợp lệ.", 400);
            Require(input.TrangThai is "ChuaDoiSoat" or "CanKiemKeLai" or "DaDoiSoat", "Trạng thái đối soát không hợp lệ.", 400);
            if (input.NguoiDoiSoatId.HasValue)
                Require(await db.tblTaiKhoan.AnyAsync(x => x.TK_ID == input.NguoiDoiSoatId && x.TK_TrangThai && (x.TK_VaiTro == "Admin" || x.TK_VaiTro == "QuanLyTaiSan")), "Người được phân công đối soát phải có quyền quản lý.", 400);
            if (input.TrangThai == "DaDoiSoat")
            {
                Require(actor.Manager, "Chỉ người có quyền quản lý xác nhận đối soát.", 403);
                Require(line.CTKK_KetQua != "CanXacMinh" && !line.CTKK_CanXacMinh && line.CTKK_DaTimThay.HasValue, "Cần kiểm kê lại và giải quyết căn cứ chưa rõ trước khi xác nhận đối soát.");
                if (line.TS_ID.HasValue)
                {
                    var book = (await ReadBooks([line.TS_ID.Value])).SingleOrDefault();
                    Require(book != null && !book.XungDot, "Nguồn bàn giao hiện tại còn mâu thuẫn; đối soát tại phân hệ bàn giao trước.");
                    Require(line.CTKK_DauVetNguon == book!.DauVet || line.CTKK_XacNhanCanCu && Clean(line.CTKK_BangChung) != null, "Có biến động sau chốt; cần bổ sung bằng chứng và ghi nhận lại.");
                }
                line.CTKK_NguoiDoiSoatID = actor.Id; line.CTKK_ThoiDiemDoiSoatUTC = DateTime.UtcNow;
            }
            else { line.CTKK_NguoiDoiSoatID = input.NguoiDoiSoatId; line.CTKK_ThoiDiemDoiSoatUTC = null; }
            line.CTKK_TrangThaiDoiSoat = input.TrangThai; line.CTKK_LyDoChenhLech = input.NguyenNhan.Trim(); line.CTKK_HuongXuLy = input.HuongXuLy; line.CTKK_DeXuatXuLy = input.DeXuat.Trim(); return (doc, line);
        });
        return await FormResponse(result, "DoiSoat", async () => { var row = await VisibleLine(id); return row == null ? null : await PrepareReconcile(row, input); });
    }
    private async Task<KiemKeDoiSoatForm> PrepareReconcile(KiemKeDongRow row, KiemKeDoiSoatInput input) => new()
    {
        Dong = row, RequestKey = input.RequestKey, PhienBan = input.PhienBan, LyDo = input.LyDo, NguyenNhan = input.NguyenNhan,
        HuongXuLy = input.HuongXuLy, DeXuat = input.DeXuat, TrangThai = input.TrangThai, NguoiDoiSoatId = input.NguoiDoiSoatId, NguoiDoiSoats = await People(true)
    };
    [HttpGet]
    public async Task<IActionResult> CapNhatXuLy([FromRoute] int id)
    {
        var actor = await ReadActor(); if (actor?.Manager != true) return StatusCode(403);
        var row = await VisibleLine(id); if (row == null) return NotFound(); var doc = await Visible(row.Dong.KK_ID);
        if (doc!.KK_LaDuLieuCu || doc.KK_TrangThai != "HoanThanh" || !row.ChenhLech) return Conflict("Chỉ theo dõi xử lý chênh lệch của phiếu mới đã hoàn thành.");
        return View("XuLy", new KiemKeXuLyForm { Dong = row, RequestKey = Key(), PhienBan = row.Dong.CTKK_PhienBan, TrangThai = row.Dong.CTKK_TrangThaiXuLy, KetQua = row.Dong.CTKK_KetQuaXuLy ?? "", ThamChieu = row.Dong.CTKK_ThamChieuXuLy });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CapNhatXuLy([FromRoute] int id, KiemKeXuLyInput input)
    {
        var result = await Write("CapNhatXuLy", null, id, input, async (actor, doc, line) =>
        {
            Require(doc!.KK_TrangThai == "HoanThanh" && KiemKeUi.IsDifference(line!.CTKK_KetQua), "Chỉ cập nhật tiến độ chênh lệch đã phê duyệt.");
            Require(input.TrangThai is "ChuaXuLy" or "DangXuLy" or "DaXuLy" or "KhongCanXuLy", "Trạng thái xử lý không hợp lệ.", 400);
            Require(Clean(input.KetQua) != null, "Cần kết quả/giải trình xử lý.", 400);
            if (input.TrangThai == "DaXuLy") await ValidateReference(line!, input.ThamChieu);
            else if (Clean(input.ThamChieu) != null) await ValidateReference(line!, input.ThamChieu);
            // BR29: chỉ sáu trường hậu kiểm; phiên bản được tăng trong Write.
            line!.CTKK_TrangThaiXuLy = input.TrangThai; line.CTKK_KetQuaXuLy = input.KetQua.Trim(); line.CTKK_ThamChieuXuLy = Clean(input.ThamChieu);
            line.CTKK_NguoiXuLyID = actor.Id; line.CTKK_ThoiDiemXuLyUTC = DateTime.UtcNow; return (doc, line);
        });
        return await FormResponse(result, "XuLy", async () => { var row = await VisibleLine(id); return row == null ? null : new KiemKeXuLyForm { Dong = row, RequestKey = input.RequestKey, PhienBan = input.PhienBan, LyDo = input.LyDo, TrangThai = input.TrangThai, KetQua = input.KetQua, ThamChieu = input.ThamChieu }; });
    }
    private async Task ValidateReference(tblChiTietKiemKe line, string? reference)
    {
        Require(Clean(reference) != null && reference!.Contains(':'), "Tham chiếu dạng CP:mã-phiếu, BT:mã-phiếu, TL:mã-phiếu hoặc TS:mã-tài-sản.", 400);
        var parts = reference!.Split(':', 2, StringSplitOptions.TrimEntries); var code = parts[1]; int[] assetIds;
        Require(code.Length > 0, "Mã tham chiếu không được trống.", 400);
        if (parts[0].Equals("CP", StringComparison.OrdinalIgnoreCase))
            assetIds = await db.tblChiTietCapPhat.AsNoTracking().Where(x => x.CapPhat!.CP_MaPhieu == code && (x.CapPhat.CP_TrangThai == "DaBanGiao" || x.CapPhat.CP_TrangThai == "DaThuHoi")).Select(x => x.TS_ID).ToArrayAsync();
        else if (parts[0].Equals("BT", StringComparison.OrdinalIgnoreCase))
            assetIds = await db.tblBaoTriTaiSan.AsNoTracking().Where(x => x.BT_MaPhieu == code && (x.BT_TrangThai == "HoanThanh" || x.BT_TrangThai == "DaHoanThanh") && x.BT_NgayHoanThanh != null && x.BT_NgayHoanThanh <= Today).Select(x => x.TS_ID).ToArrayAsync();
        else if (parts[0].Equals("TL", StringComparison.OrdinalIgnoreCase))
            assetIds = await db.tblChiTietThanhLy.AsNoTracking().Where(x => x.ThanhLy!.TL_MaPhieu == code && (x.ThanhLy.TL_TrangThai == "HoanThanh" || x.ThanhLy.TL_TrangThai == "DaHoanThanh" || x.ThanhLy.TL_TrangThai == "DaThanhLy") && x.ThanhLy.TL_NgayThanhLy != null && x.ThanhLy.TL_NgayThanhLy <= Today).Select(x => x.TS_ID).ToArrayAsync();
        else if (parts[0].Equals("TS", StringComparison.OrdinalIgnoreCase))
        {
            var asset = await db.tblTaiSan.AsNoTracking().SingleOrDefaultAsync(x => x.TS_MaTaiSan == code);
            assetIds = asset == null ? [] : [asset.TS_ID];
            if (asset != null && line.TS_ID == asset.TS_ID)
            {
                var corrected = Clean(line.CTKK_ViTriThucTe) != null && Normal(asset.TS_ViTri) == Normal(line.CTKK_ViTriThucTe) && Normal(asset.TS_ViTri) != Normal(line.CTKK_ViTriDuKien) ||
                    Clean(line.CTKK_TinhTrangThucTe) != null && asset.TS_TinhTrang == line.CTKK_TinhTrangThucTe && asset.TS_TinhTrang != line.CTKK_TinhTrangDuKien ||
                    Clean(line.CTKK_SerialThucTe) != null && Normal(asset.TS_Serial) == Normal(line.CTKK_SerialThucTe) && Normal(asset.TS_Serial) != Normal(line.CTKK_SerialDuKien) ||
                    Clean(line.CTKK_TenThucTe) != null && Normal(asset.TS_TenTaiSan) == Normal(line.CTKK_TenThucTe) && Normal(asset.TS_TenTaiSan) != Normal(line.CTKK_TenTaiSanSnapshot);
                Require(corrected, "Hồ sơ tài sản tham chiếu chưa thể hiện điều chỉnh theo kết quả thực tế. Thực hiện tại phân hệ tài sản hoặc dùng chứng từ bàn giao/bảo trì/thanh lý đã hoàn tất.");
            }
        }
        else { Require(false, "Loại tham chiếu không được hỗ trợ.", 400); return; }
        Require(assetIds.Length > 0, "Hồ sơ tham chiếu không tồn tại hoặc chưa hoàn tất.");
        if (line.TS_ID.HasValue) Require(assetIds.Contains(line.TS_ID.Value), "Hồ sơ tham chiếu không thuộc tài sản kiểm kê.");
        else
        {
            var assets = await db.tblTaiSan.AsNoTracking().Where(x => assetIds.Contains(x.TS_ID)).Select(x => new { x.TS_MaTaiSan, x.TS_Serial }).ToListAsync();
            Require(assets.Any(x => Clean(line.CTKK_MaNhanDienThucTe) != null && Normal(x.TS_MaTaiSan) == Normal(line.CTKK_MaNhanDienThucTe) || Clean(line.CTKK_SerialThucTe) != null && Normal(x.TS_Serial) == Normal(line.CTKK_SerialThucTe)), "Tài sản trong hồ sơ không khớp mã/serial đã phát hiện ngoài sổ.");
        }
    }
    #endregion

    #region Tra cứu, tổng hợp, báo cáo và lịch sử
    private static readonly Expression<Func<tblKiemKeTaiSan, KiemKePhieuRow>> PhieuProjection = x => new()
    {
        Id = x.KK_ID, Ma = x.KK_MaPhieu, Ten = x.KK_TenDot, PhamVi = x.KK_LoaiPhamVi, PhongBan = x.PhongBan != null ? x.PhongBan.PB_TenPhongBan : null,
        NguoiKiemKe = x.NguoiKiemKe != null ? x.NguoiKiemKe.TK_HoTen : null, Ngay = x.KK_NgayKiemKe, TrangThai = x.KK_TrangThai, LaDuLieuCu = x.KK_LaDuLieuCu,
        DuKien = x.tblChiTietKiemKes.Count(d => d.CTKK_LoaiDong == "TrongSo"), DaKiem = x.tblChiTietKiemKes.Count(d => d.CTKK_LoaiDong == "TrongSo" && d.CTKK_DaTimThay != null),
        ChenhLech = x.tblChiTietKiemKes.Count(d => d.CTKK_KetQua != "Khop" && d.CTKK_KetQua != "ChuaKiem" && d.CTKK_KetQua != "BinhThuong")
    };
    private IQueryable<tblKiemKeTaiSan> FilterPhieu(Actor actor, string? keyword, string? state, int? department, DateOnly? from, DateOnly? to, bool legacy)
    {
        var query = Scope(db.tblKiemKeTaiSan.AsNoTracking(), actor);
        keyword = Clean(keyword); if (keyword != null) query = query.Where(x => x.KK_MaPhieu.Contains(keyword) || x.KK_TenDot != null && x.KK_TenDot.Contains(keyword));
        if (Clean(state) != null) query = query.Where(x => x.KK_TrangThai == state);
        if (department.HasValue) query = query.Where(x => x.PB_ID == department);
        if (from.HasValue) query = query.Where(x => x.KK_NgayKiemKe >= from);
        if (to.HasValue) query = query.Where(x => x.KK_NgayKiemKe <= to);
        if (legacy) query = query.Where(x => x.KK_LaDuLieuCu);
        return query;
    }
    [HttpGet]
    public Task<IActionResult> Index(string? keyword = null, string? trangThai = null, int? phongBanId = null, DateOnly? tuNgay = null, DateOnly? denNgay = null, bool chiLichSu = false, int page = 1) =>
        ListPhieu(keyword, trangThai, phongBanId, tuNgay, denNgay, chiLichSu, page);
    [HttpGet]
    public async Task<IActionResult> BaoCao(string? keyword = null, string? trangThai = null, int? phongBanId = null, DateOnly? tuNgay = null, DateOnly? denNgay = null, bool chiLichSu = false, int page = 1)
    {
        var actor = await ReadActor(); if (actor == null) return StatusCode(403);
        if (keyword?.Length > 150) return BadRequest("Từ khóa tối đa 150 ký tự.");
        if (tuNgay > denNgay) ModelState.AddModelError("denNgay", "Đến ngày không được trước từ ngày.");
        var query = FilterPhieu(actor, keyword, trangThai, phongBanId, tuNgay, denNgay, chiLichSu);
        var model = new KiemKeBaoCao
        {
            Keyword = keyword, TrangThai = trangThai, PhongBanId = phongBanId, TuNgay = tuNgay, DenNgay = denNgay, ChiLichSu = chiLichSu,
            PhongBans = await FilterDepartments(actor)
        };
        // Tổng hợp tại database cho toàn bộ bộ lọc, không phụ thuộc trang danh sách đang mở.
        model.TheoTrangThai = await query.GroupBy(x => x.KK_TrangThai).OrderBy(x => x.Key).Select(g => new KiemKeBaoCaoTrangThai
        {
            TrangThai = g.Key, SoDotMoi = g.Count(x => !x.KK_LaDuLieuCu), SoDotLichSu = g.Count(x => x.KK_LaDuLieuCu)
        }).ToListAsync();
        model.Total = model.TheoTrangThai.Sum(x => x.TongSo);
        model.SoDotLichSu = model.TheoTrangThai.Sum(x => x.SoDotLichSu);
        model.TheoPhamVi = await query.GroupBy(x => new { x.KK_LoaiPhamVi, x.PB_ID, Ten = x.PhongBan == null ? null : x.PhongBan.PB_TenPhongBan })
            .OrderBy(x => x.Key.KK_LoaiPhamVi).ThenBy(x => x.Key.Ten).ThenBy(x => x.Key.PB_ID).Select(g => new KiemKeBaoCaoPhamVi
            {
                LoaiPhamVi = g.Key.KK_LoaiPhamVi, PhongBanId = g.Key.PB_ID, PhongBan = g.Key.Ten, SoDot = g.Count(),
                SoDotLichSu = g.Count(x => x.KK_LaDuLieuCu), SoDotDaChot = g.Count(x => !x.KK_LaDuLieuCu && x.KK_ThoiDiemChotUTC != null)
            }).ToListAsync();
        model.SoDotDaChot = model.TheoPhamVi.Sum(x => x.SoDotDaChot);
        var modernIds = query.Where(x => !x.KK_LaDuLieuCu && x.KK_ThoiDiemChotUTC != null).Select(x => x.KK_ID);
        var lines = db.tblChiTietKiemKe.AsNoTracking().Where(x => modernIds.Contains(x.KK_ID));
        model.TongHop = await Summarize(lines);
        var groups = await lines.GroupBy(x => new { x.KiemKe!.KK_LoaiPhamVi, x.KiemKe.PB_ID }).Select(g => new KiemKeBaoCaoPhamVi
        {
            LoaiPhamVi = g.Key.KK_LoaiPhamVi, PhongBanId = g.Key.PB_ID,
            DuKien = g.Count(x => x.CTKK_LoaiDong == "TrongSo"), DaKiem = g.Count(x => x.CTKK_LoaiDong == "TrongSo" && x.CTKK_DaTimThay != null),
            ChenhLech = g.Count(x => x.CTKK_KetQua != "Khop" && x.CTKK_KetQua != "ChuaKiem" && x.CTKK_KetQua != "BinhThuong"),
            ChuaDoiSoat = g.Count(x => x.CTKK_KetQua != "Khop" && x.CTKK_KetQua != "ChuaKiem" && x.CTKK_KetQua != "BinhThuong" && x.CTKK_TrangThaiDoiSoat != "DaDoiSoat"),
            DaXuLy = g.Count(x => x.CTKK_TrangThaiXuLy == "DaXuLy")
        }).ToListAsync();
        var byScope = groups.ToDictionary(x => (x.LoaiPhamVi, x.PhongBanId));
        foreach (var row in model.TheoPhamVi)
            if (byScope.TryGetValue((row.LoaiPhamVi, row.PhongBanId), out var counts))
            { row.DuKien = counts.DuKien; row.DaKiem = counts.DaKiem; row.ChenhLech = counts.ChenhLech; row.ChuaDoiSoat = counts.ChuaDoiSoat; row.DaXuLy = counts.DaXuLy; }
        var legacyIds = query.Where(x => x.KK_LaDuLieuCu).Select(x => x.KK_ID);
        model.KetQuaLichSu = await db.tblChiTietKiemKe.AsNoTracking().Where(x => legacyIds.Contains(x.KK_ID))
            .GroupBy(x => x.CTKK_KetQua ?? "Chưa có kết quả").Select(g => new { KetQua = g.Key, SoDong = g.Count() }).ToDictionaryAsync(x => x.KetQua, x => x.SoDong);
        return View("BaoCao", model);
    }
    private async Task<List<SelectListItem>> FilterDepartments(Actor actor)
    {
        var visibleDepartments = Scope(db.tblKiemKeTaiSan, actor).Select(x => x.PB_ID); var manager = actor.Manager;
        return await db.tblPhongBan.AsNoTracking().Where(x => manager || visibleDepartments.Contains(x.PB_ID)).OrderBy(x => x.PB_TenPhongBan)
            .Select(x => new SelectListItem(x.PB_TenPhongBan, x.PB_ID.ToString())).ToListAsync();
    }
    private async Task<IActionResult> ListPhieu(string? keyword, string? state, int? department, DateOnly? from, DateOnly? to, bool legacy, int page)
    {
        var actor = await ReadActor(); if (actor == null) return StatusCode(403);
        if (keyword?.Length > 150) return BadRequest("Từ khóa tối đa 150 ký tự.");
        if (from > to) ModelState.AddModelError("denNgay", "Đến ngày không được trước từ ngày.");
        var query = FilterPhieu(actor, keyword, state, department, from, to, legacy);
        var model = new KiemKeIndex { Keyword = keyword, TrangThai = state, PhongBanId = department, TuNgay = from, DenNgay = to, ChiLichSu = legacy, Total = await query.CountAsync() };
        model.Page = Math.Clamp(page, 1, model.Pages);
        model.Items = await query.OrderByDescending(x => x.KK_NgayTaoUTC).ThenByDescending(x => x.KK_ID).Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).Select(PhieuProjection).ToListAsync();
        model.PhongBans = await FilterDepartments(actor);
        return View("Index", model);
    }
    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, string? keyword = null, string? ketQua = null, int page = 1)
    {
        var model = await DetailsModel(id, keyword, ketQua, page); return model == null ? NotFound() : View(model);
    }
    [HttpGet]
    public async Task<IActionResult> DanhSachKiemKe([FromRoute] int id, string? keyword = null, string? ketQua = null, int page = 1)
    {
        var model = await DetailsModel(id, keyword, ketQua, page); return model == null ? NotFound() : View("Details", model);
    }
    private async Task<KiemKeChiTiet?> DetailsModel(int id, string? keyword, string? result, int page, bool all = false)
    {
        var actor = await ReadActor(); if (actor == null) return null;
        var doc = await Visible(id); if (doc == null) return null;
        var query = db.tblChiTietKiemKe.AsNoTracking().Where(x => x.KK_ID == id);
        var summary = await Summarize(query); keyword = Clean(keyword);
        if (keyword?.Length > 150) keyword = keyword[..150];
        if (keyword != null) query = query.Where(x => x.CTKK_MaTaiSanSnapshot != null && x.CTKK_MaTaiSanSnapshot.Contains(keyword) || x.CTKK_TenTaiSanSnapshot != null && x.CTKK_TenTaiSanSnapshot.Contains(keyword) ||
            x.CTKK_SerialDuKien != null && x.CTKK_SerialDuKien.Contains(keyword) || x.CTKK_MaNhanDienThucTe != null && x.CTKK_MaNhanDienThucTe.Contains(keyword) || x.CTKK_SerialThucTe != null && x.CTKK_SerialThucTe.Contains(keyword) ||
            x.CTKK_TenThucTe != null && x.CTKK_TenThucTe.Contains(keyword) || doc.KK_LaDuLieuCu && x.TaiSan != null && (x.TaiSan.TS_MaTaiSan.Contains(keyword) || x.TaiSan.TS_TenTaiSan.Contains(keyword)));
        if (result == "ChenhLech") query = query.Where(x => x.CTKK_KetQua != "Khop" && x.CTKK_KetQua != "ChuaKiem" && x.CTKK_KetQua != "BinhThuong");
        else if (Clean(result) != null) query = query.Where(x => x.CTKK_KetQua == result);
        var model = new KiemKeChiTiet { Phieu = doc, TongHop = summary, Keyword = keyword, KetQua = result, Total = await query.CountAsync(), CanManage = actor.Manager,
            CanRecord = !doc.KK_LaDuLieuCu && doc.KK_TrangThai == "DangKiemKe" && (actor.Manager || doc.KK_NguoiKiemKeID == actor.Id) };
        if (actor.Manager && !doc.KK_LaDuLieuCu && doc.KK_TrangThai == "DangKiemKe") model.NguoiKiemKes = await People();
        if (all) model.PageSize = Math.Max(1, model.Total);
        model.Page = Math.Clamp(page, 1, model.Pages);
        var lines = await query.Include(x => x.TaiSan).Include(x => x.PhongBanThucTe).Include(x => x.NguoiDoiSoat).OrderBy(x => x.CTKK_ID).Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).ToListAsync();
        var books = (await ReadBooks(lines.Where(x => x.TS_ID.HasValue).Select(x => x.TS_ID!.Value).Distinct().ToArray())).ToDictionary(x => x.Id);
        model.Items = lines.Select(x => new KiemKeDongRow { Dong = x, HienTai = x.TS_ID.HasValue ? books.GetValueOrDefault(x.TS_ID.Value) : null }).ToList();
        if (!doc.KK_LaDuLieuCu)
        {
            model.Events = await db.tblSuKienKiemKe.AsNoTracking().Where(x => x.KK_ID == id).OrderByDescending(x => x.SKK_ID).Take(5).ToListAsync();
            model.CanhBaos = model.Items.Where(x => x.HienTai?.XungDot == true).Select(x => x.HienTai!.Ma + ": " + x.HienTai.CanhBao).ToList();
            if (model.Items.Any(x => x.BienDong)) model.CanhBaos.Add("Có biến động sổ sau thời điểm chốt. Snapshot vẫn được giữ nguyên; cần kiểm tra bằng chứng và thời điểm nghiệp vụ liên quan.");
        }
        return model;
    }
    private async Task<KiemKeTongHop> Summarize(IQueryable<tblChiTietKiemKe> query) => await query.GroupBy(_ => 1).Select(g => new KiemKeTongHop
    {
        TongDong = g.Count(), DuKien = g.Count(x => x.CTKK_LoaiDong == "TrongSo"), DaTimThay = g.Count(x => x.CTKK_LoaiDong == "TrongSo" && x.CTKK_DaTimThay == true),
        KhongTimThay = g.Count(x => x.CTKK_LoaiDong == "TrongSo" && x.CTKK_DaTimThay == false), ChuaKiem = g.Count(x => x.CTKK_LoaiDong == "TrongSo" && x.CTKK_DaTimThay == null),
        Khop = g.Count(x => x.CTKK_KetQua == "Khop"), Thua = g.Count(x => x.CTKK_LoaiDong == "NgoaiSo"), NgoaiPhamVi = g.Count(x => x.CTKK_LoaiDong == "NgoaiPhamVi"),
        CanXacMinh = g.Count(x => x.CTKK_KetQua == "CanXacMinh" || x.CTKK_CanXacMinh), SaiPhongBan = g.Count(x => x.CTKK_CoSaiPhongBan), SaiViTri = g.Count(x => x.CTKK_CoSaiViTri),
        SaiNguoiDung = g.Count(x => x.CTKK_CoSaiNguoiSuDung), Hong = g.Count(x => x.CTKK_CoHuHong), ThieuPhuKien = g.Count(x => x.CTKK_ThieuPhuKien), NhieuSaiLech = g.Count(x => x.CTKK_KetQua == "NhieuSaiLech"),
        DongChenhLech = g.Count(x => x.CTKK_KetQua != "Khop" && x.CTKK_KetQua != "ChuaKiem" && x.CTKK_KetQua != "BinhThuong"),
        ChuaDoiSoat = g.Count(x => x.CTKK_KetQua != "Khop" && x.CTKK_KetQua != "ChuaKiem" && x.CTKK_TrangThaiDoiSoat != "DaDoiSoat"), DaXuLy = g.Count(x => x.CTKK_TrangThaiXuLy == "DaXuLy")
    }).SingleOrDefaultAsync() ?? new();
    [HttpGet]
    public async Task<IActionResult> LichSu([FromRoute] int id, int page = 1)
    {
        var doc = await Visible(id); if (doc == null) return NotFound();
        var query = db.tblSuKienKiemKe.AsNoTracking().Where(x => x.KK_ID == id);
        var model = new KiemKeLichSu { Phieu = doc, Total = await query.CountAsync() }; model.Page = Math.Clamp(page, 1, model.Pages);
        model.Items = await query.OrderByDescending(x => x.SKK_ID).Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).ToListAsync(); return View(model);
    }
    [HttpGet]
    public async Task<IActionResult> InBienBan([FromRoute] int id)
    {
        var model = await DetailsModel(id, null, null, 1, true); return model == null ? NotFound() : View(model);
    }
    [HttpGet]
    public async Task<IActionResult> XuatBaoCao(int? id = null, string? keyword = null, string? trangThai = null, int? phongBanId = null, DateOnly? tuNgay = null, DateOnly? denNgay = null, bool chiLichSu = false)
    {
        var actor = await ReadActor(); if (actor == null) return StatusCode(403);
        var docs = FilterPhieu(actor, keyword, trangThai, phongBanId, tuNgay, denNgay, chiLichSu);
        if (id.HasValue) { docs = docs.Where(x => x.KK_ID == id); if (!await docs.AnyAsync()) return NotFound(); }
        var ids = docs.Select(x => x.KK_ID);
        var rows = db.tblChiTietKiemKe.AsNoTracking().Where(x => ids.Contains(x.KK_ID)).Include(x => x.KiemKe).Include(x => x.TaiSan).OrderBy(x => x.KK_ID).ThenBy(x => x.CTKK_ID);
        var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(true), 4096, leaveOpen: true))
        {
            await writer.WriteLineAsync(CsvRow("Mã phiếu", "Tên đợt", "Dữ liệu lịch sử", "Trạng thái", "Mã tài sản/phát hiện", "Tên tài sản", "Loại dòng", "Dự kiến (1 tài sản)", "Tìm thấy", "Kết quả", "Phòng ban tại chốt", "Người giữ tại chốt", "Vị trí tại chốt", "Vị trí thực tế", "Tình trạng thực tế", "Sai phòng ban", "Sai vị trí", "Sai người dùng", "Hỏng", "Thiếu phụ kiện", "Số cờ sai lệch", "Nguyên nhân", "Đề xuất", "Đối soát", "Xử lý hậu kiểm", "Tham chiếu", "Bằng chứng"));
            await foreach (var row in rows.AsAsyncEnumerable())
            {
                var count = new[] { row.CTKK_CoSaiPhongBan, row.CTKK_CoSaiViTri, row.CTKK_CoSaiNguoiSuDung, row.CTKK_CoHuHong, row.CTKK_ThieuPhuKien }.Count(x => x);
                await writer.WriteLineAsync(CsvRow(row.KiemKe!.KK_MaPhieu, row.KiemKe.KK_TenDot, row.KiemKe.KK_LaDuLieuCu ? "Có: không có snapshot, không gộp KPI mới" : "Không",
                    KiemKeUi.Label(row.KiemKe.KK_TrangThai), row.CTKK_MaTaiSanSnapshot ?? row.CTKK_MaPhatHien ?? row.TaiSan?.TS_MaTaiSan,
                    row.CTKK_TenTaiSanSnapshot ?? row.CTKK_TenThucTe ?? row.TaiSan?.TS_TenTaiSan, KiemKeUi.Label(row.CTKK_LoaiDong), row.KiemKe.KK_LaDuLieuCu ? "" : row.CTKK_LoaiDong == "TrongSo" ? "1" : "0",
                    row.CTKK_DaTimThay.HasValue ? row.CTKK_DaTimThay.Value ? "1" : "0" : "Chưa kiểm", KiemKeUi.Label(row.CTKK_KetQua), row.CTKK_PhongBanDuKienSnapshot, row.CTKK_NguoiNhanDuKienSnapshot,
                    row.CTKK_ViTriDuKien, row.CTKK_ViTriThucTe, row.CTKK_TinhTrangThucTe, row.CTKK_CoSaiPhongBan ? "1" : "0", row.CTKK_CoSaiViTri ? "1" : "0", row.CTKK_CoSaiNguoiSuDung ? "1" : "0",
                    row.CTKK_CoHuHong ? "1" : "0", row.CTKK_ThieuPhuKien ? "1" : "0", count.ToString(CultureInfo.InvariantCulture), row.CTKK_LyDoChenhLech, row.CTKK_DeXuatXuLy, KiemKeUi.Label(row.CTKK_TrangThaiDoiSoat), KiemKeUi.Label(row.CTKK_TrangThaiXuLy), row.CTKK_ThamChieuXuLy, row.CTKK_BangChung));
            }
            await writer.FlushAsync();
        }
        stream.Position = 0; return File(stream, "text/csv; charset=utf-8", "kiem-ke-" + Today.ToString("yyyyMMdd") + ".csv");
    }
    private static string CsvRow(params string?[] values) => string.Join(",", values.Select(value =>
    {
        var text = value ?? ""; var trimmed = text.TrimStart();
        if (trimmed.StartsWith('=') || trimmed.StartsWith('+') || trimmed.StartsWith('-') || trimmed.StartsWith('@') || text.StartsWith('\t') || text.StartsWith('\r') || text.StartsWith('\n')) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }));
    #endregion
}
