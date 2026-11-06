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

        [Required]
        [Column("TS_ID")]
        public int TS_ID { get; set; }

        [StringLength(100)]
        [Column("CTKK_TinhTrangThucTe")]
        public string? CTKK_TinhTrangThucTe { get; set; }

        [StringLength(255)]
        [Column("CTKK_ViTriThucTe")]
        public string? CTKK_ViTriThucTe { get; set; }

        [StringLength(50)]
        [Column("CTKK_KetQua")]
        public string? CTKK_KetQua { get; set; } = "BinhThuong";

        [Column("CTKK_GhiChu")]
        public string? CTKK_GhiChu { get; set; }

        [ForeignKey("KK_ID")]
        public virtual tblKiemKeTaiSan? KiemKe { get; set; }

        [ForeignKey("TS_ID")]
        public virtual tblTaiSan? TaiSan { get; set; }
    }
}
