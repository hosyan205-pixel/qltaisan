using DoAn.Models;
using Microsoft.EntityFrameworkCore;

namespace DoAn.Services.Assets;

public static class AssetHistory
{
    // Khi bổ sung bảng sửa chữa/timeline, nối kiểm tra TS_ID tại đây.
    public static async Task<bool> HasHistory(DataContext context, int id) =>
        await context.tblBaoTriTaiSan.AnyAsync(x => x.TS_ID == id) ||
        await context.tblChiTietCapPhat.AnyAsync(x => x.TS_ID == id) ||
        await context.tblChiTietKiemKe.AnyAsync(x => x.TS_ID == id) ||
        await context.tblChiTietThanhLy.AnyAsync(x => x.TS_ID == id);
}
