using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DoAn.Models;

public partial class Tbltaikhoan
{
    public int TkId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập!")]
    [RegularExpression(@"^[a-zA-Z0-9_]+$",
        ErrorMessage = "Tên đăng nhập không được có dấu hoặc dấu cách!")]
    public string TkTenDangNhap { get; set; } = null!;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu!")]
    [StringLength(255, ErrorMessage = "Mật khẩu không được quá 255 ký tự")]
    public string TkMatKhau { get; set; } = null!;

    [Required(ErrorMessage = "Vui lòng nhập họ và tên!")]
    [StringLength(100, ErrorMessage = "Họ và tên không được quá 100 ký tự")]
    public string TkHoTen { get; set; } = null!;

    [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
    public string? TkEmail { get; set; }

    public string? TkSoDienThoai { get; set; }

    public string TkVaiTro { get; set; } = "NhanVien";

    public bool? TkTrangThai { get; set; } = true;

    public DateTime TkNgayTao { get; set; } = DateTime.Now;

    public virtual ICollection<Tblbaotritaisan> Tblbaotritaisans { get; set; }
        = new List<Tblbaotritaisan>();

    public virtual ICollection<Tblcapphattaisan> Tblcapphattaisans { get; set; }
        = new List<Tblcapphattaisan>();

    public virtual ICollection<Tblkiemketaisan> Tblkiemketaisans { get; set; }
        = new List<Tblkiemketaisan>();

    public virtual ICollection<Tblthanhlytaisan> Tblthanhlytaisans { get; set; }
        = new List<Tblthanhlytaisan>();
}