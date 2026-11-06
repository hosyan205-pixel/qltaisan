using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblthanhlytaisan")]
    public class tblThanhLyTaiSan
    {
        [Key]
        [Column("TL_ID")]
        public int TL_ID { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mã phiếu thanh lý!")]
        [StringLength(30, ErrorMessage = "Mã phiếu thanh lý không quá 30 ký tự")]
        [Column("TL_MaPhieu")]
        public string TL_MaPhieu { get; set; } = null!;

        [Column("TL_NgayThanhLy")]
        public DateOnly? TL_NgayThanhLy { get; set; }

        [StringLength(100)]
        [Column("TL_PhuongThuc")]
        public string? TL_PhuongThuc { get; set; }

        [Column("TL_TongGiaTri")]
        public decimal? TL_TongGiaTri { get; set; }

        [Column("TL_LyDo")]
        public string? TL_LyDo { get; set; }

        [Required]
        [Column("TL_NguoiLapID")]
        public int TL_NguoiLapID { get; set; }

        [StringLength(50)]
        [Column("TL_TrangThai")]
        public string TL_TrangThai { get; set; } = "ChoDuyet";

        [Column("TL_GhiChu")]
        public string? TL_GhiChu { get; set; }

        [ForeignKey("TL_NguoiLapID")]
        public virtual tblTaiKhoan? NguoiLap { get; set; }

        public virtual ICollection<tblChiTietThanhLy> tblChiTietThanhLies { get; set; } = new List<tblChiTietThanhLy>();
    }
}
