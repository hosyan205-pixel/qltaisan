using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoAn.Areas.Admin.Controllers;
using DoAn.Areas.Admin.Models;
using DoAn.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace AssetModule.Checks;

internal static partial class Program
{
    private static readonly string[] InventoryTables = ["tblkiemketaisan", "tblchitietkiemke", "tblsukienkiemke"];
    private static int inventoryDoc, inventoryGlobal, inventoryConflict, inventoryEmpty, inactiveInspector;
    private static int[] inventoryAssets = [];
    private static string inventoryEvidence = "";

    private static async Task<int> RunSprint3()
    {
        using var config = JsonDocument.Parse(await File.ReadAllTextAsync("appsettings.json"));
        var source = new MySqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!);
        Assert(source.Server is "localhost" or "127.0.0.1" && source.Port == 3307 && source.Database.Equals("QuanLyTaiSan",StringComparison.OrdinalIgnoreCase), "Expected localhost:3307 primary source, read only");
        var scratch = "qltaisan_sprint2_checks_" + Guid.NewGuid().ToString("N");
        var target = new MySqlConnectionStringBuilder(source.ConnectionString) { Database = scratch };
        var adminOptions = new MySqlConnectionStringBuilder(source.ConnectionString) { Database = "" };
        await using var admin = new MySqlConnection(adminOptions.ConnectionString); await admin.OpenAsync();
        inventoryEvidence = Path.GetFullPath(Path.Combine(".local-backups", "sprint3-stage-" + DateTime.UtcNow.AddHours(7).ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(inventoryEvidence);
        var baseline = await RunNativeDatabaseTool(source, false);
        Assert(!Regex.IsMatch(baseline,@"(?im)^\s*(USE\s|CREATE DATABASE|DROP DATABASE|GRANT\s|SET\s+GLOBAL)"), "Backup must not change database scope");
        await File.WriteAllTextAsync(Path.Combine(inventoryEvidence,"before.sql"),baseline,new UTF8Encoding(false));
        var migration = await File.ReadAllTextAsync("2026_11_09_sprint3_kiemke_migration.sql");
        Assert(Regex.Matches(migration,@"(?im)^USE `quanlytaisan`;\s*$").Count == 1,"One explicit migration target");
        var created = false;
        try
        {
            await using(var command=admin.CreateCommand()){command.CommandText="CREATE DATABASE `"+scratch+"` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci";await command.ExecuteNonQueryAsync();created=true;}
            await RunNativeDatabaseTool(target,true,baseline); stageChecksConnection=target.ConnectionString;
            await StageCase("Backup restored byte-for-byte: original data, schema, FK, indexes and 7 triggers",async()=>Assert(await RunNativeDatabaseTool(target,false)==baseline,"Restored backup mismatch"));
            var before=await InventoryDataHashes(target);
            // Rewrite only the approved USE line in memory. Never import a script that still selects the primary.
            var scoped = Regex.Replace(migration,@"(?im)^USE `quanlytaisan`;\s*$","USE `"+scratch+"`;");
            Assert(!scoped.Contains("USE `quanlytaisan`"),"Primary migration target removed");
            await RunNativeDatabaseTool(target,true,scoped);
            await StageCase("Migration/EF: 19 tables, 45 FK, 9 triggers, preserved legacy fields and navigation",async()=>
            {
                Assert(await Scalar("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()") == 19,"19 tables");
                Assert(await Scalar("SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE()") == 45,"45 FK");
                Assert(await Scalar("SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE()") == 9,"7 original + 2 inventory triggers");
                var after=await InventoryDataHashes(target,before.Keys.ToArray());
                foreach(var table in before.Keys.Where(x=>!InventoryTables.Contains(x))) Assert(before[table]==after[table],"Migration changed "+table);
                await using var db=StageDb();
                Assert(await db.tblKiemKeTaiSan.CountAsync(x=>x.KK_ID<=5&&x.KK_LaDuLieuCu&&x.KK_ThoiDiemChotUTC==null)==5,"No fabricated legacy snapshot");
                Assert(await InventoryLegacyHash(target)==await InventoryLegacyHash(source),"Original inventory column values changed");
                var line=db.Model.FindEntityType(typeof(tblChiTietKiemKe))!;
                Assert(line.FindProperty(nameof(tblChiTietKiemKe.TS_ID))!.IsNullable&&line.FindProperty(nameof(tblChiTietKiemKe.CTKK_PhienBan))!.IsConcurrencyToken,"Nullable FK and version");
                Assert(line.GetNavigations().Any(x=>x.Name=="TaiSan")&&line.GetNavigations().Any(x=>x.Name=="KiemKe"),"Original navigation retained");
                Assert(await db.tblChiTietKiemKe.Include(x=>x.TaiSan).Include(x=>x.KiemKe).Include(x=>x.PhongBanDuKien).CountAsync()==5,"New and old FK mappings query");
                // Persist actual schema for review without introducing a second migration file.
                await File.WriteAllTextAsync(Path.Combine(inventoryEvidence,"schema-after.json"),JsonSerializer.Serialize(await InventorySchema(target),new JsonSerializerOptions{WriteIndented=true}));
            });
            await SeedStagePeople();
            await using(var db=StageDb())
            {
                (await db.tblTaiKhoan.FindAsync(stageActorB))!.TK_VaiTro="QuanLyTaiSan";
                var inactive=new tblTaiKhoan{TK_TenDangNhap="s3_inactive",TK_MatKhau="unused",TK_HoTen="Inactive",TK_TrangThai=false};db.Add(inactive);await db.SaveChangesAsync();inactiveInspector=inactive.TK_ID;
            }
            inventoryAssets=await NewAssets(4);
            await using(var db=StageDb())
            {
                foreach(var a in await db.tblTaiSan.Where(x=>inventoryAssets.Contains(x.TS_ID)).ToListAsync()){a.TS_Serial="S3-SERIAL-"+a.TS_ID;a.TS_ViTri="Phòng A";}
                await db.SaveChangesAsync();
            }
            await GiveAssets(inventoryAssets[..3]);
            var b=await Create(Input([inventoryAssets[3]],stagePersonB,stageDepartmentB));await Submit(b);await Confirm(b,GiaoNhanStates.Give);
            var protectedRows=await InventoryDataHashes(target);
            await InventoryAcceptance();
            await StageCase("BR30: every non-inventory table unchanged by all inventory acceptance writes",async()=>
            {
                var actual=await InventoryDataHashes(target,protectedRows.Keys.ToArray());
                foreach(var table in protectedRows.Keys.Where(x=>!InventoryTables.Contains(x))) Assert(actual[table]==protectedRows[table],"Unexpected write to "+table);
            });
            await InventoryExtendedChecks();
            await InventoryHttpChecks();
            await InventoryAvailabilityHttp(source);
            await StageCase("Sprint 1 regression on upgraded clone: asset CRUD/history deletion guard",Sprint1OnStage);
            await StageCase("Sprint 2 regression on upgraded clone: multi-asset handover, partial return, transfer, repeat confirmation",async()=>
            {
                var assets=await NewAssets(3);var give=await GiveAssets(assets);var lines=await Lines(give);
                var ret=await Create(SourceInput(give,[lines[0]],"Tot"),GiaoNhanStates.Return);await Submit(ret);await Confirm(ret,GiaoNhanStates.Return);
                Assert(await ActiveCount(assets)==2,"Partial return preserved remaining responsibility");
                var move=await Create(SourceInput(give,[lines[1]],recipient:stagePersonB,department:stageDepartmentB),GiaoNhanStates.Transfer);await Submit(move);await Confirm(move,GiaoNhanStates.Transfer);await Confirm(move,GiaoNhanStates.Transfer);
                Assert(await ActiveCount(assets)==2,"Transfer retains one effective responsibility per asset");
            });
            await StageCase("Primary database remains byte-for-byte unchanged after migration and write tests",async()=>Assert(await RunNativeDatabaseTool(source,false)==baseline,"Source changed during verification; investigate external writes"));
            return failed==0?0:1;
        }
        finally
        {
            var report=new{Database=scratch,Source="localhost:3307/QuanLyTaiSan (read only)",BackupSHA256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(baseline))),MigrationSHA256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(migration))),Passed=passed,Failed=failed,Results=stageOutcomes};
            await File.WriteAllTextAsync(Path.Combine(inventoryEvidence,"results.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"SPRINT3 RESULT: {passed} passed, {failed} failed; evidence: {inventoryEvidence}");
            if(created){Assert(Regex.IsMatch(scratch,"^qltaisan_sprint2_checks_[0-9a-f]{32}$"),"Unsafe cleanup target");await using var command=admin.CreateCommand();command.CommandText="DROP DATABASE `"+scratch+"`";await command.ExecuteNonQueryAsync();Console.WriteLine("Removed only disposable Sprint 3 database");}
        }
    }
    private static async Task InventoryAcceptance()
    {
        await StageCase("TC01: concurrent department/global drafts generate unique server codes; repeated create is idempotent",async()=>
        {
            var a=InventoryInput();var b=InventoryInput(global:true);
            var ids=await Task.WhenAll(InventoryCreate(a),InventoryCreate(b));inventoryDoc=ids[0];inventoryGlobal=ids[1];
            Assert(await InventoryCreate(a)==inventoryDoc,"Repeat draft");
            await using var db=StageDb();Assert(await db.tblKiemKeTaiSan.Where(x=>ids.Contains(x.KK_ID)).Select(x=>x.KK_MaPhieu).Distinct().CountAsync()==2,"Unique codes");
        });
        await StageCase("TC02: invalid dates, request code, scope, missing/inactive department or inspector write nothing",async()=>
        {
            await using var db=StageDb();var count=await db.tblKiemKeTaiSan.CountAsync();
            for(var i=0;i<7;i++)
            {
                var input=InventoryInput();switch(i){case 0:input.NgayKiemKe=null;break;case 1:input.RequestKey="bad";break;case 2:input.PhongBanId=int.MaxValue;break;case 3:input.NguoiKiemKeId=inactiveInspector;break;case 4:input.LoaiPhamVi="bad";break;case 5:input.NgayKiemKe=new(1900,1,1);break;case 6:input.TenDot=" ";break;}
                Assert(Code(await InventoryCall(c=>c.Create(input))) is 400 or 404 or 409,"Rejected invalid "+i);
            }
            Assert(await db.tblKiemKeTaiSan.CountAsync()==count,"No invalid draft persisted");
        });
        await StageCase("TC03/04: start atomically snapshots only valid departmental custody, NULL remains unobserved; repeated start adds no events",async()=>
        {
            var input=await InventoryRequest(inventoryDoc);Assert(Code(await InventoryCall(c=>c.BatDau(inventoryDoc,input)))==302,"Start");
            Assert(Code(await InventoryCall(c=>c.BatDau(inventoryDoc,input)))==302,"Idempotent start");
            await using var db=StageDb();var lines=await InventoryLines(inventoryDoc);
            Assert(lines.Select(x=>x.TS_ID!.Value).Order().SequenceEqual(inventoryAssets[..3].Order()),"Correct book scope");
            Assert(lines.All(x=>x.CTKK_DaTimThay==null&&x.CTKK_KetQua=="ChuaKiem"&&x.CTKK_ViTriDuKien=="Phòng A"),"Frozen book and no fabricated observation");
            Assert(await db.tblSuKienKiemKe.CountAsync(x=>x.KK_ID==inventoryDoc&&x.SKK_HanhDong=="BatDau")==1,"Exactly one start event");
        });
        await StageCase("TC05: active departmental overlap and global overlap are blocked; disjoint department allowed",async()=>
        {
            inventoryConflict=await InventoryCreate(InventoryInput());
            Assert(Code(await InventoryCall(c=>c.BatDau(inventoryConflict,InventoryRequestSync(inventoryConflict))))==409,"Same department");
            Assert(Code(await InventoryCall(c=>c.BatDau(inventoryGlobal,InventoryRequestSync(inventoryGlobal))))==409,"Global overlaps department");
            var other=await InventoryCreate(InventoryInput(department:stageDepartmentB));await InventoryStart(other);await InventoryCancel(other);
        });
        await StageCase("TC07: unobserved is not missing; explicit missing becomes Thieu, explicit matched observation becomes Khop",async()=>
        {
            var lines=await InventoryLines(inventoryDoc);
            var missing=new KiemKeGhiNhanInput{RequestKey=Guid.NewGuid().ToString(),PhienBan=lines[0].CTKK_PhienBan,CachGhiNhan="KhongThay",DaKiemTraThucTe=true,GhiChu="Đã kiểm tra phòng và xác nhận với người giữ"};
            Assert(Code(await InventoryCall(c=>c.GhiNhan(lines[0].CTKK_ID,missing),stagePersonA))==302,"Explicit missing");
            Assert((await InventoryLine(lines[0].CTKK_ID)).CTKK_KetQua=="Thieu","Missing classification");
            foreach(var line in lines[..2]) await InventoryObserve(line.CTKK_ID);
            Assert((await InventoryLine(lines[0].CTKK_ID)).CTKK_KetQua=="Khop","Match");
            Assert((await InventoryLine(lines[2].CTKK_ID)).CTKK_KetQua=="ChuaKiem","Untouched line is not missing");
        });
        await StageCase("TC08: five independent flags + multiple discrepancy classification require evidence; whitespace-normalized location matches",async()=>
        {
            var line=(await InventoryLines(inventoryDoc))[2];var input=await InventoryObservation(line.CTKK_ID);
            input.PhongBanId=stageDepartmentB;input.NguoiSuDungId=stagePersonB;input.ViTri="Phòng B";input.CoHuHong=true;input.ThieuPhuKien=true;
            input.GhiChu="Vỏ nứt, thiếu dây nguồn, khác người/phòng giữ";input.BangChung="BB-01 ảnh hiện vật";
            Assert(Code(await InventoryCall(c=>c.GhiNhan(line.CTKK_ID,input),stagePersonA))==302,"Record differences");
            var saved=await InventoryLine(line.CTKK_ID);Assert(saved.CTKK_KetQua=="NhieuSaiLech"&&saved.CTKK_CoSaiPhongBan&&saved.CTKK_CoSaiViTri&&saved.CTKK_CoSaiNguoiSuDung&&saved.CTKK_CoHuHong&&saved.CTKK_ThieuPhuKien,"All 5 flags");
            var matched=(await InventoryLines(inventoryDoc))[0];var normal=await InventoryObservation(matched.CTKK_ID);normal.ViTri=" phòng   A ";
            Assert(Code(await InventoryCall(c=>c.GhiNhan(matched.CTKK_ID,normal),stagePersonA))==302,"Normalized location");Assert(!(await InventoryLine(matched.CTKK_ID)).CTKK_CoSaiViTri,"No false location discrepancy");
        });
        await StageCase("TC09/10: nullable unknown discovery and real outside-scope FK; reject duplicate TS/temp/code/serial and pretending known as unknown",async()=>
        {
            var unknown=await InventoryDiscovery();Assert(Code(await InventoryCall(c=>c.PhatHienNgoaiDanhSach(inventoryDoc,unknown),stagePersonA))==302,"Unknown discovery");
            Assert(Code(await InventoryCall(c=>c.PhatHienNgoaiDanhSach(inventoryDoc,unknown),stagePersonA))==302,"Repeat discovery");
            var known=await InventoryDiscovery(inventoryAssets[3]);Assert(Code(await InventoryCall(c=>c.PhatHienNgoaiDanhSach(inventoryDoc,known),stagePersonA))==302,"Known discovery");
            var lines=await InventoryLines(inventoryDoc);
            Assert(lines.Single(x=>x.CTKK_LoaiDong=="NgoaiSo").TS_ID==null&&lines.Single(x=>x.CTKK_LoaiDong=="NgoaiSo").CTKK_KetQua=="Thua","NULL FK outside book");
            Assert(lines.Single(x=>x.CTKK_LoaiDong=="NgoaiPhamVi").TS_ID==inventoryAssets[3],"Correct real FK");
            var duplicate=await InventoryDiscovery(inventoryAssets[3]);Assert(Code(await InventoryCall(c=>c.PhatHienNgoaiDanhSach(inventoryDoc,duplicate),stagePersonA))==409,"Duplicate real TS");
            duplicate=await InventoryDiscovery();Assert(Code(await InventoryCall(c=>c.PhatHienNgoaiDanhSach(inventoryDoc,duplicate),stagePersonA))==409,"Duplicate observed serial/temp");
            duplicate=await InventoryDiscovery();duplicate.MaPhatHien="OTHER";duplicate.MaNhanDien=(await InventoryLine(lines[0].CTKK_ID)).CTKK_MaTaiSanSnapshot;
            Assert(Code(await InventoryCall(c=>c.PhatHienNgoaiDanhSach(inventoryDoc,duplicate),stagePersonA))==409,"Known cannot masquerade as unknown");
        });
        await StageCase("TC11 / BR22: block unfinished submission, recheck keeps before/after audit without duplicating lines",async()=>
        {
            Assert(Code(await InventoryCall(c=>c.GuiDuyet(inventoryDoc,InventoryRequestSync(inventoryDoc)),stagePersonA))==409,"Unreconciled differences block");
            var line=(await InventoryLines(inventoryDoc))[0];var check=await InventoryObservation(line.CTKK_ID);check.CachGhiNhan="CanXacMinh";check.GhiChu="Chờ xác minh giấy tờ";
            Assert(Code(await InventoryCall(c=>c.GhiNhan(line.CTKK_ID,check),stagePersonA))==302,"Mark unclear");
            var request=await InventoryRequest(line.CTKK_ID,true);request.LyDo="Kiểm kê lại khi nhận đủ giấy tờ";
            Assert(Code(await InventoryCall(c=>c.KiemKeLai(line.CTKK_ID,request),stagePersonA))==302,"Recheck");
            Assert((await InventoryLine(line.CTKK_ID)).CTKK_DaTimThay==null,"Recheck unobserved");await InventoryObserve(line.CTKK_ID);
            await using var db=StageDb();Assert(await db.tblSuKienKiemKe.AnyAsync(x=>x.CTKK_ID==line.CTKK_ID&&x.SKK_HanhDong=="KiemKeLai"&&x.SKK_Truoc!=null&&x.SKK_Sau!=null),"Recheck audit preserved");
            Assert(await db.tblChiTietKiemKe.CountAsync(x=>x.KK_ID==inventoryDoc)==5,"No duplicate line");
            foreach(var diff in (await InventoryLines(inventoryDoc)).Where(x=>KiemKeUi.IsDifference(x.CTKK_KetQua))) await InventoryReconcile(diff.CTKK_ID);
            Assert(Code(await InventoryCall(c=>c.GuiDuyet(inventoryDoc,InventoryRequestSync(inventoryDoc)),stagePersonA))==302,"Ready submit");
        });
        await StageCase("TC12/13: direct inspector cannot approve; return/resubmit preserves snapshot and complete audit",async()=>
        {
            Assert(Code(await InventoryCall(c=>c.Duyet(inventoryDoc,InventoryRequestSync(inventoryDoc)),stagePersonA))==403,"Employee approval denied");
            var snapshots=InventorySnapshot(await InventoryLines(inventoryDoc));
            var request=await InventoryRequest(inventoryDoc);request.LyDo="Cần bổ sung căn cứ BB-02";
            Assert(Code(await InventoryCall(c=>c.TraLai(inventoryDoc,request),stageActorB))==302,"Manager return");
            var first=(await InventoryLines(inventoryDoc))[0];await InventoryObserve(first.CTKK_ID);
            Assert(Code(await InventoryCall(c=>c.GuiDuyet(inventoryDoc,InventoryRequestSync(inventoryDoc)),stagePersonA))==302,"Resubmit");
            request=await InventoryRequest(inventoryDoc);request.LyDo="Đã kiểm tra đầy đủ chênh lệch và đối soát độc lập";
            Assert(Code(await InventoryCall(c=>c.Duyet(inventoryDoc,request)))==302,"Other manager approval");
            Assert(InventorySnapshot(await InventoryLines(inventoryDoc))==snapshots,"Snapshot unchanged after return and approval");
        });
        await StageCase("TC14: completed/cancelled immutable; postprocessing changes only allowed six fields and requires real asset reference",async()=>
        {
            var lines=await InventoryLines(inventoryDoc);Assert(Code(await InventoryCall(c=>c.GhiNhan(lines[0].CTKK_ID,InventoryObservationSync(lines[0].CTKK_ID)),stagePersonA))==409,"Complete observation frozen");
            Assert(Code(await InventoryCall(c=>c.Huy(inventoryDoc,InventoryRequestSync(inventoryDoc))))==409,"Complete cancellation denied");
            var diff=lines.First(x=>x.CTKK_KetQua=="NhieuSaiLech");var before=InventoryOriginalResult(diff);
            var input=new KiemKeXuLyInput{RequestKey=Guid.NewGuid().ToString(),PhienBan=diff.CTKK_PhienBan,TrangThai="DaXuLy",KetQua="Đã sửa hồ sơ ở phân hệ tài sản",ThamChieu="TS:missing"};
            Assert(Code(await InventoryCall(c=>c.CapNhatXuLy(diff.CTKK_ID,input)))==409,"Fake reference denied");
            input.RequestKey=Guid.NewGuid().ToString();input.ThamChieu="TS:"+diff.CTKK_MaTaiSanSnapshot;
            Assert(Code(await InventoryCall(c=>c.CapNhatXuLy(diff.CTKK_ID,input)))==409,"Existing uncorrected master alone is not completed processing");
            await using(var db=StageDb()){input.ThamChieu="CP:"+await db.tblChiTietCapPhat.Where(x=>x.TS_ID==diff.TS_ID&&x.CTCP_TrangThai=="DangHieuLuc").Select(x=>x.CapPhat!.CP_MaPhieu).SingleAsync();}
            Assert(Code(await InventoryCall(c=>c.CapNhatXuLy(diff.CTKK_ID,input)))==302,"Real reference accepted");
            var after=await InventoryLine(diff.CTKK_ID);Assert(InventoryOriginalResult(after)==before&&after.CTKK_TrangThaiXuLy=="DaXuLy"&&after.CTKK_PhienBan==diff.CTKK_PhienBan+1,"Only postprocessing whitelist changed");
            await InventoryCancel(inventoryConflict);Assert(Code(await InventoryCall(c=>c.BatDau(inventoryConflict,InventoryRequestSync(inventoryConflict))))==409,"Cancelled cannot reopen");
        });
        await StageCase("TC15: backend assignment scope and fresh active role checks defeat forged Admin session",async()=>
        {
            Assert(Code(await InventoryCall(c=>c.Create(InventoryInput()),stagePersonA))==403,"Employee create forbidden");
            Assert(await InventoryCall(c=>c.Details(inventoryDoc),stagePersonB) is NotFoundResult,"Other employee read denied");
            var line=(await InventoryLines(inventoryDoc))[0];Assert(await InventoryCall(c=>c.GhiNhan(line.CTKK_ID),stagePersonB) is NotFoundResult,"Other employee line read denied");
            Assert(Code(await InventoryCall(c=>c.GuiDuyet(inventoryDoc,InventoryRequestSync(inventoryDoc)),inactiveInspector))==403,"Inactive denied despite forged session role");
        });
        await StageCase("TC16: simultaneous updates with same line version have exactly one winner",async()=>
        {
            var id=await InventoryCreate(InventoryInput());await InventoryStart(id);var line=(await InventoryLines(id))[0];
            var a=await InventoryObservation(line.CTKK_ID);var b=await InventoryObservation(line.CTKK_ID);b.ViTri="Khác";
            var result=await Task.WhenAll(InventoryCall(c=>c.GhiNhan(line.CTKK_ID,a),stagePersonA),InventoryCall(c=>c.GhiNhan(line.CTKK_ID,b),stageActorB));
            Assert(result.Select(Code).Order().SequenceEqual(new[]{302,409}),"One success, one conflict: "+string.Join(",",result.Select(Describe)));await InventoryCancel(id);
        });
        await StageCase("TC17: all five legacy documents remain read-only with original results",async()=>
        {
            for(var i=1;i<=5;i++){var model=(KiemKeChiTiet)((ViewResult)await InventoryCall(c=>c.Details(i))).Model!;Assert(model.Phieu.KK_LaDuLieuCu&&!model.CanRecord,"Legacy read-only");Assert(model.Items.Single().Dong.CTKK_KetQua==(i==5?"CanXuLy":"BinhThuong"),"Original result");Assert(Code(await InventoryCall(c=>c.BatDau(i,InventoryRequestSync(i))))==409,"Legacy start blocked");}
        });
        await StageCase("TC18: TS005 inconsistency remains warned and unchanged",async()=>
        {
            var books=(List<KiemKeSach>)((ViewResult)await InventoryCall(c=>c.NguonDuLieu())).Model!;
            Assert(books.Single(x=>x.Id==5).XungDot,"TS005 source conflict warned");
            await InventoryStart(inventoryGlobal);var line=(await InventoryLines(inventoryGlobal)).Single(x=>x.TS_ID==5);
            Assert(line.CTKK_CanhBaoNguon!=null,"Conflict frozen in book");await InventoryCancel(inventoryGlobal);
        });
        await StageCase("TC19: report counts distinct assets vs flags; UTF8 BOM CSV escapes quotes, newlines and formulas",async()=>
        {
            var page=(KiemKeChiTiet)((ViewResult)await InventoryCall(c=>c.Details(inventoryDoc))).Model!;
            Assert(page.TongHop.DuKien==3&&page.TongHop.DaTimThay==3&&page.TongHop.Thua==1&&page.TongHop.NgoaiPhamVi==1&&page.TongHop.DongChenhLech==3&&page.TongHop.TongCo==7,"Counts agree with 5 physical rows / 7 flags including the outside-scope asset: "+JsonSerializer.Serialize(page.TongHop));
            var result=(FileStreamResult)await InventoryCall(c=>c.XuatBaoCao(id:inventoryDoc));using var bytes=new MemoryStream();await result.FileStream.CopyToAsync(bytes);await result.FileStream.DisposeAsync();
            Assert(bytes.ToArray().Take(3).SequenceEqual(new byte[]{239,187,191}),"UTF8 BOM");var text=Encoding.UTF8.GetString(bytes.ToArray());
            Assert(text.Contains("\"'=1+1\nĐề xuất \"\"kiểm tra\"\"\"")&&text.Contains("Bằng chứng"),"CSV formula, newline and quote escaping");
        });
        await StageCase("TC20: real SQL error inserting audit rolls back created draft, start snapshot and recorded line",async()=>
        {
            var duplicate=await InventoryAnyAuditKey();var input=InventoryInput();await using(var db=StageDb()){var count=await db.tblKiemKeTaiSan.CountAsync();Assert(Code(await InventoryCall(c=>c.Create(input),interceptors:[new InventoryFailAudit(duplicate)]))==409,"Duplicate audit SQL error");Assert(await db.tblKiemKeTaiSan.CountAsync()==count,"Draft rolled back");}
            var id=await InventoryCreate(InventoryInput());var request=await InventoryRequest(id);Assert(Code(await InventoryCall(c=>c.BatDau(id,request),interceptors:[new InventoryFailAudit(duplicate)]))==409,"Snapshot SQL failure");
            Assert((await InventoryLines(id)).Length==0,"No half snapshot");await InventoryStart(id);var line=(await InventoryLines(id))[0];var observation=await InventoryObservation(line.CTKK_ID);
            Assert(Code(await InventoryCall(c=>c.GhiNhan(line.CTKK_ID,observation),stagePersonA,[new InventoryFailAudit(duplicate)]))==409,"Line SQL failure");Assert((await InventoryLine(line.CTKK_ID)).CTKK_KetQua=="ChuaKiem","Line rolled back");await InventoryCancel(id);
        });
    }

    private static async Task InventoryExtendedChecks()
    {
        await StageCase("Actual inactive custodian remains selectable without gaining inventory permissions or losing custody",async()=>
        {
            var id=await InventoryCreate(InventoryInput());await InventoryStart(id);var first=(await InventoryLines(id))[0];
            await using(var db=StageDb()){(await db.tblTaiKhoan.FindAsync(stagePersonA))!.TK_TrangThai=false;await db.SaveChangesAsync();}
            try
            {
                var form=(KiemKeGhiNhanForm)((ViewResult)await InventoryCall(c=>c.GhiNhan(first.CTKK_ID))).Model!;Assert(form.NguoiDungs.Any(x=>x.Value==stagePersonA.ToString()&&x.Text.Contains("đã khóa")),"Inactive physical custodian is retained");
                await InventoryObserve(first.CTKK_ID,stageActorA);Assert((await InventoryLine(first.CTKK_ID)).CTKK_KetQua=="Khop","Locked account does not erase physical custody");
                Assert(Code(await InventoryCall(c=>c.GhiNhan(first.CTKK_ID,InventoryObservationSync(first.CTKK_ID)),stagePersonA))==403,"Inactive account still cannot perform inventory writes");
            }
            finally{await using var db=StageDb();(await db.tblTaiKhoan.FindAsync(stagePersonA))!.TK_TrangThai=true;await db.SaveChangesAsync();await InventoryCancel(id);}
        });
        await StageCase("BR05: completed disposal and unmanaged assets excluded, pending disposal retained; unknown unit report available",async()=>
        {
            var assets=await NewAssets(3);await using(var db=StageDb())
            {
                (await db.tblTaiSan.FindAsync(assets[0]))!.TS_TrangThai="DaThanhLy";(await db.tblTaiSan.FindAsync(assets[1]))!.TS_TrangThai="ChoThanhLy";(await db.tblTaiSan.FindAsync(assets[2]))!.TS_DangQuanLy=false;
                foreach(var i in new[]{0,1})db.Add(new tblThanhLyTaiSan{TL_MaPhieu="S3-DISPOSE-"+i,TL_NgayThanhLy=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),TL_NguoiLapID=stageActorA,TL_TongGiaTri=0,TL_LyDo="Synthetic disposal fixture",TL_TrangThai=i==0?"HoanThanh":"ChoDuyet",tblChiTietThanhLies=[new(){TS_ID=assets[i],CTTL_GiaTriThanhLy=0}]});await db.SaveChangesAsync();
            }
            var id=await InventoryCreate(InventoryInput(global:true));await InventoryStart(id);var lines=await InventoryLines(id);Assert(!lines.Any(x=>x.TS_ID==assets[0])&&lines.Any(x=>x.TS_ID==assets[1])&&!lines.Any(x=>x.TS_ID==assets[2]),"Actual completed disposal only");await InventoryCancel(id);
        });
        await StageCase("Migration constraints: raw invalid scope, NULL book FK, duplicate asset/discovery, invalid found flag and bad department are rejected",async()=>
        {
            var lines=await InventoryLines(inventoryDoc);var book=lines.First(x=>x.CTKK_LoaiDong=="TrongSo");var outside=lines.First(x=>x.CTKK_LoaiDong=="NgoaiSo");
            async Task Rejected(FormattableString sql,int number){await using var db=StageDb();var rejected=false;try{await db.Database.ExecuteSqlInterpolatedAsync(sql);}catch(MySqlException ex)when(ex.Number==number){rejected=true;}Assert(rejected,"Expected database constraint "+number);}
            await Rejected($"UPDATE tblkiemketaisan SET PB_ID=NULL WHERE KK_ID={inventoryDoc}",4025);
            await Rejected($"INSERT INTO tblchitietkiemke(KK_ID,TS_ID,CTKK_LoaiDong) VALUES({inventoryDoc},NULL,'TrongSo')",4025);
            await Rejected($"INSERT INTO tblchitietkiemke(KK_ID,TS_ID) VALUES({inventoryDoc},{book.TS_ID})",1062);
            await Rejected($"INSERT INTO tblchitietkiemke(KK_ID,TS_ID,CTKK_LoaiDong,CTKK_MaPhatHien) VALUES({inventoryDoc},NULL,'NgoaiSo',{outside.CTKK_MaPhatHien})",1062);
            await Rejected($"UPDATE tblchitietkiemke SET CTKK_DaTimThay=2 WHERE CTKK_ID={book.CTKK_ID}",4025);
            await Rejected($"UPDATE tblchitietkiemke SET CTKK_PBThucTeID={int.MaxValue} WHERE CTKK_ID={book.CTKK_ID}",1452);
        });
        await StageCase("Index/report filters and server pagination preserve assignment scope and exclude legacy from new KPI",async()=>
        {
            await using var db=StageDb();
            var needed=22-await db.tblKiemKeTaiSan.CountAsync();for(var i=0;i<needed;i++){var input=InventoryInput();input.TenDot="Phân trang "+i;await InventoryCreate(input);}
            var c=InventoryController(db);var all=(KiemKeIndex)((ViewResult)await c.Index()).Model!;
            Assert(all.Total>20&&all.Items.Count==20&&all.Pages>1,"Server-side paging");
            var second=(KiemKeIndex)((ViewResult)await c.Index(page:2)).Model!;Assert(!all.Items.Select(x=>x.Id).Intersect(second.Items.Select(x=>x.Id)).Any(),"Stable distinct pages");
            var legacy=(KiemKeIndex)((ViewResult)await c.BaoCao(chiLichSu:true)).Model!;Assert(legacy.Total==5&&legacy.TongHop.TongDong==0,"Legacy excluded from new KPI");
            var target=(KiemKeIndex)((ViewResult)await c.Index(keyword:all.Items[0].Ma,phongBanId:all.Items[0].PhamVi=="PhongBan"?stageDepartmentA:null)).Model!;
            Assert(target.Total==1&&target.Items[0].Id==all.Items[0].Id,"Keyword and department combined filter");
            var assigned=(KiemKeIndex)((ViewResult)await InventoryCall(x=>x.Index(phongBanId:stageDepartmentA),stagePersonB)).Model!;Assert(assigned.Items.All(x=>x.NguoiKiemKe=="Stage Person B"),"Assignment scope survives filters");
            var day=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));var dated=(KiemKeIndex)((ViewResult)await c.Index(trangThai:"HoanThanh",tuNgay:day,denNgay:day)).Model!;Assert(dated.Total>=1&&dated.Items.All(x=>x.TrangThai=="HoanThanh"&&x.Ngay==day),"State and date filters");
        });
        await StageCase("Reassignment: only manager with reason can change open inspector; past recorder/snapshot retained; old assignee loses access",async()=>
        {
            var id=await InventoryCreate(InventoryInput());await InventoryStart(id);var rows=await InventoryLines(id);await InventoryObserve(rows[0].CTKK_ID);
            var old=await InventoryLine(rows[0].CTKK_ID);var request=await InventoryRequest(id);var input=new KiemKePhanCongInput{RequestKey=request.RequestKey,PhienBan=request.PhienBan,NguoiKiemKeId=stagePersonB,LyDo="Quyết định phân công kiểm kê bổ sung"};
            Assert(Code(await InventoryCall(c=>c.DoiNguoiKiemKe(id,input),stagePersonA))==403,"Employee reassignment denied");
            Assert(Code(await InventoryCall(c=>c.DoiNguoiKiemKe(id,input)))==302,"Manager reassignment");
            Assert(await InventoryCall(c=>c.Details(id),stagePersonA) is NotFoundResult,"Former assignee scope removed");
            Assert(await InventoryCall(c=>c.Details(id),stagePersonB) is ViewResult,"New assignee scope allowed");
            var current=await InventoryLine(old.CTKK_ID);Assert(InventoryOriginalResult(old)==InventoryOriginalResult(current),"Past observation and book not rewritten");await InventoryObserve(rows[1].CTKK_ID,stagePersonB);await InventoryCancel(id);
        });
        await StageCase("TC06: external master change is warned; frozen values remain unchanged; inventory never repairs source",async()=>
        {
            var id=await InventoryCreate(InventoryInput());await InventoryStart(id);var first=(await InventoryLines(id))[0];
            await using(var db=StageDb()){(await db.tblTaiSan.FindAsync(first.TS_ID))!.TS_ViTri="Vị trí mới do phân hệ tài sản thay đổi";await db.SaveChangesAsync();}
            try
            {
                var model=(KiemKeChiTiet)((ViewResult)await InventoryCall(c=>c.Details(id))).Model!;
                Assert(model.Items.Single(x=>x.Dong.CTKK_ID==first.CTKK_ID).BienDong&&model.CanhBaos.Count>0,"Current drift warning");
                Assert((await InventoryLine(first.CTKK_ID)).CTKK_ViTriDuKien=="Phòng A","Frozen location preserved");
                var input=await InventoryObservation(first.CTKK_ID);input.XacNhanCanCu=false;input.BangChung=null;
                Assert(Code(await InventoryCall(c=>c.GhiNhan(first.CTKK_ID,input),stagePersonA))==302,"Observe with drift");
                Assert((await InventoryLine(first.CTKK_ID)).CTKK_KetQua=="CanXacMinh","Drift without evidence cannot match");
            }
            finally{await using var db=StageDb();(await db.tblTaiSan.FindAsync(first.TS_ID))!.TS_ViTri="Phòng A";await db.SaveChangesAsync();await InventoryCancel(id);}
        });
        await StageCase("Concurrent starts of overlapping scope serialize through one named lock; unrelated starts do not corrupt snapshot",async()=>
        {
            var a=await InventoryCreate(InventoryInput());var b=await InventoryCreate(InventoryInput());var ra=await InventoryRequest(a);var rb=await InventoryRequest(b);
            var results=await Task.WhenAll(InventoryCall(c=>c.BatDau(a,ra)),InventoryCall(c=>c.BatDau(b,rb),stageActorB));
            Assert(results.Select(Code).Order().SequenceEqual(new[]{302,409}),"Exactly one overlapping start: "+string.Join(",",results.Select(Describe)));
            var all=(await InventoryLines(a)).Length+(await InventoryLines(b)).Length;Assert(all==3,"Exactly one complete snapshot");await InventoryCancel(a);await InventoryCancel(b);
        });
        await StageCase("BR26: assigned manager cannot self-approve; a recorder needs independent reconciliation even on matched rows",async()=>
        {
            var own=await InventoryCreate(InventoryInput(inspector:stageActorA));await InventoryStart(own);
            foreach(var line in await InventoryLines(own))await InventoryObserve(line.CTKK_ID,stageActorA);
            Assert(Code(await InventoryCall(c=>c.GuiDuyet(own,InventoryRequestSync(own))))==302,"Submit own-manager assigned");
            Assert(Code(await InventoryCall(c=>c.Duyet(own,InventoryRequestSync(own))))==403,"Assigned manager cannot approve");
            Assert(Code(await InventoryCall(c=>c.Duyet(own,InventoryRequestSync(own)),stageActorB))==302,"Independent manager approves");
            var id=await InventoryCreate(InventoryInput());await InventoryStart(id);var rows=await InventoryLines(id);
            await InventoryObserve(rows[0].CTKK_ID,stageActorB);foreach(var row in rows[1..])await InventoryObserve(row.CTKK_ID);
            Assert(Code(await InventoryCall(c=>c.GuiDuyet(id,InventoryRequestSync(id)),stagePersonA))==302,"Matched submission");
            Assert(Code(await InventoryCall(c=>c.Duyet(id,InventoryRequestSync(id)),stageActorB))==403,"Recorder cannot approve own result");
            var returned=await InventoryRequest(id);returned.LyDo="Cần đối soát độc lập";Assert(Code(await InventoryCall(c=>c.TraLai(id,returned)))==302,"Return for independent verification");
            await InventoryReconcile(rows[0].CTKK_ID,stageActorA);
            Assert(Code(await InventoryCall(c=>c.GuiDuyet(id,InventoryRequestSync(id)),stagePersonA))==302,"Resubmit after independent reconciliation");
            var approval=await InventoryRequest(id);var oldAudit=await InventoryAnyAuditKey();
            Assert(Code(await InventoryCall(c=>c.Duyet(id,approval),stageActorB,[new InventoryFailAudit(oldAudit)]))==409,"Approval audit SQL error rolls back");
            await using(var db=StageDb()){var doc=await db.tblKiemKeTaiSan.FindAsync(id);Assert(doc!.KK_TrangThai=="ChoDuyet"&&doc.KK_NgayDuyetUTC==null&&doc.KK_NguoiDuyetID==null,"No unlogged approval");}
            Assert(Code(await InventoryCall(c=>c.Duyet(id,approval),stageActorB))==302,"Manager with independent verification approves");
        });
        await StageCase("BR12/25: pending maintenance requires evidence, is not auto-found/missing; empty scope cannot submit",async()=>
        {
            inventoryEmpty=await InventoryCreate(InventoryInput(department:stageDepartmentB));await InventoryStart(inventoryEmpty);
            var first=(await InventoryLines(inventoryEmpty)).Single();
            await using(var db=StageDb()){var repair=new tblBaoTriTaiSan{TS_ID=first.TS_ID!.Value,BT_MaPhieu="S3-REPAIR",BT_ChiPhi=0,BT_NgayBaoTri=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),BT_NguoiLapID=stageActorA,BT_TrangThai="ChoXuLy",BT_LyDo="Kiểm thử chứng từ"};db.Add(repair);await db.SaveChangesAsync();}
            var input=await InventoryObservation(first.CTKK_ID);input.CachGhiNhan="KhongThay";input.XacNhanCanCu=false;input.BangChung=null;
            Assert(Code(await InventoryCall(c=>c.GhiNhan(first.CTKK_ID,input),stagePersonA))==302,"Observe absent but external maintenance exists");
            var recorded=await InventoryLine(first.CTKK_ID);Assert(recorded.CTKK_KetQua=="CanXacMinh"&&recorded.CTKK_DaTimThay==null,"No automatic missing verdict");
            Assert(Code(await InventoryCall(c=>c.GuiDuyet(inventoryEmpty,InventoryRequestSync(inventoryEmpty)),stagePersonA))==409,"CanVerify blocked");await InventoryCancel(inventoryEmpty);
            var emptyDepartment=new tblPhongBan{PB_MaPhongBan="S3EMPTY",PB_TenPhongBan="Phạm vi rỗng",PB_TrangThai=true};await using(var db=StageDb()){db.Add(emptyDepartment);await db.SaveChangesAsync();}
            var empty=await InventoryCreate(InventoryInput(department:emptyDepartment.PB_ID));await InventoryStart(empty);
            Assert(Code(await InventoryCall(c=>c.GuiDuyet(empty,InventoryRequestSync(empty)),stagePersonA))==409,"Zero book rows cannot submit");
            var model=(KiemKeChiTiet)((ViewResult)await InventoryCall(c=>c.Details(empty))).Model!;Assert(model.TongHop.TienDo=="Không có tài sản","Zero denominator");await InventoryCancel(empty);
        });
        await StageCase("Audit append-only triggers block SQL UPDATE/DELETE and history is scoped and complete",async()=>
        {
            await using var db=StageDb();var evt=await db.tblSuKienKiemKe.FirstAsync();
            foreach(var sql in new[]{"UPDATE tblsukienkiemke SET SKK_LyDo='tamper' WHERE SKK_ID={0}","DELETE FROM tblsukienkiemke WHERE SKK_ID={0}"})
            {var blocked=false;try{await db.Database.ExecuteSqlRawAsync(sql,evt.SKK_ID);}catch(MySqlException ex)when(ex.Number==1644){blocked=true;}Assert(blocked,"Append-only trigger");}
            var history=(KiemKeLichSu)((ViewResult)await InventoryCall(c=>c.LichSu(inventoryDoc))).Model!;Assert(history.Total>10&&history.Items.All(x=>x.SKK_NguoiThucHienID>0&&x.SKK_RequestKey.Length==36),"Real actor/time and full audit");
        });
        await StageCase("Real lock timeout is reported as conflict; retry is safe and no partial state/event is committed",async()=>
        {
            var id=await InventoryCreate(InventoryInput());var request=await InventoryRequest(id);
            await using var connection=new MySqlConnection(stageChecksConnection);await connection.OpenAsync();await using var tx=await connection.BeginTransactionAsync();
            await using(var command=connection.CreateCommand()){command.Transaction=tx;command.CommandText="SELECT KK_ID FROM tblkiemketaisan WHERE KK_ID=@id FOR UPDATE";command.Parameters.AddWithValue("@id",id);await command.ExecuteScalarAsync();}
            var result=await InventoryCall(c=>c.Huy(id,request),interceptors:[new InventoryShortLockWait()]);Assert(Code(result)==409&&Describe(result).Contains("Xung đột khóa"),"Actual 1205 mapped to friendly conflict: "+Describe(result));await tx.RollbackAsync();
            Assert(Code(await InventoryCall(c=>c.Huy(id,request)))==302,"Same request can retry after rollback");
        });
        await StageCase("Real inventory InnoDB deadlock: victim transaction becomes 409 and leaves no draft or audit",async()=>
        {
            var assets=await NewAssets(6);var input=InventoryInput();
            await using var connection=new MySqlConnection(stageChecksConnection);await connection.OpenAsync();await using var tx=await connection.BeginTransactionAsync();
            await using(var command=connection.CreateCommand())
            {
                command.Transaction=tx;command.CommandText="UPDATE tbltaisan SET TS_GhiChu='Synthetic deadlock weight' WHERE TS_ID IN ("+string.Join(",",assets)+")";await command.ExecuteNonQueryAsync();
                command.CommandText="SELECT TK_ID FROM tbltaikhoan WHERE TK_ID=@inspector FOR UPDATE";command.Parameters.AddWithValue("@inspector",stagePersonA);await command.ExecuteScalarAsync();
            }
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending=InventoryCall(c=>c.Create(input),interceptors:[new InventorySignalLock(stagePersonA,ready)]);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using(var command=connection.CreateCommand()){command.Transaction=tx;command.CommandText="SELECT TK_ID FROM tbltaikhoan WHERE TK_ID=@actor FOR UPDATE";command.Parameters.AddWithValue("@actor",stageActorA);await command.ExecuteScalarAsync();}
            var result=await pending.WaitAsync(TimeSpan.FromSeconds(10));Assert(Code(result)==409&&Describe(result).Contains("Xung đột khóa"),"Real 1213 translated: "+Describe(result));await tx.RollbackAsync();
            await using var db=StageDb();Assert(!await db.tblKiemKeTaiSan.AnyAsync(x=>x.KK_RequestKey==input.RequestKey)&&!await db.tblSuKienKiemKe.AnyAsync(x=>x.SKK_RequestKey==input.RequestKey),"No partial deadlock writes");
        });
    }

    private sealed class InventorySignalLock(int target,TaskCompletionSource ready):DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
        {
            if(command.CommandText.Contains("FROM tbltaikhoan")&&command.CommandText.Contains("FOR UPDATE")&&command.Parameters.Cast<DbParameter>().Any(x=>Convert.ToInt32(x.Value)==target))ready.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class InventoryShortLockWait:DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection connection,ConnectionEndEventData eventData,CancellationToken cancellationToken=default)
        {await using var command=connection.CreateCommand();command.CommandText="SET SESSION innodb_lock_wait_timeout=1";await command.ExecuteNonQueryAsync(cancellationToken);}
    }

    private static async Task InventoryAvailabilityHttp(MySqlConnectionStringBuilder source)
    {
        foreach(var outage in new[]{false,true})
        {
            var target=new MySqlConnectionStringBuilder(source.ConnectionString){ConnectionTimeout=1,Pooling=false};if(outage)target.Port=65530;
            var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(KiemKeController).Assembly.GetName().Name,ContentRootPath=Directory.GetCurrentDirectory(),EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<DataContext>(o=>o.UseMySql(target.ConnectionString,new MariaDbServerVersion(new Version(10,4,32))));builder.Services.AddDbContext<DBContext>(o=>o.UseMySql(target.ConnectionString,new MariaDbServerVersion(new Version(10,4,32))));builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();builder.Services.AddControllersWithViews().AddApplicationPart(typeof(KiemKeController).Assembly);
            await using var app=builder.Build();app.Use(async(http,next)=>{http.Session=new MemorySession();http.Session.SetInt32("TaiKhoanID",1);await next();});app.MapControllerRoute("areas","{area:exists}/{controller=Home}/{action=Index}/{id?}");await app.StartAsync();
            try
            {
                using var client=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){BaseAddress=new Uri(app.Urls.Single())};
                await StageCase(outage?"MVC availability: actual refused database connection renders safe 503 without querying menu again":"Primary read-only HTTP: un-migrated inventory renders clear 503 inside preserved Admin layout",async()=>{var result=await client.GetAsync("/Admin/KiemKe/Index");var body=WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync());Assert((int)result.StatusCode==503&&body.Contains("Kiểm kê tài sản chưa sẵn sàng")&&!body.Contains("MySqlException"),"Safe unavailable view: "+(int)result.StatusCode);});
            }
            finally{await app.StopAsync();}
        }
    }

    private static async Task InventoryHttpChecks()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(KiemKeController).Assembly.GetName().Name,ContentRootPath=Directory.GetCurrentDirectory(),EnvironmentName="Development"});
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<DataContext>(o=>o.UseMySql(stageChecksConnection,new MariaDbServerVersion(new Version(10,4,32))));
        builder.Services.AddDbContext<DBContext>(o=>o.UseMySql(stageChecksConnection,new MariaDbServerVersion(new Version(10,4,32))));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();builder.Services.AddControllersWithViews().AddApplicationPart(typeof(KiemKeController).Assembly);
        await using var app=builder.Build();app.Use(async(http,next)=>{http.Session=new MemorySession();var actor=int.TryParse(http.Request.Headers["X-Stage-Actor"],out var id)?id:stageActorA;http.Session.SetInt32("TaiKhoanID",actor);http.Session.SetString("VaiTro","Admin");await next();});
        app.MapControllerRoute("areas","{area:exists}/{controller=Home}/{action=Index}/{id?}");await app.StartAsync();
        try
        {
            using var client=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false,CookieContainer=new CookieContainer()}){BaseAddress=new Uri(app.Urls.Single())};client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
            async Task<string> Page(string url){var response=await client.GetAsync(url);var body=await response.Content.ReadAsStringAsync();Assert((int)response.StatusCode==200,"GET "+url+" => "+(int)response.StatusCode+" "+WebUtility.HtmlDecode(body)[..Math.Min(180,body.Length)]);return body;}
            string Field(string html,string name){var tag=Regex.Matches(html,"<input\\b[^>]*>").Select(x=>x.Value).FirstOrDefault(x=>x.Contains("name=\""+name+"\""));return tag==null?"":WebUtility.HtmlDecode(Regex.Match(tag,"value=\"([^\"]*)\"").Groups[1].Value);}
            int uiDoc=0;
            await StageCase("MVC UI: real CSRF, overposting rejection, encoded Vietnamese validation, all inventory screens in Admin layout",async()=>
            {
                var form=await Page("/Admin/KiemKe/Create");var token=Field(form,"__RequestVerificationToken");Assert(token.Length>0&&Field(form,"RequestKey").Length==36,"Real forms and CSRF");
                var input=InventoryInput();input.RequestKey=Field(form,"RequestKey");input.GhiChu="<script>alert('x')</script>";
                var values=new Dictionary<string,string>{{"RequestKey",input.RequestKey},{"TenDot",input.TenDot},{"LoaiPhamVi",input.LoaiPhamVi},{"PhongBanId",input.PhongBanId.ToString()!},{"NguoiKiemKeId",input.NguoiKiemKeId.ToString()},{"NgayKiemKe",input.NgayKiemKe!.Value.ToString("yyyy-MM-dd")},{"KK_TrangThai","HoanThanh"},{"KK_NguoiTaoID",stagePersonB.ToString()},{"KK_NguoiDuyetID",stagePersonB.ToString()},{"KK_LaDuLieuCu","true"},{"GhiChu",input.GhiChu}};
                Assert((int)(await client.PostAsync("/Admin/KiemKe/Create",new FormUrlEncodedContent(values))).StatusCode==400,"No token blocked");values["__RequestVerificationToken"]=token;
                var bad=new Dictionary<string,string>(values){["TenDot"]=" "};var invalid=await client.PostAsync("/Admin/KiemKe/Create",new FormUrlEncodedContent(bad));var body=await invalid.Content.ReadAsStringAsync();Assert((int)invalid.StatusCode==400&&body.Contains("TenDot")&&!body.Contains("<script>alert"),"Validation redisplay encodes XSS");
                Assert((int)(await client.PostAsync("/Admin/KiemKe/Create",new FormUrlEncodedContent(values))).StatusCode==302,"HTTP draft");
                await using(var db=StageDb()){var doc=await db.tblKiemKeTaiSan.SingleAsync(x=>x.KK_RequestKey==input.RequestKey);uiDoc=doc.KK_ID;Assert(doc.KK_TrangThai=="Nhap"&&!doc.KK_LaDuLieuCu&&doc.KK_NguoiTaoID==stageActorA&&doc.KK_NguoiDuyetID==null,"Overposting ignored");}
                await Page("/Admin/KiemKe/Edit/"+uiDoc);
                await InventoryStart(uiDoc);var line=(await InventoryLines(uiDoc))[0];
                foreach(var url in new[]{"/Admin/KiemKe/Index","/Admin/KiemKe/BaoCao","/Admin/KiemKe/Details/"+uiDoc,"/Admin/KiemKe/DanhSachKiemKe/"+uiDoc,"/Admin/KiemKe/GhiNhan/"+line.CTKK_ID,"/Admin/KiemKe/PhatHienNgoaiDanhSach/"+uiDoc+"?keyword=S3-SERIAL","/Admin/KiemKe/DoiSoat/"+line.CTKK_ID,"/Admin/KiemKe/NguonDuLieu","/Admin/KiemKe/LichSu/"+inventoryDoc,"/Admin/KiemKe/InBienBan/"+inventoryDoc,"/Admin/KiemKe/Details/1","/Admin/KiemKe/Create?dotGocId="+inventoryDoc})await Page(url);
                var detail=await Page("/Admin/KiemKe/Details/"+uiDoc);Assert(!detail.Contains("<script>alert")&&detail.Contains("sidebar"),"Preserved admin layout and safe encoding");
                var processed=(await InventoryLines(inventoryDoc)).First(x=>x.CTKK_KetQua=="NhieuSaiLech");await Page("/Admin/KiemKe/CapNhatXuLy/"+processed.CTKK_ID);
                var record=await Page("/Admin/KiemKe/GhiNhan/"+line.CTKK_ID);var observation=await InventoryObservation(line.CTKK_ID);
                var physical=new Dictionary<string,string>{{"RequestKey",Field(record,"RequestKey")},{"PhienBan",Field(record,"PhienBan")},{"CachGhiNhan","CoMat"},{"TinhTrang","Tot"},{"ViTri","Phòng A"},{"PhongBanId",stageDepartmentA.ToString()},{"NguoiSuDungId",stagePersonA.ToString()},{"MaNhanDien",observation.MaNhanDien!},{"Serial",observation.Serial!},{"DaKiemTraThucTe","true"},{"__RequestVerificationToken",Field(record,"__RequestVerificationToken")},{"CTKK_KetQua","Thieu"},{"CTKK_PBDuKienID",stageDepartmentB.ToString()}};
                Assert((int)(await client.PostAsync("/Admin/KiemKe/GhiNhan/"+line.CTKK_ID,new FormUrlEncodedContent(physical))).StatusCode==302,"Real Razor observation post");var saved=await InventoryLine(line.CTKK_ID);Assert(saved.CTKK_KetQua=="Khop"&&saved.CTKK_PBDuKienID==stageDepartmentA,"Calculated server result ignores spoof");
            });
            await StageCase("MVC UI: assigned employee access only; direct approval denied, manager role is read from database",async()=>
            {
                Assert(uiDoc>0,"Prior UI draft");client.DefaultRequestHeaders.Add("X-Stage-Actor",stagePersonA.ToString());var details=await Page("/Admin/KiemKe/Details/"+uiDoc);var html=await Page("/Admin/KiemKe/Index");Assert(!WebUtility.HtmlDecode(html).Contains("Lập đợt kiểm kê</a>"),"Employee sidebar/actions scoped");
                Assert((int)(await client.GetAsync("/Admin/KiemKe/Create")).StatusCode==403,"Employee create GET forbidden");
                var denied=await client.PostAsync("/Admin/KiemKe/Duyet/"+uiDoc,new FormUrlEncodedContent(new Dictionary<string,string>{{"RequestKey",Guid.NewGuid().ToString()},{"PhienBan",(await InventoryRequest(uiDoc)).PhienBan.ToString()},{"LyDo","forged"},{"__RequestVerificationToken",Field(details,"__RequestVerificationToken")}}));Assert((int)denied.StatusCode==403,"Forged approve denied after passing CSRF");
                client.DefaultRequestHeaders.Remove("X-Stage-Actor");client.DefaultRequestHeaders.Add("X-Stage-Actor",stagePersonB.ToString());Assert((int)(await client.GetAsync("/Admin/KiemKe/Details/"+uiDoc)).StatusCode==404,"Foreign document invisible");
                client.DefaultRequestHeaders.Remove("X-Stage-Actor");client.DefaultRequestHeaders.Add("X-Stage-Actor",stageActorB.ToString());await Page("/Admin/KiemKe/Create");
                foreach(var method in typeof(KiemKeController).GetMethods().Where(x=>x.GetCustomAttributes(typeof(HttpPostAttribute),true).Length>0))Assert(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute),true).Length>0,"CSRF on "+method.Name);
            });
            if(uiDoc>0)await InventoryCancel(uiDoc);
        }
        finally{await app.StopAsync();}
    }

    private static async Task<int> RunInventoryReportChecks()
    {
        using var config=JsonDocument.Parse(await File.ReadAllTextAsync("appsettings.json"));
        var source=new MySqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!);
        Assert(source.Server is "localhost" or "127.0.0.1" && source.Port==3307 && source.Database.Equals("QuanLyTaiSan",StringComparison.OrdinalIgnoreCase),"Reports tests require approved read-only source");
        var scratch="qltaisan_sprint2_checks_"+Guid.NewGuid().ToString("N");var target=new MySqlConnectionStringBuilder(source.ConnectionString){Database=scratch};
        var adminOptions=new MySqlConnectionStringBuilder(source.ConnectionString){Database=""};await using var admin=new MySqlConnection(adminOptions.ConnectionString);await admin.OpenAsync();
        var backup=await RunNativeDatabaseTool(source,false);Assert(!Regex.IsMatch(backup,@"(?im)^\s*(USE\s|CREATE DATABASE|DROP DATABASE|GRANT\s|SET\s+GLOBAL)"),"Backup scope");
        var created=false;var reportPrefix="RPT"+Guid.NewGuid().ToString("N")[..10];var today=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var evidence=Path.GetFullPath(Path.Combine(".local-backups","kiemke-report-checks-"+DateTime.UtcNow.AddHours(7).ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(evidence);
        try
        {
            await using(var command=admin.CreateCommand()){command.CommandText="CREATE DATABASE `"+scratch+"` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci";await command.ExecuteNonQueryAsync();created=true;}
            await RunNativeDatabaseTool(target,true,backup);stageChecksConnection=target.ConnectionString;
            await StageCase("Restore current upgraded source into disposable clone; never reapply migration",async()=>Assert(await RunNativeDatabaseTool(target,false)==backup,"Restored source mismatch"));
            await SeedStagePeople();var assets=await NewAssets(5);
            await using(var db=StageDb())
            {
                tblKiemKeTaiSan Header(string suffix,int? department,int inspector,string state,DateOnly date)=>new(){KK_MaPhieu=reportPrefix+suffix,KK_TenDot=reportPrefix+" kiểm kê "+suffix,PB_ID=department,KK_LoaiPhamVi=department==null?"ToanBo":"PhongBan",KK_NguoiKiemKeID=inspector,KK_NguoiTaoID=stageActorA,KK_TrangThai=state,KK_NgayKiemKe=date,KK_NgayTaoUTC=DateTime.UtcNow,KK_ThoiDiemChotUTC=state=="Nhap"?null:DateTime.UtcNow,KK_RequestKey=Guid.NewGuid().ToString()};
                tblChiTietKiemKe Line(int? asset,string result,bool? found,string kind="TrongSo")=>new(){TS_ID=asset,CTKK_LoaiDong=kind,CTKK_MaPhatHien=asset==null?"PH-REPORT":null,CTKK_MaTaiSanSnapshot=asset.HasValue?"TS-REPORT-"+asset:null,CTKK_TenTaiSanSnapshot="Tài sản thử báo cáo",CTKK_KetQua=result,CTKK_DaTimThay=found,CTKK_TinhTrangThucTe=found==true?"Tot":null,CTKK_ViTriThucTe=found==true?"Phòng kiểm kê":null};
                var a=Header("A",stageDepartmentA,stagePersonA,"HoanThanh",today);
                var multiple=Line(assets[1],"NhieuSaiLech",true);multiple.CTKK_CoSaiViTri=true;multiple.CTKK_CoHuHong=true;multiple.CTKK_TrangThaiDoiSoat="DaDoiSoat";multiple.CTKK_NguoiDoiSoatID=stageActorB;multiple.CTKK_TrangThaiXuLy="DaXuLy";
                var missing=Line(assets[2],"Thieu",false);missing.CTKK_TrangThaiDoiSoat="DaDoiSoat";missing.CTKK_NguoiDoiSoatID=stageActorB;
                a.tblChiTietKiemKes=[Line(assets[0],"Khop",true),multiple,missing,Line(null,"Thua",true,"NgoaiSo")];
                var b=Header("B",stageDepartmentB,stagePersonB,"DangKiemKe",today.AddDays(-1));var outside=Line(assets[4],"NgoaiPhamVi",true,"NgoaiPhamVi");outside.CTKK_CoSaiPhongBan=true;
                b.tblChiTietKiemKes=[Line(assets[3],"ChuaKiem",null),outside];
                var global=Header("G",null,stagePersonB,"HoanThanh",today);global.tblChiTietKiemKes=[Line(assets[0],"Khop",true)];
                db.AddRange(a,b,global);
                for(var i=0;i<22;i++)db.Add(Header("D"+i,stageDepartmentA,stagePersonA,"Nhap",today));
                await db.SaveChangesAsync();
            }
            async Task<KiemKeBaoCao> Report(int? actor=null,int? department=null,string? state=null,DateOnly? end=null,int page=1)
            {var result=(ViewResult)await InventoryCall(c=>c.BaoCao(keyword:reportPrefix,trangThai:state,phongBanId:department,denNgay:end,page:page),actor);Assert(result.ViewName=="BaoCao","Dedicated report Razor view");return (KiemKeBaoCao)result.Model!;}
            await StageCase("List and report use distinct views; report covers all 25 documents beyond a 20-row page",async()=>
            {
                var index=(ViewResult)await InventoryCall(c=>c.Index(keyword:reportPrefix));var list=(KiemKeIndex)index.Model!;
                Assert(index.ViewName=="Index"&&list.Total==25&&list.Items.Count==20,"List retains server pagination");
                var report=await Report(page:2);Assert(report.Total==25&&report.SoDotDaChot==3&&report.SoDotMoi==25&&report.SoDotLichSu==0,"Report uses complete filter independently of list page");
            });
            await StageCase("Report counts observed/missing/unobserved, outside rows, overlapping flags and postprocessing correctly",async()=>
            {
                var report=await Report();var s=report.TongHop;
                Assert(s.TongDong==7&&s.DuKien==5&&s.DaTimThay==3&&s.KhongTimThay==1&&s.ChuaKiem==1,"One asset per round, not distinct TS across rounds");
                Assert(s.Khop==2&&s.Thua==1&&s.NgoaiPhamVi==1&&s.DongChenhLech==4&&s.TongCo==3&&s.ChuaDoiSoat==2&&s.DaXuLy==1&&s.TienDo=="80%","Differences and flags are separate counts");
            });
            await StageCase("Grouped scope and state reports agree with all totals and preserve global scope",async()=>
            {
                var report=await Report();Assert(report.TheoTrangThai.Single(x=>x.TrangThai=="Nhap").SoDotMoi==22&&report.TheoTrangThai.Single(x=>x.TrangThai=="HoanThanh").SoDotMoi==2,"State distribution");
                Assert(report.TheoPhamVi.Count==3&&report.TheoPhamVi.Sum(x=>x.SoDot)==25&&report.TheoPhamVi.Sum(x=>x.DuKien)==5&&report.TheoPhamVi.Sum(x=>x.ChenhLech)==4,"Scope totals");
                var a=report.TheoPhamVi.Single(x=>x.PhongBanId==stageDepartmentA);Assert(a.SoDot==23&&a.SoDotDaChot==1&&a.DuKien==3&&a.DaKiem==3&&a.TienDo=="100%"&&a.DaXuLy==1,"Department A");
                Assert(report.TheoPhamVi.Single(x=>x.LoaiPhamVi=="ToanBo").PhongBanId==null,"Global is not attributed to an arbitrary department");
            });
            await StageCase("Report filters: department, state, date, no matches and current employee assignment",async()=>
            {
                Assert((await Report(department:stageDepartmentB)).Total==1&&(await Report(state:"HoanThanh")).TongHop.Khop==2,"Department/state");
                Assert((await Report(end:today.AddDays(-1))).Total==1,"Date range");
                var own=await Report(stagePersonA);Assert(own.Total==23&&own.TongHop.DuKien==3&&own.TheoPhamVi.All(x=>x.PhongBanId==stageDepartmentA),"Employee scope");
                Assert((await Report(stagePersonB,stageDepartmentA)).Total==0,"Department filter cannot bypass assignment");
                var empty=(KiemKeBaoCao)((ViewResult)await InventoryCall(c=>c.BaoCao(keyword:Guid.NewGuid().ToString()))).Model!;Assert(empty.Total==0&&empty.TongHop.TongDong==0&&empty.TheoPhamVi.Count==0,"Empty report");
            });
            await StageCase("Historical results remain separate from modern snapshot/progress metrics",async()=>
            {
                var legacy=(KiemKeBaoCao)((ViewResult)await InventoryCall(c=>c.BaoCao(chiLichSu:true))).Model!;
                await using var db=StageDb();Assert(legacy.Total==await db.tblKiemKeTaiSan.CountAsync(x=>x.KK_LaDuLieuCu)&&legacy.TongHop.TongDong==0&&legacy.SoDotDaChot==0,"No fabricated historical KPI");
                var ids=db.tblKiemKeTaiSan.Where(x=>x.KK_LaDuLieuCu).Select(x=>x.KK_ID);Assert(legacy.KetQuaLichSu.Values.Sum()==await db.tblChiTietKiemKe.CountAsync(x=>ids.Contains(x.KK_ID)),"Actual legacy results included");
            });
            await StageCase("Report CSV uses identical filter and UTF8 BOM, includes all seven detail rows",async()=>
            {
                var export=(FileStreamResult)await InventoryCall(c=>c.XuatBaoCao(keyword:reportPrefix));using var bytes=new MemoryStream();await export.FileStream.CopyToAsync(bytes);await export.FileStream.DisposeAsync();
                Assert(bytes.ToArray().Take(3).SequenceEqual(new byte[]{239,187,191}),"UTF8 BOM");var csv=Encoding.UTF8.GetString(bytes.ToArray());Assert(csv.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length==8,"Header plus seven scoped detail rows");
            });
            await StageCase("MVC HTTP: distinct Vietnamese list/report pages, Admin content wrapper, scoped filters and active navigation",async()=>
            {
                var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(KiemKeController).Assembly.GetName().Name,ContentRootPath=Directory.GetCurrentDirectory(),EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Services.AddDbContext<DataContext>(o=>o.UseMySql(stageChecksConnection,new MariaDbServerVersion(new Version(10,4,32))));builder.Services.AddDbContext<DBContext>(o=>o.UseMySql(stageChecksConnection,new MariaDbServerVersion(new Version(10,4,32))));builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();builder.Services.AddControllersWithViews().AddApplicationPart(typeof(KiemKeController).Assembly);
                await using var app=builder.Build();app.Use(async(http,next)=>{http.Session=new MemorySession();var actor=int.TryParse(http.Request.Headers["X-Stage-Actor"],out var id)?id:stageActorA;http.Session.SetInt32("TaiKhoanID",actor);http.Session.SetString("VaiTro","Admin");await next();});app.MapControllerRoute("areas","{area:exists}/{controller=Home}/{action=Index}/{id?}");await app.StartAsync();
                try
                {
                    using var client=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){BaseAddress=new Uri(app.Urls.Single())};
                    async Task<string> Page(string path){var response=await client.GetAsync(path);var html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());Assert((int)response.StatusCode==200,"GET "+path+" => "+(int)response.StatusCode);Assert(Regex.Matches(html,"<main\\b[^>]*id=\"main\"").Count==1,"Exactly one Admin main content region");return html;}
                    var list=await Page("/Admin/KiemKe/Index?keyword="+reportPrefix);var report=await Page("/Admin/KiemKe/BaoCao?keyword="+reportPrefix);
                    Assert(!list.Contains("Phân bố trạng thái đợt")&&!list.Contains("Tổng hợp kết quả — tiến độ")&&report.Contains("Phân bố trạng thái đợt")&&report.Contains("So sánh theo phạm vi kiểm kê")&&report.Contains("80%"),"Distinct screens and actual report numbers");
                    Assert(report.Contains("aria-current=\"page\"")&&report.Contains("keyword="+reportPrefix)&&report.Contains("CSV chi tiết"),"Active menu and preserved export filter");
                    await Page("/Admin/KiemKe/BaoCao?chiLichSu=true");await Page("/Admin/KiemKe/BaoCao?keyword=NO-MATCH-"+Guid.NewGuid().ToString("N"));
                    client.DefaultRequestHeaders.Add("X-Stage-Actor",stagePersonA.ToString());var employee=await Page("/Admin/KiemKe/BaoCao?keyword="+reportPrefix);Assert(!employee.Contains("Staging B"),"Other department report not leaked");
                }
                finally{await app.StopAsync();}
            });
            await StageCase("Primary database unchanged after all report and UI checks",async()=>Assert(await RunNativeDatabaseTool(source,false)==backup,"Primary changed during checks"));
            return failed==0?0:1;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(evidence,"results.json"),JsonSerializer.Serialize(new{Passed=passed,Failed=failed,Results=stageOutcomes},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"INVENTORY REPORTS RESULT: {passed} passed, {failed} failed; {evidence}");
            if(created){Assert(Regex.IsMatch(scratch,"^qltaisan_sprint2_checks_[0-9a-f]{32}$"),"Unsafe cleanup target");await using var command=admin.CreateCommand();command.CommandText="DROP DATABASE `"+scratch+"`";await command.ExecuteNonQueryAsync();}
        }
    }

    private static KiemKePhieuInput InventoryInput(bool global=false,int? department=null,int? inspector=null)=>new(){RequestKey=Guid.NewGuid().ToString(),TenDot="Kiểm kê thử nghiệm Sprint 3",LoaiPhamVi=global?"ToanBo":"PhongBan",PhongBanId=global?null:department??stageDepartmentA,NguoiKiemKeId=inspector??stagePersonA,NgayKiemKe=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7))};
    private static KiemKeController InventoryController(DataContext db,int? actor=null)
    {
        var http=new DefaultHttpContext{Session=new MemorySession()};http.Session.SetInt32("TaiKhoanID",actor??stageActorA);http.Session.SetString("VaiTro","Admin");
        return new(db,NullLogger<KiemKeController>.Instance){ControllerContext=new ControllerContext(new ActionContext(http,new RouteData(),new ControllerActionDescriptor())),TempData=new TempDataDictionary(http,new MemoryTempData())};
    }
    private static async Task<IActionResult> InventoryCall(Func<KiemKeController,Task<IActionResult>> action,int? actor=null,params IInterceptor[] interceptors){await using var db=StageDb(interceptors);return await action(InventoryController(db,actor));}
    private static async Task<int> InventoryCreate(KiemKePhieuInput input){var result=await InventoryCall(c=>c.Create(input));Assert(result is RedirectToActionResult,"Create inventory: "+Describe(result));return Convert.ToInt32(((RedirectToActionResult)result).RouteValues!["id"]);}
    private static async Task<KiemKeRequest> InventoryRequest(int id,bool line=false){await using var db=StageDb();return new(){RequestKey=Guid.NewGuid().ToString(),PhienBan=line?await db.tblChiTietKiemKe.Where(x=>x.CTKK_ID==id).Select(x=>x.CTKK_PhienBan).SingleAsync():await db.tblKiemKeTaiSan.Where(x=>x.KK_ID==id).Select(x=>x.KK_PhienBan).SingleAsync(),LyDo="Kiểm thử nghiệp vụ"};}
    private static KiemKeRequest InventoryRequestSync(int id)=>InventoryRequest(id).GetAwaiter().GetResult();
    private static async Task InventoryStart(int id){var request=await InventoryRequest(id);var result=await InventoryCall(c=>c.BatDau(id,request));Assert(Code(result)==302,"Start inventory: "+Describe(result));}
    private static async Task InventoryCancel(int id){var request=await InventoryRequest(id);Assert(Code(await InventoryCall(c=>c.Huy(id,request)))==302,"Cancel inventory");}
    private static async Task<tblChiTietKiemKe[]> InventoryLines(int id){await using var db=StageDb();return await db.tblChiTietKiemKe.AsNoTracking().Where(x=>x.KK_ID==id).OrderBy(x=>x.CTKK_ID).ToArrayAsync();}
    private static async Task<tblChiTietKiemKe> InventoryLine(int id){await using var db=StageDb();return await db.tblChiTietKiemKe.AsNoTracking().SingleAsync(x=>x.CTKK_ID==id);}
    private static async Task<KiemKeGhiNhanInput> InventoryObservation(int id){var line=await InventoryLine(id);return new(){RequestKey=Guid.NewGuid().ToString(),PhienBan=line.CTKK_PhienBan,CachGhiNhan="CoMat",DaKiemTraThucTe=true,TinhTrang=line.CTKK_TinhTrangDuKien??"Tot",ViTri=line.CTKK_ViTriDuKien??"Kho phát hiện",PhongBanId=line.CTKK_PBDuKienID,NguoiSuDungId=line.CTKK_NguoiNhanDuKienID,MaNhanDien=line.CTKK_MaTaiSanSnapshot,Serial=line.CTKK_SerialDuKien,GhiChu="Đã kiểm tra thực tế",BangChung="BB-S3 kiểm tra tại hiện vật",XacNhanCanCu=true,LyDo="Bổ sung căn cứ"};}
    private static KiemKeGhiNhanInput InventoryObservationSync(int id)=>InventoryObservation(id).GetAwaiter().GetResult();
    private static async Task InventoryObserve(int id,int? actor=null){var input=await InventoryObservation(id);var result=await InventoryCall(c=>c.GhiNhan(id,input),actor??stagePersonA);Assert(Code(result)==302,"Observation: "+Describe(result));}
    private static async Task InventoryReconcile(int id,int? actor=null){var version=await InventoryRequest(id,true);var input=new KiemKeDoiSoatInput{RequestKey=version.RequestKey,PhienBan=version.PhienBan,LyDo="Đối chiếu độc lập",NguyenNhan="Đã xác minh hiện vật và chứng từ",HuongXuLy="Khac",DeXuat="=1+1\nĐề xuất \"kiểm tra\"",TrangThai="DaDoiSoat",NguoiDoiSoatId=stageActorB};var result=await InventoryCall(c=>c.DoiSoat(id,input),actor??stageActorB);Assert(Code(result)==302,"Reconcile: "+Describe(result));}
    private static async Task<KiemKePhatHienInput> InventoryDiscovery(int? asset=null){await using var db=StageDb();var source=asset.HasValue?await db.tblTaiSan.SingleAsync(x=>x.TS_ID==asset):null;return new(){RequestKey=Guid.NewGuid().ToString(),PhienBan=(await InventoryRequest(inventoryDoc)).PhienBan,TaiSanId=asset,TenThucTe=source?.TS_TenTaiSan??"Máy phát hiện ngoài sổ",MaPhatHien=asset==null?"PH-S3-001":null,MaNhanDien=source?.TS_MaTaiSan??"UNKNOWN-S3",Serial=source?.TS_Serial??"UNKNOWN-SERIAL",CachGhiNhan="CoMat",TinhTrang="Tot",ViTri="Phòng A",PhongBanId=stageDepartmentA,NguoiSuDungId=stagePersonA,DaKiemTraThucTe=true,XacNhanCanCu=true,GhiChu="Phát hiện hiện vật ngoài danh sách",BangChung="BB-PH-01"};}
    private static string InventorySnapshot(IEnumerable<tblChiTietKiemKe> rows)=>JsonSerializer.Serialize(rows.Select(x=>new{x.CTKK_ID,x.TS_ID,x.CTKK_MaTaiSanSnapshot,x.CTKK_TenTaiSanSnapshot,x.CTKK_DauVetNguon,x.CTKK_NguyenGiaDuKien,x.CTKK_ViTriDuKien,x.CTKK_PBDuKienID,x.CTKK_NguoiNhanDuKienID}));
    private static string InventoryOriginalResult(tblChiTietKiemKe row)=>JsonSerializer.Serialize(row.GetType().GetProperties().Where(p=>(p.PropertyType.IsValueType||p.PropertyType==typeof(string))&&!new[]{"CTKK_TrangThaiXuLy","CTKK_KetQuaXuLy","CTKK_ThamChieuXuLy","CTKK_NguoiXuLyID","CTKK_ThoiDiemXuLyUTC","CTKK_PhienBan"}.Contains(p.Name)).ToDictionary(p=>p.Name,p=>p.GetValue(row)));
    private static async Task<string> InventoryAnyAuditKey(){await using var db=StageDb();return await db.tblSuKienKiemKe.Select(x=>x.SKK_RequestKey).FirstAsync();}
    private sealed class InventoryFailAudit(string duplicate):SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,InterceptionResult<int> result,CancellationToken cancellationToken=default)
        {foreach(var entry in eventData.Context!.ChangeTracker.Entries<tblSuKienKiemKe>().Where(x=>x.State==EntityState.Added))entry.Entity.SKK_RequestKey=duplicate;return ValueTask.FromResult(result);}
    }
    private static async Task<List<string[]>> InventoryQuery(MySqlConnectionStringBuilder connection,string sql)
    {
        await using var db=new MySqlConnection(connection.ConnectionString);await db.OpenAsync();await using var command=db.CreateCommand();command.CommandText=sql;await using var reader=await command.ExecuteReaderAsync();var rows=new List<string[]>();while(await reader.ReadAsync()){var row=new string[reader.FieldCount];for(var i=0;i<row.Length;i++)row[i]=reader.IsDBNull(i)?"<NULL>":Convert.ToString(reader.GetValue(i),System.Globalization.CultureInfo.InvariantCulture)!;rows.Add(row);}return rows;
    }
    private static async Task<Dictionary<string,string>> InventoryDataHashes(MySqlConnectionStringBuilder connection,string[]? tables=null)
    {
        tables??=(await InventoryQuery(connection,"SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME")).Select(x=>x[0]).ToArray();var result=new Dictionary<string,string>();
        foreach(var table in tables){Assert(Regex.IsMatch(table,"^[a-zA-Z0-9_]+$"),"Identifier");var rows=await InventoryQuery(connection,"SELECT * FROM `"+table+"` ORDER BY 1");result[table]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));}return result;
    }
    private static async Task<string> InventoryLegacyHash(MySqlConnectionStringBuilder connection)=>JsonSerializer.Serialize(new[]{await InventoryQuery(connection,"SELECT KK_ID,KK_MaPhieu,PB_ID,KK_NgayKiemKe,KK_NguoiKiemKeID,KK_GhiChu,KK_TrangThai FROM tblkiemketaisan WHERE KK_ID<=5 ORDER BY KK_ID"),await InventoryQuery(connection,"SELECT CTKK_ID,KK_ID,TS_ID,CTKK_TinhTrangThucTe,CTKK_ViTriThucTe,CTKK_KetQua,CTKK_GhiChu FROM tblchitietkiemke WHERE CTKK_ID<=5 ORDER BY CTKK_ID")});
    private static async Task<object> InventorySchema(MySqlConnectionStringBuilder connection)=>new{Columns=await InventoryQuery(connection,"SELECT TABLE_NAME,COLUMN_NAME,COLUMN_TYPE,IS_NULLABLE,COALESCE(COLUMN_DEFAULT,'NULL') FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME,ORDINAL_POSITION"),ForeignKeys=await InventoryQuery(connection,"SELECT TABLE_NAME,CONSTRAINT_NAME,REFERENCED_TABLE_NAME,UPDATE_RULE,DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() ORDER BY TABLE_NAME,CONSTRAINT_NAME"),Indexes=await InventoryQuery(connection,"SELECT TABLE_NAME,INDEX_NAME,NON_UNIQUE,COLUMN_NAME,SEQ_IN_INDEX FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME,INDEX_NAME,SEQ_IN_INDEX"),Triggers=await InventoryQuery(connection,"SELECT TRIGGER_NAME,EVENT_OBJECT_TABLE,ACTION_STATEMENT FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE() ORDER BY TRIGGER_NAME")};
}
