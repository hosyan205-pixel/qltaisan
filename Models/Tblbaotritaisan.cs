using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tblbaotritaisan
{
    public int BtId { get; set; }

    public string BtMaPhieu { get; set; } = null!;

    public int TsId { get; set; }

    public int BtNguoiLapId { get; set; }

    public DateOnly BtNgayBaoTri { get; set; }

    public DateOnly? BtNgayHoanThanh { get; set; }

    public string BtLyDo { get; set; } = null!;

    public string? BtNoiDung { get; set; }

    public decimal BtChiPhi { get; set; }

    public string? BtDonViBaoTri { get; set; }

    public string BtTrangThai { get; set; } = null!;

    public string? BtGhiChu { get; set; }

    public virtual Tbltaikhoan BtNguoiLap { get; set; } = null!;

    public virtual Tbltaisan Ts { get; set; } = null!;
}
