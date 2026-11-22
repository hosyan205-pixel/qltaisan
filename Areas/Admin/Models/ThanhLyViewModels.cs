using System.ComponentModel.DataAnnotations;

namespace DoAn.Areas.Admin.Models;

public static class ThanhLyStates
{
    public const string ChoDuyet = "ChoDuyet";
    public const string DaDuyet = "DaDuyet";
    public const string Huy = "Huy";

    public static string Label(string? state) => state switch
    {
        ChoDuyet => "Chờ duyệt",
        DaDuyet => "Đã duyệt",
        Huy => "Đã hủy",
        _ => state ?? "Không xác định"
    };

    public static string BadgeClass(string? state) => state switch
    {
        ChoDuyet => "bg-warning text-dark",
        DaDuyet => "bg-success",
        Huy => "bg-secondary",
        _ => "bg-light text-dark"
    };
}

public class ThanhLyIndexViewModel
{
    public string? Keyword { get; set; }
    public string? TrangThai { get; set; }
    public DateOnly? TuNgay { get; set; }
    public DateOnly? DenNgay { get; set; }
    public string Sort { get; set; } = "date_desc";

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int Total { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling((double)Total / PageSize));

    public int TongPhieu { get; set; }
    public int SoChoDuyet { get; set; }
    public int SoDaDuyet { get; set; }
    public decimal TongGiaTriDaDuyet { get; set; }

    public List<ThanhLyRowViewModel> Items { get; set; } = [];
}

public class ThanhLyRowViewModel
{
    public int Id { get; set; }
    public string MaPhieu { get; set; } = "";
    public DateOnly? NgayThanhLy { get; set; }
    public string? NguoiLap { get; set; }
    public string? PhuongThuc { get; set; }
    public string? LyDo { get; set; }
    public decimal TongGiaTri { get; set; }
    public string TrangThai { get; set; } = ThanhLyStates.ChoDuyet;
    public string? GhiChu { get; set; }
    public int SoTaiSan { get; set; }
}

public class ThanhLyLineViewModel
{
    public int Id { get; set; }
    public int TaiSanId { get; set; }
    public string MaTaiSan { get; set; } = "";
    public string TenTaiSan { get; set; } = "";
    public string? DanhMuc { get; set; }
    public string? TinhTrangHienTai { get; set; }
    public string? TrangThaiHienTai { get; set; }
    public decimal GiaTriThanhLy { get; set; }
    public string? TinhTrangThanhLy { get; set; }
    public string? GhiChu { get; set; }
}

public class ThanhLyDetailsViewModel
{
    public ThanhLyRowViewModel Phieu { get; set; } = new();
    public List<ThanhLyLineViewModel> Items { get; set; } = [];
}

public class ThanhLyAssetOption
{
    public int Id { get; set; }
    public string MaTaiSan { get; set; } = "";
    public string TenTaiSan { get; set; } = "";
    public string? DanhMuc { get; set; }
    public string? TinhTrang { get; set; }
    public string? TrangThai { get; set; }
}

public class ThanhLyLineInput
{
    public int TaiSanId { get; set; }

    [Range(0, 9999999999999, ErrorMessage = "Giá trị thanh lý không hợp lệ.")]
    public decimal GiaTriThanhLy { get; set; }

    [StringLength(100, ErrorMessage = "Tình trạng tối đa 100 ký tự.")]
    public string? TinhTrang { get; set; }

    [StringLength(2000, ErrorMessage = "Ghi chú dòng tối đa 2000 ký tự.")]
    public string? GhiChu { get; set; }
}

public class ThanhLyFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã phiếu thanh lý.")]
    [StringLength(30, ErrorMessage = "Mã phiếu không quá 30 ký tự.")]
    public string MaPhieu { get; set; } = "";

    [Required(ErrorMessage = "Vui lòng chọn ngày thanh lý.")]
    public DateOnly? NgayThanhLy { get; set; }

    [StringLength(100, ErrorMessage = "Phương thức tối đa 100 ký tự.")]
    public string? PhuongThuc { get; set; } = "Bán thanh lý";

    [Required(ErrorMessage = "Vui lòng nhập lý do thanh lý.")]
    [StringLength(2000, ErrorMessage = "Lý do tối đa 2000 ký tự.")]
    public string LyDo { get; set; } = "";

    [StringLength(4000, ErrorMessage = "Ghi chú tối đa 4000 ký tự.")]
    public string? GhiChu { get; set; }

    public List<ThanhLyLineInput> Lines { get; set; } = [];
    public List<ThanhLyAssetOption> AvailableAssets { get; set; } = [];
}