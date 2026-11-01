using DoAn.Areas.Admin.Models;
using DoAn.Models;

namespace DoAn.Services.Assets;

public static class AssetProfile
{
    public static Tbltaisan Create(AssetCreateViewModel model)
    {
        var asset = new Tbltaisan
        {
            TsMaTaiSan = model.Ma.Trim(), TsTrangThai = AssetStatuses.Unallocated,
            TsNgayTao = DateTime.UtcNow.AddHours(7)
        };
        Update(model, asset);
        return asset;
    }

    // Điểm tập trung để bổ sung khóa trường/ghi lịch sử hồ sơ ở Sprint sau.
    // Không cập nhật ID, mã, ngày tạo hoặc trạng thái nghiệp vụ.
    public static void Update(AssetFieldsViewModel model, Tbltaisan asset)
    {
        asset.TsTenTaiSan = model.Ten.Trim();
        asset.DmtsId = model.DanhMucId;
        asset.TsNgayMua = model.NgayMua;
        asset.TsNguyenGia = model.NguyenGia!.Value;
        asset.TsHanBaoHanh = model.HanBaoHanh;
        asset.TsTinhTrang = model.TinhTrang;
        asset.TsMoTa = model.MoTa?.Trim();
        asset.TsGhiChu = model.GhiChu?.Trim();
    }
}
