using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tbltaikhoan
{
    public int TkId { get; set; }

    public string TkTenDangNhap { get; set; } = null!;

    public string TkMatKhau { get; set; } = null!;

    public string TkHoTen { get; set; } = null!;

    public string? TkEmail { get; set; }

    public string? TkSoDienThoai { get; set; }

    public string TkVaiTro { get; set; } = null!;

    public bool? TkTrangThai { get; set; }

    public DateTime TkNgayTao { get; set; }

    public virtual ICollection<Tblbaotritaisan> Tblbaotritaisans { get; set; } = new List<Tblbaotritaisan>();

    public virtual ICollection<Tblcapphattaisan> Tblcapphattaisans { get; set; } = new List<Tblcapphattaisan>();

    public virtual ICollection<Tblkiemketaisan> Tblkiemketaisans { get; set; } = new List<Tblkiemketaisan>();

    public virtual ICollection<Tblthanhlytaisan> Tblthanhlytaisans { get; set; } = new List<Tblthanhlytaisan>();
}
