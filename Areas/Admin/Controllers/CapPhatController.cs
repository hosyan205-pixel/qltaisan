using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using System.Text.Json;
using DoAn.Areas.Admin.Models;
using DoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;
using static DoAn.Areas.Admin.Models.GiaoNhanStates;

namespace DoAn.Areas.Admin.Controllers;

[Area("Admin")]
public class CapPhatController(DataContext context, ILogger<CapPhatController> logger) : Controller
{
    #region Phân quyền và tra cứu phiếu

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
            // Session chỉ xác định danh tính; kiểm tra lại quyền/khóa tài khoản tại DB mỗi request.
            var account = await context.tblTaiKhoan.AsNoTracking().Where(x=>x.TK_ID==accountId)
                .Select(x=>new{x.TK_TrangThai,x.TK_VaiTro}).SingleOrDefaultAsync();
            var manager=account?.TK_TrangThai==true&&account.TK_VaiTro=="Admin";
            var ownRead=account?.TK_TrangThai==true&&account.TK_VaiTro=="NhanVien"&&HttpMethods.IsGet(Request.Method)&&
                filterContext.ActionDescriptor.RouteValues.TryGetValue("action",out var action)&&action is "Index" or "Details" or "History" or "ActiveAssignments" or "Print";
            if (!manager&&!ownRead)
            {
                filterContext.Result = StatusCode(StatusCodes.Status403Forbidden);
                return;
            }
            HttpContext.Items["GiaoNhanCanManage"]=manager;HttpContext.Items["GiaoNhanViewer"]=accountId.Value;
            ViewData["CanManage"]=manager;
        }
        catch (Exception ex) when (IsDatabaseError(ex))
        {
            logger.LogError(ex, "Không thể kiểm tra quyền truy cập module cấp phát.");
            filterContext.Result = DatabaseUnavailable();
            return;
        }

        var executed = await next();
        if (executed.Exception is Exception databaseError && IsDatabaseError(databaseError))
        {
            logger.LogError(databaseError, "Không thể đọc dữ liệu cấp phát ở {Action}.", filterContext.ActionDescriptor.DisplayName);
            executed.ExceptionHandled = true;
            executed.Result = DatabaseUnavailable();
        }
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? keyword = null, string? nguoiNhan = null, int? phongBanId = null,
        string? trangThai = null, string? thuHoi = null, DateOnly? tuNgay = null, DateOnly? denNgay = null,
        bool phongBanNgungHoatDong = false, string sort = "date_desc", int page = 1, int pageSize = 10,
        string? loaiPhieu = null, bool canDoiSoat = false, bool nguoiNhanNgungHoatDong = false)
    {
        keyword = keyword?.Trim();
        nguoiNhan = nguoiNhan?.Trim();
        trangThai = trangThai?.Trim();
        pageSize = pageSize is 10 or 20 or 50 ? pageSize : 10;
        sort = sort is "date_asc" or "code_asc" or "code_desc" ? sort : "date_desc";
        thuHoi = thuHoi is "none" or "partial" or "all" ? thuHoi : null;
        ModelState.Remove(nameof(sort));
        ModelState.Remove(nameof(pageSize));
        ModelState.Remove(nameof(thuHoi));
        if (tuNgay.HasValue && denNgay.HasValue && tuNgay > denNgay)
            ModelState.AddModelError(nameof(denNgay), "Đến ngày phải bằng hoặc sau từ ngày.");

        var query = ScopeReceipts(context.tblCapPhatTaiSan.AsNoTracking());
        if (loaiPhieu == "Cu") query = query.Where(x => x.CP_LoaiPhieu == null);
        else if (loaiPhieu is "Giao" or "ThuHoi" or "Chuyen") query = query.Where(x => x.CP_LoaiPhieu == loaiPhieu);
        if (canDoiSoat) query = query.Where(x => x.tblChiTietCapPhats.Any(d => d.CTCP_TrangThai == null));
        if (nguoiNhanNgungHoatDong) query = query.Where(x => x.NguoiNhan != null && !x.NguoiNhan.TK_TrangThai);
        if (!string.IsNullOrEmpty(keyword))
            query = query.Where(x => x.CP_MaPhieu.Contains(keyword) || x.tblChiTietCapPhats.Any(d =>
                d.TaiSan != null && (d.TaiSan.TS_MaTaiSan.Contains(keyword) || d.TaiSan.TS_TenTaiSan.Contains(keyword))));
        if (!string.IsNullOrEmpty(nguoiNhan)) query = query.Where(x => (x.CP_NguoiNhan != null && x.CP_NguoiNhan.Contains(nguoiNhan)) || (x.NguoiNhan != null && x.NguoiNhan.TK_HoTen.Contains(nguoiNhan)));
        if (phongBanId.HasValue) query = query.Where(x => x.PB_ID == phongBanId);
        if (!string.IsNullOrEmpty(trangThai)) query = query.Where(x => x.CP_TrangThai == trangThai);
        if (phongBanNgungHoatDong) query = query.Where(x => x.PhongBan == null || x.PhongBan.PB_TrangThai != true);
        if (tuNgay.HasValue) query = query.Where(x => x.CP_NgayCapPhat >= tuNgay);
        if (denNgay.HasValue) query = query.Where(x => x.CP_NgayCapPhat <= denNgay);
        query = thuHoi switch
        {
            "none" => query.Where(x => x.tblChiTietCapPhats.Any() && !x.tblChiTietCapPhats.Any(d => d.CTCP_NgayThuHoi != null)),
            "partial" => query.Where(x => x.tblChiTietCapPhats.Any(d => d.CTCP_NgayThuHoi != null) && x.tblChiTietCapPhats.Any(d => d.CTCP_NgayThuHoi == null)),
            "all" => query.Where(x => x.tblChiTietCapPhats.Any() && !x.tblChiTietCapPhats.Any(d => d.CTCP_NgayThuHoi == null)),
            _ => query
        };
        var visibleDepartmentIds = ScopeReceipts(context.tblCapPhatTaiSan).Select(x => x.PB_ID);
        var ownDepartments = EmployeeView;
        var model = new CapPhatIndex
        {
            Keyword = keyword, NguoiNhan = nguoiNhan, PhongBanId = phongBanId, TrangThai = trangThai,
            LoaiPhieu = loaiPhieu, CanDoiSoat = canDoiSoat, NguoiNhanNgungHoatDong = nguoiNhanNgungHoatDong,
            ThuHoi = thuHoi, TuNgay = tuNgay, DenNgay = denNgay, Sort = sort, PageSize = pageSize,
            PhongBanNgungHoatDong = phongBanNgungHoatDong, Total = await query.CountAsync(),
            PhongBans = await context.tblPhongBan.AsNoTracking().Where(x=>!ownDepartments||visibleDepartmentIds.Contains(x.PB_ID)).OrderBy(x => x.PB_TenPhongBan).ThenBy(x => x.PB_ID)
                .Select(x => new SelectListItem(x.PB_TenPhongBan + (x.PB_TrangThai == true ? "" : " (ngừng hoạt động)"), x.PB_ID.ToString())).ToListAsync(),
            TrangThais = await ScopeReceipts(context.tblCapPhatTaiSan.AsNoTracking()).Select(x => x.CP_TrangThai).Distinct().OrderBy(x => x).ToListAsync()
        };
        SetPage(model, page);
        var ordered = sort switch
        {
            "date_asc" => query.OrderBy(x => x.CP_NgayCapPhat).ThenBy(x => x.CP_ID),
            "code_asc" => query.OrderBy(x => x.CP_MaPhieu).ThenBy(x => x.CP_ID),
            "code_desc" => query.OrderByDescending(x => x.CP_MaPhieu).ThenBy(x => x.CP_ID),
            _ => query.OrderByDescending(x => x.CP_NgayCapPhat).ThenBy(x => x.CP_ID)
        };
        model.Items = await ordered.Skip((model.Page - 1) * pageSize).Take(pageSize).Select(ReceiptProjection).ToListAsync();
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute] int id, int page = 1, int eventPage = 1)
    {
        var receipt = await ScopeReceipts(context.tblCapPhatTaiSan.AsNoTracking()).Where(x => x.CP_ID == id)
            .Select(ReceiptProjection).SingleOrDefaultAsync();
        if (receipt == null) return NotFound();
        var model = new CapPhatDetails { Phieu = receipt, Total = receipt.SoTaiSan, PageSize = 50 };
        SetPage(model, page);
        model.Items = await context.tblChiTietCapPhat.AsNoTracking().Where(x => x.CP_ID == id)
            .OrderBy(x => x.CTCP_ID).Skip((model.Page - 1) * model.PageSize).Take(model.PageSize)
            .Select(LineProjection).ToListAsync();
        var visibleIds=ScopeReceipts(context.tblCapPhatTaiSan).Select(x=>x.CP_ID);
        var events = context.tblSuKienCapPhat.AsNoTracking().Where(x => visibleIds.Contains(x.CP_ID)&&(x.CP_ID == id || x.CapPhat!.tblChiTietCapPhats.Any(d => d.DongNguon != null && d.DongNguon.CP_ID == id)));
        model.EventTotal = await events.CountAsync();
        model.EventPage = Math.Clamp(eventPage,1,model.EventPages);
        model.Events = await events.OrderBy(x => x.SK_ThoiDiemUTC).ThenBy(x => x.SK_ID).Skip((model.EventPage-1)*20).Take(20)
            .Select(x => new GiaoNhanAuditRow { Id=x.SK_ID, Type=x.SK_Loai, Actor=x.SK_NguoiThucHienSnapshot, AtUtc=x.SK_ThoiDiemUTC, Reason=x.SK_LyDo }).ToListAsync();
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> History([FromRoute] int id, int page = 1)
    {
        var asset = await context.tblTaiSan.AsNoTracking().Where(x => x.TS_ID == id)
            .Select(x => new { x.TS_MaTaiSan, x.TS_TenTaiSan }).SingleOrDefaultAsync();
        if (asset == null) return NotFound();
        var visibleIds=ScopeReceipts(context.tblCapPhatTaiSan).Select(x=>x.CP_ID);
        var query = context.tblChiTietCapPhat.AsNoTracking().Where(x => x.TS_ID == id&&visibleIds.Contains(x.CP_ID));
        if(EmployeeView&&!await query.AnyAsync())return NotFound();
        var model = new CapPhatHistory
        {
            TaiSanId = id, MaTaiSan = asset.TS_MaTaiSan, TenTaiSan = asset.TS_TenTaiSan,
            Total = await query.CountAsync(), PageSize = 20
        };
        SetPage(model, page);
        model.Items = await query.OrderBy(x => x.CapPhat!.CP_NgayCapPhat).ThenBy(x => x.CTCP_ID)
            .Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).Select(LineProjection).ToListAsync();
        return View("LichSu", model);
    }

    private static void SetPage(CapPhatPage model, int page) => model.Page = Math.Clamp(page, 1, model.TotalPages);

    // Pomelo có thể bọc lỗi kết nối tạm thời trong InvalidOperationException.
    private static bool IsDatabaseError(Exception error) =>
        error is DbException or InvalidOperationException { InnerException: DbException };

    private ObjectResult DatabaseUnavailable() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        "Không thể kết nối cơ sở dữ liệu. Vui lòng thử lại sau hoặc liên hệ quản trị viên.");

    private static readonly Expression<Func<tblCapPhatTaiSan, CapPhatRow>> ReceiptProjection = x => new CapPhatRow
    {
        LoaiPhieu = x.CP_LoaiPhieu,
        SoCanDoiSoat = x.tblChiTietCapPhats.Count(d => d.CTCP_TrangThai == null),
        SoDangGiu = x.tblChiTietCapPhats.Count(d => d.CTCP_TrangThai == "DangHieuLuc"),
        SoDaChuyen = x.tblChiTietCapPhats.Count(d => d.CTCP_TrangThai == "DaChuyen"),
        PhienBan = x.CP_PhienBan, NgayDuKien=x.CP_NgayDuKien, NgayTraDuKien=x.CP_NgayTraDuKien,
        NguoiLap=x.NguoiLap != null ? x.NguoiLap.TK_HoTen : null,
        NguoiNhanXacMinh=x.NguoiNhan != null ? x.NguoiNhan.TK_HoTen : null,
        NgayLapUTC = x.CP_NgayLapUTC, ThucHienUTC = x.CP_ThucHienUTC, XacNhanUTC = x.CP_XacNhanUTC,
        NguoiXacNhan = x.NguoiXacNhan != null ? x.NguoiXacNhan.TK_HoTen : null,
        Id = x.CP_ID, MaPhieu = x.CP_MaPhieu, NgayCapPhat = x.CP_NgayCapPhat,
        NguoiGiao = x.CP_NguoiGiaoSnapshot ?? (x.NguoiGiao != null ? x.NguoiGiao.TK_HoTen : null), NguoiNhan = x.CP_NguoiNhan,
        PhongBan = x.CP_TenPhongBanSnapshot ?? (x.PhongBan != null ? x.PhongBan.PB_TenPhongBan : null),
        PhongBanHoatDong = x.PhongBan != null ? x.PhongBan.PB_TrangThai : null,
        TrangThai = x.CP_TrangThai, LyDo = x.CP_LyDo, GhiChu = x.CP_GhiChu,
        SoTaiSan = x.tblChiTietCapPhats.Count(), SoDaThuHoi = x.tblChiTietCapPhats.Count(d => d.CTCP_NgayThuHoi != null)
    };

    private static readonly Expression<Func<tblChiTietCapPhat, CapPhatLine>> LineProjection = x => new CapPhatLine
    {
        LoaiPhieu = x.CapPhat != null ? x.CapPhat.CP_LoaiPhieu : null, TrangThaiDong = x.CTCP_TrangThai,
        NguoiChiuTrachNhiem=x.CapPhat != null && x.CapPhat.NguoiNhan != null ? x.CapPhat.NguoiNhan.TK_HoTen : null,
        NguoiNhanId=x.CapPhat != null ? x.CapPhat.CP_NguoiNhanID : null,
        DaDoiSoat=x.CTCP_DoiSoatUTC != null,
        TrangThaiTaiSan = x.TaiSan != null ? x.TaiSan.TS_TrangThai : null,
        TinhTrangTaiSan = x.TaiSan != null ? x.TaiSan.TS_TinhTrang : null,
        ViTriSnapshot=x.CTCP_ViTriSnapshot, PhuKienSnapshot=x.CTCP_PhuKienSnapshot,
        BatDauUTC = x.CTCP_BatDauUTC, KetThucUTC = x.CTCP_KetThucUTC,
        NgayTraDuKien = x.CapPhat != null ? x.CapPhat.CP_NgayTraDuKien : null,
        DongNguonId = x.CTCP_DongNguonID,
        Id = x.CTCP_ID, TaiSanId = x.TS_ID, PhieuId = x.CP_ID,
        MaPhieu = x.CapPhat != null ? x.CapPhat.CP_MaPhieu : "",
        NgayCapPhat = x.CapPhat != null ? x.CapPhat.CP_NgayCapPhat : null,
        NguoiNhan = x.CapPhat != null ? x.CapPhat.CP_NguoiNhan : null,
        PhongBan = x.CapPhat != null ? x.CapPhat.CP_TenPhongBanSnapshot ?? (x.CapPhat.PhongBan != null ? x.CapPhat.PhongBan.PB_TenPhongBan : null) : null,
        MaTaiSan = x.CTCP_MaTaiSanSnapshot ?? (x.TaiSan != null ? x.TaiSan.TS_MaTaiSan : null),
        TenTaiSan = x.CTCP_TenTaiSanSnapshot ?? (x.TaiSan != null ? x.TaiSan.TS_TenTaiSan : null),
        DanhMuc = x.CTCP_DanhMucSnapshot ?? (x.TaiSan != null && x.TaiSan.tblDanhMucTaiSan != null ? x.TaiSan.tblDanhMucTaiSan.DMTS_TenDanhMuc : null),
        TinhTrangKhiGiao = x.CTCP_TinhTrangKhiGiao, TinhTrangKhiThuHoi = x.CTCP_TinhTrangKhiThuHoi,
        NgayThuHoi = x.CTCP_NgayThuHoi, GhiChu = x.CTCP_GhiChu
    };

    #endregion

    #region Phạm vi truy cập và in phiếu

    private bool EmployeeView => HttpContext.Items["GiaoNhanCanManage"] is false;
    private int ViewerId => HttpContext.Items["GiaoNhanViewer"] is int id ? id : 0;
    private IQueryable<tblCapPhatTaiSan> ScopeReceipts(IQueryable<tblCapPhatTaiSan> query)
    {
        if (!EmployeeView) return query;
        var viewer = ViewerId;
        return query.Where(x => x.CP_NguoiNhanID == viewer ||
            ((x.CP_LoaiPhieu == "ThuHoi" || x.CP_LoaiPhieu == "Chuyen") && x.CP_NguoiGiaoID == viewer));
    }

    [HttpGet]
    public async Task<IActionResult> Print(int id)
    {
        var receipt = await ScopeReceipts(context.tblCapPhatTaiSan.AsNoTracking()).Where(x => x.CP_ID == id)
            .Select(ReceiptProjection).SingleOrDefaultAsync();
        if (receipt == null) return NotFound();
        var model = new CapPhatDetails { Phieu = receipt };
        model.Items = await context.tblChiTietCapPhat.AsNoTracking().Where(x => x.CP_ID == id)
            .OrderBy(x => x.CTCP_ID).Select(LineProjection).ToListAsync();
        return View("InPhieu", model);
    }

    #endregion

    #region Biểu mẫu và phản hồi giao diện

    private bool WantsHtml => Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    [HttpGet]
    public async Task<IActionResult> Create() => View("Form", await PrepareForm(new GiaoNhanInput
    {
        RequestKey = Guid.NewGuid().ToString("D"), NgayDuKien = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7))
    }, Give, null));

    [HttpGet]
    public async Task<IActionResult> CreateReturn([FromRoute] int id) => await SourceForm(id, Return);

    [HttpGet]
    public async Task<IActionResult> CreateTransfer([FromRoute] int id) => await SourceForm(id, Transfer);

    [HttpGet]
    public async Task<IActionResult> EditDraft([FromRoute] int id)
    {
        var doc = await context.tblCapPhatTaiSan.AsNoTracking().Include(x => x.tblChiTietCapPhats).SingleOrDefaultAsync(x => x.CP_ID == id);
        if (doc == null) return NotFound();
        if (doc.CP_TrangThai != Draft || doc.CP_LoaiPhieu == null) return Conflict("Chỉ được sửa phiếu nháp; lịch sử đã xác nhận được giữ nguyên.");
        var input = new GiaoNhanInput
        {
            RequestKey = doc.CP_RequestKey!, PhienBan = doc.CP_PhienBan, NguoiNhanId = doc.CP_NguoiNhanID,
            PhongBanId = doc.PB_ID, LoaiDoiTuong = doc.CP_LoaiDoiTuong ?? "CaNhan",
            NgayDuKien = doc.CP_NgayDuKien, NgayTraDuKien = doc.CP_NgayTraDuKien, LyDo = doc.CP_LyDo ?? "", GhiChu = doc.CP_GhiChu,
            Dongs = doc.tblChiTietCapPhats.Select(x => new GiaoNhanDongInput
            {
                TaiSanId = x.TS_ID, DongNguonId = x.CTCP_DongNguonID, GhiChu = x.CTCP_GhiChu,
                PhuKien = x.CTCP_PhuKienSnapshot, ViTri = x.CTCP_ViTriSnapshot, TinhTrangNhan = x.CTCP_TinhTrangKhiThuHoi
            }).ToList()
        };
        if (doc.CP_LoaiPhieu != Give)
            input.PhieuGocId = await context.tblChiTietCapPhat.Where(x => x.CTCP_ID == input.Dongs[0].DongNguonId).Select(x => (int?)x.CP_ID).SingleOrDefaultAsync();
        return View("Form", await PrepareForm(input, doc.CP_LoaiPhieu, id));
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmHandover([FromRoute] int id) => await ConfirmationPage(id, Give);
    [HttpGet]
    public async Task<IActionResult> ConfirmReturn([FromRoute] int id) => await ConfirmationPage(id, Return);
    [HttpGet]
    public async Task<IActionResult> ConfirmTransfer([FromRoute] int id) => await ConfirmationPage(id, Transfer);

    [HttpGet]
    public async Task<IActionResult> AssetChoices(string? keyword, int page = 1)
    {
        keyword = keyword?.Trim();
        if (keyword?.Length > 150) return BadRequest("Từ khóa tối đa 150 ký tự.");
        var query = AssetChoiceQuery();
        if (!string.IsNullOrEmpty(keyword)) query = query.Where(x => x.Code.Contains(keyword) || x.Name.Contains(keyword) || (x.Serial != null && x.Serial.Contains(keyword)));
        var model = new GiaoNhanAssetPage { Keyword = keyword, Total = await query.CountAsync(), PageSize = 20 };
        SetPage(model, page);
        model.Items = await query.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).ToListAsync();
        return PartialView("_AssetChoices", model);
    }

    private async Task<IActionResult> SourceForm(int id, string kind)
    {
        var source = await context.tblCapPhatTaiSan.AsNoTracking().Where(x => x.CP_ID == id).Select(x => new { x.PB_ID }).SingleOrDefaultAsync();
        if (source == null) return NotFound();
        var input = new GiaoNhanInput
        {
            RequestKey = Guid.NewGuid().ToString("D"), PhieuGocId = id, PhongBanId = source.PB_ID,
            NgayDuKien = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), LyDo = kind == Return ? "Hết nhu cầu sử dụng" : ""
        };
        return View("Form", await PrepareForm(input, kind, null));
    }

    private async Task<GiaoNhanForm> PrepareForm(GiaoNhanInput input, string? kind, int? id)
    {
        if (kind == null && id.HasValue)
            kind = await context.tblCapPhatTaiSan.AsNoTracking().Where(x => x.CP_ID == id).Select(x => x.CP_LoaiPhieu).SingleOrDefaultAsync();
        var model = new GiaoNhanForm
        {
            DocumentId = id, Kind = kind ?? Give, RequestKey = input.RequestKey, PhienBan = input.PhienBan,
            NguoiNhanId = input.NguoiNhanId, PhongBanId = input.PhongBanId, PhieuGocId = input.PhieuGocId,
            NgayDuKien = input.NgayDuKien, NgayTraDuKien = input.NgayTraDuKien, LoaiDoiTuong = input.LoaiDoiTuong,
            LyDo = input.LyDo, GhiChu = input.GhiChu, Dongs = input.Dongs ?? []
        };
        model.People = await context.tblTaiKhoan.AsNoTracking().OrderBy(x => x.TK_HoTen).ThenBy(x => x.TK_ID)
            .Select(x => new GiaoNhanPerson
            {
                Id = x.TK_ID, Name = x.TK_HoTen, DepartmentId = x.PB_ID,
                DepartmentName = x.PhongBanLamViec != null ? x.PhongBanLamViec.PB_TenPhongBan : null,
                Active = x.TK_TrangThai && x.PhongBanLamViec != null && x.PhongBanLamViec.PB_TrangThai == true,
                UnavailableReason = !x.TK_TrangThai ? "Tài khoản đã khóa" : !x.PB_ID.HasValue ? "Chưa gán phòng ban"
                    : x.PhongBanLamViec == null ? "Không tìm thấy phòng ban" : x.PhongBanLamViec.PB_TrangThai != true ? "Phòng ban đã ngừng hoạt động" : null
            }).ToListAsync();
        model.Departments = await context.tblPhongBan.AsNoTracking().Where(x => x.PB_TrangThai == true || x.PB_ID == model.PhongBanId)
            .OrderBy(x => x.PB_TenPhongBan).Select(x => new SelectListItem(x.PB_TenPhongBan + (x.PB_TrangThai == true ? "" : " (ngừng hoạt động)"), x.PB_ID.ToString())).ToListAsync();
        if (model.Kind == Give)
        {
            var ids = model.Dongs.Select(x => x.TaiSanId).ToArray();
            var assets = await AssetChoiceQuery().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            model.SelectedAssets = model.Dongs.Select(x => assets.GetValueOrDefault(x.TaiSanId) ?? new GiaoNhanAsset { Id = x.TaiSanId, Code = "#" + x.TaiSanId, Name = "Không tìm thấy tài sản", Managed = false }).ToList();
        }
        else if (model.PhieuGocId.HasValue)
        {
            var source = await context.tblCapPhatTaiSan.AsNoTracking().Where(x => x.CP_ID == model.PhieuGocId)
                .Select(x => new { x.CP_MaPhieu, x.CP_NguoiNhan, x.PB_ID, x.CP_NguoiNhanID, VerifiedHolder = x.NguoiNhan != null ? x.NguoiNhan.TK_HoTen : null, Department = x.PhongBan!.PB_TenPhongBan }).SingleOrDefaultAsync();
            model.SourceCode = source?.CP_MaPhieu; model.SourceHolder = source?.VerifiedHolder ?? source?.CP_NguoiNhan; model.SourceDepartment = source?.Department;
            model.SourceRecordedHolder = source?.CP_NguoiNhan; model.SourceHolderVerified = source?.CP_NguoiNhanID.HasValue == true;
            if (model.Kind == Return && source != null) model.PhongBanId = source.PB_ID;
            model.Sources = await context.tblChiTietCapPhat.AsNoTracking().Where(x => x.CP_ID == model.PhieuGocId && x.CTCP_TrangThai == Active)
                .OrderBy(x => x.CTCP_ID).Select(x => new GiaoNhanSource
                {
                    Id = x.CTCP_ID, AssetId = x.TS_ID, Code = x.CTCP_MaTaiSanSnapshot ?? x.TaiSan!.TS_MaTaiSan,
                    Name = x.CTCP_TenTaiSanSnapshot ?? x.TaiSan!.TS_TenTaiSan, ConditionWhenGiven = x.CTCP_TinhTrangKhiGiao,
                    CurrentCondition = x.TaiSan!.TS_TinhTrang, State = x.CTCP_TrangThai, StartUtc = x.CTCP_BatDauUTC,
                    AssetState = x.TaiSan.TS_TrangThai, Managed = x.TaiSan.TS_DangQuanLy,
                    PendingMaintenance = x.TaiSan.tblBaoTriTaiSans.Any(d => d.BT_TrangThai != "DaHoanThanh"),
                    PendingDisposal = x.TaiSan.tblChiTietThanhLies.Any(d => d.ThanhLy!.TL_TrangThai != "Huy")
                }).ToListAsync();
            var posted = model.Dongs.Where(x => x.DongNguonId.HasValue).GroupBy(x=>x.DongNguonId!.Value).ToDictionary(x=>x.Key,x=>x.First());
            model.Dongs = model.Sources.Select(x => posted.GetValueOrDefault(x.Id) ?? new GiaoNhanDongInput
            {
                TaiSanId = x.AssetId, DongNguonId = x.Id, Chon = false
            }).ToList();
        }
        return model;
    }

    private async Task<IActionResult> DraftResponse(IActionResult result, GiaoNhanInput input, string? kind, int? id)
    {
        if (WantsHtml && result is ObjectResult { StatusCode: >= 400 and < 600 } error)
        {
            ModelState.AddModelError("", error.Value?.ToString() ?? "Không thể lưu phiếu.");
            var model = await PrepareForm(input, kind, id);
            // Reconstructed source rows may have different indexes after filtering unchecked rows.
            foreach(var key in ModelState.Keys.Where(x=>x.StartsWith("Dongs[",StringComparison.Ordinal)).ToArray())
            {
                var messages=ModelState[key]!.Errors.Select(x=>x.ErrorMessage).Where(x=>!string.IsNullOrEmpty(x)).ToArray();
                ModelState.Remove(key);
                foreach(var message in messages) ModelState.AddModelError("",message);
            }
            var view = View("Form", model); view.StatusCode = error.StatusCode; return view;
        }
        return result;
    }

    private IActionResult DocumentResponse(IActionResult result,int id)
    {
        if(WantsHtml && result is ObjectResult{StatusCode:>=400 and <600} error)
        {
            TempData["Error"]=error.Value?.ToString()??"Thao tác không thành công. Hãy tải lại và kiểm tra phiếu.";
            return RedirectToAction(nameof(Details),new{id});
        }
        return result;
    }

    private async Task<IActionResult> ConfirmationPage(int id, string kind, GiaoNhanConfirmInput? input = null, int? status = null)
    {
        var doc = await context.tblCapPhatTaiSan.AsNoTracking().Where(x => x.CP_ID == id).Select(x => new GiaoNhanConfirmation
        {
            Id = x.CP_ID, Code = x.CP_MaPhieu, Kind = x.CP_LoaiPhieu ?? "", State = x.CP_TrangThai,
            PhienBan = x.CP_PhienBan, Receiver = x.CP_NguoiNhan, Department = x.PhongBan!.PB_TenPhongBan, Purpose = x.CP_LyDo
        }).SingleOrDefaultAsync();
        if (doc == null) return NotFound();
        if (doc.Kind != kind) return Conflict("Loại phiếu không hợp lệ hoặc lịch sử chưa đối soát.");
        doc.ThucHienVietNam = input?.ThucHienVietNam;
        doc.Lines = await context.tblChiTietCapPhat.AsNoTracking().Where(x => x.CP_ID == id).OrderBy(x => x.CTCP_ID).Select(LineProjection).ToListAsync();
        var view = View("Confirm", doc); view.StatusCode = status; return view;
    }

    private async Task<IActionResult> ConfirmationResponse(IActionResult result, int id, string kind, GiaoNhanConfirmInput input)
    {
        if (WantsHtml && result is ObjectResult { StatusCode: >= 400 and < 600 } error)
        {
            ModelState.AddModelError("", error.Value?.ToString() ?? "Không thể xác nhận phiếu.");
            return await ConfirmationPage(id, kind, input, error.StatusCode);
        }
        return result;
    }

    private IQueryable<GiaoNhanAsset> AssetChoiceQuery() => context.tblTaiSan.AsNoTracking().Select(x => new GiaoNhanAsset
    {
        Id = x.TS_ID, Code = x.TS_MaTaiSan, Name = x.TS_TenTaiSan, Serial = x.TS_Serial, Location = x.TS_ViTri,
        Category = x.tblDanhMucTaiSan!.DMTS_TenDanhMuc, State = x.TS_TrangThai, Condition = x.TS_TinhTrang, Managed = x.TS_DangQuanLy,
        UnknownHistory = x.tblChiTietCapPhats.Any(d => d.CTCP_TrangThai == null), HasHolder = x.tblChiTietCapPhats.Any(d => d.CTCP_TrangThai == Active),
        Maintenance = x.tblBaoTriTaiSans.Any(d => d.BT_TrangThai != "DaHoanThanh"),
        Disposal = x.tblChiTietThanhLies.Any(d => d.ThanhLy!.TL_TrangThai != "Huy"),
        Inventory = x.tblChiTietKiemKes.Any(d => d.KiemKe!.KK_TrangThai == "DangKiemKe")
    });

    #endregion

    #region Giao dịch giao, thu hồi và chuyển giao

    private bool IsApprovedDatabase() => context.Database.IsRelational() &&
        (context.Database.GetDbConnection().Database.Equals("QuanLyTaiSan", StringComparison.OrdinalIgnoreCase) ||
         System.Text.RegularExpressions.Regex.IsMatch(context.Database.GetDbConnection().Database, "^qltaisan_sprint2_(stage|checks)_[0-9a-f]{32}$"));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(GiaoNhanInput model) => await DraftResponse(await SaveDraft(model, Give, null), model, Give, null);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateReturn(GiaoNhanInput model) => await DraftResponse(await SaveDraft(model, Return, null), model, Return, null);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTransfer(GiaoNhanInput model) => await DraftResponse(await SaveDraft(model, Transfer, null), model, Transfer, null);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDraft([FromRoute] int id, GiaoNhanInput model) => await DraftResponse(await SaveDraft(model, null, id), model, null, id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromRoute] int id, long phienBan) => DocumentResponse(await StageWrite(async actorId =>
    {
        var doc = await LockedDocument(id);
        Ensure(doc.CP_LoaiPhieu != null, "Phiếu lịch sử cần đối soát; không được sửa hoặc xác nhận tự động.");
        if (doc.CP_TrangThai == Waiting) return doc.CP_ID;
        Ensure(doc.CP_TrangThai == Draft && doc.CP_PhienBan == phienBan, "Phiếu đã thay đổi. Hãy tải lại.");
        var actor = await LockParticipants(doc, actorId);
        await ValidateDocument(doc);
        var before = Snapshot(doc);
        doc.CP_TrangThai = Waiting; doc.CP_PhienBan++;
        Audit(doc, actor, "GuiXacNhan", before);
        return doc.CP_ID;
    }), id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel([FromRoute] int id, long phienBan, string lyDo) => DocumentResponse(await StageWrite(async actorId =>
    {
        var doc = await LockedDocument(id);
        Ensure(doc.CP_LoaiPhieu != null, "Không được hủy phiếu lịch sử chưa đối soát.");
        if (doc.CP_TrangThai == Cancelled) return id;
        Ensure(doc.CP_TrangThai is Draft or Waiting && doc.CP_PhienBan == phienBan, "Chỉ được hủy nháp hoặc phiếu chờ xác nhận.");
        Ensure(!string.IsNullOrWhiteSpace(lyDo) && lyDo.Trim().Length <= 2000, "Cần lý do hủy, tối đa 2000 ký tự.");
        var actor = await LockParticipants(doc, actorId);
        var before = Snapshot(doc);
        doc.CP_TrangThai = Cancelled; doc.CP_PhienBan++;
        Audit(doc, actor, "Huy", before, lyDo.Trim());
        return id;
    }), id);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmHandover([FromRoute] int id, GiaoNhanConfirmInput model) => await ConfirmationResponse(await Confirm(id, Give, model), id, Give, model);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmReturn([FromRoute] int id, GiaoNhanConfirmInput model) => await ConfirmationResponse(await Confirm(id, Return, model), id, Return, model);

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmTransfer([FromRoute] int id, GiaoNhanConfirmInput model) => await ConfirmationResponse(await Confirm(id, Transfer, model), id, Transfer, model);

    private async Task<IActionResult> SaveDraft(GiaoNhanInput model, string? kind, int? id)
    {
        if (model.Dongs?.Any(x => x == null) == true) return BadRequest("Danh sách tài sản không hợp lệ.");
        model.Dongs = model.Dongs?.Where(x => x.Chon).ToList() ?? [];
        if (!ModelState.IsValid) return BadRequest("Dữ liệu phiếu không hợp lệ.");
        if (!Guid.TryParseExact(model.RequestKey, "D", out _) || model.Dongs == null ||
            model.Dongs.Count is < 1 or > 100 || model.Dongs.Any(x => x == null || x.TaiSanId < 1) ||
            model.Dongs.Select(x => x.TaiSanId).Distinct().Count() != model.Dongs.Count ||
            string.IsNullOrWhiteSpace(model.LyDo) || model.LyDo.Trim().Length > 2000 ||
            (model.GhiChu?.Length ?? 0) > 4000 || model.LoaiDoiTuong is not ("CaNhan" or "PhongBan") ||
            model.Dongs.Any(x => (x.GhiChu?.Length ?? 0) > 2000 || (x.PhuKien?.Length ?? 0) > 4000 || (x.ViTri?.Length ?? 0) > 150))
            return BadRequest("Chọn 1–100 tài sản khác nhau, mã yêu cầu hợp lệ và nhập đầy đủ lý do.");
        if (model.NgayTraDuKien.HasValue && (!model.NgayDuKien.HasValue || model.NgayTraDuKien < model.NgayDuKien))
            return BadRequest("Hạn trả không được trước ngày bàn giao dự kiến.");

        return await StageWrite(async actorId =>
        {
            if (id == null)
            {
                var existing = await context.tblCapPhatTaiSan.AsNoTracking().SingleOrDefaultAsync(x => x.CP_RequestKey == model.RequestKey);
                if (existing != null)
                {
                    Ensure(existing.CP_NguoiLapID == actorId && existing.CP_LoaiPhieu == kind, "Mã yêu cầu đã được sử dụng bởi giao dịch khác.");
                    return existing.CP_ID;
                }
            }
            tblCapPhatTaiSan doc;
            if (id.HasValue)
            {
                doc = await LockedDocument(id.Value, model.Dongs.Select(x => x.TaiSanId));
                Ensure(doc.CP_LoaiPhieu != null && doc.CP_TrangThai == Draft && doc.CP_PhienBan == model.PhienBan,
                    "Chỉ được sửa bản nháp chưa thay đổi; lịch sử đã xác nhận không được ghi đè.");
                Ensure(doc.CP_RequestKey == model.RequestKey, "Mã yêu cầu của phiếu không hợp lệ.");
                kind = doc.CP_LoaiPhieu;
            }
            else
            {
                await LockAssets(model.Dongs.Select(x => x.TaiSanId));
                doc = new tblCapPhatTaiSan
                {
                    CP_LoaiPhieu = kind, CP_TrangThai = Draft, CP_NgayCapPhat = null,
                    CP_MaPhieu = (kind == Return ? "TH" : kind == Transfer ? "CG" : "G") + Guid.NewGuid().ToString("N")[..27],
                    CP_RequestKey = model.RequestKey, CP_NguoiLapID = actorId, CP_NgayLapUTC = DateTime.UtcNow
                };
            }
            var before = id.HasValue ? Snapshot(doc) : null;
            var proposed = model.Dongs.Select(x => new tblChiTietCapPhat
            {
                TS_ID = x.TaiSanId, CTCP_DongNguonID = x.DongNguonId, CTCP_TrangThai = Pending(kind!),
                CTCP_TinhTrangKhiThuHoi = kind == Return ? x.TinhTrangNhan : null,
                CTCP_GhiChu = x.GhiChu?.Trim(), CTCP_PhuKienSnapshot = x.PhuKien?.Trim(), CTCP_ViTriSnapshot = x.ViTri?.Trim()
            }).ToList();
            if (kind == Give) Ensure(proposed.All(x => x.CTCP_DongNguonID == null), "Phiếu giao mới không nhận dòng nguồn.");
            else
            {
                Ensure(proposed.All(x => x.CTCP_DongNguonID.HasValue) &&
                    proposed.Select(x => x.CTCP_DongNguonID).Distinct().Count() == proposed.Count, "Chọn đúng từng dòng cấp phát gốc.");
                var sources = await LockSources(proposed);
                Ensure(model.PhieuGocId.HasValue && sources.All(x => x.CP_ID == model.PhieuGocId), "Tài sản không thuộc phiếu gốc được chọn.");
                await ValidateSources(sources, proposed);
                if (kind == Return) Ensure(model.PhongBanId == sources[0].CapPhat!.PB_ID, "Phòng ban thu hồi không đúng trách nhiệm gốc.");
                doc.CP_NguoiGiaoID = sources[0].CapPhat!.CP_NguoiNhanID!.Value;
            }
            if (kind == Give) doc.CP_NguoiGiaoID = actorId;
            if (kind == Return)
            {
                Ensure(proposed.All(x => x.CTCP_TinhTrangKhiThuHoi != null && ReturnConditions.Contains(x.CTCP_TinhTrangKhiThuHoi)),
                    "Chọn tình trạng nhận lại cho từng tài sản; mất không phải thu hồi vật lý.");
                doc.CP_NguoiNhanID = actorId;
            }
            else
            {
                Ensure(model.NguoiNhanId.HasValue, "Chọn người nhận hoặc đại diện phòng ban.");
                doc.CP_NguoiNhanID = model.NguoiNhanId;
            }
            doc.PB_ID = model.PhongBanId; doc.CP_LoaiDoiTuong = model.LoaiDoiTuong;
            doc.CP_NgayDuKien = model.NgayDuKien; doc.CP_NgayTraDuKien = model.NgayTraDuKien;
            doc.CP_LyDo = model.LyDo.Trim(); doc.CP_GhiChu = model.GhiChu?.Trim();
            var actor = await LockParticipants(doc, actorId);
            if (id.HasValue) context.tblChiTietCapPhat.RemoveRange(doc.tblChiTietCapPhats);
            doc.tblChiTietCapPhats = proposed;
            await ValidateDocument(doc);
            doc.CP_PhienBan++;
            if (!id.HasValue)
            {
                context.tblCapPhatTaiSan.Add(doc);
                await context.SaveChangesAsync();
            }
            Audit(doc, actor, id.HasValue ? "SuaNhap" : "LapNhap", before);
            return doc.CP_ID;
        }, model.RequestKey, kind, id == null);
    }

    private Task<IActionResult> Confirm(int id, string kind, GiaoNhanConfirmInput input) => StageWrite(async actorId =>
    {
        var doc = await LockedDocument(id);
        Ensure(doc.CP_LoaiPhieu == kind, "Sai loại phiếu hoặc phiếu lịch sử chưa được đối soát.");
        if (doc.CP_TrangThai == Confirmed(kind)) return id;
        Ensure(doc.CP_TrangThai == Waiting && doc.CP_PhienBan == input.PhienBan, "Phiếu đã thay đổi hoặc không ở trạng thái chờ xác nhận.");
        var actor = await LockParticipants(doc, actorId);
        await ValidateDocument(doc);
        var before = Snapshot(doc);
        var now = DateTime.UtcNow;
        var actual = kind == Return ? (input.ThucHienVietNam.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(input.ThucHienVietNam.Value, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).UtcDateTime
            : input.ThucHien?.UtcDateTime ?? now) : now;
        Ensure(actual <= now, "Thời điểm thực hiện không được sau hiện tại.");
        var assets = await context.tblTaiSan.Where(x => doc.tblChiTietCapPhats.Select(d => d.TS_ID).Contains(x.TS_ID))
            .Include(x => x.tblDanhMucTaiSan).ToDictionaryAsync(x => x.TS_ID);
        var sources = kind == Give ? [] : await LockSources(doc.tblChiTietCapPhats);
        if (kind != Give)
        {
            await ValidateSources(sources, doc.tblChiTietCapPhats);
            foreach (var source in sources)
            {
                Ensure(source.CTCP_BatDauUTC.HasValue && actual >= source.CTCP_BatDauUTC, "Thời điểm nhận lại/chuyển không được trước lúc bàn giao.");
                source.CTCP_TrangThai = kind == Return ? ClosedReturn : ClosedTransfer;
                source.CTCP_KetThucUTC = actual;
                if (kind == Return)
                {
                    var receiving = doc.tblChiTietCapPhats.Single(x => x.CTCP_DongNguonID == source.CTCP_ID);
                    source.CTCP_NgayThuHoi = DateOnly.FromDateTime(actual.AddHours(7));
                    source.CTCP_TinhTrangKhiThuHoi = receiving.CTCP_TinhTrangKhiThuHoi;
                }
            }
            // Free the unique custody slot inside the same transaction before activating successor rows.
            await context.SaveChangesAsync();
        }
        foreach (var line in doc.tblChiTietCapPhats.OrderBy(x => x.TS_ID))
        {
            var asset = assets[line.TS_ID];
            line.CTCP_MaTaiSanSnapshot = asset.TS_MaTaiSan;
            line.CTCP_TenTaiSanSnapshot = asset.TS_TenTaiSan;
            line.CTCP_DanhMucSnapshot = asset.tblDanhMucTaiSan?.DMTS_TenDanhMuc;
            if (kind == Return)
            {
                Ensure(line.CTCP_TinhTrangKhiThuHoi != null && ReturnConditions.Contains(line.CTCP_TinhTrangKhiThuHoi), "Tình trạng nhận lại không hợp lệ.");
                line.CTCP_TrangThai = Received; line.CTCP_KiemTraUTC = now; line.CTCP_NguoiKiemTraID = actorId;
                line.CTCP_NgayThuHoi = DateOnly.FromDateTime(actual.AddHours(7));
                asset.TS_TinhTrang = line.CTCP_TinhTrangKhiThuHoi!;
                asset.TS_TrangThai = asset.TS_TinhTrang switch
                {
                    "Tot" => "ChuaCapPhat", "CanBaoTri" => "ChoBaoTri", "HuHong" => "ChoXuLy", _ => "ChoKiemTra"
                };
                if (line.CTCP_ViTriSnapshot != null) asset.TS_ViTri = line.CTCP_ViTriSnapshot;
            }
            else
            {
                line.CTCP_TinhTrangKhiGiao = asset.TS_TinhTrang;
                line.CTCP_ViTriSnapshot = asset.TS_ViTri;
                line.CTCP_BatDauUTC = actual; line.CTCP_TrangThai = Active;
                asset.TS_TrangThai = "DaCapPhat";
            }
        }
        doc.CP_NguoiNhan = (await context.tblTaiKhoan.AsNoTracking().Where(x => x.TK_ID == doc.CP_NguoiNhanID).Select(x => x.TK_HoTen).SingleAsync());
        doc.CP_TenPhongBanSnapshot = await context.tblPhongBan.AsNoTracking().Where(x => x.PB_ID == doc.PB_ID).Select(x => x.PB_TenPhongBan).SingleAsync();
        doc.CP_NguoiGiaoSnapshot = await context.tblTaiKhoan.AsNoTracking().Where(x => x.TK_ID == doc.CP_NguoiGiaoID).Select(x => x.TK_HoTen).SingleAsync();
        doc.CP_NguoiXacNhanID = actorId; doc.CP_XacNhanUTC = now; doc.CP_ThucHienUTC = actual;
        doc.CP_NgayCapPhat = DateOnly.FromDateTime(actual.AddHours(7));
        doc.CP_TrangThai = Confirmed(kind); doc.CP_PhienBan++;
        Audit(doc, actor, "XacNhan" + kind, before);
        return id;
    });

    private async Task<tblTaiKhoan> LockParticipants(tblCapPhatTaiSan doc, int actorId)
    {
        var ids = new[] { actorId, doc.CP_NguoiGiaoID, doc.CP_NguoiNhanID ?? 0 }.Distinct().Order().ToList();
        foreach (var accountId in ids) await LockRow("account", accountId);
        await LockRow("department", doc.PB_ID);
        var actor = await context.tblTaiKhoan.SingleAsync(x => x.TK_ID == actorId);
        Ensure(actor.TK_TrangThai && actor.TK_VaiTro == "Admin", "Tài khoản không còn quyền thực hiện.", 403);
        var receiver = await context.tblTaiKhoan.SingleAsync(x => x.TK_ID == doc.CP_NguoiNhanID);
        var department = await context.tblPhongBan.SingleAsync(x => x.PB_ID == doc.PB_ID);
        Ensure(doc.CP_LoaiPhieu == Return || department.PB_TrangThai == true, "Phòng ban nhận đã ngừng hoạt động.");
        Ensure(receiver.TK_TrangThai, "Tài khoản nhận không hoạt động.");
        if (doc.CP_LoaiPhieu != Return)
            Ensure(receiver.PB_ID == doc.PB_ID, "Người nhận chưa thuộc phòng ban được chọn.");
        if (doc.CP_TrangThai == Draft) doc.CP_NguoiNhan = receiver.TK_HoTen;
        return actor;
    }

    private async Task ValidateDocument(tblCapPhatTaiSan doc)
    {
        Ensure(doc.tblChiTietCapPhats.Count is > 0 and <= 100, "Phiếu phải có 1–100 tài sản.");
        Ensure(doc.tblChiTietCapPhats.Select(x => x.TS_ID).Distinct().Count() == doc.tblChiTietCapPhats.Count, "Không được chọn trùng tài sản.");
        if (doc.CP_LoaiPhieu == Give)
        {
            foreach (var line in doc.tblChiTietCapPhats) await RequireAvailable(line.TS_ID);
        }
        else
        {
            var sources = await LockSources(doc.tblChiTietCapPhats);
            await ValidateSources(sources, doc.tblChiTietCapPhats);
            if (doc.CP_LoaiPhieu == Transfer)
            {
                Ensure(sources.All(x => x.CapPhat!.CP_NguoiNhanID != doc.CP_NguoiNhanID || x.CapPhat.PB_ID != doc.PB_ID), "Người/phòng ban nhận trùng trách nhiệm hiện tại.");
                foreach (var line in doc.tblChiTietCapPhats)
                {
                    var asset = await context.tblTaiSan.AsNoTracking().SingleAsync(x => x.TS_ID == line.TS_ID);
                    Ensure(asset.TS_DangQuanLy && asset.TS_TrangThai == "DaCapPhat" && asset.TS_TinhTrang == "Tot", "Tài sản đang hỏng, mất hoặc chờ xử lý; không thể chuyển giao.");
                    Ensure(!await context.tblBaoTriTaiSan.AnyAsync(x => x.TS_ID == line.TS_ID && x.BT_TrangThai != "DaHoanThanh"), "Tài sản đang chờ bảo trì; không thể chuyển giao.");
                    Ensure(!await context.tblChiTietThanhLy.AnyAsync(x => x.TS_ID == line.TS_ID && x.ThanhLy!.TL_TrangThai != "Huy"), "Tài sản đang có hồ sơ thanh lý; không thể chuyển giao.");
                }
            }
            if (doc.CP_LoaiPhieu == Return)
            {
                Ensure(doc.tblChiTietCapPhats.All(x => x.CTCP_TinhTrangKhiThuHoi != null && ReturnConditions.Contains(x.CTCP_TinhTrangKhiThuHoi)), "Cần tình trạng nhận lại hợp lệ cho từng tài sản.");
                foreach (var line in doc.tblChiTietCapPhats)
                    Ensure(!await context.tblTaiSan.AnyAsync(x => x.TS_ID == line.TS_ID && x.TS_TinhTrang == "MatDangXuLy"), "Tài sản báo mất chưa được coi là đã nhận lại vật lý.");
            }
        }
    }

    private async Task RequireAvailable(int id)
    {
        var asset = await context.tblTaiSan.AsNoTracking().SingleOrDefaultAsync(x => x.TS_ID == id);
        Ensure(asset != null && asset.TS_DangQuanLy, "Tài sản không tồn tại hoặc đã ngừng quản lý.");
        Ensure(asset!.TS_TrangThai == "ChuaCapPhat" && asset.TS_TinhTrang == "Tot", "Tài sản không ở tình trạng sẵn sàng cấp phát.");
        Ensure(!await context.tblChiTietCapPhat.AnyAsync(x => x.TS_ID == id && (x.CTCP_TrangThai == null || x.CTCP_TrangThai == Active)),
            "Tài sản đang có người giữ hoặc có dữ liệu lịch sử cần đối soát.");
        Ensure(!await context.tblBaoTriTaiSan.AnyAsync(x => x.TS_ID == id && x.BT_TrangThai != "DaHoanThanh"), "Tài sản đang chờ xử lý bảo trì.");
        Ensure(!await context.tblChiTietThanhLy.AnyAsync(x => x.TS_ID == id && x.ThanhLy!.TL_TrangThai != "Huy"), "Tài sản đang có hồ sơ thanh lý.");
        Ensure(!await context.tblChiTietKiemKe.AnyAsync(x => x.TS_ID == id && x.KiemKe!.KK_TrangThai == "DangKiemKe"), "Tài sản đang kiểm kê.");
    }

    private async Task ValidateSources(List<tblChiTietCapPhat> sources, IEnumerable<tblChiTietCapPhat> targets)
    {
        var list = targets.ToList();
        Ensure(sources.Count == list.Count && sources.Count > 0, "Dòng gốc không tồn tại hoặc bị trùng.");
        Ensure(sources.All(x => x.CTCP_TrangThai == Active && (x.CapPhat?.CP_TrangThai == Handed || (x.CapPhat?.CP_TrangThai == "DaCapPhat" && x.CTCP_DoiSoatUTC.HasValue)) && x.CapPhat!.CP_NguoiNhanID.HasValue && x.CTCP_BatDauUTC.HasValue),
            "Dòng gốc đã kết thúc hoặc lịch sử chưa được xác minh.");
        Ensure(sources.All(x => list.Any(t => t.CTCP_DongNguonID == x.CTCP_ID && t.TS_ID == x.TS_ID)), "Dòng gốc không đúng tài sản.");
        Ensure(sources.Select(x => new { x.CapPhat!.CP_NguoiNhanID, x.CapPhat.PB_ID }).Distinct().Count() == 1, "Các dòng phải thuộc cùng người và phòng ban đang giữ.");
        // Return may receive damaged property; transfer must check the current physical condition below.
        await Task.CompletedTask;
    }

    private async Task<List<tblChiTietCapPhat>> LockSources(IEnumerable<tblChiTietCapPhat> targets)
    {
        var ids = targets.Select(x => x.CTCP_DongNguonID ?? 0).ToList();
        Ensure(ids.All(x => x > 0) && ids.Distinct().Count() == ids.Count, "Dòng cấp phát gốc không hợp lệ.");
        var facts = await context.tblChiTietCapPhat.AsNoTracking().Where(x => ids.Contains(x.CTCP_ID)).Select(x => new { x.CTCP_ID, x.CP_ID }).ToListAsync();
        foreach (var receiptId in facts.Select(x => x.CP_ID).Distinct().Order()) await LockRow("receipt", receiptId);
        foreach (var lineId in ids.Order()) await LockRow("line", lineId);
        return await context.tblChiTietCapPhat.Include(x => x.CapPhat).Where(x => ids.Contains(x.CTCP_ID)).OrderBy(x => x.CTCP_ID).ToListAsync();
    }

    private async Task<tblCapPhatTaiSan> LockedDocument(int id, IEnumerable<int>? extraAssets = null)
    {
        var facts = await context.tblChiTietCapPhat.AsNoTracking().Where(x => x.CP_ID == id).Select(x => x.TS_ID).ToListAsync();
        await LockAssets(facts.Concat(extraAssets ?? []));
        await LockRow("receipt", id);
        var doc = await context.tblCapPhatTaiSan.Include(x => x.tblChiTietCapPhats).SingleAsync(x => x.CP_ID == id);
        Ensure(doc.tblChiTietCapPhats.All(x => facts.Contains(x.TS_ID)), "Danh sách tài sản vừa thay đổi. Hãy tải lại phiếu.");
        return doc;
    }

    private async Task LockAssets(IEnumerable<int> ids)
    {
        foreach (var id in ids.Distinct().Order()) await LockRow("asset", id);
    }

    private async Task LockRow(string table, int id)
    {
        FormattableString sql = table switch
        {
            "asset" => $"SELECT TS_ID AS Value FROM tbltaisan WHERE TS_ID={id} FOR UPDATE",
            "receipt" => $"SELECT CP_ID AS Value FROM tblcapphattaisan WHERE CP_ID={id} FOR UPDATE",
            "line" => $"SELECT CTCP_ID AS Value FROM tblchitietcapphat WHERE CTCP_ID={id} FOR UPDATE",
            "account" => $"SELECT TK_ID AS Value FROM tbltaikhoan WHERE TK_ID={id} FOR UPDATE",
            "department" => $"SELECT PB_ID AS Value FROM tblphongban WHERE PB_ID={id} FOR UPDATE",
            _ => throw new InvalidOperationException("Unknown lock target")
        };
        Ensure((await context.Database.SqlQuery<int>(sql).ToListAsync()).Count == 1, "Dữ liệu liên quan không tồn tại. Hãy tải lại.");
    }

    private async Task<IActionResult> StageWrite(Func<int, Task<int>> operation, string? requestKey = null, string? kind = null, bool creating = false)
    {
        if (!IsApprovedDatabase()) return StatusCode(503, "Cơ sở dữ liệu chưa được cấu hình cho giao nhận tài sản.");
        if (!ModelState.IsValid) return BadRequest("Dữ liệu biểu mẫu không hợp lệ. Vui lòng kiểm tra lại.");
        var actorId = HttpContext.Session.GetInt32("TaiKhoanID");
        if (actorId == null) return StatusCode(403);
        if (!await context.tblTaiKhoan.AsNoTracking().AnyAsync(x => x.TK_ID == actorId && x.TK_TrangThai && x.TK_VaiTro == "Admin"))
            return StatusCode(403);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var commitStarted = false;
        try
        {
            var returnedId = await operation(actorId.Value);
            await context.SaveChangesAsync();
            if (creating && returnedId == 0)
                returnedId = await context.tblCapPhatTaiSan.Where(x => x.CP_RequestKey == requestKey && x.CP_NguoiLapID == actorId).Select(x => x.CP_ID).SingleAsync();
            commitStarted = true;
            await transaction.CommitAsync();
            TempData["Success"] = "Đã xử lý yêu cầu.";
            return RedirectToAction(nameof(Details), new { id = returnedId });
        }
        catch (WorkflowFailure ex)
        {
            var rolledBack = await TryRollback(transaction); context.ChangeTracker.Clear();
            if (!rolledBack) return StatusCode(503, "Chưa xác định được kết quả giao dịch. Hãy tra cứu phiếu trước khi thử lại.");
            return StatusCode(ex.Status, ex.Message);
        }
        catch (Exception ex)
        {
            var rolledBack = await TryRollback(transaction); context.ChangeTracker.Clear();
            var number = SqlError(ex);
            logger.LogWarning(ex, "Lỗi giao dịch Sprint 2; mã lỗi {Number}.", number);
            if (commitStarted || !rolledBack)
                return StatusCode(503, "Chưa xác định được kết quả xác nhận. Hãy tra cứu phiếu trước khi thử lại cùng yêu cầu.");
            if (number == 1062 && creating && requestKey != null)
            {
                var existing = await context.tblCapPhatTaiSan.AsNoTracking().Where(x => x.CP_RequestKey == requestKey && x.CP_NguoiLapID == actorId && x.CP_LoaiPhieu == kind).Select(x => (int?)x.CP_ID).SingleOrDefaultAsync();
                if (existing.HasValue) return RedirectToAction(nameof(Details), new { id = existing });
            }
            return StatusCode(number is 1062 or 1205 or 1213 || ex is DbUpdateConcurrencyException ? 409 : 500,
                number switch { 1205 => "Hết thời gian chờ khóa; giao dịch đã hoàn tác. Hãy tải lại.", 1213 => "Xung đột khóa; giao dịch đã hoàn tác. Hãy tải lại.", 1062 => "Tài sản/yêu cầu đã được xử lý đồng thời. Hãy tải lại.", _ => "Không thể hoàn tất giao dịch. Mọi thay đổi đã được hoàn tác." });
        }
    }

    private static int? SqlError(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
            if (current is MySqlException mysql) return mysql.Number;
        return null;
    }

    private async Task<bool> TryRollback(IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(); return true; }
        catch (Exception rollbackError)
        {
            logger.LogError(rollbackError, "Không thể xác minh rollback giao dịch Sprint 2.");
            return false;
        }
    }

    private void Audit(tblCapPhatTaiSan doc, tblTaiKhoan actor, string action, string? before, string? reason = null)
    {
        context.tblSuKienCapPhat.Add(new tblSuKienCapPhat
        {
            CapPhat = doc, SK_Loai = action, SK_NguoiThucHienID = actor.TK_ID,
            SK_NguoiThucHienSnapshot = actor.TK_HoTen, SK_ThoiDiemUTC = DateTime.UtcNow,
            SK_LyDo = reason ?? doc.CP_LyDo, SK_Truoc = before, SK_Sau = Snapshot(doc), SK_RequestKey = Guid.NewGuid().ToString("D")
        });
    }

    private static string Snapshot(tblCapPhatTaiSan doc) => JsonSerializer.Serialize(new
    {
        doc.CP_ID, doc.CP_MaPhieu, doc.CP_LoaiPhieu, doc.CP_TrangThai, doc.CP_NguoiNhanID, doc.PB_ID,
        doc.CP_PhienBan, doc.CP_LyDo, doc.CP_GhiChu,
        Dongs = doc.tblChiTietCapPhats.OrderBy(x => x.TS_ID).Select(x => new { x.TS_ID, x.CTCP_DongNguonID, x.CTCP_TrangThai, x.CTCP_TinhTrangKhiGiao, x.CTCP_TinhTrangKhiThuHoi, x.CTCP_GhiChu })
    });

    private static void Ensure(bool valid, string message, int status = 409)
    {
        if (!valid) throw new WorkflowFailure(message, status);
    }

    private sealed class WorkflowFailure(string message, int status) : Exception(message)
    {
        public int Status { get; } = status;
    }

    #endregion

    #region Tài sản đang giữ và đối soát

    [HttpGet]
    public async Task<IActionResult> ActiveAssignments(int? nguoiNhanId = null, int? phongBanId = null, bool quaHan = false, int page = 1)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var query = context.tblChiTietCapPhat.AsNoTracking().Where(x => x.CTCP_TrangThai == Active && (x.CapPhat!.CP_TrangThai == Handed || (x.CapPhat.CP_TrangThai=="DaCapPhat" && x.CTCP_DoiSoatUTC != null)));
        if(EmployeeView){var viewer=ViewerId;query=query.Where(x=>x.CapPhat!.CP_NguoiNhanID==viewer);}
        if (nguoiNhanId.HasValue) query = query.Where(x => x.CapPhat!.CP_NguoiNhanID == nguoiNhanId);
        if (phongBanId.HasValue) query = query.Where(x => x.CapPhat!.PB_ID == phongBanId);
        if (quaHan) query = query.Where(x => x.CapPhat!.CP_NgayTraDuKien.HasValue && x.CapPhat.CP_NgayTraDuKien < today);
        var model = new GiaoNhanRows { Total = await query.CountAsync(), PageSize = 20, NguoiNhanId = nguoiNhanId, PhongBanId = phongBanId, QuaHan = quaHan };
        var visiblePeople = query.Select(x=>x.CapPhat!.CP_NguoiNhanID);
        var visibleDepartments = query.Select(x=>x.CapPhat!.PB_ID);
        model.People = await context.tblTaiKhoan.AsNoTracking().Where(x=>visiblePeople.Contains(x.TK_ID)).OrderBy(x=>x.TK_HoTen)
            .Select(x=>new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(x.TK_HoTen+(x.TK_TrangThai?"":" (đã khóa)"),x.TK_ID.ToString())).ToListAsync();
        model.Departments = await context.tblPhongBan.AsNoTracking().Where(x=>visibleDepartments.Contains(x.PB_ID)).OrderBy(x=>x.PB_TenPhongBan)
            .Select(x=>new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(x.PB_TenPhongBan,x.PB_ID.ToString())).ToListAsync();
        SetPage(model, page);
        model.Items = await query.OrderBy(x => x.CTCP_BatDauUTC).ThenBy(x => x.CTCP_ID)
            .Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).Select(LineProjection).ToListAsync();
        return View("TaiSanDangGiu", model);
    }

    [HttpGet]
    public async Task<IActionResult> Reconciliation(int page = 1)
    {
        var query = context.tblChiTietCapPhat.AsNoTracking().Where(x => x.CTCP_TrangThai == null ||
            (x.CTCP_TrangThai == Active && x.TaiSan!.TS_TrangThai != "DaCapPhat"));
        var model = new GiaoNhanRows { Total = await query.CountAsync(), PageSize = 20, Reconciliation = true };
        SetPage(model, page);
        model.Items = await query.OrderBy(x => x.TS_ID).ThenBy(x => x.CTCP_ID)
            .Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).Select(LineProjection).ToListAsync();
        return View("TaiSanDangGiu", model);
    }

    private Task<IActionResult> ReportLostCore(int id, string lyDo, string? requestKey, string? expectedCondition) => StageWrite(async actorId =>
    {
        Ensure(!string.IsNullOrWhiteSpace(lyDo) && lyDo.Trim().Length <= 2000, "Cần lý do báo mất.");
        if(requestKey!=null)
        {
            Ensure(Guid.TryParseExact(requestKey,"D",out _),"Mã yêu cầu không hợp lệ.",400);
            var prior=await context.tblSuKienCapPhat.AsNoTracking().SingleOrDefaultAsync(x=>x.SK_RequestKey==requestKey);
            if(prior!=null){Ensure(prior.SK_NguoiThucHienID==actorId&&prior.SK_Loai=="BaoMat"&&prior.CTCP_ID==id,"Mã yêu cầu đã được sử dụng.");return prior.CP_ID;}
        }
        var fact = await context.tblChiTietCapPhat.AsNoTracking().Where(x => x.CTCP_ID == id).Select(x => new { x.CP_ID, x.TS_ID }).SingleOrDefaultAsync();
        Ensure(fact != null, "Không tìm thấy dòng cấp phát.");
        await LockAssets([fact!.TS_ID]);
        await LockRow("receipt", fact.CP_ID); await LockRow("line", id); await LockRow("account", actorId);
        var actor = await context.tblTaiKhoan.SingleAsync(x => x.TK_ID == actorId);
        Ensure(actor.TK_TrangThai && actor.TK_VaiTro == "Admin", "Tài khoản không đủ quyền.", 403);
        var line = await context.tblChiTietCapPhat.Include(x => x.CapPhat).SingleAsync(x => x.CTCP_ID == id);
        Ensure(line.CTCP_TrangThai == Active && (line.CapPhat!.CP_TrangThai == Handed || (line.CapPhat.CP_TrangThai=="DaCapPhat"&&line.CTCP_DoiSoatUTC.HasValue)), "Không có trách nhiệm hiệu lực đã xác minh.");
        var asset = await context.tblTaiSan.SingleAsync(x => x.TS_ID == fact.TS_ID);
        if (asset.TS_TinhTrang == "MatDangXuLy") return fact.CP_ID;
        Ensure(expectedCondition==null||asset.TS_TinhTrang==expectedCondition,"Tình trạng vừa thay đổi. Hãy tải lại.");
        var before = JsonSerializer.Serialize(new { asset.TS_ID, asset.TS_TinhTrang, asset.TS_TrangThai });
        asset.TS_TinhTrang = "MatDangXuLy";
        context.tblSuKienCapPhat.Add(new tblSuKienCapPhat
        {
            CapPhat = line.CapPhat, ChiTiet = line, SK_Loai = "BaoMat", SK_NguoiThucHienID = actorId,
            SK_NguoiThucHienSnapshot = actor.TK_HoTen, SK_ThoiDiemUTC = DateTime.UtcNow,
            SK_LyDo = lyDo.Trim(), SK_Truoc = before,
            SK_Sau = JsonSerializer.Serialize(new { asset.TS_ID, asset.TS_TinhTrang, asset.TS_TrangThai }),
            SK_RequestKey = requestKey ?? Guid.NewGuid().ToString("D")
        });
        return fact.CP_ID;
    });

    #endregion

    #region Xác minh dữ liệu lịch sử

    [HttpGet]
    public async Task<IActionResult> ReconcileLegacy([FromRoute]int id)
    {
        var form=await LegacyForm(id);
        return form==null?NotFound():View("DoiSoatLichSu",form);
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> ReconcileLegacy([FromRoute]int id,GiaoNhanReconcileInput model)
    {
        var result=await StageWrite(async actorId=>
        {
            Ensure(Guid.TryParseExact(model.RequestKey,"D",out _)&&model.DaXacMinhDanhTinhVaHienVat&&model.BanGiaoThucTeVietNam.HasValue&&!string.IsNullOrWhiteSpace(model.CanCu)&&model.CanCu.Trim().Length<=2000,"Cần bằng chứng, danh tính và thời điểm bàn giao thực tế đã được xác minh.",400);
            var prior=await context.tblSuKienCapPhat.AsNoTracking().SingleOrDefaultAsync(x=>x.SK_RequestKey==model.RequestKey);
            if(prior!=null){Ensure(prior.CP_ID==id&&prior.SK_Loai=="DoiSoatLichSu"&&prior.SK_NguoiThucHienID==actorId,"Mã yêu cầu đã được dùng.");return id;}
            var doc=await LockedDocument(id);
            Ensure(doc.CP_LoaiPhieu==null&&doc.CP_TrangThai=="DaCapPhat"&&doc.tblChiTietCapPhats.Count>0&&doc.tblChiTietCapPhats.All(x=>x.CTCP_TrangThai==null&&x.CTCP_NgayThuHoi==null),"Chỉ đối soát các dòng lịch sử chưa thu hồi/chưa xác minh. Không tự sửa dữ liệu mâu thuẫn.");
            foreach(var accountId in new[]{actorId,model.NguoiNhanId}.Distinct().Order())await LockRow("account",accountId);
            var actor=await context.tblTaiKhoan.SingleAsync(x=>x.TK_ID==actorId);
            Ensure(actor.TK_TrangThai&&actor.TK_VaiTro=="Admin","Không đủ quyền.",403);
            var receiver=await context.tblTaiKhoan.SingleAsync(x=>x.TK_ID==model.NguoiNhanId);
            var local=DateTime.SpecifyKind(model.BanGiaoThucTeVietNam!.Value,DateTimeKind.Unspecified);
            var actual=new DateTimeOffset(local,TimeSpan.FromHours(7)).UtcDateTime;
            Ensure(doc.CP_NgayCapPhat.HasValue&&DateOnly.FromDateTime(local)==doc.CP_NgayCapPhat&&actual<=DateTime.UtcNow,"Thời điểm phải có căn cứ, cùng ngày đã ghi trên phiếu cũ và không sau hiện tại.");
            var assets=doc.tblChiTietCapPhats.Select(x=>x.TS_ID).ToArray();
            Ensure(!await context.tblChiTietCapPhat.AnyAsync(x=>assets.Contains(x.TS_ID)&&x.CP_ID!=id&&(x.CTCP_TrangThai==Active||x.CTCP_TrangThai==null)),"Còn dòng giữ hiệu lực hoặc lịch sử khác chưa đối soát của tài sản. Cần kiểm tra trước.");
            var before=Snapshot(doc);
            doc.CP_NguoiNhanID=model.NguoiNhanId;doc.CP_PhienBan++;
            foreach(var line in doc.tblChiTietCapPhats)
            {
                line.CTCP_TrangThai=Active;line.CTCP_BatDauUTC=actual;
                line.CTCP_NguoiDoiSoatID=actorId;line.CTCP_DoiSoatUTC=DateTime.UtcNow;
            }
            context.tblSuKienCapPhat.Add(new tblSuKienCapPhat
            {
                CapPhat=doc,SK_Loai="DoiSoatLichSu",SK_NguoiThucHienID=actorId,SK_NguoiThucHienSnapshot=actor.TK_HoTen,
                SK_ThoiDiemUTC=DateTime.UtcNow,SK_LyDo=model.CanCu.Trim(),SK_Truoc=before,
                SK_Sau=JsonSerializer.Serialize(new{doc.CP_ID,doc.CP_NguoiNhanID,NguoiNhanXacMinh=receiver.TK_HoTen,BanGiaoThucTeUTC=actual,DuLieu=Snapshot(doc)}),
                SK_RequestKey=model.RequestKey
            });
            // No legacy receiver text/date/status, asset state or physical condition is overwritten here.
            return id;
        });
        if(WantsHtml&&result is ObjectResult{StatusCode:>=400 and <600} error)
        {
            ModelState.AddModelError("",error.Value?.ToString()??"Không thể đối soát.");
            var form=await LegacyForm(id,model);
            if(form!=null){var view=View("DoiSoatLichSu",form);view.StatusCode=error.StatusCode;return view;}
        }
        return result;
    }

    private async Task<GiaoNhanReconcileForm?> LegacyForm(int id,GiaoNhanReconcileInput? posted=null)
    {
        var form=await context.tblCapPhatTaiSan.AsNoTracking().Where(x=>x.CP_ID==id&&x.CP_LoaiPhieu==null)
            .Select(x=>new GiaoNhanReconcileForm{Id=x.CP_ID,Code=x.CP_MaPhieu,RecordedReceiver=x.CP_NguoiNhan,RecordedDate=x.CP_NgayCapPhat}).SingleOrDefaultAsync();
        if(form==null)return null;
        form.RequestKey=posted?.RequestKey??Guid.NewGuid().ToString("D");form.NguoiNhanId=posted?.NguoiNhanId??0;
        form.BanGiaoThucTeVietNam=posted?.BanGiaoThucTeVietNam;form.CanCu=posted?.CanCu??"";form.DaXacMinhDanhTinhVaHienVat=posted?.DaXacMinhDanhTinhVaHienVat??false;
        form.People=await context.tblTaiKhoan.AsNoTracking().OrderBy(x=>x.TK_HoTen).ThenBy(x=>x.TK_ID)
            .Select(x=>new SelectListItem(x.TK_HoTen+" (#"+x.TK_ID+")"+(x.TK_TrangThai?"":" (đã khóa)"),x.TK_ID.ToString())).ToListAsync();
        return form;
    }

    #endregion

    #region Tình trạng tài sản và xử lý sự việc

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> ReportLost([FromRoute] int id,string lyDo,string? requestKey=null,string? expectedCondition=null)
    {
        var result=await ReportLostCore(id,lyDo,requestKey,expectedCondition);
        if(WantsHtml&&result is ObjectResult{StatusCode:>=400 and <600} error)
        {
            ModelState.AddModelError("",error.Value?.ToString()??"Không thể báo mất.");
            var form=await IncidentForm(id);
            if(form!=null){form.LyDo=lyDo;form.RequestKey=requestKey??form.RequestKey;var view=View("XuLySuViec",form);view.StatusCode=error.StatusCode;return view;}
        }
        return result;
    }
    [HttpGet]
    public async Task<IActionResult> Incident([FromRoute] int id)
    {
        var form=await IncidentForm(id);
        return form==null?NotFound():View("XuLySuViec",form);
    }

    [HttpGet]
    public async Task<IActionResult> Inspect([FromRoute] int id)
    {
        var form=await InspectionForm(id);
        return form==null?NotFound():View("XuLySuViec",form);
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveLoss([FromRoute] int id,GiaoNhanResolutionInput model)
    {
        var result=await ResolveAsset(id,model,true);
        if(WantsHtml&&result is ObjectResult{StatusCode:>=400 and <600} error)
        {
            ModelState.AddModelError("",error.Value?.ToString()??"Không thể ghi nhận tìm thấy.");
            var form=await IncidentForm(id,model);
            if(form!=null){var view=View("XuLySuViec",form);view.StatusCode=error.StatusCode;return view;}
        }
        return result;
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Inspect([FromRoute] int id,GiaoNhanResolutionInput model)
    {
        var result=await ResolveAsset(id,model,false);
        if(WantsHtml&&result is ObjectResult{StatusCode:>=400 and <600} error)
        {
            ModelState.AddModelError("",error.Value?.ToString()??"Không thể ghi nhận kiểm tra.");
            var form=await InspectionForm(id,model);
            if(form!=null){var view=View("XuLySuViec",form);view.StatusCode=error.StatusCode;return view;}
        }
        return result;
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> RecordCondition([FromRoute] int id,GiaoNhanResolutionInput model)
    {
        var result=await ResolveAsset(id,model,false,true);
        if(WantsHtml&&result is ObjectResult{StatusCode:>=400 and <600} error)
        {
            ModelState.AddModelError("",error.Value?.ToString()??"Không thể ghi nhận tình trạng.");
            var form=await IncidentForm(id,model);
            if(form!=null){var view=View("XuLySuViec",form);view.StatusCode=error.StatusCode;return view;}
        }
        return result;
    }

    private Task<IActionResult> ResolveAsset(int id,GiaoNhanResolutionInput model,bool found,bool activeCondition=false) => StageWrite(async actorId=>
    {
        Ensure(Guid.TryParseExact(model.RequestKey,"D",out _)&&model.DaKiemTraThucTe&&!string.IsNullOrWhiteSpace(model.LyDo)&&model.LyDo.Trim().Length<=2000&&ReturnConditions.Contains(model.TinhTrang),"Cần kiểm tra thực tế, lý do và tình trạng hợp lệ.",400);
        var action=activeCondition?"GhiNhanTinhTrang":found?"TimThay":"KiemTraSauThuHoi";
        var prior=await context.tblSuKienCapPhat.AsNoTracking().Where(x=>x.SK_RequestKey==model.RequestKey)
            .Select(x=>new{x.CP_ID,x.CTCP_ID,x.SK_Loai,x.SK_NguoiThucHienID,AssetId=x.ChiTiet!=null?(int?)x.ChiTiet.TS_ID:null}).SingleOrDefaultAsync();
        if(prior!=null)
        {
            Ensure(prior.SK_Loai==action&&prior.SK_NguoiThucHienID==actorId&&(found||activeCondition?prior.CTCP_ID==id:prior.AssetId==id),"Mã yêu cầu đã được sử dụng cho giao dịch khác.");
            return prior.CP_ID;
        }
        var fact=found||activeCondition
            ? await context.tblChiTietCapPhat.AsNoTracking().Where(x=>x.CTCP_ID==id).Select(x=>new{x.CTCP_ID,x.CP_ID,x.TS_ID}).SingleOrDefaultAsync()
            : await context.tblChiTietCapPhat.AsNoTracking().Where(x=>x.TS_ID==id&&x.CTCP_TrangThai==Received&&x.CapPhat!.CP_TrangThai==Returned).OrderByDescending(x=>x.CapPhat!.CP_ThucHienUTC).ThenByDescending(x=>x.CTCP_ID).Select(x=>new{x.CTCP_ID,x.CP_ID,x.TS_ID}).FirstOrDefaultAsync();
        Ensure(fact!=null,"Không có dòng giao nhận phù hợp đã xác minh.");
        await LockAssets([fact!.TS_ID]);await LockRow("receipt",fact.CP_ID);await LockRow("line",fact.CTCP_ID);await LockRow("account",actorId);
        var actor=await context.tblTaiKhoan.SingleAsync(x=>x.TK_ID==actorId);
        Ensure(actor.TK_TrangThai&&actor.TK_VaiTro=="Admin","Không đủ quyền.",403);
        var asset=await context.tblTaiSan.SingleAsync(x=>x.TS_ID==fact.TS_ID);
        var line=await context.tblChiTietCapPhat.Include(x=>x.CapPhat).SingleAsync(x=>x.CTCP_ID==fact.CTCP_ID);
        Ensure(asset.TS_TinhTrang==model.ExpectedCondition&&asset.TS_TrangThai==model.ExpectedState,"Tình trạng tài sản vừa thay đổi. Hãy tải lại.");
        if(found||activeCondition)
        {
            Ensure(line.CTCP_TrangThai==Active&&(line.CapPhat!.CP_TrangThai==Handed||line.CTCP_DoiSoatUTC.HasValue),"Không còn trách nhiệm đang hiệu lực đã xác minh.");
            Ensure(found?asset.TS_TinhTrang=="MatDangXuLy":asset.TS_TinhTrang!="MatDangXuLy",found?"Sự việc mất không còn ở trạng thái cần ghi nhận tìm thấy.":"Tài sản đang báo mất, cần ghi nhận tìm thấy trước.");
        }
        else
        {
            Ensure(!await context.tblChiTietCapPhat.AnyAsync(x=>x.TS_ID==asset.TS_ID&&(x.CTCP_TrangThai==null||x.CTCP_TrangThai==Active)),"Tài sản đang có người giữ hoặc cần đối soát.");
            Ensure(line.CTCP_TrangThai==Received&&asset.TS_TinhTrang!="MatDangXuLy","Chưa có tiếp nhận vật lý hợp lệ.");
            if(model.TinhTrang=="Tot")
            {
                Ensure(!await context.tblBaoTriTaiSan.AnyAsync(x=>x.TS_ID==asset.TS_ID&&x.BT_TrangThai!="DaHoanThanh"),"Hồ sơ bảo trì chưa hoàn thành.");
                Ensure(!await context.tblChiTietThanhLy.AnyAsync(x=>x.TS_ID==asset.TS_ID&&x.ThanhLy!.TL_TrangThai!="Huy"),"Tài sản đang có hồ sơ thanh lý.");
            }
        }
        var before=JsonSerializer.Serialize(new{asset.TS_ID,asset.TS_TinhTrang,asset.TS_TrangThai});
        asset.TS_TinhTrang=model.TinhTrang;
        if(!found&&!activeCondition)asset.TS_TrangThai=model.TinhTrang switch{"Tot"=>"ChuaCapPhat","CanBaoTri"=>"ChoBaoTri","HuHong"=>"ChoXuLy",_=>"ChoKiemTra"};
        context.tblSuKienCapPhat.Add(new tblSuKienCapPhat
        {
            CapPhat=line.CapPhat,ChiTiet=line,SK_Loai=action,SK_NguoiThucHienID=actorId,SK_NguoiThucHienSnapshot=actor.TK_HoTen,
            SK_ThoiDiemUTC=DateTime.UtcNow,SK_LyDo=model.LyDo.Trim(),SK_Truoc=before,
            SK_Sau=JsonSerializer.Serialize(new{asset.TS_ID,asset.TS_TinhTrang,asset.TS_TrangThai}),SK_RequestKey=model.RequestKey
        });
        return fact.CP_ID;
    });

    private async Task<GiaoNhanResolutionForm?> IncidentForm(int id,GiaoNhanResolutionInput? posted=null)
    {
        var form=await context.tblChiTietCapPhat.AsNoTracking().Where(x=>x.CTCP_ID==id&&x.CTCP_TrangThai==Active)
            .Select(x=>new GiaoNhanResolutionForm{LineId=x.CTCP_ID,ReceiptId=x.CP_ID,AssetId=x.TS_ID,Code=x.TaiSan!.TS_MaTaiSan,Name=x.TaiSan.TS_TenTaiSan,ExpectedCondition=x.TaiSan.TS_TinhTrang,ExpectedState=x.TaiSan.TS_TrangThai,Lost=x.TaiSan.TS_TinhTrang=="MatDangXuLy"}).SingleOrDefaultAsync();
        if(form!=null)FillResolution(form,posted);
        return form;
    }
    private async Task<GiaoNhanResolutionForm?> InspectionForm(int id,GiaoNhanResolutionInput? posted=null)
    {
        var form=await context.tblChiTietCapPhat.AsNoTracking().Where(x=>x.TS_ID==id&&x.CTCP_TrangThai==Received&&x.CapPhat!.CP_TrangThai==Returned)
            .OrderByDescending(x=>x.CapPhat!.CP_ThucHienUTC).ThenByDescending(x=>x.CTCP_ID)
            .Select(x=>new GiaoNhanResolutionForm{LineId=x.CTCP_ID,ReceiptId=x.CP_ID,AssetId=x.TS_ID,Code=x.TaiSan!.TS_MaTaiSan,Name=x.TaiSan.TS_TenTaiSan,ExpectedCondition=x.TaiSan.TS_TinhTrang,ExpectedState=x.TaiSan.TS_TrangThai,Inspection=true}).FirstOrDefaultAsync();
        if(form!=null)FillResolution(form,posted);
        return form;
    }
    private static void FillResolution(GiaoNhanResolutionForm form,GiaoNhanResolutionInput? input)
    {
        form.RequestKey=input?.RequestKey??Guid.NewGuid().ToString("D");form.LyDo=input?.LyDo??"";
        form.TinhTrang=input?.TinhTrang??"Tot";form.DaKiemTraThucTe=input?.DaKiemTraThucTe??false;
    }

    #endregion

}
