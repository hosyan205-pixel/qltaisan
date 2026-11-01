using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tblcapphattaisan
{
    public int CpId { get; set; }

    public string CpMaPhieu { get; set; } = null!;

    public int PbId { get; set; }

    public string CpNguoiNhan { get; set; } = null!;

    public int CpNguoiGiaoId { get; set; }

    public DateOnly CpNgayCapPhat { get; set; }

    public string? CpLyDo { get; set; }

    public string CpTrangThai { get; set; } = null!;

    public string? CpGhiChu { get; set; }

    public virtual Tbltaikhoan CpNguoiGiao { get; set; } = null!;

    public virtual Tblphongban Pb { get; set; } = null!;

    public virtual ICollection<Tblchitietcapphat> Tblchitietcapphats { get; set; } = new List<Tblchitietcapphat>();
}
