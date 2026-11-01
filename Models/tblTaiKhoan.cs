using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tbltaikhoan
{
    public int TkId { get; set; }
    public string TkTenDangNhap { get; set; } = null!;

    public string TkMatKhau { get; set; } = null!;

    public string TkHoTen { get; set; } = null!;

    public string? TkEmail { get; set; }

    public string? TkSoDienThoai { get; set; }

    public string TkVaiTro { get; set; } = null!;

    public bool? TkTrangThai { get; set; }

    public DateTime TkNgayTao { get; set; }

    public virtual ICollection<Tblbaotritaisan> Tblbaotritaisans { get; set; } = new List<Tblbaotritaisan>();

    public virtual ICollection<Tblcapphattaisan> Tblcapphattaisans { get; set; } = new List<Tblcapphattaisan>();

    public virtual ICollection<Tblkiemketaisan> Tblkiemketaisans { get; set; } = new List<Tblkiemketaisan>();

    public virtual ICollection<Tblthanhlytaisan> Tblthanhlytaisans { get; set; } = new List<Tblthanhlytaisan>();
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập!")]
        [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Tên đăng nhập không được có dấu hoặc dấu cách!")]
        public string TK_TenDangNhap { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu!")]
        [StringLength(255, ErrorMessage = "Mật khẩu không được quá 255 ký tự")]
        public string TK_MatKhau { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập họ và tên!")]
        [StringLength(100, ErrorMessage = "Họ và tên không được quá 100 ký tự")]
        public string TK_HoTen { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
        public string? TK_Email { get; set; }
        public string? TK_SoDienThoai { get; set; }
        public string TK_VaiTro { get; set; } = "NhanVien";
        public bool TK_TrangThai { get; set; } = true;
        public DateTime TK_NgayTao { get; set; } = DateTime.Now;
    }
 5929fb32e2ffe5915829efe930bf0cb5849b0a12
}
