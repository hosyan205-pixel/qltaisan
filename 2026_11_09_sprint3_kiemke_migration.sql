-- ============================================================
-- DU AN QUAN LY TAI SAN - SPRINT 3: NANG CAP NGHIEP VU KIEM KE
-- Co so: quanlytaisan (MariaDB 10.4.32 / XAMPP)
-- Nguon: ban dump ngay 08/11/2026 cua nhom
-- PHAM VI: 2 bang kiem ke hien co + 1 bang su kien kiem ke
-- KHONG thay doi bang cap phat, tai san, bao tri, thanh ly.
-- CHAY DUY NHAT 1 LAN tren moi database, SAU KHI SAO LUU.
-- Khong chay lai neu da chay. DDL cua MariaDB auto-commit.
-- Khong import file nay thay the database; day la migration bo sung.
-- Dieu kien: database da duoc nang cap Sprint 2; ket noi dung localhost:3307.
-- Nhom: dung ghi -> Export SQL (structure + data + triggers) trong phpMyAdmin
-- -> import backup vao database tam de kiem tra phuc hoi -> doi chieu du lieu.
-- Chay migration tren ban sao truoc: chi thay dong USE trong noi dung SQL
-- dang chay thanh ten database tam; KHONG de USE quanlytaisan khi thu tren ban sao.
-- Khi duoc phep cap nhat DB chinh: backup moi ngay truoc khi chay, chon
-- QuanLyTaiSan trong phpMyAdmin tai cong 3307, Import CHINH file nay 1 lan.
-- Neu ALTER loi: dung; DDL da auto-commit. Danh gia DB hien tai va phuc hoi
-- backup da thu khoi phuc khi ung dung van dung ghi; khong chi ROLLBACK.
-- Cac bo sung so voi ban SQL goc: KK_DotGocID (dot bo sung), dau vet nguon,
-- canh bao nguon, bang chung, co can xac minh va xac nhan can cu tren chi tiet.
-- ============================================================

USE `quanlytaisan`;

SELECT DATABASE() AS DatabaseDangCapNhat, @@port AS Cong, VERSION() AS PhienBan;

-- 1) Bo sung phieu/dot kiem ke. Cot moi NULL de giu lai du lieu cu.
ALTER TABLE `tblkiemketaisan`
  MODIFY COLUMN `PB_ID` INT(11) NULL,
  MODIFY COLUMN `KK_TrangThai` VARCHAR(50) NOT NULL DEFAULT 'Nhap',
  ADD COLUMN `KK_TenDot` VARCHAR(200) NULL AFTER `KK_MaPhieu`,
  ADD COLUMN `KK_LoaiPhamVi` VARCHAR(20) NOT NULL DEFAULT 'PhongBan' AFTER `PB_ID`,
  ADD COLUMN `KK_NgayKetThucDuKien` DATE NULL AFTER `KK_NgayKiemKe`,
  ADD COLUMN `KK_NguoiTaoID` INT(11) NULL AFTER `KK_NguoiKiemKeID`,
  ADD COLUMN `KK_NguoiDuyetID` INT(11) NULL AFTER `KK_NguoiTaoID`,
  ADD COLUMN `KK_NgayTaoUTC` DATETIME(6) NULL AFTER `KK_TrangThai`,
  ADD COLUMN `KK_ThoiDiemChotUTC` DATETIME(6) NULL AFTER `KK_NgayTaoUTC`,
  ADD COLUMN `KK_NgayGuiDuyetUTC` DATETIME(6) NULL AFTER `KK_ThoiDiemChotUTC`,
  ADD COLUMN `KK_NgayDuyetUTC` DATETIME(6) NULL AFTER `KK_NgayGuiDuyetUTC`,
  ADD COLUMN `KK_NgayHuyUTC` DATETIME(6) NULL AFTER `KK_NgayDuyetUTC`,
  ADD COLUMN `KK_LyDoTraLai` TEXT NULL AFTER `KK_NgayHuyUTC`,
  ADD COLUMN `KK_LyDoHuy` TEXT NULL AFTER `KK_LyDoTraLai`,
  ADD COLUMN `KK_KetLuan` TEXT NULL AFTER `KK_LyDoHuy`,
  ADD COLUMN `KK_RequestKey` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `KK_KetLuan`,
  ADD COLUMN `KK_PhienBan` BIGINT NOT NULL DEFAULT 0 AFTER `KK_RequestKey`,
  ADD COLUMN `KK_LaDuLieuCu` TINYINT(1) NOT NULL DEFAULT 0 AFTER `KK_PhienBan`,
  ADD COLUMN `KK_DotGocID` INT(11) NULL AFTER `KK_LaDuLieuCu`;

-- 2) Bo sung dong chi tiet: snapshot, ket qua kiem dem, doi soat.
-- TS_ID chuyen sang NULL de co the ghi nhan tai san phat hien ngoai so.
-- UNIQUE (KK_ID, TS_ID) da ton tai: van ngan trung tai san trong so;
-- MySQL/MariaDB cho phep nhieu gia tri NULL trong UNIQUE.
ALTER TABLE `tblchitietkiemke`
  MODIFY COLUMN `TS_ID` INT(11) NULL,
  MODIFY COLUMN `CTKK_KetQua` VARCHAR(50) NOT NULL DEFAULT 'ChuaKiem',
  ADD COLUMN `CTKK_LoaiDong` VARCHAR(20) NOT NULL DEFAULT 'TrongSo' AFTER `TS_ID`,
  ADD COLUMN `CTKK_MaPhatHien` VARCHAR(50) NULL AFTER `CTKK_LoaiDong`,
  ADD COLUMN `CTKK_MaTaiSanSnapshot` VARCHAR(30) NULL AFTER `CTKK_MaPhatHien`,
  ADD COLUMN `CTKK_TenTaiSanSnapshot` VARCHAR(150) NULL AFTER `CTKK_MaTaiSanSnapshot`,
  ADD COLUMN `CTKK_DanhMucSnapshot` VARCHAR(255) NULL AFTER `CTKK_TenTaiSanSnapshot`,
  ADD COLUMN `CTKK_NguyenGiaDuKien` DECIMAL(15,2) NULL AFTER `CTKK_DanhMucSnapshot`,
  ADD COLUMN `CTKK_SerialDuKien` VARCHAR(100) NULL AFTER `CTKK_NguyenGiaDuKien`,
  ADD COLUMN `CTKK_PBDuKienID` INT(11) NULL AFTER `CTKK_SerialDuKien`,
  ADD COLUMN `CTKK_PhongBanDuKienSnapshot` VARCHAR(150) NULL AFTER `CTKK_PBDuKienID`,
  ADD COLUMN `CTKK_NguoiNhanDuKienID` INT(11) NULL AFTER `CTKK_PhongBanDuKienSnapshot`,
  ADD COLUMN `CTKK_NguoiNhanDuKienSnapshot` VARCHAR(255) NULL AFTER `CTKK_NguoiNhanDuKienID`,
  ADD COLUMN `CTKK_ViTriDuKien` VARCHAR(255) NULL AFTER `CTKK_NguoiNhanDuKienSnapshot`,
  ADD COLUMN `CTKK_TinhTrangDuKien` VARCHAR(50) NULL AFTER `CTKK_ViTriDuKien`,
  ADD COLUMN `CTKK_TrangThaiDuKien` VARCHAR(50) NULL AFTER `CTKK_TinhTrangDuKien`,
  ADD COLUMN `CTKK_DaTimThay` TINYINT(1) NULL AFTER `CTKK_TrangThaiDuKien`,
  ADD COLUMN `CTKK_PBThucTeID` INT(11) NULL AFTER `CTKK_ViTriThucTe`,
  ADD COLUMN `CTKK_NguoiSuDungThucTeID` INT(11) NULL AFTER `CTKK_PBThucTeID`,
  ADD COLUMN `CTKK_NguoiSuDungThucTe` VARCHAR(255) NULL AFTER `CTKK_NguoiSuDungThucTeID`,
  ADD COLUMN `CTKK_MaNhanDienThucTe` VARCHAR(100) NULL AFTER `CTKK_NguoiSuDungThucTe`,
  ADD COLUMN `CTKK_TenThucTe` VARCHAR(150) NULL AFTER `CTKK_MaNhanDienThucTe`,
  ADD COLUMN `CTKK_SerialThucTe` VARCHAR(100) NULL AFTER `CTKK_TenThucTe`,
  ADD COLUMN `CTKK_CoSaiPhongBan` TINYINT(1) NOT NULL DEFAULT 0 AFTER `CTKK_SerialThucTe`,
  ADD COLUMN `CTKK_CoSaiViTri` TINYINT(1) NOT NULL DEFAULT 0 AFTER `CTKK_CoSaiPhongBan`,
  ADD COLUMN `CTKK_CoSaiNguoiSuDung` TINYINT(1) NOT NULL DEFAULT 0 AFTER `CTKK_CoSaiViTri`,
  ADD COLUMN `CTKK_CoHuHong` TINYINT(1) NOT NULL DEFAULT 0 AFTER `CTKK_CoSaiNguoiSuDung`,
  ADD COLUMN `CTKK_ThieuPhuKien` TINYINT(1) NOT NULL DEFAULT 0 AFTER `CTKK_CoHuHong`,
  ADD COLUMN `CTKK_LyDoChenhLech` TEXT NULL AFTER `CTKK_GhiChu`,
  ADD COLUMN `CTKK_HuongXuLy` VARCHAR(40) NULL AFTER `CTKK_LyDoChenhLech`,
  ADD COLUMN `CTKK_DeXuatXuLy` TEXT NULL AFTER `CTKK_HuongXuLy`,
  ADD COLUMN `CTKK_TrangThaiDoiSoat` VARCHAR(20) NOT NULL DEFAULT 'ChuaDoiSoat' AFTER `CTKK_DeXuatXuLy`,
  ADD COLUMN `CTKK_NguoiDoiSoatID` INT(11) NULL AFTER `CTKK_TrangThaiDoiSoat`,
  ADD COLUMN `CTKK_ThoiDiemDoiSoatUTC` DATETIME(6) NULL AFTER `CTKK_NguoiDoiSoatID`,
  ADD COLUMN `CTKK_NguoiGhiNhanID` INT(11) NULL AFTER `CTKK_ThoiDiemDoiSoatUTC`,
  ADD COLUMN `CTKK_ThoiDiemGhiNhanUTC` DATETIME(6) NULL AFTER `CTKK_NguoiGhiNhanID`,
  ADD COLUMN `CTKK_TrangThaiXuLy` VARCHAR(20) NOT NULL DEFAULT 'ChuaXuLy' AFTER `CTKK_ThoiDiemGhiNhanUTC`,
  ADD COLUMN `CTKK_KetQuaXuLy` TEXT NULL AFTER `CTKK_TrangThaiXuLy`,
  ADD COLUMN `CTKK_ThamChieuXuLy` VARCHAR(100) NULL AFTER `CTKK_KetQuaXuLy`,
  ADD COLUMN `CTKK_NguoiXuLyID` INT(11) NULL AFTER `CTKK_ThamChieuXuLy`,
  ADD COLUMN `CTKK_ThoiDiemXuLyUTC` DATETIME(6) NULL AFTER `CTKK_NguoiXuLyID`,
  ADD COLUMN `CTKK_PhienBan` BIGINT NOT NULL DEFAULT 0 AFTER `CTKK_ThoiDiemXuLyUTC`,
  ADD COLUMN `CTKK_CanhBaoNguon` TEXT NULL,
  ADD COLUMN `CTKK_DauVetNguon` CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NULL,
  ADD COLUMN `CTKK_BangChung` TEXT NULL,
  ADD COLUMN `CTKK_CanXacMinh` TINYINT(1) NOT NULL DEFAULT 0,
  ADD COLUMN `CTKK_XacNhanCanCu` TINYINT(1) NOT NULL DEFAULT 0;

-- 3) Gan nhan du lieu lich su co san.
-- KHONG tu tao snapshot nguoc tu tbltaisan hien tai vi se sai lich su.
-- KHONG doi cac CTKK_KetQua cu (BinhThuong, CanXuLy).
UPDATE `tblkiemketaisan`
SET `KK_LaDuLieuCu` = 1,
    `KK_TenDot` = COALESCE(`KK_TenDot`, CONCAT('Kiem ke lich su ', `KK_MaPhieu`));

-- 4) Danh muc tra cuu, chong trung va khoa ngoai.
ALTER TABLE `tblkiemketaisan`
  ADD UNIQUE KEY `UX_KK_RequestKey` (`KK_RequestKey`),
  ADD KEY `IX_KK_TrangThai_Ngay` (`KK_TrangThai`, `KK_NgayKiemKe`, `KK_ID`),
  ADD KEY `IX_KK_PhamVi` (`KK_LoaiPhamVi`, `PB_ID`, `KK_TrangThai`),
  ADD KEY `IX_KK_NguoiTao` (`KK_NguoiTaoID`),
  ADD KEY `IX_KK_NguoiDuyet` (`KK_NguoiDuyetID`),
  ADD KEY `IX_KK_DotGoc` (`KK_DotGocID`),
  ADD CONSTRAINT `FK_KK_DotGoc` FOREIGN KEY (`KK_DotGocID`) REFERENCES `tblkiemketaisan` (`KK_ID`),
  ADD CONSTRAINT `FK_KK_NguoiTao` FOREIGN KEY (`KK_NguoiTaoID`) REFERENCES `tbltaikhoan` (`TK_ID`),
  ADD CONSTRAINT `FK_KK_NguoiDuyet` FOREIGN KEY (`KK_NguoiDuyetID`) REFERENCES `tbltaikhoan` (`TK_ID`),
  ADD CONSTRAINT `CK_KK_PhamVi` CHECK
    (`KK_LoaiPhamVi` IN ('ToanBo', 'PhongBan')),
  ADD CONSTRAINT `CK_KK_PB` CHECK
    ((`KK_LoaiPhamVi` = 'ToanBo' AND `PB_ID` IS NULL)
      OR (`KK_LoaiPhamVi` = 'PhongBan' AND `PB_ID` IS NOT NULL));

ALTER TABLE `tblchitietkiemke`
  ADD UNIQUE KEY `UX_CTKK_MaPhatHien` (`KK_ID`, `CTKK_MaPhatHien`),
  ADD KEY `IX_CTKK_KetQua` (`KK_ID`, `CTKK_KetQua`),
  ADD KEY `IX_CTKK_DoiSoat` (`KK_ID`, `CTKK_TrangThaiDoiSoat`),
  ADD KEY `IX_CTKK_XuLy` (`KK_ID`, `CTKK_TrangThaiXuLy`),
  ADD KEY `IX_CTKK_PBDuKien` (`CTKK_PBDuKienID`),
  ADD KEY `IX_CTKK_PBThucTe` (`CTKK_PBThucTeID`),
  ADD KEY `IX_CTKK_NguoiNhan` (`CTKK_NguoiNhanDuKienID`),
  ADD KEY `IX_CTKK_NguoiDung` (`CTKK_NguoiSuDungThucTeID`),
  ADD KEY `IX_CTKK_NguoiGhiNhan` (`CTKK_NguoiGhiNhanID`),
  ADD KEY `IX_CTKK_NguoiDoiSoat` (`CTKK_NguoiDoiSoatID`),
  ADD KEY `IX_CTKK_NguoiXuLy` (`CTKK_NguoiXuLyID`),
  ADD CONSTRAINT `FK_CTKK_PBDuKien` FOREIGN KEY (`CTKK_PBDuKienID`) REFERENCES `tblphongban` (`PB_ID`),
  ADD CONSTRAINT `FK_CTKK_PBThucTe` FOREIGN KEY (`CTKK_PBThucTeID`) REFERENCES `tblphongban` (`PB_ID`),
  ADD CONSTRAINT `FK_CTKK_NguoiNhan` FOREIGN KEY (`CTKK_NguoiNhanDuKienID`) REFERENCES `tbltaikhoan` (`TK_ID`),
  ADD CONSTRAINT `FK_CTKK_NguoiDung` FOREIGN KEY (`CTKK_NguoiSuDungThucTeID`) REFERENCES `tbltaikhoan` (`TK_ID`),
  ADD CONSTRAINT `FK_CTKK_NguoiGhiNhan` FOREIGN KEY (`CTKK_NguoiGhiNhanID`) REFERENCES `tbltaikhoan` (`TK_ID`),
  ADD CONSTRAINT `FK_CTKK_NguoiDoiSoat` FOREIGN KEY (`CTKK_NguoiDoiSoatID`) REFERENCES `tbltaikhoan` (`TK_ID`),
  ADD CONSTRAINT `FK_CTKK_NguoiXuLy` FOREIGN KEY (`CTKK_NguoiXuLyID`) REFERENCES `tbltaikhoan` (`TK_ID`),
  ADD CONSTRAINT `CK_CTKK_LoaiDong` CHECK
    ((`CTKK_LoaiDong` IN ('TrongSo', 'NgoaiPhamVi') AND `TS_ID` IS NOT NULL)
      OR (`CTKK_LoaiDong` = 'NgoaiSo' AND `TS_ID` IS NULL)),
  ADD CONSTRAINT `CK_CTKK_MaPhatHien` CHECK
    (`CTKK_LoaiDong` <> 'NgoaiSo' OR `CTKK_MaPhatHien` IS NOT NULL),
  ADD CONSTRAINT `CK_CTKK_DaTimThay` CHECK
    (`CTKK_DaTimThay` IS NULL OR `CTKK_DaTimThay` IN (0, 1));

-- 5) Nhat ky bat bien theo nguyen tac append-only o tang ung dung.
-- Tai khoan khong duoc phep chinh sua/xoa cac ban ghi nhat ky tu UI.
CREATE TABLE `tblsukienkiemke` (
  `SKK_ID` BIGINT NOT NULL AUTO_INCREMENT,
  `KK_ID` INT(11) NOT NULL,
  `CTKK_ID` INT(11) NULL,
  `SKK_HanhDong` VARCHAR(40) NOT NULL,
  `SKK_NguoiThucHienID` INT(11) NOT NULL,
  `SKK_NguoiThucHienSnapshot` VARCHAR(255) NOT NULL,
  `SKK_ThoiDiemUTC` DATETIME(6) NOT NULL,
  `SKK_LyDo` TEXT NULL,
  `SKK_Truoc` LONGTEXT NULL,
  `SKK_Sau` LONGTEXT NULL,
  `SKK_RequestKey` CHAR(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  PRIMARY KEY (`SKK_ID`),
  UNIQUE KEY `UX_SKK_RequestKey` (`SKK_RequestKey`),
  KEY `IX_SKK_KK_ThoiDiem` (`KK_ID`, `SKK_ThoiDiemUTC`, `SKK_ID`),
  KEY `IX_SKK_CTKK_ThoiDiem` (`CTKK_ID`, `SKK_ThoiDiemUTC`),
  KEY `IX_SKK_NguoiThucHien` (`SKK_NguoiThucHienID`),
  CONSTRAINT `FK_SKK_KK` FOREIGN KEY (`KK_ID`) REFERENCES `tblkiemketaisan` (`KK_ID`),
  CONSTRAINT `FK_SKK_CTKK` FOREIGN KEY (`CTKK_ID`) REFERENCES `tblchitietkiemke` (`CTKK_ID`),
  CONSTRAINT `FK_SKK_NguoiThucHien` FOREIGN KEY (`SKK_NguoiThucHienID`) REFERENCES `tbltaikhoan` (`TK_ID`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- Khoa lich su: khong cho UPDATE/DELETE su kien da ghi, ke ca tu SQL thong thuong.
-- Tai khoan DBA van co quyen can thiep DDL nen can phan quyen database phu hop.
DELIMITER $$
CREATE TRIGGER `kiemke_audit_no_update`
BEFORE UPDATE ON `tblsukienkiemke`
FOR EACH ROW
BEGIN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Inventory audit is append-only';
END$$

CREATE TRIGGER `kiemke_audit_no_delete`
BEFORE DELETE ON `tblsukienkiemke`
FOR EACH ROW
BEGIN
  SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Inventory audit cannot be deleted';
END$$
DELIMITER ;

-- 6) Kiem tra sau migration: khong chinh sua du lieu.
SELECT COUNT(*) AS SoDotKiemKe, SUM(`KK_LaDuLieuCu`) AS SoDotLichSu
FROM `tblkiemketaisan`;
SELECT COUNT(*) AS SoChiTietKiemKe FROM `tblchitietkiemke`;
SHOW COLUMNS FROM `tblkiemketaisan`;
SHOW COLUMNS FROM `tblchitietkiemke`;
SHOW TABLES LIKE 'tblsukienkiemke';
SELECT COUNT(*) AS SoBang FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE();
SELECT COUNT(*) AS SoKhoaNgoai FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE();
SELECT COUNT(*) AS SoTrigger FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE();
-- Tren baseline da doi chieu cua du an: 19 bang, 45 FK, 9 trigger (7 cu + 2 audit).
-- 24 cot tblkiemketaisan, 52 cot tblchitietkiemke, 11 cot tblsukienkiemke.

-- 7) Doi soat canh bao du lieu mau; KHONG tu dong thay doi.
SELECT a.`TS_ID`, a.`TS_MaTaiSan`, a.`TS_TrangThai`,
       d.`CTCP_TrangThai`, c.`CP_MaPhieu`, c.`PB_ID`
FROM `tblchitietcapphat` d
JOIN `tblcapphattaisan` c ON c.`CP_ID` = d.`CP_ID`
JOIN `tbltaisan` a ON a.`TS_ID` = d.`TS_ID`
WHERE d.`CTCP_TrangThai` = 'DangHieuLuc'
  AND a.`TS_TrangThai` <> 'DaCapPhat';

-- QUAN TRONG:
-- * Chi chay file mot lan. Neu loi giua chừng, DUNG; khong chay lai
--   toan bo migration vi ALTER TABLE da commit mot phan.
-- * Neu can rollback, phuc hoi tu backup cua database truoc khi chay.
-- * Du lieu lich su KK_LaDuLieuCu=1 phai hien thi READ-ONLY trong MVC.
-- * Sau khi chay, cap nhat Entity/DbContext tuong ung trong Models,
--   va KHONG Scaffold -Force de ghi de cac chinh sua cua thanh vien khac.
