using System;
using System.Collections.Generic;

namespace DoAn.Areas.Admin.Models
{
    public class Tblthongke
    {
        public string FilterPreset { get; set; } = "all";
        public DateTime? TuNgay { get; set; }
        public DateTime? DenNgay { get; set; }
        public int? PhongBanId { get; set; }
        public List<PhongBanSelectItem> DanhSachPhongBan { get; set; } = new();

        public int DangSuDung { get; set; }
        public int ChuaCapPhat { get; set; }
        public int DangBaoTri { get; set; }
        public int DaThanhLy { get; set; }
        public int TongTaiSan { get; set; }

        public decimal NguyenGiaDangSuDung { get; set; }
        public decimal NguyenGiaChuaCapPhat { get; set; }
        public decimal TongChiPhiBaoTri { get; set; }
        public decimal TongGiaTriThanhLy { get; set; }

        public List<string> BienDongLabels { get; set; } = new();
        public List<int> BienDongNhapMoi { get; set; } = new();
        public List<int> BienDongCapPhat { get; set; } = new();

        public List<TinhTrangChartItem> TinhTrangItems { get; set; } = new();

        public List<PhongBanChartItem> TaiSanTheoPhongBans { get; set; } = new();

        public List<DanhMucGiaTriItem> GiaTriTheoDanhMucs { get; set; } = new();

        public List<HoatDongGanDayItem> HoatDongGanDays { get; set; } = new();
    }

    public class PhongBanSelectItem
    {
        public int PB_ID { get; set; }
        public string PB_TenPhongBan { get; set; } = "";
    }

    public class TinhTrangChartItem
    {
        public string Ten { get; set; } = "";
        public int SoLuong { get; set; }
        public double PhanTram { get; set; }
        public string MauHex { get; set; } = "#2563eb";
    }

    public class PhongBanChartItem
    {
        public string TenPhongBan { get; set; } = "";
        public int SoLuong { get; set; }
        public double PhanTram { get; set; }
        public string MauHex { get; set; } = "#2563eb";
    }

    public class DanhMucGiaTriItem
    {
        public string TenDanhMuc { get; set; } = "";
        public int SoLuong { get; set; }
        public decimal TongGiaTri { get; set; }
        public double TyLeThanh { get; set; }
        public string MauHex { get; set; } = "#2563eb";
    }

    public class HoatDongGanDayItem
    {
        public string MaTaiSan { get; set; } = "";
        public string TenTaiSan { get; set; } = "";
        public string NghiepVu { get; set; } = "";
        public string BadgeClass { get; set; } = "";
        public string PhongBan { get; set; } = "";
        public DateTime ThoiGian { get; set; }
        public long SortKey { get; set; }
        public string ControllerName { get; set; } = "TaiSan";
        public int RecordId { get; set; }
    }
}