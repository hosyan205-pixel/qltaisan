using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tbltaisan
{
    public int TsId { get; set; }

    public string TsMaTaiSan { get; set; } = null!;

    public string TsTenTaiSan { get; set; } = null!;

    public int DmtsId { get; set; }

    public string? TsMoTa { get; set; }

    public DateOnly? TsNgayMua { get; set; }

    public decimal TsNguyenGia { get; set; }

    public DateOnly? TsHanBaoHanh { get; set; }

    public string TsTinhTrang { get; set; } = null!;

    public string TsTrangThai { get; set; } = null!;

    public string? TsGhiChu { get; set; }

    public DateTime TsNgayTao { get; set; }

    public virtual Tbldanhmuctaisan Dmts { get; set; } = null!;

    public virtual ICollection<Tblbaotritaisan> Tblbaotritaisans { get; set; } = new List<Tblbaotritaisan>();

    public virtual ICollection<Tblchitietcapphat> Tblchitietcapphats { get; set; } = new List<Tblchitietcapphat>();

    public virtual ICollection<Tblchitietkiemke> Tblchitietkiemkes { get; set; } = new List<Tblchitietkiemke>();

    public virtual ICollection<Tblchitietthanhly> Tblchitietthanhlies { get; set; } = new List<Tblchitietthanhly>();
}
