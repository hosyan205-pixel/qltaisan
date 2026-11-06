using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tbltaisan")]
    public class tblTaiSan
    {
        [Key]
        [Column("TS_ID")]
        public int TS_ID { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mã tài sản!")]
        [StringLength(30, ErrorMessage = "Mã tài sản không quá 30 ký tự")]
        [Column("TS_MaTaiSan")]
        public string TS_MaTaiSan { get; set; } = null!;

        [Required(ErrorMessage = "Vui lòng nhập tên tài sản!")]
        [StringLength(150, ErrorMessage = "Tên tài sản không quá 150 ký tự")]
        [Column("TS_TenTaiSan")]
        public string TS_TenTaiSan { get; set; } = null!;

        [Required]
        [Column("DMTS_ID")]
        public int DMTS_ID { get; set; }

        [Column("TS_MoTa")]
        public string? TS_MoTa { get; set; }

        [Column("TS_NgayMua")]
        public DateOnly? TS_NgayMua { get; set; }

        [Column("TS_NguyenGia")]
        public decimal TS_NguyenGia { get; set; }

        [Column("TS_HanBaoHanh")]
        public DateOnly? TS_HanBaoHanh { get; set; }

        [Column("TS_TinhTrang")]
        public string TS_TinhTrang { get; set; } = "Tot";

        [Column("TS_TrangThai")]
        public string TS_TrangThai { get; set; } = "ChuaCapPhat";

        [Column("TS_GhiChu")]
        public string? TS_GhiChu { get; set; }

        [Column("TS_NgayTao")]
        public DateTime TS_NgayTao { get; set; } = DateTime.Now;

        [ForeignKey(nameof(DMTS_ID))]
        public virtual tblDanhMucTaiSan? tblDanhMucTaiSan { get; set; }

        public virtual ICollection<tblBaoTriTaiSan> tblBaoTriTaiSans { get; set; } = new List<tblBaoTriTaiSan>();

        public virtual ICollection<tblChiTietCapPhat> tblChiTietCapPhats { get; set; } = new List<tblChiTietCapPhat>();

        public virtual ICollection<tblChiTietKiemKe> tblChiTietKiemKes { get; set; } = new List<tblChiTietKiemKe>();

        public virtual ICollection<tblChiTietThanhLy> tblChiTietThanhLies { get; set; } = new List<tblChiTietThanhLy>();
    }
}
