using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblbaotritaisan")]
    public class tblBaoTriTaiSan
    {
        [Key]
        [Column("BT_ID")]
        public int BT_ID { get; set; }

        [Required]
        [Column("TS_ID")]
        public int TS_ID { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mã phiếu bảo trì!")]
        [StringLength(30, ErrorMessage = "Mã phiếu bảo trì không quá 30 ký tự")]
        [Column("BT_MaPhieu")]
        public string BT_MaPhieu { get; set; } = null!;

        [Column("BT_NgayBaoTri")]
        public DateOnly? BT_NgayBaoTri { get; set; }

        [Column("BT_NgayHoanThanh")]
        public DateOnly? BT_NgayHoanThanh { get; set; }

        [StringLength(150)]
        [Column("BT_DonViBaoTri")]
        public string? BT_DonViBaoTri { get; set; }

        [Column("BT_ChiPhi")]
        public decimal? BT_ChiPhi { get; set; }

        [Column("BT_NoiDung")]
        public string? BT_NoiDung { get; set; }

        [Column("BT_LyDo")]
        public string? BT_LyDo { get; set; }

        [Column("BT_GhiChu")]
        public string? BT_GhiChu { get; set; }

        [StringLength(50)]
        [Column("BT_TrangThai")]
        public string BT_TrangThai { get; set; } = "ChoXuLy";

        [Required]
        [Column("BT_NguoiLapID")]
        public int BT_NguoiLapID { get; set; }

        [ForeignKey("TS_ID")]
        public virtual tblTaiSan? TaiSan { get; set; }

        [ForeignKey("BT_NguoiLapID")]
        public virtual tblTaiKhoan? NguoiLap { get; set; }
    }
}
