using DoAn.Areas.Admin.Models;
using DoAn.Models;

namespace DoAn.Services.Assets;

public static class AssetProfile
{
    public static tblTaiSan Create(AssetCreateViewModel model)
    {
        var asset = new tblTaiSan
        {
            TS_MaTaiSan = model.Ma.Trim(),
            TS_TrangThai = AssetStatuses.Unallocated,
            TS_NgayTao = DateTime.UtcNow.AddHours(7)
        };
        Update(model, asset);
        return asset;
    }

    // Điểm tập trung để bổ sung khóa trường/ghi lịch sử hồ sơ ở Sprint sau.
    // Không cập nhật ID, mã, ngày tạo hoặc trạng thái nghiệp vụ.
    public static void Update(AssetFieldsViewModel model, tblTaiSan asset)
    {
        asset.TS_TenTaiSan = model.Ten.Trim();
        asset.DMTS_ID = model.DanhMucId;
        asset.TS_NgayMua = model.NgayMua;
        asset.TS_NguyenGia = model.NguyenGia!.Value;
        asset.TS_HanBaoHanh = model.HanBaoHanh;
        asset.TS_TinhTrang = model.TinhTrang;
        asset.TS_MoTa = model.MoTa?.Trim();
        asset.TS_GhiChu = model.GhiChu?.Trim();
    }
}
