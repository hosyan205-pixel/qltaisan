using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tbldanhmuctaisan")]
    public class tblDanhMucTaiSan
    {
        [Key]
        [Column("DMTS_ID")]
        public int DMTS_ID { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mã danh mục!")]
        [StringLength(30, ErrorMessage = "Mã danh mục không quá 30 ký tự")]
        [Column("DMTS_MaDanhMuc")]
        public string DMTS_MaDanhMuc { get; set; } = null!;

        [Required(ErrorMessage = "Vui lòng nhập tên danh mục!")]
        [StringLength(100, ErrorMessage = "Tên danh mục không quá 100 ký tự")]
        [Column("DMTS_TenDanhMuc")]
        public string DMTS_TenDanhMuc { get; set; } = null!;

        [Column("DMTS_MoTa")]
        public string? DMTS_MoTa { get; set; }

        [Column("DMTS_TrangThai")]
        public bool? DMTS_TrangThai { get; set; } = true;

        [Column("DMTS_NgayTao")]
        public DateTime DMTS_NgayTao { get; set; } = DateTime.Now;

        public virtual ICollection<tblTaiSan> tblTaiSans { get; set; } = new List<tblTaiSan>();
    }
}
