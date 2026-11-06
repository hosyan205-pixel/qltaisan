using DoAn.Models;

namespace DoAn.Services.Assets;

public static class AssetQueries
{
    public static IReadOnlyDictionary<string, string> Sorts { get; } = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>
    {
        ["code_asc"] = "Mã tài sản ↑", ["code_desc"] = "Mã tài sản ↓", ["name_asc"] = "Tên tài sản ↑", ["name_desc"] = "Tên tài sản ↓",
        ["price_asc"] = "Nguyên giá ↑", ["price_desc"] = "Nguyên giá ↓", ["purchase_asc"] = "Ngày mua ↑", ["purchase_desc"] = "Ngày mua ↓",
        ["created_asc"] = "Ngày tạo ↑", ["created_desc"] = "Ngày tạo ↓"
    });

    public static IQueryable<tblTaiSan> Filter(IQueryable<tblTaiSan> query, string? keyword, int? categoryId, string? condition, string? status)
    {
        keyword = keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword)) query = query.Where(x => x.TS_MaTaiSan.Contains(keyword) || x.TS_TenTaiSan.Contains(keyword));
        if (categoryId.HasValue) query = query.Where(x => x.DMTS_ID == categoryId);
        if (!string.IsNullOrEmpty(condition))
            query = condition == AssetConditions.Damaged
                ? query.Where(x => x.TS_TinhTrang == AssetConditions.Damaged || x.TS_TinhTrang == AssetConditions.LegacyDamaged)
                : query.Where(x => x.TS_TinhTrang == condition);
        if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.TS_TrangThai == status);
        return query;
    }

    public static IOrderedQueryable<tblTaiSan> Sort(IQueryable<tblTaiSan> query, string sort) => (sort switch
    {
        "code_asc" => query.OrderBy(x => x.TS_MaTaiSan), "code_desc" => query.OrderByDescending(x => x.TS_MaTaiSan),
        "name_asc" => query.OrderBy(x => x.TS_TenTaiSan), "name_desc" => query.OrderByDescending(x => x.TS_TenTaiSan),
        "price_asc" => query.OrderBy(x => x.TS_NguyenGia), "price_desc" => query.OrderByDescending(x => x.TS_NguyenGia),
        "purchase_asc" => query.OrderBy(x => x.TS_NgayMua), "purchase_desc" => query.OrderByDescending(x => x.TS_NgayMua),
        "created_asc" => query.OrderBy(x => x.TS_NgayTao), _ => query.OrderByDescending(x => x.TS_NgayTao)
    }).ThenBy(x => x.TS_ID);
}
