using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblchitietkiemke")]
    public class tblChiTietKiemKe
    {
        [Key]
        [Column("CTKK_ID")]
        public int CTKK_ID { get; set; }

        [Required]
        [Column("KK_ID")]
        public int KK_ID { get; set; }

        [Column("TS_ID")]
        public int? TS_ID { get; set; }

        [StringLength(100)]
        [Column("CTKK_TinhTrangThucTe")]
        public string? CTKK_TinhTrangThucTe { get; set; }

        [StringLength(255)]
        [Column("CTKK_ViTriThucTe")]
        public string? CTKK_ViTriThucTe { get; set; }

        [StringLength(50)]
        [Column("CTKK_KetQua")]
        public string? CTKK_KetQua { get; set; } = "ChuaKiem";

        [Column("CTKK_GhiChu")]
        public string? CTKK_GhiChu { get; set; }

        [StringLength(20)] public string CTKK_LoaiDong { get; set; } = "TrongSo";
        [StringLength(50)] public string? CTKK_MaPhatHien { get; set; }
        [StringLength(30)] public string? CTKK_MaTaiSanSnapshot { get; set; }
        [StringLength(150)] public string? CTKK_TenTaiSanSnapshot { get; set; }
        [StringLength(255)] public string? CTKK_DanhMucSnapshot { get; set; }
        public decimal? CTKK_NguyenGiaDuKien { get; set; }
        [StringLength(100)] public string? CTKK_SerialDuKien { get; set; }
        public int? CTKK_PBDuKienID { get; set; }
        [StringLength(150)] public string? CTKK_PhongBanDuKienSnapshot { get; set; }
        public int? CTKK_NguoiNhanDuKienID { get; set; }
        [StringLength(255)] public string? CTKK_NguoiNhanDuKienSnapshot { get; set; }
        [StringLength(255)] public string? CTKK_ViTriDuKien { get; set; }
        [StringLength(50)] public string? CTKK_TinhTrangDuKien { get; set; }
        [StringLength(50)] public string? CTKK_TrangThaiDuKien { get; set; }
        public bool? CTKK_DaTimThay { get; set; }
        public int? CTKK_PBThucTeID { get; set; }
        public int? CTKK_NguoiSuDungThucTeID { get; set; }
        [StringLength(255)] public string? CTKK_NguoiSuDungThucTe { get; set; }
        [StringLength(100)] public string? CTKK_MaNhanDienThucTe { get; set; }
        [StringLength(150)] public string? CTKK_TenThucTe { get; set; }
        [StringLength(100)] public string? CTKK_SerialThucTe { get; set; }
        public bool CTKK_CoSaiPhongBan { get; set; }
        public bool CTKK_CoSaiViTri { get; set; }
        public bool CTKK_CoSaiNguoiSuDung { get; set; }
        public bool CTKK_CoHuHong { get; set; }
        public bool CTKK_ThieuPhuKien { get; set; }
        public string? CTKK_LyDoChenhLech { get; set; }
        [StringLength(40)] public string? CTKK_HuongXuLy { get; set; }
        public string? CTKK_DeXuatXuLy { get; set; }
        [StringLength(20)] public string CTKK_TrangThaiDoiSoat { get; set; } = "ChuaDoiSoat";
        public int? CTKK_NguoiDoiSoatID { get; set; }
        public DateTime? CTKK_ThoiDiemDoiSoatUTC { get; set; }
        public int? CTKK_NguoiGhiNhanID { get; set; }
        public DateTime? CTKK_ThoiDiemGhiNhanUTC { get; set; }
        [StringLength(20)] public string CTKK_TrangThaiXuLy { get; set; } = "ChuaXuLy";
        public string? CTKK_KetQuaXuLy { get; set; }
        [StringLength(100)] public string? CTKK_ThamChieuXuLy { get; set; }
        public int? CTKK_NguoiXuLyID { get; set; }
        public DateTime? CTKK_ThoiDiemXuLyUTC { get; set; }
        public long CTKK_PhienBan { get; set; }
        public string? CTKK_CanhBaoNguon { get; set; }
        [StringLength(64)] public string? CTKK_DauVetNguon { get; set; }
        public string? CTKK_BangChung { get; set; }
        public bool CTKK_CanXacMinh { get; set; }
        public bool CTKK_XacNhanCanCu { get; set; }

        [ForeignKey("KK_ID")]
        public virtual tblKiemKeTaiSan? KiemKe { get; set; }

        [ForeignKey("TS_ID")]
        public virtual tblTaiSan? TaiSan { get; set; }
        public virtual tblPhongBan? PhongBanDuKien { get; set; }
        public virtual tblPhongBan? PhongBanThucTe { get; set; }
        public virtual tblTaiKhoan? NguoiNhanDuKien { get; set; }
        public virtual tblTaiKhoan? NguoiSuDungThucTe { get; set; }
        public virtual tblTaiKhoan? NguoiDoiSoat { get; set; }
        public virtual tblTaiKhoan? NguoiGhiNhan { get; set; }
        public virtual tblTaiKhoan? NguoiXuLy { get; set; }
        public virtual ICollection<tblSuKienKiemKe> SuKiens { get; set; } = new List<tblSuKienKiemKe>();
    }
}
