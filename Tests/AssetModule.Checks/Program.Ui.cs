using System.Net;
using System.Text.RegularExpressions;
using DoAn.Areas.Admin.Controllers;
using DoAn.Areas.Admin.Models;
using DoAn.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static DoAn.Areas.Admin.Models.GiaoNhanStates;

namespace AssetModule.Checks;

internal static partial class Program
{
    private static async Task RunSprint2UiChecks()
    {
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
            var actor=int.TryParse(http.Request.Headers["X-Stage-Actor"],out var id)?id:stageActorA;
            http.Session.SetInt32("TaiKhoanID",actor);http.Session.SetString("VaiTro","Admin");
            await next();
        });
        app.MapControllerRoute("areas","{area:exists}/{controller=Home}/{action=Index}/{id?}");
        app.MapControllerRoute("default","{controller=DangNhap}/{action=Index}/{id?}");
        await app.StartAsync();
        try
        {
            using var client=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false,CookieContainer=new CookieContainer()}){BaseAddress=new Uri(app.Urls.Single())};
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
            async Task<string> Page(string url)
            {
                var response=await client.GetAsync(url);var body=await response.Content.ReadAsStringAsync();
                Assert((int)response.StatusCode==200,"GET "+url+": "+(int)response.StatusCode+" "+body[..Math.Min(250,body.Length)]);return body;
            }
            string Field(string html,string name)
            {
                var tag=Regex.Matches(html,"<input\\b[^>]*>").Select(x=>x.Value).FirstOrDefault(x=>Regex.IsMatch(x,"name=\""+Regex.Escape(name)+"\""));
                return tag==null?"":WebUtility.HtmlDecode(Regex.Match(tag,"value=\"([^\"]*)\"").Groups[1].Value);
            }
            async Task<HttpResponseMessage> Post(string url,Dictionary<string,string> form,string? html=null)
            {
                html??=await Page("/Admin/CapPhat/Create");form["__RequestVerificationToken"]=Field(html,"__RequestVerificationToken");
                return await client.PostAsync(url,new FormUrlEncodedContent(form));
            }
            async Task<int> Save(string action,GiaoNhanInput input,string? html=null,int? id=null)
            {
                var response=await Post("/Admin/CapPhat/"+action+(id.HasValue?"/"+id:""),Form(input),html);
                var body=await response.Content.ReadAsStringAsync();
                Assert((int)response.StatusCode==302,"POST "+action+": "+(int)response.StatusCode+" "+WebUtility.HtmlDecode(body)[..Math.Min(300,body.Length)]);
                await using var db=StageDb();return await db.tblCapPhatTaiSan.Where(x=>x.CP_RequestKey==input.RequestKey).Select(x=>x.CP_ID).SingleAsync();
            }
            async Task Finish(int id,string kind)
            {
                var details=await Page("/Admin/CapPhat/Details/"+id);
                Assert((int)(await Post("/Admin/CapPhat/Submit/"+id,new(){{"phienBan",(await Version(id)).ToString()}},details)).StatusCode==302,"HTTP submit");
                var action=kind==Return?"ConfirmReturn":kind==Transfer?"ConfirmTransfer":"ConfirmHandover";
                var page=await Page("/Admin/CapPhat/"+action+"/"+id);
                Assert(page.Contains("business-confirmation")&&Field(page,"PhienBan")== (await Version(id)).ToString(),"Razor confirmation/version");
                var post=new Dictionary<string,string>{{"PhienBan",Field(page,"PhienBan")}};
                Assert((int)(await Post("/Admin/CapPhat/"+action+"/"+id,post,page)).StatusCode==302,"HTTP confirmation "+kind);
                Assert((int)(await Post("/Admin/CapPhat/"+action+"/"+id,post,page)).StatusCode==302,"Repeated confirmation stays idempotent");
            }
            var assets=await NewAssets(3);int give=0,ret=0,move=0;
            await StageCase("MVC UI: multi-asset create/edit, validation redisplay, waiting and confirmation",async()=>
            {
                var html=await Page("/Admin/CapPhat/Create");
                Assert(Field(html,"RequestKey").Length==36&&html.Contains("asset-options"),"Draft form and request key");
                var chooser=await Page("/Admin/CapPhat/AssetChoices?keyword=Stage%20asset&page=1");Assert(chooser.Contains("asset-add"),"Asset chooser renders real availability");
                var input=Input(assets);input.RequestKey=Field(html,"RequestKey");input.GhiChu="<script>ui unsafe</script>";
                var invalid=Form(input);invalid["LyDo"]="";
                var bad=await Post("/Admin/CapPhat/Create",invalid,html);var badBody=await bad.Content.ReadAsStringAsync();
                Assert((int)bad.StatusCode==400&&badBody.Contains("receipt-form")&&badBody.Contains(assets[0].ToString())&&!badBody.Contains("<script>ui unsafe"),"Invalid form redisplays encoded selection");
                give=await Save("Create",input,html);Assert(await ActiveCount(assets)==0,"Draft does not reserve assets");
                var edit=await Page("/Admin/CapPhat/EditDraft/"+give);input.PhienBan=long.Parse(Field(edit,"PhienBan"));input.LyDo="Edited UI purpose";
                Assert(await Save("EditDraft",input,edit,give)==give,"Edit same draft");
                var stale=await Post("/Admin/CapPhat/EditDraft/"+give,Form(input),edit);
                Assert((int)stale.StatusCode==409&&(await stale.Content.ReadAsStringAsync()).Contains("receipt-form"),"Stale edit is a readable conflict");
                await Finish(give,Give);Assert(await ActiveCount(assets)==3,"Confirmed all three");
                var details=WebUtility.HtmlDecode(await Page("/Admin/CapPhat/Details/"+give));Assert(details.Contains("Edited UI purpose")&&details.Contains("Xác nhận giao"),"Details and audit timeline");
            });
            await StageCase("MVC UI: checked-row partial return, actual-time validation and preserved remaining custody",async()=>
            {
                Assert(give>0,"Prior UI handover");var source=await Lines(give);var html=await Page("/Admin/CapPhat/CreateReturn/"+give);
                var input=SourceInput(give,source,"Tot");input.RequestKey=Field(html,"RequestKey");
                input.Dongs[1].Chon=false;input.Dongs[2].Chon=false;input.Dongs[0].TinhTrangNhan="CanBaoTri";
                ret=await Save("CreateReturn",input,html);Assert((await Lines(ret)).Length==1&&await ActiveCount(assets)==3,"Unchecked rows omitted, draft leaves custody");
                await Submit(ret);var confirm=await Page("/Admin/CapPhat/ConfirmReturn/"+ret);
                var future=await Post("/Admin/CapPhat/ConfirmReturn/"+ret,new(){{"PhienBan",(await Version(ret)).ToString()},{"ThucHienVietNam",DateTime.UtcNow.AddHours(31).ToString("yyyy-MM-ddTHH:mm:ss")}},confirm);
                Assert((int)future.StatusCode==409&&await ActiveCount(assets)==3,"Future physical return rejected");
                Assert((int)(await Post("/Admin/CapPhat/ConfirmReturn/"+ret,new(){{"PhienBan",(await Version(ret)).ToString()}},confirm)).StatusCode==302,"Actual receipt");
                Assert(await ActiveCount(assets)==2&&(await Lines(give))[1].CTCP_KetThucUTC==null,"Other assets still held");
            });
            await StageCase("MVC UI: selected transfer and full return keep exact responsibility boundaries",async()=>
            {
                var source=(await Lines(give)).Where(x=>x.CTCP_TrangThai==Active).ToArray();
                var html=await Page("/Admin/CapPhat/CreateTransfer/"+give);var input=SourceInput(give,source,recipient:stagePersonB,department:stageDepartmentB);
                input.RequestKey=Field(html,"RequestKey");input.Dongs[1].Chon=false;
                move=await Save("CreateTransfer",input,html);await Finish(move,Transfer);
                var old=(await Lines(give)).Single(x=>x.CTCP_ID==source[0].CTCP_ID);var successor=(await Lines(move)).Single();
                Assert(old.CTCP_KetThucUTC==successor.CTCP_BatDauUTC&&old.CTCP_NgayThuHoi==null&&successor.CTCP_DongNguonID==old.CTCP_ID,"Atomic transfer boundary, no invented warehouse return");
                foreach(var original in new[]{give,move})
                {
                    var rows=(await Lines(original)).Where(x=>x.CTCP_TrangThai==Active).ToArray();
                    var page=await Page("/Admin/CapPhat/CreateReturn/"+original);var receive=SourceInput(original,rows,"Tot",department:original==move?stageDepartmentB:stageDepartmentA);receive.RequestKey=Field(page,"RequestKey");
                    var returned=await Save("CreateReturn",receive,page);await Finish(returned,Return);
                }
                Assert(await ActiveCount(assets)==0,"Full return of both remaining owners");
                var before=await Scalar("SELECT COUNT(*) FROM tblsukiencapphat");
                var print=await Page("/Admin/CapPhat/Print/"+give);Assert(print.Contains("window.print()")&&await Scalar("SELECT COUNT(*) FROM tblsukiencapphat")==before,"Print is read only");
                Assert((await Page("/Admin/CapPhat/History/"+source[0].TS_ID)).Contains("Nối từ dòng"),"History links transfer predecessor");
            });
            await StageCase("MVC UI: inspected damage/maintenance, lost/found, replay and stale-state controls",async()=>
            {
                var inspection=await Page("/Admin/CapPhat/Inspect/"+assets[0]);
                var form=new Dictionary<string,string>{{"RequestKey",Field(inspection,"RequestKey")},{"ExpectedState",Field(inspection,"ExpectedState")},{"ExpectedCondition",Field(inspection,"ExpectedCondition")},{"DaKiemTraThucTe","true"},{"TinhTrang","Tot"},{"LyDo","Physical repair verified in test"}};
                Assert((int)(await Post("/Admin/CapPhat/Inspect/"+assets[0],form,inspection)).StatusCode==302,"Inspection resolves received damaged asset");
                Assert((int)(await Post("/Admin/CapPhat/Inspect/"+assets[0],form,inspection)).StatusCode==302,"Inspection replay");
                form["RequestKey"]=Guid.NewGuid().ToString("D");Assert((int)(await Post("/Admin/CapPhat/Inspect/"+assets[0],form,inspection)).StatusCode==409,"Stale inspection state");
                var lostAssets=await NewAssets(1);var id=await GiveAssets(lostAssets);var line=(await Lines(id)).Single();
                var incident=await Page("/Admin/CapPhat/Incident/"+line.CTCP_ID);var lossKey=Field(incident,"RequestKey");
                var loss=new Dictionary<string,string>{{"RequestKey",lossKey},{"ExpectedCondition",Field(incident,"ExpectedCondition")},{"lyDo","Confirmed missing in UI check"}};
                Assert((int)(await Post("/Admin/CapPhat/ReportLost/"+line.CTCP_ID,loss,incident)).StatusCode==302,"Record lost");
                var found=await Page("/Admin/CapPhat/Incident/"+line.CTCP_ID);
                var find=new Dictionary<string,string>{{"RequestKey",Field(found,"RequestKey")},{"ExpectedCondition",Field(found,"ExpectedCondition")},{"ExpectedState",Field(found,"ExpectedState")},{"DaKiemTraThucTe","true"},{"TinhTrang","Tot"},{"LyDo","Found and checked"}};
                Assert((int)(await Post("/Admin/CapPhat/ResolveLoss/"+line.CTCP_ID,find,found)).StatusCode==302,"Record found");
                Assert((int)(await Post("/Admin/CapPhat/ReportLost/"+line.CTCP_ID,loss,incident)).StatusCode==302,"Replay old loss does not reopen event");
                await using var db=StageDb();var asset=await db.tblTaiSan.SingleAsync(x=>x.TS_ID==lostAssets[0]);
                Assert(asset.TS_TinhTrang=="Tot"&&asset.TS_TrangThai=="DaCapPhat"&&await ActiveCount(lostAssets)==1,"Found keeps responsibility until physical return");
            });
            await StageCase("MVC UI: employee own scope, no other-holder leaks and fresh account locking",async()=>
            {
                client.DefaultRequestHeaders.Add("X-Stage-Actor",stagePersonA.ToString());
                var own=await Page("/Admin/CapPhat/Details/"+give);Assert(!own.Contains("Sửa nháp")&&!own.Contains("Đối soát có bằng chứng"),"Own details have no write controls");
                foreach(var url in new[]{"/Admin/CapPhat","/Admin/CapPhat/ActiveAssignments","/Admin/CapPhat/History/"+assets[0],"/Admin/CapPhat/Print/"+give})await Page(url);
                Assert((int)(await client.GetAsync("/Admin/CapPhat/Details/1")).StatusCode==404,"Unmapped legacy not assigned to employee");
                Assert((int)(await client.GetAsync("/Admin/CapPhat/Create")).StatusCode==403,"No employee form");
                await Exec($"UPDATE tbltaikhoan SET TK_TrangThai=0 WHERE TK_ID={stagePersonA}");
                Assert((int)(await client.GetAsync("/Admin/CapPhat")).StatusCode==403,"Database lock overrides session role");
                await Exec($"UPDATE tbltaikhoan SET TK_TrangThai=1 WHERE TK_ID={stagePersonA}");
                client.DefaultRequestHeaders.Remove("X-Stage-Actor");
                var active=await Page("/Admin/CapPhat/ActiveAssignments?quaHan=true");Assert(active.Contains("QuaHan"),"Overdue filter Razor renders");
                await Page("/Admin/CapPhat/Reconciliation");
            });
            await StageCase("MVC UI: current damage/maintenance is audited without releasing custody",async()=>
            {
                var a=await NewAssets(1);var id=await GiveAssets(a);var row=(await Lines(id)).Single();
                var page=await Page("/Admin/CapPhat/Incident/"+row.CTCP_ID);
                var input=new Dictionary<string,string>{{"RequestKey",Field(page,"RequestKey")},{"ExpectedState",Field(page,"ExpectedState")},{"ExpectedCondition",Field(page,"ExpectedCondition")},{"TinhTrang","CanBaoTri"},{"DaKiemTraThucTe","true"},{"LyDo","Verified maintenance needed while in use"}};
                Assert((int)(await Post("/Admin/CapPhat/RecordCondition/"+row.CTCP_ID,input,page)).StatusCode==302,"Record condition");
                Assert((int)(await Post("/Admin/CapPhat/RecordCondition/"+row.CTCP_ID,input,page)).StatusCode==302,"Repeat condition request");
                Assert(await ActiveCount(a)==1&&await Status(c=>c.CreateTransfer(SourceInput(id,[row],recipient:stagePersonB,department:stageDepartmentB)))==409,"Maintenance blocks transfer, keeps custody");
                await using var db=StageDb();Assert(await db.tblSuKienCapPhat.CountAsync(x=>x.CP_ID==id&&x.SK_Loai=="GhiNhanTinhTrang")==1,"Single audit event");
                var ret=await Create(SourceInput(id,[row],"HuHong"),Return);await Submit(ret);await Confirm(ret,Return);
                Assert(await ActiveCount(a)==0&&(await db.tblTaiSan.AsNoTracking().SingleAsync(x=>x.TS_ID==a[0])).TS_TrangThai=="ChoXuLy","Physical return keeps damaged asset unavailable");
            });
            await StageCase("MVC UI: overdue custody and readable submit/cancel conflicts",async()=>
            {
                var a=await NewAssets(1);var input=Input(a);var today=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));input.NgayDuKien=today.AddDays(-4);input.NgayTraDuKien=today.AddDays(-1);
                var id=await Create(input);await Submit(id);await Confirm(id,Give);
                await using(var db=StageDb())
                {
                    var rows=(GiaoNhanRows)((Microsoft.AspNetCore.Mvc.ViewResult)await StageController(db).ActiveAssignments(quaHan:true)).Model!;
                    Assert(rows.Items.Any(x=>x.PhieuId==id&&x.SoNgayQuaHan==1),"Overdue data computed from active responsibility");
                }
                var page=await Page("/Admin/CapPhat/Details/"+id);
                var cancel=await Post("/Admin/CapPhat/Cancel/"+id,new(){{"phienBan",(await Version(id)).ToString()},{"lyDo","Cannot cancel confirmed"}},page);
                Assert((int)cancel.StatusCode==302&&cancel.Headers.Location!.ToString().Contains("Details"),"Browser conflict redirects to document");
                var feedback=WebUtility.HtmlDecode(await Page(cancel.Headers.Location!.ToString()));Assert(feedback.Contains("Chỉ được hủy nháp"),"Readable error feedback");
                Assert(await ActiveCount(a)==1,"Failed cancel never releases custody");
            });
            await StageCase("MVC UI: explicit synthetic legacy reconciliation preserves original facts and enables return",async()=>
            {
                var asset=(await NewAssets(1))[0];int id;var originalName="Synthetic unidentified historical person";
                await using(var db=StageDb())
                {
                    var doc=new tblCapPhatTaiSan{CP_MaPhieu="UI"+Guid.NewGuid().ToString("N")[..24],CP_NguoiGiaoID=stageActorA,PB_ID=stageDepartmentA,CP_NguoiNhan=originalName,CP_NgayCapPhat=new(2025,1,1),tblChiTietCapPhats=[new(){TS_ID=asset}]};db.Add(doc);await db.SaveChangesAsync();id=doc.CP_ID;
                }
                var page=await Page("/Admin/CapPhat/ReconcileLegacy/"+id);
                var form=new Dictionary<string,string>{{"RequestKey",Field(page,"RequestKey")},{"NguoiNhanId",stagePersonA.ToString()},{"BanGiaoThucTeVietNam","2025-01-01T09:00"},{"CanCu","Synthetic signed evidence only on disposable clone"}};
                Assert((int)(await Post("/Admin/CapPhat/ReconcileLegacy/"+id,form,page)).StatusCode==400,"Evidence attestation mandatory");
                form["DaXacMinhDanhTinhVaHienVat"]="true";
                Assert((int)(await Post("/Admin/CapPhat/ReconcileLegacy/"+id,form,page)).StatusCode==302,"Explicit verified mapping");
                Assert((int)(await Post("/Admin/CapPhat/ReconcileLegacy/"+id,form,page)).StatusCode==302,"Reconcile replay");
                await using(var db=StageDb())
                {
                    var doc=await Load(db,id);Assert(doc.CP_NguoiNhan==originalName&&doc.CP_NgayCapPhat==new DateOnly(2025,1,1)&&doc.CP_TrangThai=="DaCapPhat"&&doc.CP_LoaiPhieu==null,"Original legacy fields retained");
                    Assert((await db.tblTaiSan.FindAsync(asset))!.TS_TrangThai=="ChuaCapPhat","No automatic asset state repair");
                    Assert(await db.tblCapPhatTaiSan.CountAsync(x=>x.CP_ID<=5&&x.CP_NguoiNhanID==null)==5,"Original five never mapped");
                }
                var receipt=await Create(SourceInput(id,await Lines(id),"Tot"),Return);await Submit(receipt);await Confirm(receipt,Return);Assert(await ActiveCount([asset])==0,"Reconciled legacy can be physically returned");
            });
            await StageCase("MVC UI: transfer explains missing departments and every asset blocker without changing custody",async()=>
            {
                var a=await NewAssets(2);var id=await GiveAssets(a);
                await using(var db=StageDb())
                {
                    var blocked=await db.tblTaiSan.SingleAsync(x=>x.TS_ID==a[0]);blocked.TS_TrangThai="ChuaCapPhat";blocked.TS_TinhTrang="ThieuPhuKien";
                    db.tblBaoTriTaiSan.Add(new(){TS_ID=a[0],BT_MaPhieu="UIBT"+Guid.NewGuid().ToString("N")[..20],BT_NguoiLapID=stageActorA,BT_TrangThai="ChoXuLy",BT_NgayBaoTri=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)),BT_LyDo="Synthetic transfer blocker",BT_ChiPhi=0});
                    await db.SaveChangesAsync();
                }
                await using var snapshot=StageDb();
                var accounts=await snapshot.tblTaiKhoan.AsNoTracking().Where(x=>x.PB_ID!=null).Select(x=>new{x.TK_ID,x.PB_ID}).ToListAsync();
                var oldName=await snapshot.tblTaiKhoan.Where(x=>x.TK_ID==stagePersonA).Select(x=>x.TK_HoTen).SingleAsync();
                try
                {
                    foreach(var account in accounts)await Exec($"UPDATE tbltaikhoan SET PB_ID=NULL WHERE TK_ID={account.TK_ID}");
                    await Exec($"UPDATE tbltaikhoan SET TK_HoTen={"Verified current holder"} WHERE TK_ID={stagePersonA}");
                    var html=WebUtility.HtmlDecode(await Page("/Admin/CapPhat/CreateTransfer/"+id));
                    Assert(html.Contains("Chưa gán phòng ban")&&html.Contains("Chưa có tài khoản đủ điều kiện"),"Exact account setup reason instead of claiming accounts are locked");
                    Assert(html.Contains("Thiếu phụ kiện")&&html.Contains("Trạng thái tài sản: Chưa cấp phát")&&html.Contains("Còn hồ sơ bảo trì chưa hoàn thành"),"Every transfer blocker visible before submit");
                    Assert(html.Contains("Verified current holder")&&html.Contains("Tên lưu trên phiếu gốc"),"Verified current holder distinct from preserved receipt text");
                    var blocked=Regex.Matches(html,"<input\\b[^>]*>").Select(x=>x.Value).Single(x=>x.Contains("name=\"Dongs[0].Chon\"")&&x.Contains("type=\"checkbox\""));
                    var good=Regex.Matches(html,"<input\\b[^>]*>").Select(x=>x.Value).Single(x=>x.Contains("name=\"Dongs[1].Chon\"")&&x.Contains("type=\"checkbox\""));
                    Assert(blocked.Contains("disabled")&&!good.Contains("disabled"),"Only blocked asset selection disabled");
                    Assert(!html.Contains("class=\"alert alert-danger\""),"No empty error summary on valid GET");
                    Assert(await ActiveCount(a)==2,"Read-only diagnosis preserves custody");
                }
                finally
                {
                    foreach(var account in accounts)await Exec($"UPDATE tbltaikhoan SET PB_ID={account.PB_ID} WHERE TK_ID={account.TK_ID}");
                    await Exec($"UPDATE tbltaikhoan SET TK_HoTen={oldName} WHERE TK_ID={stagePersonA}");
                }
                var source=(await Lines(id))[0];
                Assert(await Status(c=>c.CreateTransfer(SourceInput(id,[source],recipient:stagePersonB,department:stageDepartmentB)))==409,"Backend still rejects forged blocked selection");
            });
            await StageCase("MVC UI: manual account department, hashed/legacy login compatibility and account write authorization",async()=>
            {
                var page=await Page("/Admin/TaiKhoan/Create");Assert(page.Contains("name=\"PB_ID\""),"Department field present");
                var username="ui_"+Guid.NewGuid().ToString("N")[..20];var password="Ui-test-Password-82";
                var form=new Dictionary<string,string>{{"TK_ID","1"},{"TK_TenDangNhap",username},{"TK_MatKhau",password},{"TK_HoTen","UI synthetic account"},{"TK_VaiTro","NhanVien"},{"TK_TrangThai","true"},{"PB_ID",stageDepartmentA.ToString()}};
                Assert((int)(await Post("/Admin/TaiKhoan/Create",form,page)).StatusCode==302,"Account created with manual department");
                await using(var db=StageDb())
                {
                    var account=await db.tblTaiKhoan.SingleAsync(x=>x.TK_TenDangNhap==username);
                    Assert(account.TK_ID>5&&account.PB_ID==stageDepartmentA&&BCrypt.Net.BCrypt.Verify(password,account.TK_MatKhau),"Identity overpost ignored, hashed password");
                }
                var login=await Page("/DangNhap/Index");
                Assert((int)(await Post("/DangNhap/Index",new(){{"TenDangNhap",username},{"MatKhau",password}},login)).StatusCode==302,"New hashed account logs in");
                Assert((int)(await Post("/DangNhap/Index",new(){{"TenDangNhap","stage_person_a"},{"MatKhau","unused"}},login)).StatusCode==302,"Legacy password compatibility");
                client.DefaultRequestHeaders.Add("X-Stage-Actor",stagePersonA.ToString());
                Assert((int)(await Post("/Admin/TaiKhoan/Create",form,page)).StatusCode==403,"Employee cannot edit department through admin endpoint");client.DefaultRequestHeaders.Remove("X-Stage-Actor");
            });
        }
        finally{await app.StopAsync();}
    }

    private static Dictionary<string,string> Form(GiaoNhanInput input)
    {
        var data=new Dictionary<string,string>{{"RequestKey",input.RequestKey},{"PhienBan",input.PhienBan.ToString()},{"PhongBanId",input.PhongBanId.ToString()},{"NguoiNhanId",input.NguoiNhanId?.ToString()??""},{"PhieuGocId",input.PhieuGocId?.ToString()??""},{"LoaiDoiTuong",input.LoaiDoiTuong},{"NgayDuKien",input.NgayDuKien?.ToString("yyyy-MM-dd")??""},{"NgayTraDuKien",input.NgayTraDuKien?.ToString("yyyy-MM-dd")??""},{"LyDo",input.LyDo},{"GhiChu",input.GhiChu??""}};
        for(var i=0;i<input.Dongs.Count;i++)
        {
            var row=input.Dongs[i];var key=$"Dongs[{i}].";
            data[key+"TaiSanId"]=row.TaiSanId.ToString();data[key+"DongNguonId"]=row.DongNguonId?.ToString()??"";
            data[key+"Chon"]=row.Chon.ToString();data[key+"TinhTrangNhan"]=row.TinhTrangNhan??"";data[key+"GhiChu"]=row.GhiChu??"";data[key+"PhuKien"]=row.PhuKien??"";data[key+"ViTri"]=row.ViTri??"";
        }
        return data;
    }
}
