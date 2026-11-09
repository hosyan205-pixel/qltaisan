using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models;

[Table("tblsukienkiemke")]
public class tblSuKienKiemKe
{
    [Key] public long SKK_ID { get; set; }
    public int KK_ID { get; set; }
    public int? CTKK_ID { get; set; }
    [StringLength(40)] public string SKK_HanhDong { get; set; } = "";
    public int SKK_NguoiThucHienID { get; set; }
    [StringLength(255)] public string SKK_NguoiThucHienSnapshot { get; set; } = "";
    public DateTime SKK_ThoiDiemUTC { get; set; }
    public string? SKK_LyDo { get; set; }
    public string? SKK_Truoc { get; set; }
    public string? SKK_Sau { get; set; }
    [StringLength(36)] public string SKK_RequestKey { get; set; } = "";
    public virtual tblKiemKeTaiSan? KiemKe { get; set; }
    public virtual tblChiTietKiemKe? ChiTiet { get; set; }
    public virtual tblTaiKhoan? NguoiThucHien { get; set; }
}
