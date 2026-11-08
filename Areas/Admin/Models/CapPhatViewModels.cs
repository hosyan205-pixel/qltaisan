using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace DoAn.Areas.Admin.Models;

#region Tra cứu phiếu và lịch sử

public class CapPhatPage
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int Total { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public int FirstItem => Total == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastItem => Math.Min(Page * PageSize, Total);
}

public sealed class CapPhatIndex : CapPhatPage
{
    public string? LoaiPhieu { get; set; }
    public bool CanDoiSoat { get; set; }
    public bool NguoiNhanNgungHoatDong { get; set; }
    public string? Keyword { get; set; }
    public string? NguoiNhan { get; set; }
    public int? PhongBanId { get; set; }
    public string? TrangThai { get; set; }
    public string? ThuHoi { get; set; }
    public bool PhongBanNgungHoatDong { get; set; }
    public DateOnly? TuNgay { get; set; }
    public DateOnly? DenNgay { get; set; }
    public string Sort { get; set; } = "date_desc";
    public List<SelectListItem> PhongBans { get; set; } = [];
    public List<string> TrangThais { get; set; } = [];
    public List<CapPhatRow> Items { get; set; } = [];

    public Dictionary<string, string> Routes(int page) => new()
    {
        ["keyword"] = Keyword ?? "", ["nguoiNhan"] = NguoiNhan ?? "",
        ["phongBanId"] = PhongBanId?.ToString() ?? "", ["trangThai"] = TrangThai ?? "",
        ["thuHoi"] = ThuHoi ?? "", ["phongBanNgungHoatDong"] = PhongBanNgungHoatDong.ToString(),
        ["tuNgay"] = TuNgay?.ToString("yyyy-MM-dd") ?? "", ["denNgay"] = DenNgay?.ToString("yyyy-MM-dd") ?? "",
        ["sort"] = Sort, ["page"] = page.ToString(), ["pageSize"] = PageSize.ToString()
        , ["loaiPhieu"] = LoaiPhieu ?? "", ["canDoiSoat"] = CanDoiSoat.ToString(), ["nguoiNhanNgungHoatDong"] = NguoiNhanNgungHoatDong.ToString()
    };
}

public sealed class CapPhatRow
{
    public DateTime? NgayLapUTC { get; set; }
    public DateTime? ThucHienUTC { get; set; }
    public DateTime? XacNhanUTC { get; set; }
    public string? NguoiXacNhan { get; set; }
    public string? LoaiPhieu { get; set; }
    public bool CanDoiSoat => SoCanDoiSoat > 0 || (LoaiPhieu == null && SoTaiSan == 0);
    public int SoCanDoiSoat { get; set; }
    public int SoDangGiu { get; set; }
    public int SoDaChuyen { get; set; }
    public string? NguoiLap { get; set; }
    public string? NguoiNhanXacMinh { get; set; }
    public long PhienBan { get; set; }
    public DateOnly? NgayDuKien { get; set; }
    public DateOnly? NgayTraDuKien { get; set; }
    public int Id { get; set; }
    public string MaPhieu { get; set; } = "";
    public DateOnly? NgayCapPhat { get; set; }
    public string? NguoiGiao { get; set; }
    public string? NguoiNhan { get; set; }
    public string? PhongBan { get; set; }
    public bool? PhongBanHoatDong { get; set; }
    public string TrangThai { get; set; } = "";
    public string? LyDo { get; set; }
    public string? GhiChu { get; set; }
    public int SoTaiSan { get; set; }
    public int SoDaThuHoi { get; set; }
    // Chỉ tổng hợp các ngày nhận lại đã lưu; không suy ra xác nhận bàn giao.
    public string ThuHoiLabel => SoTaiSan == 0 ? "Chưa có tài sản"
        : SoDaThuHoi == 0 ? "Chưa ghi nhận thu hồi"
        : SoDaThuHoi == SoTaiSan ? "Đã ghi nhận thu hồi toàn bộ" : "Đã ghi nhận thu hồi một phần";
}

public sealed class CapPhatLine
{
    public string? NguoiChiuTrachNhiem { get; set; }
    public int? NguoiNhanId { get; set; }
    public bool DaDoiSoat { get; set; }
    public int SoNgayQuaHan => TrangThaiDong == "DangHieuLuc" && NgayTraDuKien.HasValue ? Math.Max(0, DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).DayNumber - NgayTraDuKien.Value.DayNumber) : 0;
    public int? SoNgaySuDung => BatDauUTC.HasValue ? Math.Max(0, DateOnly.FromDateTime((KetThucUTC ?? DateTime.UtcNow).AddHours(7)).DayNumber - DateOnly.FromDateTime(BatDauUTC.Value.AddHours(7)).DayNumber) : null;
    public string? LoaiPhieu { get; set; }
    public string? TrangThaiDong { get; set; }
    public string? TrangThaiTaiSan { get; set; }
    public string? TinhTrangTaiSan { get; set; }
    public string? ViTriSnapshot { get; set; }
    public string? PhuKienSnapshot { get; set; }
    public DateTime? BatDauUTC { get; set; }
    public DateTime? KetThucUTC { get; set; }
    public DateOnly? NgayTraDuKien { get; set; }
    public int? DongNguonId { get; set; }
    public bool CanDoiSoat => TrangThaiDong == null;
    public int Id { get; set; }
    public int TaiSanId { get; set; }
    public int PhieuId { get; set; }
    public string MaPhieu { get; set; } = "";
    public DateOnly? NgayCapPhat { get; set; }
    public string? NguoiNhan { get; set; }
    public string? PhongBan { get; set; }
    public string? MaTaiSan { get; set; }
    public string? TenTaiSan { get; set; }
    public string? DanhMuc { get; set; }
    public string? TinhTrangKhiGiao { get; set; }
    public string? TinhTrangKhiThuHoi { get; set; }
    public DateOnly? NgayThuHoi { get; set; }
    public string? GhiChu { get; set; }
}

public sealed class CapPhatDetails : CapPhatPage
{
    public int EventPage { get; set; } = 1;
    public int EventTotal { get; set; }
    public int EventPages => Math.Max(1,(int)Math.Ceiling(EventTotal / 20d));
    public List<GiaoNhanAuditRow> Events { get; set; } = [];
    public CapPhatRow Phieu { get; set; } = new();
    public List<CapPhatLine> Items { get; set; } = [];
}

public sealed class CapPhatHistory : CapPhatPage
{
    public int TaiSanId { get; set; }
    public string MaTaiSan { get; set; } = "";
    public string TenTaiSan { get; set; } = "";
    public List<CapPhatLine> Items { get; set; } = [];
}

#endregion

#region Trạng thái và dữ liệu nhập phiếu

public static class GiaoNhanStates
{
    public const string Draft = "Nhap", Waiting = "ChoXacNhan", Handed = "DaBanGiao", Returned = "DaThuHoi", Cancelled = "Huy";
    public const string Give = "Giao", Return = "ThuHoi", Transfer = "Chuyen";
    public const string Active = "DangHieuLuc", ClosedReturn = "DaThuHoi", ClosedTransfer = "DaChuyen";
    public const string PendingGive = "ChoGiao", PendingReturn = "ChoNhan", PendingTransfer = "ChoChuyen", Received = "DaNhan";
    public static string Pending(string kind) => kind switch { Return => PendingReturn, Transfer => PendingTransfer, _ => PendingGive };
    public static string Confirmed(string kind) => kind == Return ? Returned : Handed;
    public static readonly HashSet<string> ReturnConditions = ["Tot", "CanKiemTra", "CanBaoTri", "HuHong", "ThieuPhuKien"];
}

public class GiaoNhanInput
{
    [Required, StringLength(36)] public string RequestKey { get; set; } = "";
    [Range(1, int.MaxValue)] public int PhongBanId { get; set; }
    public int? NguoiNhanId { get; set; }
    public int? PhieuGocId { get; set; }
    public DateOnly? NgayDuKien { get; set; }
    public DateOnly? NgayTraDuKien { get; set; }
    [Required, StringLength(20)] public string LoaiDoiTuong { get; set; } = "CaNhan";
    [Required, StringLength(2000)] public string LyDo { get; set; } = "";
    [StringLength(4000)] public string? GhiChu { get; set; }
    public long PhienBan { get; set; }
    [MinLength(1), MaxLength(100)] public List<GiaoNhanDongInput> Dongs { get; set; } = [];
}

public sealed class GiaoNhanDongInput
{
    public bool Chon { get; set; } = true;
    public int TaiSanId { get; set; }
    public int? DongNguonId { get; set; }
    [StringLength(100)] public string? TinhTrangNhan { get; set; }
    [StringLength(2000)] public string? GhiChu { get; set; }
    [StringLength(4000)] public string? PhuKien { get; set; }
    [StringLength(150)] public string? ViTri { get; set; }
}

public sealed class GiaoNhanConfirmInput
{
    public long PhienBan { get; set; }
    public DateTimeOffset? ThucHien { get; set; }
    public DateTime? ThucHienVietNam { get; set; }
}

#endregion

#region Biểu mẫu và thông tin hiển thị

public sealed class GiaoNhanForm : GiaoNhanInput
{
    [BindNever,ValidateNever] public int? DocumentId { get; set; }
    [BindNever,ValidateNever] public string Kind { get; set; } = GiaoNhanStates.Give;
    [BindNever,ValidateNever] public string? SourceCode { get; set; }
    [BindNever,ValidateNever] public string? SourceHolder { get; set; }
    [BindNever,ValidateNever] public string? SourceRecordedHolder { get; set; }
    [BindNever,ValidateNever] public bool SourceHolderVerified { get; set; }
    [BindNever,ValidateNever] public string? SourceDepartment { get; set; }
    [BindNever,ValidateNever] public List<GiaoNhanPerson> People { get; set; } = [];
    [BindNever,ValidateNever] public List<SelectListItem> Departments { get; set; } = [];
    [BindNever,ValidateNever] public List<GiaoNhanAsset> SelectedAssets { get; set; } = [];
    [BindNever,ValidateNever] public List<GiaoNhanSource> Sources { get; set; } = [];
}
public sealed class GiaoNhanPerson
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? DepartmentId { get; set; }
    public bool Active { get; set; }
    public string? DepartmentName { get; set; }
    public string? UnavailableReason { get; set; }
}
public sealed class GiaoNhanAsset
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Category { get; set; }
    public string? Serial { get; set; }
    public string? Location { get; set; }
    public string State { get; set; } = "";
    public string Condition { get; set; } = "";
    public bool Managed { get; set; }
    public bool UnknownHistory { get; set; }
    public bool HasHolder { get; set; }
    public bool Maintenance { get; set; }
    public bool Disposal { get; set; }
    public bool Inventory { get; set; }
    public string? UnavailableReason => !Managed ? "Đã ngừng quản lý"
        : UnknownHistory ? "Có lịch sử cần đối soát"
        : HasHolder ? "Đang có người chịu trách nhiệm"
        : State != "ChuaCapPhat" ? GiaoNhanUi.AssetState(State)
        : Condition != "Tot" ? GiaoNhanUi.Condition(Condition)
        : Maintenance ? "Đang có hồ sơ bảo trì chưa hoàn thành"
        : Disposal ? "Đang có hồ sơ thanh lý"
        : Inventory ? "Đang kiểm kê" : null;
}
public sealed class GiaoNhanAssetPage : CapPhatPage
{
    public string? Keyword { get; set; }
    public List<GiaoNhanAsset> Items { get; set; } = [];
}
public sealed class GiaoNhanSource
{
    public int Id { get; set; }
    public int AssetId { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? ConditionWhenGiven { get; set; }
    public string? CurrentCondition { get; set; }
    public string? State { get; set; }
    public DateTime? StartUtc { get; set; }
    public string? AssetState { get; set; }
    public bool Managed { get; set; }
    public bool PendingMaintenance { get; set; }
    public bool PendingDisposal { get; set; }
    public IEnumerable<string> TransferBlockReasons
    {
        get
        {
            if (!Managed) yield return "Tài sản đã ngừng quản lý.";
            if (AssetState != "DaCapPhat") yield return "Trạng thái tài sản: " + GiaoNhanUi.AssetState(AssetState) + ". Cần đối soát tình trạng thực tế trước khi chuyển giao.";
            if (CurrentCondition != "Tot") yield return "Tình trạng: " + GiaoNhanUi.Condition(CurrentCondition) + ". Cần kiểm tra, xử lý trước khi chuyển giao.";
            if (PendingMaintenance) yield return "Còn hồ sơ bảo trì chưa hoàn thành.";
            if (PendingDisposal) yield return "Đang có hồ sơ thanh lý.";
        }
    }
}
public sealed class GiaoNhanConfirmation
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Kind { get; set; } = "";
    public string State { get; set; } = "";
    public long PhienBan { get; set; }
    public string? Receiver { get; set; }
    public string? Department { get; set; }
    public string? Purpose { get; set; }
    public DateTime? ThucHienVietNam { get; set; }
    public List<CapPhatLine> Lines { get; set; } = [];
}
public sealed class GiaoNhanAuditRow
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Actor { get; set; } = "";
    public DateTime AtUtc { get; set; }
    public string? Reason { get; set; }
}
public static class GiaoNhanUi
{
    public static string Kind(string? kind) => kind switch { "Giao"=>"Giao tài sản","ThuHoi"=>"Thu hồi","Chuyen"=>"Chuyển giao",_=>"Phiếu lịch sử" };
    public static string State(string? value) => value switch
    {
        "Nhap"=>"Nháp","ChoXacNhan"=>"Chờ xác nhận","DaBanGiao"=>"Đã bàn giao","Huy"=>"Đã hủy",
        "DaCapPhat"=>"Đã cấp phát (dữ liệu cũ)","DaThuHoi"=>"Đã thu hồi","DaNhan"=>"Đã nhận lại",
        "ChoGiao"=>"Chờ giao","ChoNhan"=>"Chờ nhận lại","ChoChuyen"=>"Chờ chuyển giao",
        "DangHieuLuc"=>"Đang chịu trách nhiệm","DaChuyen"=>"Đã chuyển giao",null=>"Cần đối soát",_=>value
    };
    public static string Condition(string? value) => value switch
    {
        "Tot"=>"Tốt","CanKiemTra"=>"Cần kiểm tra","CanBaoTri"=>"Cần bảo trì","HuHong" or "Hong"=>"Hư hỏng",
        "ThieuPhuKien"=>"Thiếu phụ kiện","MatDangXuLy"=>"Mất — đang xử lý","XuongCap"=>"Xuống cấp",null=>"Chưa ghi nhận",_=>value
    };
    public static string AssetState(string? value) => value switch
    {
        "ChuaCapPhat"=>"Chưa cấp phát","DaCapPhat"=>"Đang sử dụng","DangBaoTri"=>"Đang bảo trì",
        "ChoBaoTri"=>"Chờ bảo trì","ChoKiemTra"=>"Chờ kiểm tra","ChoXuLy"=>"Chờ xử lý",
        "ChoThanhLy"=>"Chờ thanh lý","DaThanhLy"=>"Đã thanh lý",null=>"Chưa ghi nhận",_=>value
    };
    public static string Event(string value) => value switch
    {
        "LapNhap"=>"Lập phiếu nháp","SuaNhap"=>"Sửa phiếu nháp","GuiXacNhan"=>"Gửi xác nhận","Huy"=>"Hủy phiếu",
        "XacNhanGiao"=>"Xác nhận giao","XacNhanThuHoi"=>"Xác nhận nhận lại","XacNhanChuyen"=>"Xác nhận chuyển giao",
        "BaoMat"=>"Báo mất","TimThay"=>"Ghi nhận tìm thấy","KiemTraSauThuHoi"=>"Kiểm tra sau thu hồi",
        "DoiSoatLichSu"=>"Đối soát lịch sử","GhiNhanTinhTrang"=>"Ghi nhận tình trạng đang sử dụng",_=>value
    };
}

#endregion

#region Tài sản đang giữ và đối soát

public sealed class GiaoNhanRows : CapPhatPage
{
    public List<CapPhatLine> Items { get; set; } = [];
    public bool Reconciliation { get; set; }
    public int? NguoiNhanId { get; set; }
    public int? PhongBanId { get; set; }
    public bool QuaHan { get; set; }
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> People { get; set; } = [];
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> Departments { get; set; } = [];
    public Dictionary<string,string> Routes(int page) => new()
    {
        ["page"] = page.ToString(), ["nguoiNhanId"] = NguoiNhanId?.ToString() ?? "",
        ["phongBanId"] = PhongBanId?.ToString() ?? "", ["quaHan"] = QuaHan.ToString()
    };
}

#endregion

#region Xác minh dữ liệu lịch sử

public class GiaoNhanReconcileInput
{
    [Required,StringLength(36)] public string RequestKey { get; set; } = "";
    [Range(1,int.MaxValue)] public int NguoiNhanId { get; set; }
    [Required] public DateTime? BanGiaoThucTeVietNam { get; set; }
    [Required,StringLength(2000)] public string CanCu { get; set; } = "";
    public bool DaXacMinhDanhTinhVaHienVat { get; set; }
}
public sealed class GiaoNhanReconcileForm:GiaoNhanReconcileInput
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string? RecordedReceiver { get; set; }
    public DateOnly? RecordedDate { get; set; }
    public List<SelectListItem> People { get; set; } = [];
}

#endregion

#region Tình trạng tài sản và xử lý sự việc

public class GiaoNhanResolutionInput
{
    [Required,StringLength(36)] public string RequestKey { get; set; } = "";
    [Required,StringLength(2000)] public string LyDo { get; set; } = "";
    [Required,StringLength(100)] public string TinhTrang { get; set; } = "Tot";
    [Required,StringLength(100)] public string ExpectedCondition { get; set; } = "";
    [Required,StringLength(50)] public string ExpectedState { get; set; } = "";
    public bool DaKiemTraThucTe { get; set; }
}
public sealed class GiaoNhanResolutionForm : GiaoNhanResolutionInput
{
    public int AssetId { get; set; }
    public int LineId { get; set; }
    public int ReceiptId { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public bool Lost { get; set; }
    public bool Inspection { get; set; }
}

#endregion
