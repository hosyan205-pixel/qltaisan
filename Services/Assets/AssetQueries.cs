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

    public static IQueryable<Tbltaisan> Filter(IQueryable<Tbltaisan> query, string? keyword, int? categoryId, string? condition, string? status)
    {
        keyword = keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword)) query = query.Where(x => x.TsMaTaiSan.Contains(keyword) || x.TsTenTaiSan.Contains(keyword));
        if (categoryId.HasValue) query = query.Where(x => x.DmtsId == categoryId);
        if (!string.IsNullOrEmpty(condition))
            query = condition == AssetConditions.Damaged
                ? query.Where(x => x.TsTinhTrang == AssetConditions.Damaged || x.TsTinhTrang == AssetConditions.LegacyDamaged)
                : query.Where(x => x.TsTinhTrang == condition);
        if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.TsTrangThai == status);
        return query;
    }

    public static IOrderedQueryable<Tbltaisan> Sort(IQueryable<Tbltaisan> query, string sort) => (sort switch
    {
        "code_asc" => query.OrderBy(x => x.TsMaTaiSan), "code_desc" => query.OrderByDescending(x => x.TsMaTaiSan),
        "name_asc" => query.OrderBy(x => x.TsTenTaiSan), "name_desc" => query.OrderByDescending(x => x.TsTenTaiSan),
        "price_asc" => query.OrderBy(x => x.TsNguyenGia), "price_desc" => query.OrderByDescending(x => x.TsNguyenGia),
        "purchase_asc" => query.OrderBy(x => x.TsNgayMua), "purchase_desc" => query.OrderByDescending(x => x.TsNgayMua),
        "created_asc" => query.OrderBy(x => x.TsNgayTao), _ => query.OrderByDescending(x => x.TsNgayTao)
    }).ThenBy(x => x.TsId);
}
