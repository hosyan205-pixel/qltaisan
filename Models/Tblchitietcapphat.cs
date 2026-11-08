using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblchitietcapphat")]
    public class tblChiTietCapPhat
    {
        [Key]
        [Column("CTCP_ID")]
        public int CTCP_ID { get; set; }

        [Required]
        [Column("CP_ID")]
        public int CP_ID { get; set; }

        [Required]
        [Column("TS_ID")]
        public int TS_ID { get; set; }

        [StringLength(100)]
        [Column("CTCP_TinhTrangKhiGiao")]
        public string? CTCP_TinhTrangKhiGiao { get; set; }

        [StringLength(100)]
        [Column("CTCP_TinhTrangKhiThuHoi")]
        public string? CTCP_TinhTrangKhiThuHoi { get; set; }

        [Column("CTCP_NgayThuHoi")]
        public DateOnly? CTCP_NgayThuHoi { get; set; }

        [Column("CTCP_GhiChu")]
        public string? CTCP_GhiChu { get; set; }

        [ForeignKey("CP_ID")]
        public virtual tblCapPhatTaiSan? CapPhat { get; set; }

        [ForeignKey("TS_ID")]
        public virtual tblTaiSan? TaiSan { get; set; }
        [StringLength(30)] public string? CTCP_TrangThai { get; set; }
        public int? CTCP_DongNguonID { get; set; }
        public DateTime? CTCP_BatDauUTC { get; set; }
        public DateTime? CTCP_KetThucUTC { get; set; }
        [StringLength(30)] public string? CTCP_MaTaiSanSnapshot { get; set; }
        [StringLength(150)] public string? CTCP_TenTaiSanSnapshot { get; set; }
        [StringLength(255)] public string? CTCP_DanhMucSnapshot { get; set; }
        [StringLength(150)] public string? CTCP_ViTriSnapshot { get; set; }
        public string? CTCP_PhuKienSnapshot { get; set; }
        public int? CTCP_NguoiKiemTraID { get; set; }
        public DateTime? CTCP_KiemTraUTC { get; set; }
        public int? CTCP_NguoiDoiSoatID { get; set; }
        public DateTime? CTCP_DoiSoatUTC { get; set; }
        public int? CTCP_TaiSanHieuLucID { get; private set; }
        public virtual tblChiTietCapPhat? DongNguon { get; set; }
        public virtual ICollection<tblChiTietCapPhat> CacDongKeTiep { get; set; } = new List<tblChiTietCapPhat>();
        public virtual tblTaiKhoan? NguoiKiemTra { get; set; }
        public virtual tblTaiKhoan? NguoiDoiSoat { get; set; }
        public virtual ICollection<tblSuKienCapPhat> SuKiens { get; set; } = new List<tblSuKienCapPhat>();
    }
}
