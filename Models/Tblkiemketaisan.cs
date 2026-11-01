using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tblkiemketaisan
{
    public int KkId { get; set; }

    public string KkMaPhieu { get; set; } = null!;

    public int? PbId { get; set; }

    public DateOnly KkNgayKiemKe { get; set; }

    public int KkNguoiKiemKeId { get; set; }

    public string? KkGhiChu { get; set; }

    public string KkTrangThai { get; set; } = null!;

    public virtual Tbltaikhoan KkNguoiKiemKe { get; set; } = null!;

    public virtual Tblphongban? Pb { get; set; }

    public virtual ICollection<Tblchitietkiemke> Tblchitietkiemkes { get; set; } = new List<Tblchitietkiemke>();
}
