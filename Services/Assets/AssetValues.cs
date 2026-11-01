using System.Collections.ObjectModel;
using System.Globalization;

namespace DoAn.Services.Assets;

public static class AssetStatuses
{
    public const string Unallocated = "ChuaCapPhat";
    public const string Allocated = "DaCapPhat";
    public const string UnderMaintenance = "DangBaoTri";
    public const string PendingDisposal = "ChoThanhLy";
    public const string Disposed = "DaThanhLy";
    public static IReadOnlyDictionary<string, string> Labels { get; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
    {
        [Unallocated] = "Chưa cấp phát", [Allocated] = "Đã cấp phát", [UnderMaintenance] = "Đang bảo trì",
        [PendingDisposal] = "Chờ thanh lý", [Disposed] = "Đã thanh lý"
    });
    public static string Badge(string value) => value switch
    {
        Unallocated => "bg-secondary", Allocated => "bg-success", UnderMaintenance => "bg-warning text-dark",
        PendingDisposal => "bg-info text-dark", Disposed => "bg-dark", _ => "bg-secondary"
    };
}

public static class AssetConditions
{
    public const string Good = "Tot";
    public const string NeedsMaintenance = "CanBaoTri";
    public const string Damaged = "HuHong";
    public const string Degraded = "XuongCap";
    public const string LegacyDamaged = "Hong";
    public static IReadOnlyDictionary<string, string> Labels { get; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
    {
        [Good] = "Tốt", [NeedsMaintenance] = "Cần bảo trì", [Damaged] = "Hư hỏng", [Degraded] = "Xuống cấp"
    });
    public static string Normalize(string value) => value == LegacyDamaged ? Damaged : value;
}

public static class AssetDisplay
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
    public static string Money(decimal value) => value.ToString(value == decimal.Truncate(value) ? "N0" : "N2", CultureInfo.GetCultureInfo("vi-VN")) + " đ";
    public static string Label(IReadOnlyDictionary<string, string> labels, string value) => labels.GetValueOrDefault(value, value);
    public static (string Label, string Css) Warranty(DateOnly? expiry, DateOnly? today = null)
    {
        var date = today ?? Today;
        if (!expiry.HasValue) return ("Không có thông tin bảo hành", "bg-secondary");
        if (expiry < date) return ("Hết bảo hành", "bg-danger");
        if (expiry.Value.DayNumber - date.DayNumber <= 30) return ("Sắp hết bảo hành", "bg-warning text-dark");
        return ("Còn bảo hành", "bg-success");
    }
}
