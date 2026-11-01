using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblTaiKhoan")]
    public class tblTaiKhoan
    {
        [Key]
        public int TK_ID { get; set; }

        [Required, StringLength(50)]
        [RegularExpression(@"^[a-zA-Z0-9_]+$", ErrorMessage = "Tên đăng nhập ko được có dấu hoặc có cách!")]
        public string TK_TenDangNhap { get; set; } = string.Empty;

        public string TK_MatKhau { get; set; } = string.Empty;

        [Required, StringLength(100)]
        public string TK_HoTen { get; set; } = string.Empty;

        public string? TK_Email { get; set; }

        public string? TK_SoDienThoai { get; set; }

        public string TK_VaiTro { get; set; } = "NhanVien";

        public bool TK_TrangThai { get; set; } = true;

        public DateTime TK_NgayTao { get; set; } = DateTime.Now;
    }
}