using System;
using System.Collections.Generic;
using DoAn.Models;
using Microsoft.EntityFrameworkCore;

namespace DoAn.Data;

public partial class DataContext : DbContext
{
    public DataContext(DbContextOptions<DataContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Adminmenu> Adminmenus { get; set; }

    public virtual DbSet<Tblbaotritaisan> Tblbaotritaisans { get; set; }

    public virtual DbSet<Tblcapphattaisan> Tblcapphattaisans { get; set; }

    public virtual DbSet<Tblchitietcapphat> Tblchitietcapphats { get; set; }

    public virtual DbSet<Tblchitietkiemke> Tblchitietkiemkes { get; set; }

    public virtual DbSet<Tblchitietthanhly> Tblchitietthanhlies { get; set; }

    public virtual DbSet<Tbldanhmuctaisan> Tbldanhmuctaisans { get; set; }

    public virtual DbSet<Tblkiemketaisan> Tblkiemketaisans { get; set; }

    public virtual DbSet<Tblmenu> Tblmenus { get; set; }

    public virtual DbSet<Tblphongban> Tblphongbans { get; set; }

    public virtual DbSet<Tbltaikhoan> Tbltaikhoans { get; set; }

    public virtual DbSet<Tbltaisan> Tbltaisans { get; set; }

    public virtual DbSet<Tblthanhlytaisan> Tblthanhlytaisans { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_general_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Adminmenu>(entity =>
        {
            entity.HasKey(e => e.AdminMenuId).HasName("PRIMARY");

            entity.ToTable("adminmenu");

            entity.Property(e => e.AdminMenuId)
                .HasColumnType("bigint(20)")
                .HasColumnName("AdminMenuID");
            entity.Property(e => e.ActionName).HasMaxLength(20);
            entity.Property(e => e.AreaName).HasMaxLength(20);
            entity.Property(e => e.ControllerName).HasMaxLength(20);
            entity.Property(e => e.Icon).HasMaxLength(50);
            entity.Property(e => e.IdName).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasColumnType("int(11)");
            entity.Property(e => e.ItemLevel).HasColumnType("int(11)");
            entity.Property(e => e.ItemName).HasMaxLength(50);
            entity.Property(e => e.ItemOrder).HasColumnType("int(11)");
            entity.Property(e => e.ItemTarget).HasMaxLength(20);
            entity.Property(e => e.ParentLevel).HasColumnType("int(11)");
        });

        modelBuilder.Entity<Tblbaotritaisan>(entity =>
        {
            entity.HasKey(e => e.BtId).HasName("PRIMARY");

            entity.ToTable("tblbaotritaisan");

            entity.HasIndex(e => e.BtMaPhieu, "BT_MaPhieu").IsUnique();

            entity.HasIndex(e => e.BtNguoiLapId, "BT_NguoiLapID");

            entity.HasIndex(e => e.TsId, "TS_ID");

            entity.Property(e => e.BtId)
                .HasColumnType("int(11)")
                .HasColumnName("BT_ID");
            entity.Property(e => e.BtChiPhi)
                .HasPrecision(15, 2)
                .HasColumnName("BT_ChiPhi");
            entity.Property(e => e.BtDonViBaoTri)
                .HasMaxLength(150)
                .HasColumnName("BT_DonViBaoTri");
            entity.Property(e => e.BtGhiChu)
                .HasColumnType("text")
                .HasColumnName("BT_GhiChu");
            entity.Property(e => e.BtLyDo)
                .HasColumnType("text")
                .HasColumnName("BT_LyDo");
            entity.Property(e => e.BtMaPhieu)
                .HasMaxLength(30)
                .HasColumnName("BT_MaPhieu");
            entity.Property(e => e.BtNgayBaoTri).HasColumnName("BT_NgayBaoTri");
            entity.Property(e => e.BtNgayHoanThanh).HasColumnName("BT_NgayHoanThanh");
            entity.Property(e => e.BtNguoiLapId)
                .HasColumnType("int(11)")
                .HasColumnName("BT_NguoiLapID");
            entity.Property(e => e.BtNoiDung)
                .HasColumnType("text")
                .HasColumnName("BT_NoiDung");
            entity.Property(e => e.BtTrangThai)
                .HasMaxLength(50)
                .HasDefaultValueSql("'ChoXuLy'")
                .HasColumnName("BT_TrangThai");
            entity.Property(e => e.TsId)
                .HasColumnType("int(11)")
                .HasColumnName("TS_ID");

            entity.HasOne(d => d.BtNguoiLap).WithMany(p => p.Tblbaotritaisans)
                .HasForeignKey(d => d.BtNguoiLapId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblbaotritaisan_ibfk_2");

            entity.HasOne(d => d.Ts).WithMany(p => p.Tblbaotritaisans)
                .HasForeignKey(d => d.TsId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblbaotritaisan_ibfk_1");
        });

        modelBuilder.Entity<Tblcapphattaisan>(entity =>
        {
            entity.HasKey(e => e.CpId).HasName("PRIMARY");

            entity.ToTable("tblcapphattaisan");

            entity.HasIndex(e => e.CpMaPhieu, "CP_MaPhieu").IsUnique();

            entity.HasIndex(e => e.CpNguoiGiaoId, "CP_NguoiGiaoID");

            entity.HasIndex(e => e.PbId, "PB_ID");

            entity.Property(e => e.CpId)
                .HasColumnType("int(11)")
                .HasColumnName("CP_ID");
            entity.Property(e => e.CpGhiChu)
                .HasColumnType("text")
                .HasColumnName("CP_GhiChu");
            entity.Property(e => e.CpLyDo)
                .HasColumnType("text")
                .HasColumnName("CP_LyDo");
            entity.Property(e => e.CpMaPhieu)
                .HasMaxLength(30)
                .HasColumnName("CP_MaPhieu");
            entity.Property(e => e.CpNgayCapPhat).HasColumnName("CP_NgayCapPhat");
            entity.Property(e => e.CpNguoiGiaoId)
                .HasColumnType("int(11)")
                .HasColumnName("CP_NguoiGiaoID");
            entity.Property(e => e.CpNguoiNhan)
                .HasMaxLength(100)
                .HasColumnName("CP_NguoiNhan");
            entity.Property(e => e.CpTrangThai)
                .HasMaxLength(50)
                .HasDefaultValueSql("'DaCapPhat'")
                .HasColumnName("CP_TrangThai");
            entity.Property(e => e.PbId)
                .HasColumnType("int(11)")
                .HasColumnName("PB_ID");

            entity.HasOne(d => d.CpNguoiGiao).WithMany(p => p.Tblcapphattaisans)
                .HasForeignKey(d => d.CpNguoiGiaoId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblcapphattaisan_ibfk_2");

            entity.HasOne(d => d.Pb).WithMany(p => p.Tblcapphattaisans)
                .HasForeignKey(d => d.PbId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblcapphattaisan_ibfk_1");
        });

        modelBuilder.Entity<Tblchitietcapphat>(entity =>
        {
            entity.HasKey(e => e.CtcpId).HasName("PRIMARY");

            entity.ToTable("tblchitietcapphat");

            entity.HasIndex(e => new { e.CpId, e.TsId }, "CP_ID").IsUnique();

            entity.HasIndex(e => e.TsId, "TS_ID");

            entity.Property(e => e.CtcpId)
                .HasColumnType("int(11)")
                .HasColumnName("CTCP_ID");
            entity.Property(e => e.CpId)
                .HasColumnType("int(11)")
                .HasColumnName("CP_ID");
            entity.Property(e => e.CtcpGhiChu)
                .HasColumnType("text")
                .HasColumnName("CTCP_GhiChu");
            entity.Property(e => e.CtcpNgayThuHoi).HasColumnName("CTCP_NgayThuHoi");
            entity.Property(e => e.CtcpTinhTrangKhiGiao)
                .HasMaxLength(100)
                .HasColumnName("CTCP_TinhTrangKhiGiao");
            entity.Property(e => e.CtcpTinhTrangKhiThuHoi)
                .HasMaxLength(100)
                .HasColumnName("CTCP_TinhTrangKhiThuHoi");
            entity.Property(e => e.TsId)
                .HasColumnType("int(11)")
                .HasColumnName("TS_ID");

            entity.HasOne(d => d.Cp).WithMany(p => p.Tblchitietcapphats)
                .HasForeignKey(d => d.CpId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblchitietcapphat_ibfk_1");

            entity.HasOne(d => d.Ts).WithMany(p => p.Tblchitietcapphats)
                .HasForeignKey(d => d.TsId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblchitietcapphat_ibfk_2");
        });

        modelBuilder.Entity<Tblchitietkiemke>(entity =>
        {
            entity.HasKey(e => e.CtkkId).HasName("PRIMARY");

            entity.ToTable("tblchitietkiemke");

            entity.HasIndex(e => new { e.KkId, e.TsId }, "KK_ID").IsUnique();

            entity.HasIndex(e => e.TsId, "TS_ID");

            entity.Property(e => e.CtkkId)
                .HasColumnType("int(11)")
                .HasColumnName("CTKK_ID");
            entity.Property(e => e.CtkkGhiChu)
                .HasColumnType("text")
                .HasColumnName("CTKK_GhiChu");
            entity.Property(e => e.CtkkKetQua)
                .HasMaxLength(50)
                .HasDefaultValueSql("'BinhThuong'")
                .HasColumnName("CTKK_KetQua");
            entity.Property(e => e.CtkkTinhTrangThucTe)
                .HasMaxLength(100)
                .HasColumnName("CTKK_TinhTrangThucTe");
            entity.Property(e => e.CtkkViTriThucTe)
                .HasMaxLength(255)
                .HasColumnName("CTKK_ViTriThucTe");
            entity.Property(e => e.KkId)
                .HasColumnType("int(11)")
                .HasColumnName("KK_ID");
            entity.Property(e => e.TsId)
                .HasColumnType("int(11)")
                .HasColumnName("TS_ID");

            entity.HasOne(d => d.Kk).WithMany(p => p.Tblchitietkiemkes)
                .HasForeignKey(d => d.KkId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblchitietkiemke_ibfk_1");

            entity.HasOne(d => d.Ts).WithMany(p => p.Tblchitietkiemkes)
                .HasForeignKey(d => d.TsId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblchitietkiemke_ibfk_2");
        });

        modelBuilder.Entity<Tblchitietthanhly>(entity =>
        {
            entity.HasKey(e => e.CttlId).HasName("PRIMARY");

            entity.ToTable("tblchitietthanhly");

            entity.HasIndex(e => new { e.TlId, e.TsId }, "TL_ID").IsUnique();

            entity.HasIndex(e => e.TsId, "TS_ID");

            entity.Property(e => e.CttlId)
                .HasColumnType("int(11)")
                .HasColumnName("CTTL_ID");
            entity.Property(e => e.CttlGhiChu)
                .HasColumnType("text")
                .HasColumnName("CTTL_GhiChu");
            entity.Property(e => e.CttlGiaTriThanhLy)
                .HasPrecision(15, 2)
                .HasColumnName("CTTL_GiaTriThanhLy");
            entity.Property(e => e.CttlTinhTrang)
                .HasMaxLength(100)
                .HasColumnName("CTTL_TinhTrang");
            entity.Property(e => e.TlId)
                .HasColumnType("int(11)")
                .HasColumnName("TL_ID");
            entity.Property(e => e.TsId)
                .HasColumnType("int(11)")
                .HasColumnName("TS_ID");

            entity.HasOne(d => d.Tl).WithMany(p => p.Tblchitietthanhlies)
                .HasForeignKey(d => d.TlId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblchitietthanhly_ibfk_1");

            entity.HasOne(d => d.Ts).WithMany(p => p.Tblchitietthanhlies)
                .HasForeignKey(d => d.TsId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblchitietthanhly_ibfk_2");
        });

        modelBuilder.Entity<Tbldanhmuctaisan>(entity =>
        {
            entity.HasKey(e => e.DmtsId).HasName("PRIMARY");

            entity.ToTable("tbldanhmuctaisan");

            entity.HasIndex(e => e.DmtsMaDanhMuc, "DMTS_MaDanhMuc").IsUnique();

            entity.Property(e => e.DmtsId)
                .HasColumnType("int(11)")
                .HasColumnName("DMTS_ID");
            entity.Property(e => e.DmtsMaDanhMuc)
                .HasMaxLength(30)
                .HasColumnName("DMTS_MaDanhMuc");
            entity.Property(e => e.DmtsMoTa)
                .HasColumnType("text")
                .HasColumnName("DMTS_MoTa");
            entity.Property(e => e.DmtsNgayTao)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("DMTS_NgayTao");
            entity.Property(e => e.DmtsTenDanhMuc)
                .HasMaxLength(100)
                .HasColumnName("DMTS_TenDanhMuc");
            entity.Property(e => e.DmtsTrangThai)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("DMTS_TrangThai");
        });

        modelBuilder.Entity<Tblkiemketaisan>(entity =>
        {
            entity.HasKey(e => e.KkId).HasName("PRIMARY");

            entity.ToTable("tblkiemketaisan");

            entity.HasIndex(e => e.KkMaPhieu, "KK_MaPhieu").IsUnique();

            entity.HasIndex(e => e.KkNguoiKiemKeId, "KK_NguoiKiemKeID");

            entity.HasIndex(e => e.PbId, "PB_ID");

            entity.Property(e => e.KkId)
                .HasColumnType("int(11)")
                .HasColumnName("KK_ID");
            entity.Property(e => e.KkGhiChu)
                .HasColumnType("text")
                .HasColumnName("KK_GhiChu");
            entity.Property(e => e.KkMaPhieu)
                .HasMaxLength(30)
                .HasColumnName("KK_MaPhieu");
            entity.Property(e => e.KkNgayKiemKe).HasColumnName("KK_NgayKiemKe");
            entity.Property(e => e.KkNguoiKiemKeId)
                .HasColumnType("int(11)")
                .HasColumnName("KK_NguoiKiemKeID");
            entity.Property(e => e.KkTrangThai)
                .HasMaxLength(50)
                .HasDefaultValueSql("'DangKiemKe'")
                .HasColumnName("KK_TrangThai");
            entity.Property(e => e.PbId)
                .HasColumnType("int(11)")
                .HasColumnName("PB_ID");

            entity.HasOne(d => d.KkNguoiKiemKe).WithMany(p => p.Tblkiemketaisans)
                .HasForeignKey(d => d.KkNguoiKiemKeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblkiemketaisan_ibfk_2");

            entity.HasOne(d => d.Pb).WithMany(p => p.Tblkiemketaisans)
                .HasForeignKey(d => d.PbId)
                .HasConstraintName("tblkiemketaisan_ibfk_1");
        });

        modelBuilder.Entity<Tblmenu>(entity =>
        {
            entity.HasKey(e => e.MenuId).HasName("PRIMARY");

            entity.ToTable("tblmenu");

            entity.Property(e => e.MenuId)
                .HasColumnType("int(11)")
                .HasColumnName("MenuID");
            entity.Property(e => e.ActionName).HasMaxLength(50);
            entity.Property(e => e.ControllerName).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasColumnType("bit(1)");
            entity.Property(e => e.Levels).HasColumnType("int(11)");
            entity.Property(e => e.Link).HasMaxLength(50);
            entity.Property(e => e.MenuName).HasMaxLength(50);
            entity.Property(e => e.MenuOrder).HasColumnType("int(11)");
            entity.Property(e => e.ParentId)
                .HasColumnType("int(11)")
                .HasColumnName("ParentID");
            entity.Property(e => e.Position).HasColumnType("int(11)");
        });

        modelBuilder.Entity<Tblphongban>(entity =>
        {
            entity.HasKey(e => e.PbId).HasName("PRIMARY");

            entity.ToTable("tblphongban");

            entity.HasIndex(e => e.PbMaPhongBan, "PB_MaPhongBan").IsUnique();

            entity.Property(e => e.PbId)
                .HasColumnType("int(11)")
                .HasColumnName("PB_ID");
            entity.Property(e => e.PbDiaChi)
                .HasMaxLength(255)
                .HasColumnName("PB_DiaChi");
            entity.Property(e => e.PbMaPhongBan)
                .HasMaxLength(30)
                .HasColumnName("PB_MaPhongBan");
            entity.Property(e => e.PbNgayTao)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("PB_NgayTao");
            entity.Property(e => e.PbNguoiPhuTrach)
                .HasMaxLength(100)
                .HasColumnName("PB_NguoiPhuTrach");
            entity.Property(e => e.PbSoDienThoai)
                .HasMaxLength(15)
                .HasColumnName("PB_SoDienThoai");
            entity.Property(e => e.PbTenPhongBan)
                .HasMaxLength(150)
                .HasColumnName("PB_TenPhongBan");
            entity.Property(e => e.PbTrangThai)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("PB_TrangThai");
        });

        modelBuilder.Entity<Tbltaikhoan>(entity =>
        {
            entity.HasKey(e => e.TkId).HasName("PRIMARY");

            entity
                .ToTable("tbltaikhoan")
                .UseCollation("utf8mb4_unicode_ci");

            entity.HasIndex(e => e.TkTenDangNhap, "TK_TenDangNhap").IsUnique();

            entity.Property(e => e.TkId)
                .HasColumnType("int(11)")
                .HasColumnName("TK_ID");
            entity.Property(e => e.TkEmail)
                .HasMaxLength(255)
                .HasColumnName("TK_Email");
            entity.Property(e => e.TkHoTen)
                .HasMaxLength(255)
                .HasColumnName("TK_HoTen");
            entity.Property(e => e.TkMatKhau)
                .HasMaxLength(255)
                .HasColumnName("TK_MatKhau");
            entity.Property(e => e.TkNgayTao)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("TK_NgayTao");
            entity.Property(e => e.TkSoDienThoai)
                .HasMaxLength(255)
                .HasColumnName("TK_SoDienThoai");
            entity.Property(e => e.TkTenDangNhap).HasColumnName("TK_TenDangNhap");
            entity.Property(e => e.TkTrangThai)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("TK_TrangThai");
            entity.Property(e => e.TkVaiTro)
                .HasDefaultValueSql("'NhanVien'")
                .HasColumnType("enum('Admin','NhanVien')")
                .HasColumnName("TK_VaiTro");
        });

        modelBuilder.Entity<Tbltaisan>(entity =>
        {
            entity.HasKey(e => e.TsId).HasName("PRIMARY");

            entity.ToTable("tbltaisan");

            entity.HasIndex(e => e.DmtsId, "DMTS_ID");

            entity.HasIndex(e => e.TsMaTaiSan, "TS_MaTaiSan").IsUnique();

            entity.Property(e => e.TsId)
                .HasColumnType("int(11)")
                .HasColumnName("TS_ID");
            entity.Property(e => e.DmtsId)
                .HasColumnType("int(11)")
                .HasColumnName("DMTS_ID");
            entity.Property(e => e.TsGhiChu)
                .HasColumnType("text")
                .HasColumnName("TS_GhiChu");
            entity.Property(e => e.TsHanBaoHanh).HasColumnName("TS_HanBaoHanh");
            entity.Property(e => e.TsMaTaiSan)
                .HasMaxLength(30)
                .HasColumnName("TS_MaTaiSan");
            entity.Property(e => e.TsMoTa)
                .HasColumnType("text")
                .HasColumnName("TS_MoTa");
            entity.Property(e => e.TsNgayMua).HasColumnName("TS_NgayMua");
            entity.Property(e => e.TsNgayTao)
                .HasDefaultValueSql("current_timestamp()")
                .HasColumnType("datetime")
                .HasColumnName("TS_NgayTao");
            entity.Property(e => e.TsNguyenGia)
                .HasPrecision(15, 2)
                .HasColumnName("TS_NguyenGia");
            entity.Property(e => e.TsTenTaiSan)
                .HasMaxLength(150)
                .HasColumnName("TS_TenTaiSan");
            entity.Property(e => e.TsTinhTrang)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Tot'")
                .HasColumnName("TS_TinhTrang");
            entity.Property(e => e.TsTrangThai)
                .HasMaxLength(50)
                .HasDefaultValueSql("'ChuaCapPhat'")
                .HasColumnName("TS_TrangThai");

            entity.HasOne(d => d.Dmts).WithMany(p => p.Tbltaisans)
                .HasForeignKey(d => d.DmtsId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tbltaisan_ibfk_1");
        });

        modelBuilder.Entity<Tblthanhlytaisan>(entity =>
        {
            entity.HasKey(e => e.TlId).HasName("PRIMARY");

            entity.ToTable("tblthanhlytaisan");

            entity.HasIndex(e => e.TlMaPhieu, "TL_MaPhieu").IsUnique();

            entity.HasIndex(e => e.TlNguoiLapId, "TL_NguoiLapID");

            entity.Property(e => e.TlId)
                .HasColumnType("int(11)")
                .HasColumnName("TL_ID");
            entity.Property(e => e.TlGhiChu)
                .HasColumnType("text")
                .HasColumnName("TL_GhiChu");
            entity.Property(e => e.TlLyDo)
                .HasColumnType("text")
                .HasColumnName("TL_LyDo");
            entity.Property(e => e.TlMaPhieu)
                .HasMaxLength(30)
                .HasColumnName("TL_MaPhieu");
            entity.Property(e => e.TlNgayThanhLy).HasColumnName("TL_NgayThanhLy");
            entity.Property(e => e.TlNguoiLapId)
                .HasColumnType("int(11)")
                .HasColumnName("TL_NguoiLapID");
            entity.Property(e => e.TlPhuongThuc)
                .HasMaxLength(100)
                .HasColumnName("TL_PhuongThuc");
            entity.Property(e => e.TlTongGiaTri)
                .HasPrecision(15, 2)
                .HasColumnName("TL_TongGiaTri");
            entity.Property(e => e.TlTrangThai)
                .HasMaxLength(50)
                .HasDefaultValueSql("'ChoDuyet'")
                .HasColumnName("TL_TrangThai");

            entity.HasOne(d => d.TlNguoiLap).WithMany(p => p.Tblthanhlytaisans)
                .HasForeignKey(d => d.TlNguoiLapId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblthanhlytaisan_ibfk_1");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
