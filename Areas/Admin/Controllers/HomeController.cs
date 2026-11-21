using DoAn.Areas.Admin.Models;
using DoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DoAn.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class HomeController(DataContext context) : Controller
    {
        private static DateTime? ToDateTime(object? val)
        {
            if (val == null) return null;
            if (val is DateTime dt) return dt;
            if (val is DateOnly dOnly) return dOnly.ToDateTime(TimeOnly.MinValue);
            return Convert.ToDateTime(val);
        }

        public async Task<IActionResult> Index(string preset = "all", DateTime? tuNgay = null, DateTime? denNgay = null, int? phongBanId = null)
        {
            var model = await BuildThongKeAsync(preset, tuNgay, denNgay, phongBanId);
            return View(model);
        }

        public async Task<IActionResult> ExportBaoCao(string preset = "all", DateTime? tuNgay = null, DateTime? denNgay = null, int? phongBanId = null)
        {
            var data = await BuildThongKeAsync(preset, tuNgay, denNgay, phongBanId);
            var sb = new StringBuilder();
            sb.AppendLine("BAO CAO THONG KE TAI SAN");
            sb.AppendLine($"Dang su dung,{data.DangSuDung},{data.NguyenGiaDangSuDung:0}");
            sb.AppendLine($"Chua cap phat,{data.ChuaCapPhat},{data.NguyenGiaChuaCapPhat:0}");
            sb.AppendLine($"Dang bao tri,{data.DangBaoTri},{data.TongChiPhiBaoTri:0}");
            sb.AppendLine($"Da thanh ly,{data.DaThanhLy},{data.TongGiaTriThanhLy:0}");
            sb.AppendLine();
            sb.AppendLine("Ma tai san,Ten tai san,Nghiep vu,Phong ban,Thoi gian");
            foreach (var hd in data.HoatDongGanDays)
            {
                sb.AppendLine($"\"{hd.MaTaiSan}\",\"{hd.TenTaiSan}\",\"{hd.NghiepVu}\",\"{hd.PhongBan}\",\"{hd.ThoiGian:dd/MM/yyyy}\"");
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv; charset=utf-8", $"BaoCaoThongKeTaiSan_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        private async Task<Tblthongke> BuildThongKeAsync(string preset, DateTime? tuNgay, DateTime? denNgay, int? phongBanId)
        {
            var today = DateTime.Now.Date;
            DateTime? startDate = null;
            DateTime? endDate = null;

            switch (preset?.ToLower())
            {
                case "7days":
                    startDate = today.AddDays(-6);
                    endDate = today.AddDays(1).AddTicks(-1);
                    break;
                case "30days":
                    startDate = today.AddDays(-29);
                    endDate = today.AddDays(1).AddTicks(-1);
                    break;
                case "quarter":
                    int q = (today.Month - 1) / 3 + 1;
                    startDate = new DateTime(today.Year, (q - 1) * 3 + 1, 1);
                    endDate = startDate.Value.AddMonths(3).AddTicks(-1);
                    break;
                case "year":
                    startDate = new DateTime(today.Year, 1, 1);
                    endDate = new DateTime(today.Year, 12, 31, 23, 59, 59);
                    break;
                case "custom":
                    if (tuNgay.HasValue) startDate = tuNgay.Value.Date;
                    if (denNgay.HasValue) endDate = denNgay.Value.Date.AddDays(1).AddTicks(-1);
                    break;
                default:
                    preset = "all";
                    break;
            }

            var phongBans = await context.tblPhongBan.AsNoTracking()
                .Where(p => p.PB_TrangThai == true)
                .OrderBy(p => p.PB_TenPhongBan).ToListAsync();

            var danhMucs = await context.tblDanhMucTaiSan.AsNoTracking()
                .Where(d => d.DMTS_TrangThai == true).ToListAsync();

            var allTaiSans = await context.tblTaiSan.AsNoTracking()
                .Include(t => t.tblDanhMucTaiSan).ToListAsync();

            var allCapPhats = await context.tblCapPhatTaiSan.AsNoTracking()
                .Include(c => c.PhongBan).ToListAsync();

            var allChiTietCapPhats = await context.tblChiTietCapPhat
                .AsNoTracking().Include(ct => ct.CapPhat)
                .Include(ct => ct.TaiSan).ToListAsync();

            var allBaoTris = await context.tblBaoTriTaiSan
                .AsNoTracking().Include(b => b.TaiSan).ToListAsync();

            var allKiemKes = await context.tblKiemKeTaiSan
                .AsNoTracking().Include(k => k.PhongBan).ToListAsync();

            var allChiTietKiemKes = await context.tblChiTietKiemKe
                .AsNoTracking().Include(ct => ct.KiemKe)
                .Include(ct => ct.TaiSan).ToListAsync();

            var allThanhLys = await context.tblThanhLyTaiSan
                .AsNoTracking().ToListAsync();

            var allChiTietThanhLys = await context.tblChiTietThanhLy
                .AsNoTracking().Include(ct => ct.ThanhLy)
                .Include(ct => ct.TaiSan).ToListAsync();

            var taiSanPhongBanMap = allChiTietCapPhats
                .Where(ct => ct.CapPhat != null && ct.CTCP_NgayThuHoi == null && ct.CTCP_TrangThai != "DaThuHoi")
                .GroupBy(ct => ct.TS_ID).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CTCP_ID)
                .First().CapPhat!.PB_ID);

            var pbNameMap = phongBans.ToDictionary(p => p.PB_ID, p => p.PB_TenPhongBan);

            var filteredTaiSans = allTaiSans.Where(t =>
            {
                var ngay = ToDateTime(t.TS_NgayMua) ?? t.TS_NgayTao;
                if (startDate.HasValue && ngay < startDate.Value) return false;
                if (endDate.HasValue && ngay > endDate.Value) return false;
                if (phongBanId.HasValue)
                {
                    if (!taiSanPhongBanMap.TryGetValue(t.TS_ID, out int pbId) || pbId != phongBanId.Value)
                        return false;
                }
                return true;
            }).ToList();

            var filteredBaoTris = allBaoTris.Where(b =>
            {
                var ngay = ToDateTime(b.BT_NgayBaoTri);
                if (!ngay.HasValue) return false;
                if (startDate.HasValue && ngay.Value < startDate.Value) return false;
                if (endDate.HasValue && ngay.Value > endDate.Value) return false;
                if (phongBanId.HasValue)
                {
                    if (!taiSanPhongBanMap.TryGetValue(b.TS_ID, out int pbId) || pbId != phongBanId.Value)
                        return false;
                }
                return true;
            }).ToList();

            var filteredThanhLys = allThanhLys.Where(tl =>
            {
                var ngay = ToDateTime(tl.TL_NgayThanhLy);
                if (!ngay.HasValue) return false;
                if (startDate.HasValue && ngay.Value < startDate.Value) return false;
                if (endDate.HasValue && ngay.Value > endDate.Value) return false;
                return true;
            }).ToList();

            var dangQuanLy = filteredTaiSans.Where(t => t.TS_DangQuanLy).ToList();
            int dangSuDung = dangQuanLy.Count(t => t.TS_TrangThai == "DaCapPhat");
            int chuaCapPhat = dangQuanLy.Count(t => t.TS_TrangThai == "ChuaCapPhat");
            int dangBaoTri = dangQuanLy.Count(t => t.TS_TinhTrang == "CanBaoTri" || t.TS_TinhTrang == "ThieuPhuKien");
            int daThanhLy = filteredTaiSans.Count(t => !t.TS_DangQuanLy || t.TS_TrangThai == "DaThanhLy" || t.TS_TinhTrang == "HuHong");
            if (daThanhLy == 0)
            {
                daThanhLy = allChiTietThanhLys.Count(ct => filteredThanhLys.Any(tl => tl.TL_ID == ct.TL_ID));
            }
            int tongTaiSan = dangQuanLy.Count;

            var mocThang = Enumerable.Range(0, 12).Select(i => new DateTime(today.Year, today.Month, 1)
                .AddMonths(-11 + i)).ToList();

            var maxYearData = allTaiSans.Select(t => (ToDateTime(t.TS_NgayMua) ?? t.TS_NgayTao).Year).DefaultIfEmpty(today.Year).Max();
            if (!allTaiSans.Any(t => (ToDateTime(t.TS_NgayMua) ?? t.TS_NgayTao) >= mocThang.First()))
            {
                mocThang = Enumerable.Range(1, 12).Select(m => new DateTime(maxYearData, m, 1)).ToList();
            }

            var bienDongLabels = new List<string>();
            var bienDongNhap = new List<int>();
            var bienDongCapPhat = new List<int>();

            foreach (var m in mocThang)
            {
                var nextM = m.AddMonths(1);
                bienDongLabels.Add($"T{m.Month}/{m.Year % 100}");
                bienDongNhap.Add(allTaiSans.Count(t =>
                {
                    var d = ToDateTime(t.TS_NgayMua) ?? t.TS_NgayTao;
                    return d >= m && d < nextM;
                }));
                bienDongCapPhat.Add(allChiTietCapPhats.Count(ct =>
                {
                    var d = ToDateTime(ct.CapPhat?.CP_NgayCapPhat);
                    return d.HasValue && d.Value >= m && d.Value < nextM;
                }));
            }

            int ttTot = dangQuanLy.Count(t => t.TS_TinhTrang == "Tot");
            int ttBaoTri = dangQuanLy.Count(t => t.TS_TinhTrang == "CanBaoTri");
            int ttThieuPK = dangQuanLy.Count(t => t.TS_TinhTrang == "ThieuPhuKien");
            int ttHuHong = dangQuanLy.Count(t => t.TS_TinhTrang == "HuHong");
            int mauSoTT = Math.Max(tongTaiSan, 1);

            var tinhTrangList = new List<TinhTrangChartItem>
            {
                new() { Ten = "Hoạt động tốt", SoLuong = ttTot, PhanTram = Math.Round(ttTot * 100.0 / mauSoTT, 0), MauHex = "#2563eb" },
                new() { Ten = "Cần bảo trì", SoLuong = ttBaoTri, PhanTram = Math.Round(ttBaoTri * 100.0 / mauSoTT, 0), MauHex = "#10b981" },
                new() { Ten = "Thiếu phụ kiện", SoLuong = ttThieuPK, PhanTram = Math.Round(ttThieuPK * 100.0 / mauSoTT, 0), MauHex = "#f59e0b" },
                new() { Ten = "Hư hỏng / Thanh lý", SoLuong = ttHuHong + daThanhLy, PhanTram = Math.Round((ttHuHong + daThanhLy) * 100.0 / mauSoTT, 0), MauHex = "#ef4444" }
            };

            string[] pbColors = ["#2563eb", "#10b981", "#f59e0b", "#8b5cf6", "#ef4444", "#06b6d4"];
            var pbRawCounts = phongBans.Select((pb, idx) =>
            {
                int count = dangQuanLy.Count(t => taiSanPhongBanMap.TryGetValue(t.TS_ID, out int pId) && pId == pb.PB_ID);
                return new { pb.PB_TenPhongBan, SoLuong = count, Color = pbColors[idx % pbColors.Length] };
            }).OrderByDescending(x => x.SoLuong).ToList();

            int tongTrongPB = Math.Max(pbRawCounts.Sum(x => x.SoLuong), 1);
            var pbChartList = pbRawCounts.Select(x => new PhongBanChartItem
            {
                TenPhongBan = x.PB_TenPhongBan,
                SoLuong = x.SoLuong,
                PhanTram = Math.Round(x.SoLuong * 100.0 / tongTrongPB, 0),
                MauHex = x.Color
            }).ToList();

            string[] dmColors = ["#2563eb", "#10b981", "#f59e0b", "#8b5cf6", "#06b6d4", "#ec4899"];
            var dmRaw = danhMucs.Select((dm, idx) =>
            {
                var items = dangQuanLy.Where(t => t.DMTS_ID == dm.DMTS_ID).ToList();
                return new
                {
                    dm.DMTS_TenDanhMuc,
                    SoLuong = items.Count,
                    TongGiaTri = items.Sum(t => t.TS_NguyenGia),
                    Color = dmColors[idx % dmColors.Length]
                };
            }).OrderByDescending(x => x.TongGiaTri).ToList();

            decimal maxGiaTriDM = dmRaw.Select(x => x.TongGiaTri).DefaultIfEmpty(1).Max();
            if (maxGiaTriDM <= 0) maxGiaTriDM = 1;

            var dmChartList = dmRaw.Select(x => new DanhMucGiaTriItem
            {
                TenDanhMuc = x.DMTS_TenDanhMuc,
                SoLuong = x.SoLuong,
                TongGiaTri = x.TongGiaTri,
                TyLeThanh = Math.Round((double)(x.TongGiaTri * 100 / maxGiaTriDM), 1),
                MauHex = x.Color
            }).ToList();

            var hoatDongs = new List<HoatDongGanDayItem>();

            foreach (var ct in allChiTietCapPhats)
            {
                if (ct.TaiSan == null || ct.CapPhat == null) continue;
                if (phongBanId.HasValue && ct.CapPhat.PB_ID != phongBanId.Value) continue;
                var ngay = ToDateTime(ct.CapPhat.CP_NgayCapPhat) ?? (ct.CapPhat.CP_NgayLapUTC?.AddHours(7)) ?? DateTime.MinValue;
                if (startDate.HasValue && ngay < startDate.Value) continue;
                if (endDate.HasValue && ngay > endDate.Value) continue;

                bool isThuHoi = ct.CTCP_NgayThuHoi != null || ct.CTCP_TrangThai == "DaThuHoi";
                hoatDongs.Add(new HoatDongGanDayItem
                {
                    MaTaiSan = ct.TaiSan.TS_MaTaiSan,
                    TenTaiSan = ct.TaiSan.TS_TenTaiSan,
                    NghiepVu = isThuHoi ? "Thu hồi" : "Cấp phát",
                    BadgeClass = isThuHoi ? "bg-purple-soft text-purple" : "bg-blue-soft text-primary",
                    PhongBan = pbNameMap.GetValueOrDefault(ct.CapPhat.PB_ID, ct.CapPhat.CP_TenPhongBanSnapshot ?? "—"),
                    ThoiGian = ngay,
                    SortKey = ngay.Ticks + ct.CTCP_ID,
                    ControllerName = "BanGiao",
                    RecordId = ct.CP_ID
                });
            }

            foreach (var bt in filteredBaoTris)
            {
                if (bt.TaiSan == null) continue;
                var ngay = ToDateTime(bt.BT_NgayBaoTri) ?? DateTime.MinValue;
                string pbName = taiSanPhongBanMap.TryGetValue(bt.TS_ID, out int pId) ? pbNameMap.GetValueOrDefault(pId, "Kho / Kỹ thuật") : "Kho / Kỹ thuật";
                hoatDongs.Add(new HoatDongGanDayItem
                {
                    MaTaiSan = bt.TaiSan.TS_MaTaiSan,
                    TenTaiSan = bt.TaiSan.TS_TenTaiSan,
                    NghiepVu = "Bảo trì",
                    BadgeClass = "bg-amber-soft text-warning",
                    PhongBan = pbName,
                    ThoiGian = ngay,
                    SortKey = ngay.Ticks + bt.BT_ID,
                    ControllerName = "BaoTri",
                    RecordId = bt.BT_ID
                });
            }

            foreach (var ct in allChiTietKiemKes)
            {
                if (ct.TaiSan == null || ct.KiemKe == null) continue;
                if (phongBanId.HasValue && ct.KiemKe.PB_ID != phongBanId.Value) continue;
                var ngay = ToDateTime(ct.KiemKe.KK_NgayKiemKe) ?? DateTime.MinValue;
                if (startDate.HasValue && ngay < startDate.Value) continue;
                if (endDate.HasValue && ngay > endDate.Value) continue;

                hoatDongs.Add(new HoatDongGanDayItem
                {
                    MaTaiSan = ct.TaiSan.TS_MaTaiSan,
                    TenTaiSan = ct.TaiSan.TS_TenTaiSan,
                    NghiepVu = "Kiểm kê",
                    BadgeClass = "bg-green-soft text-success",
                    PhongBan = ct.KiemKe.PB_ID.HasValue ? pbNameMap.GetValueOrDefault(ct.KiemKe.PB_ID.Value, "Toàn đơn vị") : "Toàn đơn vị",
                    ThoiGian = ngay,
                    SortKey = ngay.Ticks + ct.CTKK_ID,
                    ControllerName = "KiemKe",
                    RecordId = ct.KK_ID
                });
            }

            foreach (var ct in allChiTietThanhLys)
            {
                if (ct.TaiSan == null || ct.ThanhLy == null) continue;
                var ngay = ToDateTime(ct.ThanhLy.TL_NgayThanhLy) ?? DateTime.MinValue;
                if (startDate.HasValue && ngay < startDate.Value) continue;
                if (endDate.HasValue && ngay > endDate.Value) continue;

                hoatDongs.Add(new HoatDongGanDayItem
                {
                    MaTaiSan = ct.TaiSan.TS_MaTaiSan,
                    TenTaiSan = ct.TaiSan.TS_TenTaiSan,
                    NghiepVu = "Thanh lý",
                    BadgeClass = "bg-red-soft text-danger",
                    PhongBan = "Hội đồng thanh lý",
                    ThoiGian = ngay,
                    SortKey = ngay.Ticks + ct.CTTL_ID,
                    ControllerName = "ThanhLy",
                    RecordId = ct.TL_ID
                });
            }

            return new Tblthongke
            {
                FilterPreset = preset ?? "all",
                TuNgay = startDate,
                DenNgay = denNgay ?? (preset != "all" && preset != "custom" ? endDate?.Date : null),
                PhongBanId = phongBanId,
                DanhSachPhongBan = phongBans.Select(p => new PhongBanSelectItem { PB_ID = p.PB_ID, PB_TenPhongBan = p.PB_TenPhongBan }).ToList(),

                DangSuDung = dangSuDung,
                ChuaCapPhat = chuaCapPhat,
                DangBaoTri = dangBaoTri,
                DaThanhLy = daThanhLy,
                TongTaiSan = tongTaiSan,

                NguyenGiaDangSuDung = dangQuanLy.Where(t => t.TS_TrangThai == "DaCapPhat").Sum(t => t.TS_NguyenGia),
                NguyenGiaChuaCapPhat = dangQuanLy.Where(t => t.TS_TrangThai == "ChuaCapPhat").Sum(t => t.TS_NguyenGia),
                TongChiPhiBaoTri = filteredBaoTris.Sum(b => b.BT_ChiPhi ?? 0),
                TongGiaTriThanhLy = filteredThanhLys.Sum(tl => tl.TL_TongGiaTri ?? 0),

                BienDongLabels = bienDongLabels,
                BienDongNhapMoi = bienDongNhap,
                BienDongCapPhat = bienDongCapPhat,

                TinhTrangItems = tinhTrangList,
                TaiSanTheoPhongBans = pbChartList,
                GiaTriTheoDanhMucs = dmChartList,

                HoatDongGanDays = hoatDongs.OrderByDescending(x => x.SortKey).Take(5).ToList()
            };
        }
    }
}