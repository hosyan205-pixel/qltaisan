using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tblphongban
{
    public int PbId { get; set; }

    public string PbMaPhongBan { get; set; } = null!;

    public string PbTenPhongBan { get; set; } = null!;

    public string? PbDiaChi { get; set; }

    public string? PbSoDienThoai { get; set; }

    public string? PbNguoiPhuTrach { get; set; }

    public DateTime PbNgayTao { get; set; }

    public bool? PbTrangThai { get; set; }

    public virtual ICollection<Tblcapphattaisan> Tblcapphattaisans { get; set; } = new List<Tblcapphattaisan>();

    public virtual ICollection<Tblkiemketaisan> Tblkiemketaisans { get; set; } = new List<Tblkiemketaisan>();
}
