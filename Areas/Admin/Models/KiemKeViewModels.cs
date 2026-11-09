using System.ComponentModel.DataAnnotations;
using DoAn.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DoAn.Areas.Admin.Models;

public class KiemKeRequest
{
    [Required, StringLength(36)] public string RequestKey { get; set; } = "";
    public long PhienBan { get; set; }
    [StringLength(4000)] public string? LyDo { get; set; }
}
public class KiemKePhieuInput : KiemKeRequest
{
    [Required, StringLength(200)] public string TenDot { get; set; } = "";
    [Required, StringLength(20)] public string LoaiPhamVi { get; set; } = "PhongBan";
    public int? PhongBanId { get; set; }
    [Required] public DateOnly? NgayKiemKe { get; set; }
    public DateOnly? NgayKetThucDuKien { get; set; }
    [Range(1, int.MaxValue)] public int NguoiKiemKeId { get; set; }
    [StringLength(4000)] public string? GhiChu { get; set; }
    public int? DotGocId { get; set; }
}
public sealed class KiemKePhanCongInput : KiemKeRequest
{
    [Range(1, int.MaxValue)] public int NguoiKiemKeId { get; set; }
}
public sealed class KiemKePhieuForm : KiemKePhieuInput
{
    [BindNever, ValidateNever] public int? Id { get; set; }
    [BindNever, ValidateNever] public List<SelectListItem> PhongBans { get; set; } = [];
    [BindNever, ValidateNever] public List<SelectListItem> NguoiKiemKes { get; set; } = [];
}
public class KiemKeGhiNhanInput : KiemKeRequest
{
    [Required, StringLength(20)] public string CachGhiNhan { get; set; } = "";
    [StringLength(100)] public string? TinhTrang { get; set; }
    [StringLength(255)] public string? ViTri { get; set; }
    public int? PhongBanId { get; set; }
    public int? NguoiSuDungId { get; set; }
    [StringLength(255)] public string? NguoiSuDung { get; set; }
    [StringLength(100)] public string? MaNhanDien { get; set; }
    [StringLength(100)] public string? Serial { get; set; }
    [StringLength(150)] public string? TenTaiSanThucTe { get; set; }
    public bool KhongApDungViTri { get; set; }
    public bool CoHuHong { get; set; }
    public bool ThieuPhuKien { get; set; }
    public bool KhacNguoiSuDung { get; set; }
    public bool XacNhanCanCu { get; set; }
    public bool DaKiemTraThucTe { get; set; }
    [StringLength(6000)] public string? BangChung { get; set; }
    [StringLength(4000)] public string? GhiChu { get; set; }
}
public sealed class KiemKeGhiNhanForm : KiemKeGhiNhanInput
{
    [BindNever, ValidateNever] public KiemKeDongRow Dong { get; set; } = new();
    [BindNever, ValidateNever] public List<SelectListItem> PhongBans { get; set; } = [];
    [BindNever, ValidateNever] public List<SelectListItem> NguoiDungs { get; set; } = [];
}
public class KiemKePhatHienInput : KiemKeGhiNhanInput
{
    public int? TaiSanId { get; set; }
    [Required, StringLength(150)] public string TenThucTe { get; set; } = "";
    [StringLength(50)] public string? MaPhatHien { get; set; }
}
public sealed class KiemKePhatHienForm : KiemKePhatHienInput
{
    [BindNever, ValidateNever] public int DotId { get; set; }
    [BindNever, ValidateNever] public List<SelectListItem> PhongBans { get; set; } = [];
    [BindNever, ValidateNever] public List<SelectListItem> NguoiDungs { get; set; } = [];
    [BindNever, ValidateNever] public List<KiemKeSach> TaiSans { get; set; } = [];
    [BindNever, ValidateNever] public string? Keyword { get; set; }
}
public class KiemKeDoiSoatInput : KiemKeRequest
{
    [Required, StringLength(4000)] public string NguyenNhan { get; set; } = "";
    [Required, StringLength(40)] public string HuongXuLy { get; set; } = "";
    [Required, StringLength(6000)] public string DeXuat { get; set; } = "";
    [Required, StringLength(20)] public string TrangThai { get; set; } = "ChuaDoiSoat";
    public int? NguoiDoiSoatId { get; set; }
}
public sealed class KiemKeDoiSoatForm : KiemKeDoiSoatInput
{
    [BindNever, ValidateNever] public KiemKeDongRow Dong { get; set; } = new();
    [BindNever, ValidateNever] public List<SelectListItem> NguoiDoiSoats { get; set; } = [];
}
public class KiemKeXuLyInput : KiemKeRequest
{
    [Required, StringLength(20)] public string TrangThai { get; set; } = "DangXuLy";
    [Required, StringLength(4000)] public string KetQua { get; set; } = "";
    [StringLength(100)] public string? ThamChieu { get; set; }
}
public sealed class KiemKeXuLyForm : KiemKeXuLyInput
{
    [BindNever, ValidateNever] public KiemKeDongRow Dong { get; set; } = new();
}
public class KiemKePage
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int Total { get; set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
public class KiemKeIndex : KiemKePage
{
    public string? Keyword { get; set; }
    public string? TrangThai { get; set; }
    public int? PhongBanId { get; set; }
    public DateOnly? TuNgay { get; set; }
    public DateOnly? DenNgay { get; set; }
    public bool ChiLichSu { get; set; }
    public List<KiemKePhieuRow> Items { get; set; } = [];
    public List<SelectListItem> PhongBans { get; set; } = [];
    public KiemKeTongHop TongHop { get; set; } = new();
    public Dictionary<string,string> Routes(int page) => new()
    {
        ["page"]=page.ToString(),["keyword"]=Keyword??"",["trangThai"]=TrangThai??"",["phongBanId"]=PhongBanId?.ToString()??"",
        ["tuNgay"]=TuNgay?.ToString("yyyy-MM-dd")??"",["denNgay"]=DenNgay?.ToString("yyyy-MM-dd")??"",["chiLichSu"]=ChiLichSu.ToString()
    };
}
public sealed class KiemKePhieuRow
{
    public int Id { get; set; }
    public string Ma { get; set; } = "";
    public string? Ten { get; set; }
    public string PhamVi { get; set; } = "";
    public string? PhongBan { get; set; }
    public string? NguoiKiemKe { get; set; }
    public DateOnly? Ngay { get; set; }
    public string TrangThai { get; set; } = "";
    public bool LaDuLieuCu { get; set; }
    public int DuKien { get; set; }
    public int DaKiem { get; set; }
    public int ChenhLech { get; set; }
    public string TienDo => LaDuLieuCu ? "Lịch sử, không có dữ liệu chốt" : DuKien == 0 ? "Không có tài sản" : $"{DaKiem}/{DuKien} ({100m * DaKiem / DuKien:0.##}%)";
}
public sealed class KiemKeBaoCao : KiemKeIndex
{
    public int SoDotLichSu { get; set; }
    public int SoDotMoi => Total - SoDotLichSu;
    public int SoDotDaChot { get; set; }
    public List<KiemKeBaoCaoTrangThai> TheoTrangThai { get; set; } = [];
    public List<KiemKeBaoCaoPhamVi> TheoPhamVi { get; set; } = [];
    public Dictionary<string, int> KetQuaLichSu { get; set; } = [];
}
public sealed class KiemKeBaoCaoTrangThai
{
    public string TrangThai { get; set; } = "";
    public int SoDotMoi { get; set; }
    public int SoDotLichSu { get; set; }
    public int TongSo => SoDotMoi + SoDotLichSu;
}
public sealed class KiemKeBaoCaoPhamVi
{
    public string LoaiPhamVi { get; set; } = "";
    public int? PhongBanId { get; set; }
    public string? PhongBan { get; set; }
    public int SoDot { get; set; }
    public int SoDotLichSu { get; set; }
    public int SoDotDaChot { get; set; }
    public int DuKien { get; set; }
    public int DaKiem { get; set; }
    public int ChenhLech { get; set; }
    public int ChuaDoiSoat { get; set; }
    public int DaXuLy { get; set; }
    public string TenPhamVi => LoaiPhamVi == "ToanBo" ? "Toàn bộ tài sản" : PhongBan ?? "Chưa xác định phòng ban";
    public string TienDo => SoDotDaChot == 0 ? "Chưa có dữ liệu chốt" : DuKien == 0 ? "Không có tài sản" : $"{100m * DaKiem / DuKien:0.##}%";
}
public sealed class KiemKeChiTiet : KiemKePage
{
    public tblKiemKeTaiSan Phieu { get; set; } = new();
    public List<KiemKeDongRow> Items { get; set; } = [];
    public KiemKeTongHop TongHop { get; set; } = new();
    public string? Keyword { get; set; }
    public string? KetQua { get; set; }
    public List<tblSuKienKiemKe> Events { get; set; } = [];
    public bool CanRecord { get; set; }
    public bool CanManage { get; set; }
    public List<string> CanhBaos { get; set; } = [];
    public List<SelectListItem> NguoiKiemKes { get; set; } = [];
}
public sealed class KiemKeDongRow
{
    public tblChiTietKiemKe Dong { get; set; } = new();
    public KiemKeSach? HienTai { get; set; }
    public bool BienDong => Dong.CTKK_DauVetNguon != null && HienTai != null && Dong.CTKK_DauVetNguon != HienTai.DauVet;
    public bool ChenhLech => KiemKeUi.IsDifference(Dong.CTKK_KetQua);
}
public sealed class KiemKeLichSu : KiemKePage
{
    public tblKiemKeTaiSan Phieu { get; set; } = new();
    public List<tblSuKienKiemKe> Items { get; set; } = [];
}
public sealed class KiemKeTongHop
{
    public int TongDong { get; set; }
    public int DuKien { get; set; }
    public int DaTimThay { get; set; }
    public int KhongTimThay { get; set; }
    public int ChuaKiem { get; set; }
    public int Khop { get; set; }
    public int Thua { get; set; }
    public int NgoaiPhamVi { get; set; }
    public int CanXacMinh { get; set; }
    public int SaiPhongBan { get; set; }
    public int SaiViTri { get; set; }
    public int SaiNguoiDung { get; set; }
    public int Hong { get; set; }
    public int ThieuPhuKien { get; set; }
    public int NhieuSaiLech { get; set; }
    public int DongChenhLech { get; set; }
    public int ChuaDoiSoat { get; set; }
    public int DaXuLy { get; set; }
    public int TongCo => SaiPhongBan + SaiViTri + SaiNguoiDung + Hong + ThieuPhuKien;
    public string TienDo => DuKien == 0 ? "Không có tài sản" : $"{100m * (DuKien - ChuaKiem) / DuKien:0.##}%";
}
public sealed class KiemKeSach
{
    public int Id { get; set; }
    public string Ma { get; set; } = "";
    public string Ten { get; set; } = "";
    public string? Serial { get; set; }
    public string? DanhMuc { get; set; }
    public decimal NguyenGia { get; set; }
    public string? ViTri { get; set; }
    public string TinhTrang { get; set; } = "";
    public string TrangThai { get; set; } = "";
    public bool DangQuanLy { get; set; }
    public bool DaThanhLyThucTe { get; set; }
    public int? PhongBanId { get; set; }
    public string? PhongBan { get; set; }
    public int? NguoiNhanId { get; set; }
    public string? NguoiNhan { get; set; }
    public bool XungDot { get; set; }
    public string? CanhBao { get; set; }
    public bool DangBaoTri { get; set; }
    public List<int> PhongBanLienQuan { get; set; } = [];
    public string DauVet { get; set; } = "";
}
public static class KiemKeUi
{
    public static readonly string[] States = ["Nhap", "DangKiemKe", "ChoDuyet", "HoanThanh", "DaHuy"];
    public static readonly string[] Results = ["ChuaKiem", "Khop", "Thieu", "Thua", "SaiPhongBan", "SaiViTri", "SaiNguoiSuDung", "Hong", "ThieuPhuKien", "NhieuSaiLech", "NgoaiPhamVi", "CanXacMinh"];
    public static readonly string[] Directions = ["KiemTraLai", "DoiSoatBanGiao", "DeNghiCapNhatViTri", "DeNghiBaoTri", "DeNghiThuHoi", "DeNghiThanhLy", "XacMinhTaiSanThua", "LapBienBanMat", "Khac"];
    public static readonly string[] Conditions = ["Tot", "XuongCap", "CanBaoTri", "HuHong", "ThieuPhuKien", "CanKiemTra"];
    public static bool IsDifference(string? value) => value != null && value is not ("Khop" or "ChuaKiem" or "BinhThuong");
    public static string Label(string? value) => value switch
    {
        "Nhap"=>"Nháp", "DangKiemKe"=>"Đang kiểm kê", "ChoDuyet"=>"Chờ duyệt", "HoanThanh"=>"Hoàn thành", "DaHuy"=>"Đã hủy",
        "Tot"=>"Tốt", "XuongCap"=>"Xuống cấp", "CanBaoTri"=>"Cần bảo trì", "HuHong"=>"Hư hỏng", "CanKiemTra"=>"Cần kiểm tra",
        "ToanBo"=>"Toàn bộ tài sản", "PhongBan"=>"Theo phòng ban", "TrongSo"=>"Trong sổ", "NgoaiSo"=>"Ngoài sổ", "NgoaiPhamVi"=>"Ngoài phạm vi",
        "ChuaKiem"=>"Chưa kiểm", "Khop"=>"Khớp", "Thieu"=>"Không tìm thấy", "Thua"=>"Thừa ngoài sổ", "SaiPhongBan"=>"Sai phòng ban", "SaiViTri"=>"Sai vị trí",
        "SaiNguoiSuDung"=>"Sai người sử dụng", "Hong"=>"Hư hỏng", "ThieuPhuKien"=>"Thiếu phụ kiện", "NhieuSaiLech"=>"Nhiều sai lệch", "CanXacMinh"=>"Cần xác minh",
        "BinhThuong"=>"Bình thường (dữ liệu cũ)", "CanXuLy"=>"Cần xử lý (dữ liệu cũ)", "ChuaDoiSoat"=>"Chưa đối soát", "CanKiemKeLai"=>"Cần kiểm kê lại", "DaDoiSoat"=>"Đã đối soát",
        "ChuaXuLy"=>"Chưa xử lý", "DangXuLy"=>"Đang xử lý", "DaXuLy"=>"Đã xử lý", "KhongCanXuLy"=>"Không cần xử lý",
        "KiemTraLai"=>"Kiểm tra lại", "DoiSoatBanGiao"=>"Đối soát bàn giao", "DeNghiCapNhatViTri"=>"Đề nghị cập nhật vị trí", "DeNghiBaoTri"=>"Đề nghị bảo trì",
        "DeNghiThuHoi"=>"Đề nghị thu hồi", "DeNghiThanhLy"=>"Đề nghị thanh lý", "XacMinhTaiSanThua"=>"Xác minh tài sản thừa", "LapBienBanMat"=>"Lập biên bản mất", "Khac"=>"Khác",
        "Tao"=>"Lập nháp", "Sua"=>"Sửa nháp", "BatDau"=>"Bắt đầu và chốt sổ", "GhiNhan"=>"Ghi nhận thực tế", "PhatHien"=>"Phát hiện ngoài danh sách", "DoiSoat"=>"Đối soát",
        "KiemKeLai"=>"Yêu cầu kiểm kê lại", "GuiDuyet"=>"Gửi duyệt", "Duyet"=>"Phê duyệt", "TraLai"=>"Trả lại", "Huy"=>"Hủy đợt", "CapNhatXuLy"=>"Cập nhật xử lý hậu kiểm", "DoiNguoiKiemKe"=>"Thay đổi người phụ trách",
        null or ""=>"Chưa có", _=>value
    };
}
