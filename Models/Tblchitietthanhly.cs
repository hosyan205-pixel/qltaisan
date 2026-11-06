using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblchitietthanhly")]
    public class tblChiTietThanhLy
    {
        [Key]
        [Column("CTTL_ID")]
        public int CTTL_ID { get; set; }

        [Required]
        [Column("TL_ID")]
        public int TL_ID { get; set; }

        [Required]
        [Column("TS_ID")]
        public int TS_ID { get; set; }

        [Column("CTTL_GiaTriThanhLy")]
        public decimal? CTTL_GiaTriThanhLy { get; set; }

        [StringLength(100)]
        [Column("CTTL_TinhTrang")]
        public string? CTTL_TinhTrang { get; set; }

        [Column("CTTL_GhiChu")]
        public string? CTTL_GhiChu { get; set; }

        [ForeignKey("TL_ID")]
        public virtual tblThanhLyTaiSan? ThanhLy { get; set; }

        [ForeignKey("TS_ID")]
        public virtual tblTaiSan? TaiSan { get; set; }
    }
}
