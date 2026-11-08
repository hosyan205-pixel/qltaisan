using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models;

[Table("tblsukiencapphat")]
public class tblSuKienCapPhat
{
    [Key] public long SK_ID { get; set; }
    public int CP_ID { get; set; }
    public int? CTCP_ID { get; set; }
    [Required, StringLength(40)] public string SK_Loai { get; set; } = "";
    public int SK_NguoiThucHienID { get; set; }
    [Required, StringLength(255)] public string SK_NguoiThucHienSnapshot { get; set; } = "";
    public DateTime SK_ThoiDiemUTC { get; set; }
    public string? SK_LyDo { get; set; }
    public string? SK_Truoc { get; set; }
    public string? SK_Sau { get; set; }
    [Required, StringLength(36)] public string SK_RequestKey { get; set; } = "";
    public virtual tblCapPhatTaiSan? CapPhat { get; set; }
    public virtual tblChiTietCapPhat? ChiTiet { get; set; }
    public virtual tblTaiKhoan? NguoiThucHien { get; set; }
}

