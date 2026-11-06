using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblkiemketaisan")]
    public class tblKiemKeTaiSan
    {
        [Key]
        [Column("KK_ID")]
        public int KK_ID { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mã phiếu kiểm kê!")]
        [StringLength(30, ErrorMessage = "Mã phiếu kiểm kê không quá 30 ký tự")]
        [Column("KK_MaPhieu")]
        public string KK_MaPhieu { get; set; } = null!;

        [Column("KK_NgayKiemKe")]
        public DateOnly? KK_NgayKiemKe { get; set; }

        [Column("PB_ID")]
        public int? PB_ID { get; set; }

        [Required]
        [Column("KK_NguoiKiemKeID")]
        public int KK_NguoiKiemKeID { get; set; }

        [StringLength(50)]
        [Column("KK_TrangThai")]
        public string KK_TrangThai { get; set; } = "DangKiemKe";

        [Column("KK_GhiChu")]
        public string? KK_GhiChu { get; set; }

        [ForeignKey("KK_NguoiKiemKeID")]
        public virtual tblTaiKhoan? NguoiKiemKe { get; set; }

        [ForeignKey("PB_ID")]
        public virtual tblPhongBan? PhongBan { get; set; }

        public virtual ICollection<tblChiTietKiemKe> tblChiTietKiemKes { get; set; } = new List<tblChiTietKiemKe>();
    }
}
