using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblcapphattaisan")]
    public class tblCapPhatTaiSan
    {
        [Key]
        [Column("CP_ID")]
        public int CP_ID { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mã phiếu cấp phát!")]
        [StringLength(30, ErrorMessage = "Mã phiếu cấp phát không quá 30 ký tự")]
        [Column("CP_MaPhieu")]
        public string CP_MaPhieu { get; set; } = null!;

        [Column("CP_NgayCapPhat")]
        public DateOnly? CP_NgayCapPhat { get; set; }

        [Required]
        [Column("PB_ID")]
        public int PB_ID { get; set; }

        [StringLength(255)]
        [Column("CP_NguoiNhan")]
        public string? CP_NguoiNhan { get; set; } = "";

        [Column("CP_LyDo")]
        public string? CP_LyDo { get; set; }

        [Column("CP_GhiChu")]
        public string? CP_GhiChu { get; set; }

        [StringLength(50)]
        [Column("CP_TrangThai")]
        public string CP_TrangThai { get; set; } = "DaCapPhat";

        [Required]
        [Column("CP_NguoiGiaoID")]
        public int CP_NguoiGiaoID { get; set; }

        [ForeignKey("PB_ID")]
        public virtual tblPhongBan? PhongBan { get; set; }

        [ForeignKey("CP_NguoiGiaoID")]
        public virtual tblTaiKhoan? NguoiGiao { get; set; }

        [StringLength(20)] public string? CP_LoaiPhieu { get; set; }
        [StringLength(20)] public string? CP_LoaiDoiTuong { get; set; }
        public int? CP_NguoiNhanID { get; set; }
        public int? CP_NguoiLapID { get; set; }
        public int? CP_NguoiXacNhanID { get; set; }
        public DateTime? CP_NgayLapUTC { get; set; }
        public DateOnly? CP_NgayDuKien { get; set; }
        public DateOnly? CP_NgayTraDuKien { get; set; }
        public DateTime? CP_ThucHienUTC { get; set; }
        public DateTime? CP_XacNhanUTC { get; set; }
        [StringLength(150)] public string? CP_TenPhongBanSnapshot { get; set; }
        [StringLength(255)] public string? CP_NguoiGiaoSnapshot { get; set; }
        [StringLength(36)] public string? CP_RequestKey { get; set; }
        public long CP_PhienBan { get; set; }
        public virtual tblTaiKhoan? NguoiNhan { get; set; }
        public virtual tblTaiKhoan? NguoiLap { get; set; }
        public virtual tblTaiKhoan? NguoiXacNhan { get; set; }
        public virtual ICollection<tblSuKienCapPhat> SuKiens { get; set; } = new List<tblSuKienCapPhat>();
        public virtual ICollection<tblChiTietCapPhat> tblChiTietCapPhats { get; set; } = new List<tblChiTietCapPhat>();
    }
}
