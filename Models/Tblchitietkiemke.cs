using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tblchitietkiemke
{
    public int CtkkId { get; set; }

    public int KkId { get; set; }

    public int TsId { get; set; }

    public string? CtkkTinhTrangThucTe { get; set; }

    public string? CtkkViTriThucTe { get; set; }

    public string CtkkKetQua { get; set; } = null!;

    public string? CtkkGhiChu { get; set; }

    public virtual Tblkiemketaisan Kk { get; set; } = null!;

    public virtual Tbltaisan Ts { get; set; } = null!;
}
