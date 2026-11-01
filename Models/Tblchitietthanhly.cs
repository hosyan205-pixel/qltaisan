using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tblchitietthanhly
{
    public int CttlId { get; set; }

    public int TlId { get; set; }

    public int TsId { get; set; }

    public decimal CttlGiaTriThanhLy { get; set; }

    public string? CttlTinhTrang { get; set; }

    public string? CttlGhiChu { get; set; }

    public virtual Tblthanhlytaisan Tl { get; set; } = null!;

    public virtual Tbltaisan Ts { get; set; } = null!;
}
