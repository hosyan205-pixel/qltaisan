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
        public string KK_TrangThai { get; set; } = "Nhap";

        [Column("KK_GhiChu")]
        public string? KK_GhiChu { get; set; }

        [ForeignKey("KK_NguoiKiemKeID")]
        public virtual tblTaiKhoan? NguoiKiemKe { get; set; }

        [ForeignKey("PB_ID")]
        public virtual tblPhongBan? PhongBan { get; set; }

        [StringLength(200)] public string? KK_TenDot { get; set; }
        [StringLength(20)] public string KK_LoaiPhamVi { get; set; } = "PhongBan";
        public DateOnly? KK_NgayKetThucDuKien { get; set; }
        public int? KK_NguoiTaoID { get; set; }
        public int? KK_NguoiDuyetID { get; set; }
        public DateTime? KK_NgayTaoUTC { get; set; }
        public DateTime? KK_ThoiDiemChotUTC { get; set; }
        public DateTime? KK_NgayGuiDuyetUTC { get; set; }
        public DateTime? KK_NgayDuyetUTC { get; set; }
        public DateTime? KK_NgayHuyUTC { get; set; }
        public string? KK_LyDoTraLai { get; set; }
        public string? KK_LyDoHuy { get; set; }
        public string? KK_KetLuan { get; set; }
        [StringLength(36)] public string? KK_RequestKey { get; set; }
        public long KK_PhienBan { get; set; }
        public bool KK_LaDuLieuCu { get; set; }
        public int? KK_DotGocID { get; set; }
        public virtual tblTaiKhoan? NguoiTao { get; set; }
        public virtual tblTaiKhoan? NguoiDuyet { get; set; }
        public virtual tblKiemKeTaiSan? DotGoc { get; set; }
        public virtual ICollection<tblKiemKeTaiSan> CacDotBoSung { get; set; } = new List<tblKiemKeTaiSan>();
        public virtual ICollection<tblSuKienKiemKe> SuKiens { get; set; } = new List<tblSuKienKiemKe>();
        public virtual ICollection<tblChiTietKiemKe> tblChiTietKiemKes { get; set; } = new List<tblChiTietKiemKe>();
    }
}
