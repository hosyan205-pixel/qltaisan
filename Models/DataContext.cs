using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using DoAn.Areas.Admin.Models;

namespace DoAn.Models
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options)
        {
        }

        public virtual DbSet<tblTaiKhoan> tblTaiKhoan { get; set; } = null!;
        public virtual DbSet<tblTaiSan> tblTaiSan { get; set; } = null!;
        public virtual DbSet<tblDanhMucTaiSan> tblDanhMucTaiSan { get; set; } = null!;
        public virtual DbSet<tblPhongBan> tblPhongBan { get; set; } = null!;
        public virtual DbSet<tblBaoTriTaiSan> tblBaoTriTaiSan { get; set; } = null!;
        public virtual DbSet<tblCapPhatTaiSan> tblCapPhatTaiSan { get; set; } = null!;
        public virtual DbSet<tblChiTietCapPhat> tblChiTietCapPhat { get; set; } = null!;
        public virtual DbSet<tblChiTietKiemKe> tblChiTietKiemKe { get; set; } = null!;
        public virtual DbSet<tblChiTietThanhLy> tblChiTietThanhLy { get; set; } = null!;
        public virtual DbSet<tblKiemKeTaiSan> tblKiemKeTaiSan { get; set; } = null!;
        public virtual DbSet<tblThanhLyTaiSan> tblThanhLyTaiSan { get; set; } = null!;
        public virtual DbSet<tblMenu> tblMenu { get; set; } = null!;
        public virtual DbSet<AdminMenu> AdminMenu { get; set; } = null!;

        public DbSet<tblSuKienCapPhat> tblSuKienCapPhat => Set<tblSuKienCapPhat>();

        // Aliases for compatibility
        public DbSet<tblTaiKhoan> tblTaiKhoans => tblTaiKhoan;
        public DbSet<tblTaiKhoan> TaiKhoans => tblTaiKhoan;

        public DbSet<tblTaiSan> tblTaiSans => tblTaiSan;
        public DbSet<tblTaiSan> TaiSans => tblTaiSan;

        public DbSet<tblDanhMucTaiSan> tblDanhMucTaiSans => tblDanhMucTaiSan;
        public DbSet<tblDanhMucTaiSan> DanhMucTaiSans => tblDanhMucTaiSan;

        public DbSet<tblPhongBan> tblPhongBans => tblPhongBan;
        public DbSet<tblPhongBan> PhongBans => tblPhongBan;

        public DbSet<tblBaoTriTaiSan> tblBaoTriTaiSans => tblBaoTriTaiSan;
        public DbSet<tblCapPhatTaiSan> tblCapPhatTaiSans => tblCapPhatTaiSan;
        public DbSet<tblChiTietCapPhat> tblChiTietCapPhats => tblChiTietCapPhat;
        public DbSet<tblChiTietKiemKe> tblChiTietKiemKes => tblChiTietKiemKe;
        public DbSet<tblChiTietThanhLy> tblChiTietThanhLies => tblChiTietThanhLy;
        public DbSet<tblKiemKeTaiSan> tblKiemKeTaiSans => tblKiemKeTaiSan;
        public DbSet<tblThanhLyTaiSan> tblThanhLyTaiSans => tblThanhLyTaiSan;

        public DbSet<tblMenu> tblMenus => tblMenu;
        public DbSet<tblMenu> Menus => tblMenu;

        public DbSet<AdminMenu> AdminMenus => AdminMenu;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder
                .UseCollation("utf8mb4_general_ci")
                .HasCharSet("utf8mb4");

            modelBuilder.Entity<tblTaiKhoan>(entity =>
            {
                entity.ToTable("tbltaikhoan");
                entity.HasKey(e => e.TK_ID).HasName("PRIMARY");
                entity.HasIndex(e => e.TK_TenDangNhap, "TK_TenDangNhap").IsUnique();
            });

            modelBuilder.Entity<tblDanhMucTaiSan>(entity =>
            {
                entity.ToTable("tbldanhmuctaisan");
                entity.HasKey(e => e.DMTS_ID).HasName("PRIMARY");
                entity.HasIndex(e => e.DMTS_MaDanhMuc, "DMTS_MaDanhMuc").IsUnique();
            });

            modelBuilder.Entity<tblTaiSan>(entity =>
            {
                entity.ToTable("tbltaisan");
                entity.HasKey(e => e.TS_ID).HasName("PRIMARY");
                entity.HasIndex(e => e.TS_MaTaiSan, "TS_MaTaiSan").IsUnique();
                entity.HasIndex(e => e.DMTS_ID, "DMTS_ID");

                entity.Property(e => e.TS_NguyenGia).HasPrecision(15, 2);
                entity.Property(e => e.TS_TrangThai).IsConcurrencyToken();

                entity.HasOne(d => d.tblDanhMucTaiSan)
                    .WithMany(p => p.tblTaiSans)
                    .HasForeignKey(d => d.DMTS_ID)
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("tbltaisan_ibfk_1");
            });

            modelBuilder.Entity<tblPhongBan>(entity =>
            {
                entity.ToTable("tblphongban");
                entity.HasKey(e => e.PB_ID).HasName("PRIMARY");
                entity.HasIndex(e => e.PB_MaPhongBan, "PB_MaPhongBan").IsUnique();
            });

            modelBuilder.Entity<tblBaoTriTaiSan>(entity =>
            {
                entity.ToTable("tblbaotritaisan");
                entity.HasKey(e => e.BT_ID).HasName("PRIMARY");
                entity.HasIndex(e => e.BT_MaPhieu, "BT_MaPhieu").IsUnique();
                entity.HasIndex(e => e.TS_ID, "TS_ID");
                entity.HasIndex(e => e.BT_NguoiLapID, "BT_NguoiLapID");

                entity.Property(e => e.BT_ChiPhi).HasPrecision(15, 2);

                entity.HasOne(d => d.TaiSan)
                    .WithMany(p => p.tblBaoTriTaiSans)
                    .HasForeignKey(d => d.TS_ID)
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("tblbaotritaisan_ibfk_1");

                entity.HasOne(d => d.NguoiLap)
                    .WithMany(p => p.tblBaoTriTaiSans)
                    .HasForeignKey(d => d.BT_NguoiLapID)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("tblbaotritaisan_ibfk_2");
            });

            modelBuilder.Entity<tblCapPhatTaiSan>(entity =>
            {
                entity.ToTable("tblcapphattaisan");
                entity.HasKey(e => e.CP_ID).HasName("PRIMARY");
                entity.HasIndex(e => e.CP_MaPhieu, "CP_MaPhieu").IsUnique();
                entity.HasIndex(e => e.PB_ID, "PB_ID");
                entity.HasIndex(e => e.CP_NguoiGiaoID, "CP_NguoiGiaoID");

                entity.HasOne(d => d.PhongBan)
                    .WithMany(p => p.tblCapPhatTaiSans)
                    .HasForeignKey(d => d.PB_ID)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("tblcapphattaisan_ibfk_1");

                entity.HasOne(d => d.NguoiGiao)
                    .WithMany(p => p.tblCapPhatTaiSans)
                    .HasForeignKey(d => d.CP_NguoiGiaoID)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("tblcapphattaisan_ibfk_2");
            });

            modelBuilder.Entity<tblChiTietCapPhat>(entity =>
            {
                entity.ToTable("tblchitietcapphat");
                entity.HasKey(e => e.CTCP_ID).HasName("PRIMARY");
                entity.HasIndex(e => new { e.CP_ID, e.TS_ID }, "CP_ID").IsUnique();
                entity.HasIndex(e => e.TS_ID, "TS_ID");

                entity.HasOne(d => d.CapPhat)
                    .WithMany(p => p.tblChiTietCapPhats)
                    .HasForeignKey(d => d.CP_ID)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("tblchitietcapphat_ibfk_1");

                entity.HasOne(d => d.TaiSan)
                    .WithMany(p => p.tblChiTietCapPhats)
                    .HasForeignKey(d => d.TS_ID)
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("tblchitietcapphat_ibfk_2");
            });

            modelBuilder.Entity<tblChiTietKiemKe>(entity =>
            {
                entity.ToTable("tblchitietkiemke");
                entity.HasKey(e => e.CTKK_ID).HasName("PRIMARY");
                entity.HasIndex(e => new { e.KK_ID, e.TS_ID }, "KK_ID").IsUnique();
                entity.HasIndex(e => e.TS_ID, "TS_ID");

                entity.HasOne(d => d.KiemKe)
                    .WithMany(p => p.tblChiTietKiemKes)
                    .HasForeignKey(d => d.KK_ID)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("tblchitietkiemke_ibfk_1");

                entity.HasOne(d => d.TaiSan)
                    .WithMany(p => p.tblChiTietKiemKes)
                    .HasForeignKey(d => d.TS_ID)
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("tblchitietkiemke_ibfk_2");
            });

            modelBuilder.Entity<tblChiTietThanhLy>(entity =>
            {
                entity.ToTable("tblchitietthanhly");
                entity.HasKey(e => e.CTTL_ID).HasName("PRIMARY");
                entity.HasIndex(e => new { e.TL_ID, e.TS_ID }, "TL_ID").IsUnique();
                entity.HasIndex(e => e.TS_ID, "TS_ID");

                entity.Property(e => e.CTTL_GiaTriThanhLy).HasPrecision(15, 2);

                entity.HasOne(d => d.ThanhLy)
                    .WithMany(p => p.tblChiTietThanhLies)
                    .HasForeignKey(d => d.TL_ID)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("tblchitietthanhly_ibfk_1");

                entity.HasOne(d => d.TaiSan)
                    .WithMany(p => p.tblChiTietThanhLies)
                    .HasForeignKey(d => d.TS_ID)
                    .OnDelete(DeleteBehavior.Restrict)
                    .HasConstraintName("tblchitietthanhly_ibfk_2");
            });

            modelBuilder.Entity<tblKiemKeTaiSan>(entity =>
            {
                entity.ToTable("tblkiemketaisan");
                entity.HasKey(e => e.KK_ID).HasName("PRIMARY");
                entity.HasIndex(e => e.KK_MaPhieu, "KK_MaPhieu").IsUnique();
                entity.HasIndex(e => e.PB_ID, "PB_ID");
                entity.HasIndex(e => e.KK_NguoiKiemKeID, "KK_NguoiKiemKeID");

                entity.HasOne(d => d.PhongBan)
                    .WithMany(p => p.tblKiemKeTaiSans)
                    .HasForeignKey(d => d.PB_ID)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("tblkiemketaisan_ibfk_1");

                entity.HasOne(d => d.NguoiKiemKe)
                    .WithMany(p => p.tblKiemKeTaiSans)
                    .HasForeignKey(d => d.KK_NguoiKiemKeID)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("tblkiemketaisan_ibfk_2");
            });

            modelBuilder.Entity<tblThanhLyTaiSan>(entity =>
            {
                entity.ToTable("tblthanhlytaisan");
                entity.HasKey(e => e.TL_ID).HasName("PRIMARY");
                entity.HasIndex(e => e.TL_MaPhieu, "TL_MaPhieu").IsUnique();
                entity.HasIndex(e => e.TL_NguoiLapID, "TL_NguoiLapID");

                entity.Property(e => e.TL_TongGiaTri).HasPrecision(15, 2);

                entity.HasOne(d => d.NguoiLap)
                    .WithMany(p => p.tblThanhLyTaiSans)
                    .HasForeignKey(d => d.TL_NguoiLapID)
                    .OnDelete(DeleteBehavior.ClientSetNull)
                    .HasConstraintName("tblthanhlytaisan_ibfk_1");
            });

            modelBuilder.Entity<tblMenu>(entity =>
            {
                entity.ToTable("tblmenu");
                entity.HasKey(e => e.MenuID).HasName("PRIMARY");
            });

            modelBuilder.Entity<AdminMenu>(entity =>
            {
                entity.ToTable("adminmenu");
                entity.HasKey(e => e.AdminMenuID).HasName("PRIMARY");
            });

            ConfigureSprint2(modelBuilder);

            // Restrict cascade delete on assets and categories
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var foreignKey in entity.GetForeignKeys())
                {
                    if (foreignKey.PrincipalEntityType.ClrType == typeof(tblTaiSan) ||
                        foreignKey.PrincipalEntityType.ClrType == typeof(tblDanhMucTaiSan))
                    {
                        foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
                    }
                }
            }
        }

        private static void ConfigureSprint2(ModelBuilder builder)
        {
            builder.Entity<tblTaiKhoan>(e =>
            {
                e.HasIndex(x => x.PB_ID).HasDatabaseName("IX_TK_PB");
                e.HasOne(x => x.PhongBanLamViec).WithMany().HasForeignKey(x => x.PB_ID)
                    .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_TK_PB");
            });
            builder.Entity<tblTaiSan>().Property(x => x.TS_DangQuanLy).HasDefaultValue(true);
            builder.Entity<tblTaiSan>().Property(x => x.TS_TinhTrang).IsConcurrencyToken();
            builder.Entity<tblCapPhatTaiSan>(e =>
            {
                e.Property(x => x.CP_NguoiNhan).IsRequired();
                e.Property(x => x.CP_RequestKey).HasConversion<Guid>().HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin");
                e.Property(x => x.CP_PhienBan).IsConcurrencyToken().HasDefaultValue(0L);
                foreach (var p in new[] { nameof(DoAn.Models.tblCapPhatTaiSan.CP_NgayLapUTC), nameof(DoAn.Models.tblCapPhatTaiSan.CP_ThucHienUTC), nameof(DoAn.Models.tblCapPhatTaiSan.CP_XacNhanUTC) })
                    e.Property(p).HasColumnType("datetime(6)");
                e.HasIndex(x => x.CP_RequestKey).IsUnique().HasDatabaseName("UX_CP_RequestKey");
                e.HasIndex(x => x.CP_NguoiNhanID).HasDatabaseName("IX_CP_NguoiNhan");
                e.HasIndex(x => x.CP_NguoiLapID).HasDatabaseName("IX_CP_NguoiLap");
                e.HasIndex(x => x.CP_NguoiXacNhanID).HasDatabaseName("IX_CP_NguoiXacNhan");
                e.HasIndex(x => new { x.CP_LoaiPhieu, x.CP_TrangThai, x.CP_NgayLapUTC, x.CP_ID }).HasDatabaseName("IX_CP_Loai_TrangThai_Ngay");
                e.HasOne(x => x.NguoiNhan).WithMany().HasForeignKey(x => x.CP_NguoiNhanID).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_CP_NguoiNhan");
                e.HasOne(x => x.NguoiLap).WithMany().HasForeignKey(x => x.CP_NguoiLapID).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_CP_NguoiLap");
                e.HasOne(x => x.NguoiXacNhan).WithMany().HasForeignKey(x => x.CP_NguoiXacNhanID).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_CP_NguoiXacNhan");
                e.ToTable("tblcapphattaisan", t =>
                {
                    t.HasCheckConstraint("CK_CP_Loai", "CP_LoaiPhieu IS NULL OR CP_LoaiPhieu IN ('Giao','ThuHoi','Chuyen')");
                    t.HasCheckConstraint("CK_CP_HanTra", "CP_NgayTraDuKien IS NULL OR CP_NgayDuKien IS NULL OR CP_NgayTraDuKien>=CP_NgayDuKien");
                });
            });
            builder.Entity<tblChiTietCapPhat>(e =>
            {
                foreach (var p in new[] { nameof(DoAn.Models.tblChiTietCapPhat.CTCP_BatDauUTC), nameof(DoAn.Models.tblChiTietCapPhat.CTCP_KetThucUTC), nameof(DoAn.Models.tblChiTietCapPhat.CTCP_KiemTraUTC), nameof(DoAn.Models.tblChiTietCapPhat.CTCP_DoiSoatUTC) })
                    e.Property(p).HasColumnType("datetime(6)");
                e.Property(x => x.CTCP_TaiSanHieuLucID)
                    .HasComputedColumnSql("CASE WHEN CTCP_TrangThai='DangHieuLuc' THEN TS_ID ELSE NULL END", stored: true);
                e.HasIndex(x => x.CTCP_TaiSanHieuLucID).IsUnique().HasDatabaseName("UX_CTCP_TaiSanHieuLuc");
                e.HasIndex(x => new { x.TS_ID, x.CTCP_TrangThai }).HasDatabaseName("IX_CTCP_TS_TrangThai");
                e.HasIndex(x => new { x.CP_ID, x.CTCP_TrangThai }).HasDatabaseName("IX_CTCP_CP_TrangThai");
                e.HasIndex(x => x.CTCP_DongNguonID).HasDatabaseName("IX_CTCP_DongNguon");
                e.HasIndex(x => x.CTCP_NguoiKiemTraID).HasDatabaseName("IX_CTCP_NguoiKiemTra");
                e.HasIndex(x => x.CTCP_NguoiDoiSoatID).HasDatabaseName("IX_CTCP_NguoiDoiSoat");
                e.HasOne(x => x.DongNguon).WithMany(x => x.CacDongKeTiep).HasForeignKey(x => x.CTCP_DongNguonID)
                    .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_CTCP_DongNguon");
                e.HasOne(x => x.NguoiKiemTra).WithMany().HasForeignKey(x => x.CTCP_NguoiKiemTraID)
                    .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_CTCP_NguoiKiemTra");
                e.HasOne(x => x.NguoiDoiSoat).WithMany().HasForeignKey(x => x.CTCP_NguoiDoiSoatID)
                    .OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_CTCP_NguoiDoiSoat");
                e.ToTable("tblchitietcapphat", t =>
                {
                    t.HasCheckConstraint("CK_CTCP_TrangThai", "CTCP_TrangThai IS NULL OR CTCP_TrangThai IN ('ChoGiao','ChoNhan','ChoChuyen','DangHieuLuc','DaNhan','DaThuHoi','DaChuyen')");
                    t.HasCheckConstraint("CK_CTCP_ThoiGian", "CTCP_BatDauUTC IS NULL OR CTCP_KetThucUTC IS NULL OR CTCP_KetThucUTC>=CTCP_BatDauUTC");
                });
            });
            builder.Entity<tblSuKienCapPhat>(e =>
            {
                e.ToTable("tblsukiencapphat");
                e.HasKey(x => x.SK_ID);
                e.Property(x => x.SK_ID).ValueGeneratedOnAdd();
                e.Property(x => x.SK_ThoiDiemUTC).HasColumnType("datetime(6)");
                e.Property(x => x.SK_Truoc).HasColumnType("longtext");
                e.Property(x => x.SK_Sau).HasColumnType("longtext");
                e.Property(x => x.SK_RequestKey).HasConversion<Guid>().HasColumnType("char(36)").HasCharSet("ascii").UseCollation("ascii_bin");
                e.HasIndex(x => x.SK_RequestKey).IsUnique().HasDatabaseName("UX_SK_RequestKey");
                e.HasIndex(x => new { x.CP_ID, x.SK_ThoiDiemUTC, x.SK_ID }).HasDatabaseName("IX_SK_CP_ThoiDiem");
                e.HasIndex(x => new { x.CTCP_ID, x.SK_ThoiDiemUTC }).HasDatabaseName("IX_SK_CTCP_ThoiDiem");
                e.HasIndex(x => x.SK_NguoiThucHienID).HasDatabaseName("IX_SK_NguoiThucHien");
                e.HasOne(x => x.CapPhat).WithMany(x => x.SuKiens).HasForeignKey(x => x.CP_ID).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_SK_CP");
                e.HasOne(x => x.ChiTiet).WithMany(x => x.SuKiens).HasForeignKey(x => x.CTCP_ID).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_SK_CTCP");
                e.HasOne(x => x.NguoiThucHien).WithMany().HasForeignKey(x => x.SK_NguoiThucHienID).OnDelete(DeleteBehavior.Restrict).HasConstraintName("FK_SK_NguoiThucHien");
            });
        }
    }
}
