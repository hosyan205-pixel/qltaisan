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
    }
}
