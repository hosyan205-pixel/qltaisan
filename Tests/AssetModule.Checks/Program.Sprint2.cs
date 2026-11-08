using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoAn.Areas.Admin.Controllers;
using DoAn.Areas.Admin.Models;
using DoAn.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using static DoAn.Areas.Admin.Models.GiaoNhanStates;

namespace AssetModule.Checks;

internal static partial class Program
{
    private static string stageChecksConnection = "";
    private static int stageActorA, stageActorB, stagePersonA, stagePersonB, stagePersonC, stageDepartmentA, stageDepartmentB, stageCategory;
    private static readonly List<object> stageOutcomes = [];

    private static async Task<int> RunSprint2Stage()
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync("appsettings.json"));
        var original = new MySqlConnectionStringBuilder(json.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!);
        Assert((original.Database.StartsWith("qltaisan_sprint2_stage_") || original.Database.Equals("QuanLyTaiSan",StringComparison.OrdinalIgnoreCase)) && original.Port == 3307, "Approved source required; all writes must use a disposable clone");
        var scratch = "qltaisan_sprint2_checks_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new MySqlConnectionStringBuilder(original.ConnectionString) { Database = "" };
        await using var admin = new MySqlConnection(adminBuilder.ConnectionString); await admin.OpenAsync();
        var created = false;
        try
        {
            await using (var cmd = admin.CreateCommand()) { cmd.CommandText = "CREATE DATABASE " + scratch + " CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci"; await cmd.ExecuteNonQueryAsync(); created = true; }
            var dump = await RunNativeDatabaseTool(original, false);
            Assert(!Regex.IsMatch(dump, @"(?im)^\s*(USE\s|CREATE DATABASE|DROP DATABASE|GRANT\s|SET\s+GLOBAL)"), "Unexpected dump scope");
            var target = new MySqlConnectionStringBuilder(original.ConnectionString) { Database = scratch };
            await RunNativeDatabaseTool(target, true, dump);
            stageChecksConnection = target.ConnectionString;
            await SeedStagePeople();
            await StageCase("EF/schema: 18 tables, 7 triggers, generated unique, FK/navigation and concurrency mapping", async () =>
            {
                await using var db = StageDb();
                Assert(await Scalar("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE()") == 18, "18 tables");
                Assert(await Scalar("SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE()") == 7, "7 original triggers");
                Assert(await Scalar("SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND INDEX_NAME='UX_CTCP_TaiSanHieuLuc' AND NON_UNIQUE=0") == 1, "Active unique");
                Assert(await Scalar("SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA=DATABASE() AND DELETE_RULE<>'RESTRICT'") == 0, "Restrict, no cascade");
                var e = db.Model.FindEntityType(typeof(tblChiTietCapPhat))!;
                Assert(e.FindProperty(nameof(tblChiTietCapPhat.CTCP_TaiSanHieuLucID))!.GetComputedColumnSql() != null, "Generated EF field");
                Assert(e.GetNavigations().Any(x => x.Name == nameof(tblChiTietCapPhat.DongNguon)) && e.GetNavigations().Any(x => x.Name == nameof(tblChiTietCapPhat.TaiSan)), "Old/new line navigation");
                Assert(db.Model.FindEntityType(typeof(tblCapPhatTaiSan))!.FindProperty(nameof(tblCapPhatTaiSan.CP_PhienBan))!.IsConcurrencyToken, "Version");
            });
            await StageCase("Legacy: all 5 original rows remain unverified; ID 5 unchanged; writes blocked", async () =>
            {
                await using var db = StageDb();
                Assert(await db.tblCapPhatTaiSan.CountAsync(x => x.CP_ID <= 5 && x.CP_NguoiNhanID == null && x.CP_LoaiPhieu == null) == 5, "No automatic identity mapping");
                Assert(await db.tblChiTietCapPhat.CountAsync(x => x.CTCP_ID <= 5 && x.CTCP_TrangThai == null && x.CTCP_BatDauUTC == null) == 5, "No fabricated custody");
                var asset = await db.tblTaiSan.SingleAsync(x => x.TS_ID == 5);
                Assert(asset.TS_TrangThai == "ChuaCapPhat" && asset.TS_TinhTrang == "CanBaoTri", "ID 5 preserved");
                var report = (GiaoNhanRows)((ViewResult)await StageController(db).Reconciliation()).Model!;
                Assert(report.Items.Count(x => x.CanDoiSoat) == 5 && report.Items.Any(x => x.TaiSanId == 5), "Visible reconciliation");
                foreach (var id in Enumerable.Range(1,5))
                    Assert(await Status(c => c.Create(Input([id]))) == 409, "Legacy asset must be blocked: " + id);
                Assert(await Status(c => c.ConfirmHandover(1,new())) == 409, "Legacy cannot be confirmed");
            });
            await StageCase("Draft: 3 assets, edit recipient/selection, stale edit rejected, no custody before confirm", async () =>
            {
                var assets = await NewAssets(4);
                var input = Input(assets[..3]); var id = await Create(input);
                await using (var db = StageDb())
                {
                    var doc = await Load(db,id);
                    Assert(doc.CP_TrangThai == Draft && doc.CP_NgayCapPhat == null && doc.CP_XacNhanUTC == null, "Draft dates/state");
                    Assert(doc.tblChiTietCapPhats.All(x => x.CTCP_TrangThai == PendingGive && x.CTCP_TaiSanHieuLucID == null), "No draft custody");
                    Assert(await db.tblTaiSan.CountAsync(x => assets.Contains(x.TS_ID) && x.TS_TrangThai == "ChuaCapPhat") == 4, "Assets unchanged");
                    input.PhienBan = doc.CP_PhienBan;
                }
                input.NguoiNhanId = stagePersonB; input.PhongBanId = stageDepartmentB;
                input.Dongs = assets[1..].Select(x => new GiaoNhanDongInput { TaiSanId=x }).ToList();
                Assert(await Status(c => c.EditDraft(id,input)) == 302, "Edit");
                Assert(await Status(c => c.EditDraft(id,input)) == 409, "Stale edit");
                await Submit(id);
                Assert(await ActiveCount(assets) == 0, "Waiting is not custody");
                await Confirm(id,Give);
                await using var check=StageDb(); var handed=await Load(check,id);
                Assert(handed.CP_NguoiNhanID==stagePersonB && handed.tblChiTietCapPhats.All(x=>x.CTCP_TrangThai==Active), "Correct receiver and all lines");
                Assert(await Status(c=>c.EditDraft(id,input))==409, "Confirmed history immutable");
            });
            await StageCase("Handover: all assets atomic, server actor/time/snapshots, repeat request and confirmation", async () =>
            {
                var assets=await NewAssets(3);var input=Input(assets);var id=await Create(input);
                Assert(await Create(input)==id,"Repeated create reuses receipt");
                await Submit(id); var v=await Version(id); await Confirm(id,Give);
                Assert(await Status(c=>c.ConfirmHandover(id,new(){PhienBan=v}))==302,"Repeated confirm handled");
                await using var db=StageDb();var doc=await Load(db,id);
                Assert(doc.CP_NguoiLapID==stageActorA && doc.CP_NguoiXacNhanID==stageActorA && doc.CP_XacNhanUTC.HasValue,"Server identity/time");
                Assert(doc.tblChiTietCapPhats.Select(x=>x.CTCP_BatDauUTC).Distinct().Count()==1,"Single handover instant");
                Assert(doc.tblChiTietCapPhats.All(x=>x.CTCP_TaiSanHieuLucID==x.TS_ID && x.CTCP_TinhTrangKhiGiao=="Tot" && x.CTCP_MaTaiSanSnapshot!=null),"Snapshots and generated slot");
                Assert(await db.tblSuKienCapPhat.CountAsync(x=>x.CP_ID==id && x.SK_Loai=="XacNhanGiao")==1,"Exactly one confirm event");
            });
            await StageCase("Partial return: give 5, receive 2, retain 3; receive remaining 3", async () =>
            {
                var assets=await NewAssets(5);var give=await GiveAssets(assets);var sources=await Lines(give);
                var first=await Create(SourceInput(give,sources[..2],"Tot"),Return);await Submit(first);await Confirm(first,Return);
                Assert(await ActiveCount(assets)==3,"Only 2 closed");
                var rest=await Create(SourceInput(give,sources[2..],"Tot"),Return);await Submit(rest);await Confirm(rest,Return);
                Assert(await ActiveCount(assets)==0,"All 5 closed");
                await using var db=StageDb();
                Assert(await db.tblChiTietCapPhat.CountAsync(x=>x.CP_ID==give && x.CTCP_TrangThai==ClosedReturn)==5,"Root rows correctly ended");
                Assert(await db.tblTaiSan.CountAsync(x=>assets.Contains(x.TS_ID)&&x.TS_TrangThai=="ChuaCapPhat")==5,"Ready after good receipt");
                Assert(await Status(c=>c.CreateReturn(SourceInput(give,[sources[0]],"Tot")))==409,"Cannot receive same source twice");
            });
            foreach(var condition in new[]{"CanKiemTra","CanBaoTri","HuHong","ThieuPhuKien"})
                await StageCase("Return condition "+condition+" blocks reallocation",async()=>
                {
                    var assets=await NewAssets(1);var give=await GiveAssets(assets);
                    var ret=await Create(SourceInput(give,await Lines(give),condition),Return);await Submit(ret);await Confirm(ret,Return);
                    Assert(await Status(c=>c.Create(Input(assets)))==409,"Bad condition not available");
                    await using var db=StageDb();var a=await db.tblTaiSan.FindAsync(assets[0]);
                    Assert(a!.TS_TinhTrang==condition && a.TS_TrangThai!="ChuaCapPhat","Physical condition retained");
                });
            await StageCase("Transfer: pending/cancel keeps A; selected 1 of 3; A to B to C to return",async()=>
            {
                var assets=await NewAssets(3);var a=await GiveAssets(assets);var source=(await Lines(a))[0];
                var pending=await Create(SourceInput(a,[source],recipient:stagePersonB,department:stageDepartmentB),Transfer);
                await Submit(pending);Assert(await ActiveCount(assets)==3,"Pending has no new custody");
                var cancelVersion=await Version(pending);
                Assert(await Status(c=>c.Cancel(pending,cancelVersion,"Không chuyển nữa"))==302,"Cancel");
                Assert(await ActiveCount(assets)==3,"Cancel retains old responsibility");
                var b=await Create(SourceInput(a,[source],recipient:stagePersonB,department:stageDepartmentB),Transfer);await Submit(b);await Confirm(b,Transfer);
                Assert(await ActiveCount(assets)==3,"Only selected asset changes owner");
                var bLine=(await Lines(b))[0];var old=(await Lines(a))[0];
                Assert(old.CTCP_TrangThai==ClosedTransfer && old.CTCP_KetThucUTC==bLine.CTCP_BatDauUTC,"Exact transfer boundary");
                var c=await Create(SourceInput(b,[bLine],recipient:stagePersonC,department:stageDepartmentB),Transfer);await Submit(c);await Confirm(c,Transfer);
                var cLine=(await Lines(c))[0];var retInput=SourceInput(c,[cLine],"Tot",department:stageDepartmentB);
                var ret=await Create(retInput,Return);await Submit(ret);await Confirm(ret,Return);
                Assert(await ActiveCount([source.TS_ID])==0 && await ActiveCount(assets)==2,"Chain closed only selected asset");
                await using var db=StageDb();
                Assert((await Load(db,a)).CP_NguoiNhanID==stagePersonA && (await Load(db,b)).CP_NguoiNhanID==stagePersonB && (await Load(db,c)).CP_NguoiNhanID==stagePersonC,"Historical holders unchanged");
                Assert(cLine.CTCP_DongNguonID==bLine.CTCP_ID && bLine.CTCP_DongNguonID==source.CTCP_ID,"Full predecessor chain");
            });
            await RunRemainingStageChecks();
            await RunSprint2UiChecks();
            var reportPath=Path.GetFullPath(Path.Combine(".local-backups","sprint2-application-check-results.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(new{Database=scratch,Passed=passed,Failed=failed,Results=stageOutcomes},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"SPRINT2 RESULT: {passed} passed, {failed} failed; primary database untouched");
            return failed==0?0:1;
        }
        finally
        {
            if(created)
            {
                Assert(Regex.IsMatch(scratch,"^qltaisan_sprint2_checks_[0-9a-f]{32}$"),"Unsafe cleanup target");
                await using var cmd=admin.CreateCommand();cmd.CommandText="DROP DATABASE "+scratch;await cmd.ExecuteNonQueryAsync();
                Console.WriteLine("SPRINT2: removed only isolated checks database");
            }
        }
    }

    private static async Task<string> RunNativeDatabaseTool(MySqlConnectionStringBuilder connection,bool importing,string? dump=null)
    {
        var info=new ProcessStartInfo
        {
            FileName=importing?@"C:\xampp\mysql\bin\mysql.exe":@"C:\xampp\mysql\bin\mysqldump.exe",
            UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
            RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=importing,
            StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8
        };
        info.Environment["MYSQL_PWD"]=connection.Password;
        foreach(var arg in new[]{"--host="+connection.Server,"--port="+connection.Port,"--user="+connection.UserID,"--default-character-set=utf8mb4"})info.ArgumentList.Add(arg);
        if(importing)
        {
            info.StandardInputEncoding=new UTF8Encoding(false);
            foreach(var arg in new[]{"--batch","--comments","--binary-mode","--database="+connection.Database})info.ArgumentList.Add(arg);
        }
        else
            foreach(var arg in new[]{"--single-transaction","--quick","--routines","--events","--triggers","--hex-blob","--skip-comments","--skip-dump-date","--skip-add-drop-table","--skip-add-locks","--skip-disable-keys",connection.Database})info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;
        var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        if(importing){await process.StandardInput.WriteAsync(dump);process.StandardInput.Close();}
        await process.WaitForExitAsync();var stderr=await error;
        Assert(process.ExitCode==0,"Native backup/restore failed: "+stderr);
        return await output;
    }
    private static DataContext StageDb(params IInterceptor[] interceptors)
    {
        Assert(Regex.IsMatch(new MySqlConnectionStringBuilder(stageChecksConnection).Database,"^qltaisan_sprint2_checks_[0-9a-f]{32}$"),"Write checks require the disposable clone");
        var options=new DbContextOptionsBuilder<DataContext>().UseMySql(stageChecksConnection,new MariaDbServerVersion(new Version(10,4,32)));
        if(interceptors.Length>0)options.AddInterceptors(interceptors);
        return new(options.Options);
    }
    private static CapPhatController StageController(DataContext db,int? actor=null)
    {
        var http=new DefaultHttpContext{Session=new MemorySession()};
        http.Session.SetInt32("TaiKhoanID",actor??stageActorA);http.Session.SetString("VaiTro","Admin");
        return new(db,NullLogger<CapPhatController>.Instance)
        {
            ControllerContext=new ControllerContext(new ActionContext(http,new RouteData(),new ControllerActionDescriptor())),
            TempData=new TempDataDictionary(http,new MemoryTempData())
        };
    }
    private static async Task StageCase(string name,Func<Task> action)
    {
        try{await action();passed++;stageOutcomes.Add(new{Name=name,Passed=true});Console.WriteLine("PASS "+name);}
        catch(Exception ex){failed++;stageOutcomes.Add(new{Name=name,Passed=false,Error=ex.Message});Console.WriteLine("FAIL "+name+": "+ex.Message);}
    }
    private static async Task SeedStagePeople()
    {
        await using var db=StageDb();
        var a=new tblPhongBan{PB_MaPhongBan="S2A",PB_TenPhongBan="Staging A",PB_TrangThai=true};
        var b=new tblPhongBan{PB_MaPhongBan="S2B",PB_TenPhongBan="Staging B",PB_TrangThai=true};db.AddRange(a,b);await db.SaveChangesAsync();
        stageDepartmentA=a.PB_ID;stageDepartmentB=b.PB_ID;
        var people=new[]{
            new tblTaiKhoan{TK_TenDangNhap="stage_admin_a",TK_HoTen="Stage Admin A",TK_VaiTro="Admin",TK_MatKhau="unused",PB_ID=a.PB_ID},
            new tblTaiKhoan{TK_TenDangNhap="stage_admin_b",TK_HoTen="Stage Admin B",TK_VaiTro="Admin",TK_MatKhau="unused",PB_ID=a.PB_ID},
            new tblTaiKhoan{TK_TenDangNhap="stage_person_a",TK_HoTen="Stage Person A",TK_MatKhau="unused",PB_ID=a.PB_ID},
            new tblTaiKhoan{TK_TenDangNhap="stage_person_b",TK_HoTen="Stage Person B",TK_MatKhau="unused",PB_ID=b.PB_ID},
            new tblTaiKhoan{TK_TenDangNhap="stage_person_c",TK_HoTen="Stage Person C",TK_MatKhau="unused",PB_ID=b.PB_ID}};
        db.AddRange(people);await db.SaveChangesAsync();
        stageActorA=people[0].TK_ID;stageActorB=people[1].TK_ID;stagePersonA=people[2].TK_ID;stagePersonB=people[3].TK_ID;stagePersonC=people[4].TK_ID;
        stageCategory=await db.tblDanhMucTaiSan.Where(x=>x.DMTS_TrangThai==true).Select(x=>x.DMTS_ID).FirstAsync();
    }
    private static async Task<int[]> NewAssets(int count)
    {
        await using var db=StageDb();
        var assets=Enumerable.Range(0,count).Select(_=>new tblTaiSan{TS_MaTaiSan="S2"+Guid.NewGuid().ToString("N")[..24],TS_TenTaiSan="Stage asset",DMTS_ID=stageCategory,TS_TrangThai="ChuaCapPhat",TS_TinhTrang="Tot",TS_NguyenGia=1,TS_ViTri="Stage kho"}).ToArray();
        db.AddRange(assets);await db.SaveChangesAsync();return assets.Select(x=>x.TS_ID).ToArray();
    }
    private static GiaoNhanInput Input(int[] assets,int? receiver=null,int? department=null)=>new()
    {
        RequestKey=Guid.NewGuid().ToString("D"),NguoiNhanId=receiver??stagePersonA,PhongBanId=department??stageDepartmentA,
        NgayDuKien=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),LyDo="Staging approved integration check",
        Dongs=assets.Select(x=>new GiaoNhanDongInput{TaiSanId=x}).ToList()
    };
    private static GiaoNhanInput SourceInput(int receipt,tblChiTietCapPhat[] sources,string? condition=null,int? recipient=null,int? department=null)
    {
        var input=Input(sources.Select(x=>x.TS_ID).ToArray(),recipient,department);
        input.PhieuGocId=receipt;input.Dongs=sources.Select(x=>new GiaoNhanDongInput{TaiSanId=x.TS_ID,DongNguonId=x.CTCP_ID,TinhTrangNhan=condition,PhuKien="Đã kiểm tra",ViTri="Stage kho"}).ToList();
        return input;
    }
    private static async Task<int> Create(GiaoNhanInput input,string kind=Give,int? actor=null)
    {
        await using var db=StageDb();var c=StageController(db,actor);
        var result=kind switch{Return=>await c.CreateReturn(input),Transfer=>await c.CreateTransfer(input),_=>await c.Create(input)};
        Assert(result is RedirectToActionResult,"Create: "+Describe(result));
        return Convert.ToInt32(((RedirectToActionResult)result).RouteValues!["id"]);
    }
    private static async Task<long> Version(int id){await using var db=StageDb();return await db.tblCapPhatTaiSan.Where(x=>x.CP_ID==id).Select(x=>x.CP_PhienBan).SingleAsync();}
    private static async Task Submit(int id){var v=await Version(id);Assert(await Status(c=>c.Submit(id,v))==302,"Submit");}
    private static async Task Confirm(int id,string kind)
    {
        var v=await Version(id);var status=await Status(c=>kind switch{Return=>c.ConfirmReturn(id,new(){PhienBan=v}),Transfer=>c.ConfirmTransfer(id,new(){PhienBan=v}),_=>c.ConfirmHandover(id,new(){PhienBan=v})});
        Assert(status==302,"Confirm "+kind+" returned "+status);
    }
    private static async Task<int> GiveAssets(int[] assets){var id=await Create(Input(assets));await Submit(id);await Confirm(id,Give);return id;}
    private static async Task<int> Status(Func<CapPhatController,Task<IActionResult>> operation,int? actor=null,params IInterceptor[] interceptors)
    {
        await using var db=StageDb(interceptors);return Code(await operation(StageController(db,actor)));
    }
    private static int Code(IActionResult result)=>result switch{RedirectToActionResult=>302,ObjectResult r=>r.StatusCode??200,StatusCodeResult r=>r.StatusCode,_=>200};
    private static string Describe(IActionResult result)=>result is ObjectResult r?$"{r.StatusCode}: {r.Value}":result.GetType().Name;
    private static Task<tblCapPhatTaiSan> Load(DataContext db,int id)=>db.tblCapPhatTaiSan.Include(x=>x.tblChiTietCapPhats).SingleAsync(x=>x.CP_ID==id);
    private static async Task<tblChiTietCapPhat[]> Lines(int id){await using var db=StageDb();return await db.tblChiTietCapPhat.AsNoTracking().Where(x=>x.CP_ID==id).OrderBy(x=>x.CTCP_ID).ToArrayAsync();}
    private static async Task<int> ActiveCount(int[] assets){await using var db=StageDb();return await db.tblChiTietCapPhat.CountAsync(x=>assets.Contains(x.TS_ID)&&x.CTCP_TrangThai==Active);}
    private static async Task<long> Scalar(string sql)
    {
        await using var db=new MySqlConnection(stageChecksConnection);await db.OpenAsync();await using var cmd=db.CreateCommand();cmd.CommandText=sql;return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}

