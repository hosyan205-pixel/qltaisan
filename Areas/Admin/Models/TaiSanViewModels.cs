using System.ComponentModel.DataAnnotations;
using DoAn.Models;
using DoAn.Services.Assets;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DoAn.Areas.Admin.Models;

public class AssetFieldsViewModel : IValidatableObject
{
    [Display(Name = "Tên tài sản"), Required(ErrorMessage = "Vui lòng nhập tên tài sản."), StringLength(150, ErrorMessage = "Tên tài sản tối đa 150 ký tự.")]
    public string Ten { get; set; } = "";
    [Display(Name = "Danh mục"), Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục.")]
    public int DanhMucId { get; set; }
    [Display(Name = "Ngày mua"), DataType(DataType.Date)]
    public DateOnly? NgayMua { get; set; }
    [Display(Name = "Nguyên giá (VNĐ)"), Required(ErrorMessage = "Vui lòng nhập nguyên giá.")]
    [Range(typeof(decimal), "0", "9999999999999.99", ErrorMessage = "Nguyên giá phải từ 0 đến 9.999.999.999.999,99.")]
    public decimal? NguyenGia { get; set; }
    [Display(Name = "Hạn bảo hành"), DataType(DataType.Date)]
    public DateOnly? HanBaoHanh { get; set; }
    [Display(Name = "Tình trạng"), Required(ErrorMessage = "Vui lòng chọn tình trạng.")]
    public string TinhTrang { get; set; } = AssetConditions.Good;
    [Display(Name = "Mô tả"), StringLength(10000, ErrorMessage = "Mô tả tối đa 10.000 ký tự.")]
    public string? MoTa { get; set; }
    [Display(Name = "Ghi chú"), StringLength(10000, ErrorMessage = "Ghi chú tối đa 10.000 ký tự.")]
    public string? GhiChu { get; set; }
    [BindNever, ValidateNever]
    public List<SelectListItem> DanhMucs { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (NgayMua > AssetDisplay.Today)
            yield return new ValidationResult("Ngày mua không được sau ngày hiện tại.", new[] { nameof(NgayMua) });
        if (NgayMua < new DateOnly(1900, 1, 1))
            yield return new ValidationResult("Ngày mua phải từ năm 1900 trở đi.", new[] { nameof(NgayMua) });
        if (NgayMua.HasValue && HanBaoHanh.HasValue && HanBaoHanh < NgayMua)
            yield return new ValidationResult("Hạn bảo hành không được trước ngày mua.", new[] { nameof(HanBaoHanh) });
        if (!AssetConditions.Labels.ContainsKey(TinhTrang ?? ""))
            yield return new ValidationResult("Tình trạng không hợp lệ.", new[] { nameof(TinhTrang) });
        if (NguyenGia.HasValue && decimal.Round(NguyenGia.Value, 2) != NguyenGia)
            yield return new ValidationResult("Nguyên giá chỉ được có tối đa 2 chữ số thập phân.", new[] { nameof(NguyenGia) });
    }
}

public class AssetCreateViewModel : AssetFieldsViewModel
{
    [Display(Name = "Mã tài sản"), Required(ErrorMessage = "Vui lòng nhập mã tài sản."), StringLength(30, ErrorMessage = "Mã tài sản tối đa 30 ký tự.")]
    public string Ma { get; set; } = "";
}

public class AssetEditViewModel : AssetFieldsViewModel
{
    // Chỉ phục vụ hiển thị. ID, mã và trạng thái luôn lấy lại từ database.
    [BindNever, ValidateNever] public int AssetId { get; set; }
    [BindNever, ValidateNever] public string Ma { get; set; } = "";
    [BindNever, ValidateNever] public string TrangThai { get; set; } = "";
}

public class TaiSanIndex
{
    public List<tblTaiSan> Items { get; set; } = new();
    public string? Keyword { get; set; }
    public int? DanhMucId { get; set; }
    public string? TinhTrang { get; set; }
    public string? TrangThai { get; set; }
    public string Sort { get; set; } = "created_desc";
    public List<SelectListItem> DanhMucs { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int Total { get; set; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public int FirstItem => Total == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastItem => Math.Min(Page * PageSize, Total);
    public decimal TongNguyenGia { get; set; }
}
