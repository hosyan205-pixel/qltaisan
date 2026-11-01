using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tbldanhmuctaisan
{
    public int DmtsId { get; set; }

    public string DmtsMaDanhMuc { get; set; } = null!;

    public string DmtsTenDanhMuc { get; set; } = null!;

    public string? DmtsMoTa { get; set; }

    public DateTime DmtsNgayTao { get; set; }

    public bool? DmtsTrangThai { get; set; }

    public virtual ICollection<Tbltaisan> Tbltaisans { get; set; } = new List<Tbltaisan>();
}
