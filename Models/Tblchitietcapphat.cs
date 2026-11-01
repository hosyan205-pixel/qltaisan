using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tblchitietcapphat
{
    public int CtcpId { get; set; }

    public int CpId { get; set; }

    public int TsId { get; set; }

    public DateOnly? CtcpNgayThuHoi { get; set; }

    public string? CtcpTinhTrangKhiGiao { get; set; }

    public string? CtcpTinhTrangKhiThuHoi { get; set; }

    public string? CtcpGhiChu { get; set; }

    public virtual Tblcapphattaisan Cp { get; set; } = null!;

    public virtual Tbltaisan Ts { get; set; } = null!;
}
