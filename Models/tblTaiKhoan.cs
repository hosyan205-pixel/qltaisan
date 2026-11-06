using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tbltaikhoan")]
    public class tblTaiKhoan
    {
        [Key]
        [Column("TK_ID")]
        public int TK_ID { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập!")]
        [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Tên đăng nhập không được có dấu hoặc dấu cách!")]
        [StringLength(50, ErrorMessage = "Tên đăng nhập không quá 50 ký tự")]
        [Column("TK_TenDangNhap")]
        public string TK_TenDangNhap { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu!")]
        [StringLength(255, ErrorMessage = "Mật khẩu không được quá 255 ký tự")]
        [Column("TK_MatKhau")]
        public string TK_MatKhau { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập họ và tên!")]
        [StringLength(100, ErrorMessage = "Họ và tên không được quá 100 ký tự")]
        [Column("TK_HoTen")]
        public string TK_HoTen { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
        [Column("TK_Email")]
        public string? TK_Email { get; set; }

        [Column("TK_SoDienThoai")]
        public string? TK_SoDienThoai { get; set; }

        [Column("TK_VaiTro")]
        public string TK_VaiTro { get; set; } = "NhanVien";

        [Column("TK_TrangThai")]
        public bool TK_TrangThai { get; set; } = true;

        [Column("TK_NgayTao")]
        public DateTime TK_NgayTao { get; set; } = DateTime.Now;

        public virtual ICollection<tblBaoTriTaiSan> tblBaoTriTaiSans { get; set; } = new List<tblBaoTriTaiSan>();
        public virtual ICollection<tblCapPhatTaiSan> tblCapPhatTaiSans { get; set; } = new List<tblCapPhatTaiSan>();
        public virtual ICollection<tblKiemKeTaiSan> tblKiemKeTaiSans { get; set; } = new List<tblKiemKeTaiSan>();
        public virtual ICollection<tblThanhLyTaiSan> tblThanhLyTaiSans { get; set; } = new List<tblThanhLyTaiSan>();
    }
}
