using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblphongban")]
    public class tblPhongBan
    {
        [Key]
        [Column("PB_ID")]
        public int PB_ID { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mã phòng ban!")]
        [StringLength(30, ErrorMessage = "Mã phòng ban không quá 30 ký tự")]
        [Column("PB_MaPhongBan")]
        public string PB_MaPhongBan { get; set; } = null!;

        [Required(ErrorMessage = "Vui lòng nhập tên phòng ban!")]
        [StringLength(150, ErrorMessage = "Tên phòng ban không quá 150 ký tự")]
        [Column("PB_TenPhongBan")]
        public string PB_TenPhongBan { get; set; } = null!;

        [Column("PB_NguoiPhuTrach")]
        public string? PB_NguoiPhuTrach { get; set; }

        [Column("PB_SoDienThoai")]
        public string? PB_SoDienThoai { get; set; }

        [Column("PB_DiaChi")]
        public string? PB_DiaChi { get; set; }

        [Column("PB_TrangThai")]
        public bool? PB_TrangThai { get; set; } = true;

        [Column("PB_NgayTao")]
        public DateTime PB_NgayTao { get; set; } = DateTime.Now;

        public virtual ICollection<tblCapPhatTaiSan> tblCapPhatTaiSans { get; set; } = new List<tblCapPhatTaiSan>();
        public virtual ICollection<tblKiemKeTaiSan> tblKiemKeTaiSans { get; set; } = new List<tblKiemKeTaiSan>();
    }
}
