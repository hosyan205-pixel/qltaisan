using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tblthanhlytaisan
{
    public int TlId { get; set; }

    public string TlMaPhieu { get; set; } = null!;

    public DateOnly TlNgayThanhLy { get; set; }

    public int TlNguoiLapId { get; set; }

    public string TlLyDo { get; set; } = null!;

    public decimal TlTongGiaTri { get; set; }

    public string? TlPhuongThuc { get; set; }

    public string TlTrangThai { get; set; } = null!;

    public string? TlGhiChu { get; set; }

    public virtual ICollection<Tblchitietthanhly> Tblchitietthanhlies { get; set; } = new List<Tblchitietthanhly>();

    public virtual Tbltaikhoan TlNguoiLap { get; set; } = null!;
}
