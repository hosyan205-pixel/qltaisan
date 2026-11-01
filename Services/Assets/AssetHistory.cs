using DoAn.Data;
using Microsoft.EntityFrameworkCore;

namespace DoAn.Services.Assets;

public static class AssetHistory
{
    // Khi bổ sung bảng sửa chữa/timeline, nối kiểm tra TS_ID tại đây.
    public static async Task<bool> HasHistory(DataContext context, int id) =>
        await context.Tblbaotritaisans.AnyAsync(x => x.TsId == id) ||
        await context.Tblchitietcapphats.AnyAsync(x => x.TsId == id) ||
        await context.Tblchitietkiemkes.AnyAsync(x => x.TsId == id) ||
        await context.Tblchitietthanhlies.AnyAsync(x => x.TsId == id);
}
