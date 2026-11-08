using System.Data;
using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoAn.Areas.Admin.Controllers;
using DoAn.Areas.Admin.Models;
using DoAn.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
    private static async Task RunRemainingStageChecks()
    {
        await StageCase("Unverified legacy history independently blocks a physically ready asset",async()=>
        {
            var assets=await NewAssets(1);
            await using(var db=StageDb())
            {
                db.tblCapPhatTaiSan.Add(new tblCapPhatTaiSan
                {
                    CP_MaPhieu="L2"+Guid.NewGuid().ToString("N")[..24],PB_ID=stageDepartmentA,CP_NguoiGiaoID=stageActorA,
                    CP_NguoiNhan="Synthetic unverified legacy test",CP_NgayCapPhat=new DateOnly(2025,1,1),
                    tblChiTietCapPhats=[new tblChiTietCapPhat{TS_ID=assets[0]}]
                });
                await db.SaveChangesAsync();
            }
            await using var check=StageDb();
            var result=await StageController(check).Create(Input(assets));
            Assert(result is ObjectResult{StatusCode:409,Value:string message}&&message.Contains("đối soát"),"Legacy block independent of physical state");
            Assert((await check.tblTaiSan.AsNoTracking().SingleAsync(x=>x.TS_ID==assets[0])).TS_TrangThai=="ChuaCapPhat","No automatic correction");
        });
        await StageCase("Same person changes department through an explicit transfer, preserving old department",async()=>
        {
            var assets=await NewAssets(1);var old=await GiveAssets(assets);var source=await Lines(old);
            try
            {
                await Exec($"UPDATE tbltaikhoan SET PB_ID={stageDepartmentB} WHERE TK_ID={stagePersonA}");
                Assert(await ActiveCount(assets)==1,"Account department edit does not move custody");
                var move=await Create(SourceInput(old,source,recipient:stagePersonA,department:stageDepartmentB),Transfer);await Submit(move);await Confirm(move,Transfer);
                await using(var db=StageDb())
                    Assert((await Load(db,old)).PB_ID==stageDepartmentA&&(await Load(db,move)).PB_ID==stageDepartmentB,"Departments remain in separate historical receipts");
                var ret=await Create(SourceInput(move,await Lines(move),"Tot",department:stageDepartmentB),Return);await Submit(ret);await Confirm(ret,Return);
            }
            finally{await Exec($"UPDATE tbltaikhoan SET PB_ID={stageDepartmentA} WHERE TK_ID={stagePersonA}");}
        });
        await StageCase("Transfer rechecks physical condition at confirmation",async()=>
        {
            var assets=await NewAssets(1);var give=await GiveAssets(assets);
            var move=await Create(SourceInput(give,await Lines(give),recipient:stagePersonB,department:stageDepartmentB),Transfer);await Submit(move);
            await Exec($"UPDATE tbltaisan SET TS_TinhTrang={"HuHong"} WHERE TS_ID={assets[0]}");
            var version=await Version(move);Assert(await Status(c=>c.ConfirmTransfer(move,new(){PhienBan=version}))==409,"Damaged after waiting");
            Assert(await ActiveCount(assets)==1&&(await Lines(give))[0].CTCP_KetThucUTC==null,"Old responsibility unchanged");
        });
        await StageCase("Commit failure never reports success or a falsely certain outcome",async()=>
        {
            var assets=await NewAssets(1);var id=await Create(Input(assets));await Submit(id);var version=await Version(id);
            Assert(await Status(c=>c.ConfirmHandover(id,new(){PhienBan=version}),null,new FailCommit())==503,"Unknown outcome response");
            Assert(await ActiveCount(assets)==0,"Injected pre-commit failure rolled back");
            await using var db=StageDb();
            Assert((await Load(db,id)).CP_TrangThai==Waiting&&await db.tblSuKienCapPhat.CountAsync(x=>x.CP_ID==id&&x.SK_Loai=="XacNhanGiao")==0,"No persisted success/audit");
        });
        await StageCase("Confirmed history uses immutable snapshots after asset/person/department rename",async()=>
        {
            var assets=await NewAssets(1);var id=await GiveAssets(assets);
            string personName,departmentName;
            await using(var db=StageDb())
            {
                personName=await db.tblTaiKhoan.Where(x=>x.TK_ID==stagePersonA).Select(x=>x.TK_HoTen).SingleAsync();
                departmentName=await db.tblPhongBan.Where(x=>x.PB_ID==stageDepartmentA).Select(x=>x.PB_TenPhongBan).SingleAsync();
            }
            try
            {
                await Exec($"UPDATE tbltaisan SET TS_TenTaiSan={"Changed asset name"} WHERE TS_ID={assets[0]}");
                await Exec($"UPDATE tbltaikhoan SET TK_HoTen={"Changed person name"} WHERE TK_ID={stagePersonA}");
                await Exec($"UPDATE tblphongban SET PB_TenPhongBan={"Changed department name"} WHERE PB_ID={stageDepartmentA}");
                await using var db=StageDb();
                var details=(CapPhatDetails)((ViewResult)await StageController(db).Details(id)).Model!;
                var history=(CapPhatHistory)((ViewResult)await StageController(db).History(assets[0])).Model!;
                Assert(details.Phieu.NguoiNhan==personName&&details.Phieu.PhongBan==departmentName,"Header snapshot");
                Assert(history.Items.Single().TenTaiSan=="Stage asset"&&history.Items.Single().PhongBan==departmentName,"Asset history snapshot");
            }
            finally
            {
                await Exec($"UPDATE tbltaikhoan SET TK_HoTen={personName} WHERE TK_ID={stagePersonA}");
                await Exec($"UPDATE tblphongban SET PB_TenPhongBan={departmentName} WHERE PB_ID={stageDepartmentA}");
            }
        });
        await StageCase("Invalid bound confirmation data is rejected before any write",async()=>
        {
            var assets=await NewAssets(1);var id=await Create(Input(assets));await Submit(id);var v=await Version(id);
            await using var db=StageDb();var c=StageController(db);c.ModelState.AddModelError("ThucHien","Invalid date from form binding");
            Assert(Code(await c.ConfirmHandover(id,new(){PhienBan=v}))==400,"Bad bound value");
            Assert(await ActiveCount(assets)==0,"No side effect");
        });
        await StageCase("Validation: empty/duplicate/unknown assets, forged source, invalid dates and recipient",async()=>
        {
            var assets=await NewAssets(2);
            var bad=Input([]);Assert(await Status(c=>c.Create(bad))==400,"Empty");
            bad=Input([assets[0],assets[0]]);Assert(await Status(c=>c.Create(bad))==400,"Duplicate");
            bad=Input([int.MaxValue]);Assert(await Status(c=>c.Create(bad))==409,"Unknown");
            bad=Input(assets);bad.NgayTraDuKien=bad.NgayDuKien!.Value.AddDays(-1);Assert(await Status(c=>c.Create(bad))==400,"Date range");
            bad=Input(assets,stagePersonB,stageDepartmentA);Assert(await Status(c=>c.Create(bad))==409,"Department membership");
            bad=Input(assets);bad.NguoiNhanId=null;Assert(await Status(c=>c.Create(bad))==409,"Missing receiver");
            var give=await GiveAssets([assets[0]]);var source=(await Lines(give))[0];
            var other=await GiveAssets([assets[1]]);
            bad=SourceInput(other,[source],"Tot");Assert(await Status(c=>c.CreateReturn(bad))==409,"Wrong source receipt");
            bad=SourceInput(give,[source],"Tot");bad.Dongs[0].TaiSanId=assets[1];Assert(await Status(c=>c.CreateReturn(bad))==409,"Mismatched asset and source");
            bad=SourceInput(give,[source],recipient:stagePersonA,department:stageDepartmentA);Assert(await Status(c=>c.CreateTransfer(bad))==409,"Transfer to same responsibility");
            bad=SourceInput(give,[source],"MatDangXuLy");Assert(await Status(c=>c.CreateReturn(bad))==409,"Lost is not physical receipt");
        });
        foreach(var state in new[]{"DangBaoTri","ChoThanhLy","DaThanhLy","MatDangXuLy","NgungQuanLy"})
            await StageCase("Availability blocks "+state,async()=>
            {
                var assets=await NewAssets(1);
                await using(var db=StageDb())
                {
                    var asset=await db.tblTaiSan.SingleAsync(x=>x.TS_ID==assets[0]);
                    if(state=="MatDangXuLy")asset.TS_TinhTrang=state;
                    else if(state=="NgungQuanLy")asset.TS_DangQuanLy=false;
                    else asset.TS_TrangThai=state;
                    await db.SaveChangesAsync();
                }
                Assert(await Status(c=>c.Create(Input(assets)))==409,"Cannot allocate");
            });
        await StageCase("Revalidate: unavailable last asset blocks whole handover without partial updates",async()=>
        {
            var assets=await NewAssets(3);var id=await Create(Input(assets));await Submit(id);
            await Exec($"UPDATE tbltaisan SET TS_TinhTrang={"HuHong"} WHERE TS_ID={assets[^1]}");
            var v=await Version(id);Assert(await Status(c=>c.ConfirmHandover(id,new(){PhienBan=v}))==409,"Blocked");
            Assert(await ActiveCount(assets)==0,"No partial handover");
            await using var db=StageDb();Assert((await Load(db,id)).CP_TrangThai==Waiting,"Still waiting");
            Assert(await db.tblSuKienCapPhat.CountAsync(x=>x.CP_ID==id&&x.SK_Loai=="XacNhanGiao")==0,"No false event");
        });
        await StageCase("Recipient locked after waiting: confirm denied; locked historical holder may return",async()=>
        {
            var assets=await NewAssets(2);var held=await GiveAssets([assets[0]]);var waiting=await Create(Input([assets[1]]));await Submit(waiting);
            try
            {
                await Exec($"UPDATE tbltaikhoan SET TK_TrangThai={false} WHERE TK_ID={stagePersonA}");
                var v=await Version(waiting);Assert(await Status(c=>c.ConfirmHandover(waiting,new(){PhienBan=v}))==409,"Receiver locked after form");
                var ret=await Create(SourceInput(held,await Lines(held),"Tot"),Return);await Submit(ret);await Confirm(ret,Return);
                Assert(await ActiveCount([assets[0]])==0,"Admin can receive from locked holder");
            }
            finally{await Exec($"UPDATE tbltaikhoan SET TK_TrangThai={true} WHERE TK_ID={stagePersonA}");}
        });
        await StageCase("Inactive department blocks new handover but permits historical return",async()=>
        {
            var assets=await NewAssets(2);var held=await GiveAssets([assets[0]]);
            try
            {
                await Exec($"UPDATE tblphongban SET PB_TrangThai={false} WHERE PB_ID={stageDepartmentA}");
                Assert(await Status(c=>c.Create(Input([assets[1]])))==409,"Department inactive");
                var ret=await Create(SourceInput(held,await Lines(held),"Tot"),Return);await Submit(ret);await Confirm(ret,Return);
            }
            finally{await Exec($"UPDATE tblphongban SET PB_TrangThai={true} WHERE PB_ID={stageDepartmentA}");}
        });
        await StageCase("Return dates: future and before actual handover rejected",async()=>
        {
            var assets=await NewAssets(1);var give=await GiveAssets(assets);var ret=await Create(SourceInput(give,await Lines(give),"Tot"),Return);await Submit(ret);
            var v=await Version(ret);
            Assert(await Status(c=>c.ConfirmReturn(ret,new(){PhienBan=v,ThucHien=DateTimeOffset.UtcNow.AddDays(1)}))==409,"Future");
            Assert(await Status(c=>c.ConfirmReturn(ret,new(){PhienBan=v,ThucHien=DateTimeOffset.UtcNow.AddDays(-1)}))==409,"Before handover");
            Assert(await ActiveCount(assets)==1,"Still held");await Confirm(ret,Return);
        });
        await StageCase("Lost: responsibility stays active, no physical return, transfer blocked",async()=>
        {
            var assets=await NewAssets(1);var give=await GiveAssets(assets);var line=(await Lines(give))[0];
            Assert(await Status(c=>c.ReportLost(line.CTCP_ID,"Kiểm thử mất tài sản"))==302,"Report lost");
            Assert(await Status(c=>c.ReportLost(line.CTCP_ID,"Yêu cầu lặp"))==302,"Repeat lost");
            Assert(await ActiveCount(assets)==1,"Loss does not end responsibility");
            Assert(await Status(c=>c.CreateReturn(SourceInput(give,[line],"Tot")))==409,"Loss not received");
            Assert(await Status(c=>c.CreateTransfer(SourceInput(give,[line],recipient:stagePersonB,department:stageDepartmentB)))==409,"Loss not transferred");
            await using var db=StageDb();
            Assert(await db.tblSuKienCapPhat.CountAsync(x=>x.CP_ID==give&&x.SK_Loai=="BaoMat")==1,"One incident");
            Assert((await db.tblChiTietCapPhat.FindAsync(line.CTCP_ID))!.CTCP_NgayThuHoi==null,"No fabricated return");
        });
        await StageCase("Concurrent different receipts: exactly one handover of shared asset commits",async()=>
        {
            var assets=await NewAssets(1);var a=await Create(Input(assets));var b=await Create(Input(assets,stagePersonB,stageDepartmentB),actor:stageActorB);
            await Submit(a);await Submit(b);
            var outcomes=await Race(a,Give,b,Give);
            Assert(outcomes.Count(x=>x==302)==1&&outcomes.Count(x=>x==409)==1,"One winner: "+string.Join(",",outcomes));
            Assert(await ActiveCount(assets)==1,"Exactly one active episode");
        });
        await StageCase("Concurrent repeat confirmation of same receipt creates exactly one event",async()=>
        {
            var assets=await NewAssets(1);var id=await Create(Input(assets));await Submit(id);
            var outcomes=await Race(id,Give,id,Give);
            Assert(outcomes.All(x=>x==302),"Both requests return processed result");
            await using var db=StageDb();
            Assert(await ActiveCount(assets)==1&&await db.tblSuKienCapPhat.CountAsync(x=>x.CP_ID==id&&x.SK_Loai=="XacNhanGiao")==1,"No duplicated effect");
        });
        await StageCase("Concurrent duplicate create request is idempotent",async()=>
        {
            var assets=await NewAssets(1);var input=Input(assets);
            var ids=await Task.WhenAll(Create(input),Create(input));
            Assert(ids[0]==ids[1],"One receipt");
            await using var db=StageDb();
            Assert(await db.tblCapPhatTaiSan.CountAsync(x=>x.CP_RequestKey==input.RequestKey)==1,"Unique request enforced");
        });
        await StageCase("Concurrent returns: exactly one closes original custody",async()=>
        {
            var assets=await NewAssets(1);var give=await GiveAssets(assets);var source=await Lines(give);
            var a=await Create(SourceInput(give,source,"Tot"),Return);var b=await Create(SourceInput(give,source,"HuHong"),Return,stageActorB);
            await Submit(a);await Submit(b);
            var outcomes=await Race(a,Return,b,Return);
            Assert(outcomes.Count(x=>x==302)==1&&outcomes.Count(x=>x==409)==1,"One return winner");
            Assert(await ActiveCount(assets)==0,"Closed once");
            await using var db=StageDb();
            Assert(await db.tblSuKienCapPhat.CountAsync(x=>(x.CP_ID==a||x.CP_ID==b)&&x.SK_Loai=="XacNhanThuHoi")==1,"One return event");
        });
        await StageCase("Concurrent return versus transfer: exactly one transaction commits",async()=>
        {
            var assets=await NewAssets(1);var give=await GiveAssets(assets);var source=await Lines(give);
            var ret=await Create(SourceInput(give,source,"Tot"),Return);
            var transfer=await Create(SourceInput(give,source,recipient:stagePersonB,department:stageDepartmentB),Transfer,stageActorB);
            await Submit(ret);await Submit(transfer);
            var outcomes=await Race(ret,Return,transfer,Transfer);
            Assert(outcomes.Count(x=>x==302)==1&&outcomes.Count(x=>x==409)==1,"One terminal operation");
            await using var db=StageDb();var old=await db.tblChiTietCapPhat.FindAsync(source[0].CTCP_ID);
            Assert(old!.CTCP_TrangThai is ClosedReturn or ClosedTransfer,"One closure reason");
            Assert(await ActiveCount(assets)==(old.CTCP_TrangThai==ClosedTransfer?1:0),"Successor only for transfer");
        });
        foreach(var kind in new[]{Return,Transfer})
            await StageCase("Rollback "+kind+": error after closing old row restores all data",async()=>
            {
                var assets=await NewAssets(2);var give=await GiveAssets(assets);var source=await Lines(give);
                var input=kind==Return?SourceInput(give,source,"Tot"):SourceInput(give,source,recipient:stagePersonB,department:stageDepartmentB);
                var pending=await Create(input,kind);await Submit(pending);var v=await Version(pending);
                var code=await Status(c=>kind==Return?c.ConfirmReturn(pending,new(){PhienBan=v}):c.ConfirmTransfer(pending,new(){PhienBan=v}),null,new FailSecondSave());
                Assert(code==500,"Injected failure reported");
                Assert(await ActiveCount(assets)==2,"Old active rows restored");
                await using var db=StageDb();var old=await Load(db,give);var next=await Load(db,pending);
                Assert(old.tblChiTietCapPhats.All(x=>x.CTCP_KetThucUTC==null&&x.CTCP_NgayThuHoi==null),"No half closure");
                Assert(next.CP_TrangThai==Waiting&&next.tblChiTietCapPhats.All(x=>x.CTCP_BatDauUTC==null),"No new custody");
                Assert(await db.tblSuKienCapPhat.CountAsync(x=>x.CP_ID==pending&&x.SK_Loai=="XacNhan"+kind)==0,"No half audit");
            });
        await StageCase("Audit unique violation rolls back an already flushed old closure",async()=>
        {
            var assets=await NewAssets(1);var give=await GiveAssets(assets);var pending=await Create(SourceInput(give,await Lines(give),recipient:stagePersonB,department:stageDepartmentB),Transfer);await Submit(pending);
            string key;await using(var db=StageDb())key=await db.tblSuKienCapPhat.Where(x=>x.CP_ID==give).Select(x=>x.SK_RequestKey).FirstAsync();
            var v=await Version(pending);
            Assert(await Status(c=>c.ConfirmTransfer(pending,new(){PhienBan=v}),null,new DuplicateAuditKey(key))==409,"Unique conflict");
            Assert(await ActiveCount(assets)==1&&(await Lines(give))[0].CTCP_KetThucUTC==null,"Original restored");
        });
        await StageCase("Database unique constraint rejects a bypassed second active assignment",async()=>
        {
            var assets=await NewAssets(1);var a=await Create(Input(assets));var b=await Create(Input(assets));await Submit(a);await Confirm(a,Give);
            var line=(await Lines(b))[0];
            try{await Exec($"UPDATE tblchitietcapphat SET CTCP_TrangThai={Active} WHERE CTCP_ID={line.CTCP_ID}");throw new InvalidOperationException("Missing unique");}
            catch(MySqlException ex)when(ex.Number==1062){}
            Assert(await ActiveCount(assets)==1,"Unique protected even outside controller");
        });
        await StageCase("New foreign keys reject invalid receiver without changing row",async()=>
        {
            var id=await Create(Input(await NewAssets(1)));
            try{await Exec($"UPDATE tblcapphattaisan SET CP_NguoiNhanID={int.MaxValue} WHERE CP_ID={id}");throw new InvalidOperationException("Missing FK");}
            catch(MySqlException ex)when(ex.Number==1452){}
            await using var db=StageDb();Assert((await Load(db,id)).CP_NguoiNhanID==stagePersonA,"FK kept receiver");
        });
        foreach(var check in new[]{"CK_CP_Loai","CK_CP_HanTra","CK_CTCP_TrangThai","CK_CTCP_ThoiGian"})
            await StageCase("MariaDB enforces "+check+" against direct invalid SQL",async()=>
            {
                var id=await Create(Input(await NewAssets(1)));var line=(await Lines(id)).Single();
                FormattableString sql=check switch
                {
                    "CK_CP_Loai"=>$"UPDATE tblcapphattaisan SET CP_LoaiPhieu={"InvalidKind"} WHERE CP_ID={id}",
                    "CK_CP_HanTra"=>$"UPDATE tblcapphattaisan SET CP_NgayDuKien={new DateTime(2025,2,2)},CP_NgayTraDuKien={new DateTime(2025,1,1)} WHERE CP_ID={id}",
                    "CK_CTCP_TrangThai"=>$"UPDATE tblchitietcapphat SET CTCP_TrangThai={"InvalidState"} WHERE CTCP_ID={line.CTCP_ID}",
                    _=>$"UPDATE tblchitietcapphat SET CTCP_BatDauUTC={new DateTime(2025,2,2)},CTCP_KetThucUTC={new DateTime(2025,1,1)} WHERE CTCP_ID={line.CTCP_ID}"
                };
                try{await Exec(sql);throw new InvalidOperationException("CHECK was not enforced");}
                catch(MySqlException ex)when(ex.Number==4025){Assert(ex.Message.Contains(check),"Expected named CHECK");}
                await using var db=StageDb();var doc=await Load(db,id);
                Assert(doc.CP_LoaiPhieu==Give&&doc.CP_NgayTraDuKien==null&&doc.tblChiTietCapPhats.Single().CTCP_TrangThai==PendingGive&&doc.tblChiTietCapPhats.Single().CTCP_BatDauUTC==null,"Rejected SQL leaves row unchanged");
            });
        await StageCase("Lock timeout: real MariaDB 1205 becomes conflict with no partial transaction",async()=>
        {
            var assets=await NewAssets(1);var id=await Create(Input(assets));await Submit(id);var v=await Version(id);
            await using var holder=new MySqlConnection(stageChecksConnection);await holder.OpenAsync();await using var tx=await holder.BeginTransactionAsync();
            await RawLock(holder,tx,assets[0]);
            await using var db=StageDb();await db.Database.OpenConnectionAsync();await db.Database.ExecuteSqlRawAsync("SET SESSION innodb_lock_wait_timeout=1");
            var result=await StageController(db).ConfirmHandover(id,new(){PhienBan=v});
            Assert(result is ObjectResult{StatusCode:409,Value:string message}&&message.Contains("thời gian"),"Friendly real timeout");
            await tx.RollbackAsync();Assert(await ActiveCount(assets)==0,"No active row after timeout");
        });
        await StageCase("Real InnoDB deadlock is rolled back and reported as conflict",RealDeadlock);
        await StageCase("Existing category triggers reject invalid asset and survive workflow updates",async()=>
        {
            await using var db=StageDb();var inactive=new tblDanhMucTaiSan{DMTS_MaDanhMuc="S2_OFF",DMTS_TenDanhMuc="S2 inactive",DMTS_TrangThai=false};
            db.Add(inactive);await db.SaveChangesAsync();
            db.Add(new tblTaiSan{TS_MaTaiSan="S2_INVALID",TS_TenTaiSan="Must fail",DMTS_ID=inactive.DMTS_ID,TS_NguyenGia=1});
            try{await db.SaveChangesAsync();throw new InvalidOperationException("Trigger did not fire");}
            catch(DbUpdateException ex)when(ex.InnerException is MySqlException{Number:1644}){}
            Assert(await Scalar("SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE()")==7,"All original triggers remain");
        });
        await StageCase("Sprint 1 CRUD works on restored schema with all original triggers",Sprint1OnStage);
        await StageCase("MVC HTTP: write CSRF, backend roles, overposting, PRG and repeated confirmation",StageWriteHttp);
        await StageCase("Every integration write uses the disposable clone, never the primary source",async()=>
        {
            var builder=new MySqlConnectionStringBuilder(stageChecksConnection);
            Assert(Regex.IsMatch(builder.Database,"^qltaisan_sprint2_checks_[0-9a-f]{32}$"),"Isolated write target");
            await using var db=StageDb();
            Assert(db.Database.GetDbConnection().Database==builder.Database,"Context uses isolated clone");
        });
        await StageCase("Legacy recheck after all stage operations: identities, dates and asset 5 unchanged",async()=>
        {
            await using var db=StageDb();
            Assert(await db.tblChiTietCapPhat.CountAsync(x=>x.CTCP_ID<=5&&x.CTCP_TrangThai==null&&x.CTCP_DoiSoatUTC==null&&x.CTCP_NgayThuHoi==null)==5,"Old lines unchanged");
            Assert(await db.tblCapPhatTaiSan.CountAsync(x=>x.CP_ID<=5&&x.CP_NguoiNhanID==null&&x.CP_LoaiPhieu==null)==5,"Old identities unchanged");
            var a=await db.tblTaiSan.FindAsync(5);Assert(a!.TS_TrangThai=="ChuaCapPhat"&&a.TS_TinhTrang=="CanBaoTri","ID5 unchanged");
        });
    }

    private static async Task Exec(FormattableString sql){await using var db=StageDb();await db.Database.ExecuteSqlInterpolatedAsync(sql);}
    private sealed class LockBarrier
    {
        public readonly TaskCompletionSource Ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count;
    }
    private sealed class BarrierLock(LockBarrier barrier):DbCommandInterceptor
    {
        private bool used;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken token=default)
        {
            if(!used&&command.CommandText.Contains("FROM tbltaisan WHERE TS_ID")&&command.CommandText.Contains("FOR UPDATE"))
            {
                used=true;if(Interlocked.Increment(ref barrier.Count)==2)barrier.Ready.TrySetResult();
                await barrier.Ready.Task.WaitAsync(TimeSpan.FromSeconds(10),token);
            }
            return result;
        }
    }
    private static async Task<int[]> Race(int first,string firstKind,int second,string secondKind)
    {
        var a=await Version(first);var b=await Version(second);var barrier=new LockBarrier();
        Task<int> Run(int id,string kind,long version,int actor)=>Status(c=>kind switch
        {
            Return=>c.ConfirmReturn(id,new(){PhienBan=version}),
            Transfer=>c.ConfirmTransfer(id,new(){PhienBan=version}),
            _=>c.ConfirmHandover(id,new(){PhienBan=version})
        },actor,new BarrierLock(barrier));
        return await Task.WhenAll(Run(first,firstKind,a,stageActorA),Run(second,secondKind,b,stageActorB));
    }
    private sealed class FailCommit:DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,TransactionEventData data,InterceptionResult result,CancellationToken token=default)
            => throw new IOException("Injected pre-commit connection failure");
    }
    private sealed class FailSecondSave:SaveChangesInterceptor
    {
        private int calls;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {
            if(++calls==2)throw new InvalidOperationException("Injected failure after first flush");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class DuplicateAuditKey(string key):SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken token=default)
        {
            foreach(var entry in data.Context!.ChangeTracker.Entries<tblSuKienCapPhat>().Where(x=>x.State==EntityState.Added))
                entry.Entity.SK_RequestKey=key;
            return ValueTask.FromResult(result);
        }
    }
    private static async Task RawLock(MySqlConnection connection,MySqlTransaction transaction,int asset)
    {
        await using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT TS_ID FROM tbltaisan WHERE TS_ID=@id FOR UPDATE";command.Parameters.AddWithValue("@id",asset);await command.ExecuteScalarAsync();
    }
    private sealed class SignalSecondLock(int asset,TaskCompletionSource signal):DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken token=default)
        {
            if(command.CommandText.Contains("FROM tbltaisan WHERE TS_ID")&&command.CommandText.Contains("FOR UPDATE")&&command.Parameters.Cast<DbParameter>().Any(x=>Convert.ToInt32(x.Value)==asset))signal.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }
    private static async Task RealDeadlock()
    {
        var assets=await NewAssets(2);var extra=await NewAssets(20);var id=await Create(Input(assets));await Submit(id);var v=await Version(id);
        await using var connection=new MySqlConnection(stageChecksConnection);await connection.OpenAsync();await using var tx=await connection.BeginTransactionAsync();
        await RawLock(connection,tx,assets[1]);
        foreach(var asset in extra)
        {
            await using var update=connection.CreateCommand();update.Transaction=tx;update.CommandText="UPDATE tbltaisan SET TS_GhiChu='deadlock weight' WHERE TS_ID=@id";update.Parameters.AddWithValue("@id",asset);await update.ExecuteNonQueryAsync();
        }
        var signal=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var db=StageDb(new SignalSecondLock(assets[1],signal));
        var operation=StageController(db).ConfirmHandover(id,new(){PhienBan=v});
        await signal.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try{await RawLock(connection,tx,assets[0]);}
        catch(MySqlException ex)when(ex.Number==1213){throw new InvalidOperationException("Manual transaction unexpectedly chosen as deadlock victim");}
        var result=await operation;
        Assert(result is ObjectResult{StatusCode:409,Value:string message}&&message.Contains("Xung đột khóa"),"Real 1213 mapped to rollback conflict");
        await tx.RollbackAsync();Assert(await ActiveCount(assets)==0,"Deadlock left no custody");
    }
    private static async Task Sprint1OnStage()
    {
        await using var db=StageDb();var http=new DefaultHttpContext{Session=new MemorySession()};
        var c=new TaiSanController(db,NullLogger<TaiSanController>.Instance){ControllerContext=new ControllerContext{HttpContext=http},TempData=new TempDataDictionary(http,new MemoryTempData())};
        var code="S1"+Guid.NewGuid().ToString("N")[..20];
        var result=await c.Create(new AssetCreateViewModel{Ma=code,Ten="Sprint1 stage create",DanhMucId=stageCategory,NguyenGia=100,TinhTrang="Tot"});
        Assert(result is RedirectToActionResult,"S1 create");
        var asset=await db.tblTaiSan.SingleAsync(x=>x.TS_MaTaiSan==code);
        Assert(await c.Edit(asset.TS_ID,new AssetEditViewModel{Ten="Sprint1 stage edit",DanhMucId=stageCategory,NguyenGia=200,TinhTrang="Tot",Ma="SPOOF",TrangThai="DaThanhLy"}) is RedirectToActionResult,"S1 edit");
        Assert(asset.TS_MaTaiSan==code&&asset.TS_TrangThai=="ChuaCapPhat","Immutable fields");
        Assert(await c.DeleteConfirmed(asset.TS_ID) is RedirectToActionResult,"S1 unused delete");
        var assets=await NewAssets(1);var give=await GiveAssets(assets);
        Assert(await c.DeleteConfirmed(assets[0]) is ViewResult,"S1 history delete guard");
        var view=(TaiSanIndex)((ViewResult)await c.Index(null,null,null)).Model!;
        Assert(view.Total>0,"S1 list");
    }
    private static async Task StageWriteHttp()
    {
        var assets=await NewAssets(1);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(CapPhatController).Assembly.GetName().Name,ContentRootPath=Directory.GetCurrentDirectory(),EnvironmentName="Development"});
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<DataContext>(o=>o.UseMySql(stageChecksConnection,new MariaDbServerVersion(new Version(10,4,32))));
        builder.Services.AddDbContext<DBContext>(o=>o.UseMySql(stageChecksConnection,new MariaDbServerVersion(new Version(10,4,32))));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(CapPhatController).Assembly);
        await using var app=builder.Build();
        app.Use(async(http,next)=>
        {
            http.Session=new MemorySession();
            var actor=int.TryParse(http.Request.Headers["X-Stage-Actor"],out var requested)?requested:stageActorA;
            http.Session.SetInt32("TaiKhoanID",actor);http.Session.SetString("VaiTro","Admin");
            await next();
        });
        app.MapControllerRoute("areas","{area:exists}/{controller=Home}/{action=Index}/{id?}");
        app.MapControllerRoute("default","{controller=DangNhap}/{action=Index}/{id?}");
        await app.StartAsync();
        try
        {
            using var client=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false,CookieContainer=new System.Net.CookieContainer()}){BaseAddress=new Uri(app.Urls.Single())};
            var html=await client.GetStringAsync("/Admin/TaiSan/Create");
            var token=System.Net.WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Assert(token.Length>0,"CSRF token from Razor");
            var key=Guid.NewGuid().ToString("D");
            var form=new Dictionary<string,string>{{"__RequestVerificationToken",token},{"RequestKey",key},{"NguoiNhanId",stagePersonA.ToString()},{"PhongBanId",stageDepartmentA.ToString()},{"LyDo","HTTP stage"},{"Dongs[0].TaiSanId",assets[0].ToString()},{"CP_TrangThai",Handed},{"CP_NguoiLapID",stagePersonB.ToString()},{"CP_XacNhanUTC","2000-01-01"},{"TrangThai",Handed}};
            Assert((int)(await client.PostAsync("/Admin/CapPhat/Create",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode==400,"Missing token denied");
            var created=await client.PostAsync("/Admin/CapPhat/Create",new FormUrlEncodedContent(form));Assert((int)created.StatusCode==302,"PRG create");
            int id;await using(var db=StageDb())
            {
                var doc=await db.tblCapPhatTaiSan.SingleAsync(x=>x.CP_RequestKey==key);id=doc.CP_ID;
                Assert(doc.CP_TrangThai==Draft&&doc.CP_XacNhanUTC==null&&doc.CP_NguoiLapID==stageActorA,"Overposting ignored");
            }
            var v=await Version(id);
            var submitted=await client.PostAsync("/Admin/CapPhat/Submit/"+id,new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"phienBan",v.ToString()}}));
            Assert((int)submitted.StatusCode==302,"HTTP submit");
            v=await Version(id);
            var confirmForm=new Dictionary<string,string>{{"__RequestVerificationToken",token},{"PhienBan",v.ToString()}};
            Assert((int)(await client.PostAsync("/Admin/CapPhat/ConfirmHandover/"+id,new FormUrlEncodedContent(confirmForm))).StatusCode==302,"HTTP confirm");
            Assert((int)(await client.PostAsync("/Admin/CapPhat/ConfirmHandover/"+id,new FormUrlEncodedContent(confirmForm))).StatusCode==302,"HTTP repeat");
            client.DefaultRequestHeaders.Add("X-Stage-Actor",stagePersonA.ToString());
            Assert((int)(await client.PostAsync("/Admin/CapPhat/ConfirmHandover/"+id,new FormUrlEncodedContent(confirmForm))).StatusCode==403,"Employee cannot forge Admin session");
            Assert((int)(await client.GetAsync("/Admin/CapPhat/Details/"+id)).StatusCode==200,"Employee reads own receipt");
            client.DefaultRequestHeaders.Remove("X-Stage-Actor");
            client.DefaultRequestHeaders.Add("X-Stage-Actor",stagePersonB.ToString());
            Assert((int)(await client.GetAsync("/Admin/CapPhat/Details/"+id)).StatusCode==404,"Other employee cannot read a foreign receipt");
            client.DefaultRequestHeaders.Remove("X-Stage-Actor");
            foreach(var method in typeof(CapPhatController).GetMethods().Where(x=>x.GetCustomAttributes(typeof(HttpPostAttribute),true).Length>0))
                Assert(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute),true).Length>0,"CSRF on "+method.Name);
        }
        finally{await app.StopAsync();}
    }
}

