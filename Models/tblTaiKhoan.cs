using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblTaiKhoan")]
    public class tblTaiKhoan
    {
        [Key]
        public int TK_ID { get; set; }

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
}
