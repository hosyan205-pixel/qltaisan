using DoAn.Models;
using Microsoft.EntityFrameworkCore;

namespace DoAn.Data;

public partial class DataContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // Không xóa dây chuyền chứng từ/lịch sử và danh mục đang được dùng.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var foreignKey in entity.GetForeignKeys())
            if (foreignKey.PrincipalEntityType.ClrType == typeof(Tbltaisan) ||
                foreignKey.PrincipalEntityType.ClrType == typeof(Tbldanhmuctaisan))
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;

        // Không thêm cột: phát hiện workflow đổi trạng thái trong lúc lưu hồ sơ.
        modelBuilder.Entity<Tbltaisan>().Property(x => x.TsTrangThai).IsConcurrencyToken();
    }
}
