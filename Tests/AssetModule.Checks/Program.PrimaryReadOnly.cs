using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DoAn.Areas.Admin.Controllers;
using DoAn.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace AssetModule.Checks;

internal static partial class Program
{
    private static async Task<int> RunPrimaryReadOnly()
    {
        using var config=JsonDocument.Parse(await File.ReadAllTextAsync("appsettings.json"));
        var connection=new MySqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!);
        Assert(connection.Database.Equals("QuanLyTaiSan",StringComparison.OrdinalIgnoreCase)&&connection.Port==3307&&connection.Server is "localhost" or "127.0.0.1","Exact primary target required for read-only checks");
        var release=(await File.ReadAllTextAsync(Path.Combine(".local-backups","sprint2-stage-20261108","latest-primary-release-path.txt"))).Trim();
        var before=await RunNativeDatabaseTool(connection,false);
        await StageCase("Primary read-only: complete dump equals verified post-upgrade backup",async()=>
        {
            Assert(before==await File.ReadAllTextAsync(Path.Combine(release,"QuanLyTaiSan-immediate-post-upgrade.sql")),"Complete data/schema/trigger dump drift detected");
        });
        await using var db=new DataContext(new DbContextOptionsBuilder<DataContext>().UseMySql(connection.ConnectionString,new MariaDbServerVersion(new Version(10,4,32))).Options);
        var adminId=await db.tblTaiKhoan.AsNoTracking().Where(x=>x.TK_TrangThai&&x.TK_VaiTro=="Admin").Select(x=>x.TK_ID).FirstAsync();
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(CapPhatController).Assembly.GetName().Name,ContentRootPath=Directory.GetCurrentDirectory(),EnvironmentName="Development"});
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<DataContext>(o=>o.UseMySql(connection.ConnectionString,new MariaDbServerVersion(new Version(10,4,32))));
        builder.Services.AddDbContext<DBContext>(o=>o.UseMySql(connection.ConnectionString,new MariaDbServerVersion(new Version(10,4,32))));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(CapPhatController).Assembly);
        await using var app=builder.Build();
        app.Use(async(http,next)=>
        {
            if(!HttpMethods.IsGet(http.Request.Method)&&!HttpMethods.IsHead(http.Request.Method)){http.Response.StatusCode=405;return;}
            http.Session=new MemorySession();http.Session.SetInt32("TaiKhoanID",adminId);http.Session.SetString("VaiTro","Admin");await next();
        });
        app.MapControllerRoute("areas","{area:exists}/{controller=Home}/{action=Index}/{id?}");
        app.MapControllerRoute("default","{controller=DangNhap}/{action=Index}/{id?}");
        await app.StartAsync();
        var urls=new[]{"/Admin/CapPhat","/Admin/CapPhat/Details/1","/Admin/CapPhat/History/5","/Admin/CapPhat/ActiveAssignments","/Admin/CapPhat/Reconciliation","/Admin/CapPhat/Create","/Admin/CapPhat/CreateReturn/1","/Admin/CapPhat/AssetChoices","/Admin/CapPhat/Print/1","/Admin/CapPhat/ReconcileLegacy/1","/Admin/TaiSan","/Admin/TaiKhoan/Edit/"+adminId};
        try
        {
            using var client=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){BaseAddress=new Uri(app.Urls.Single())};
            await StageCase("Primary read-only: 12 authenticated MVC pages use actual preserved data",async()=>
            {
                foreach(var url in urls)
                {
                    var response=await client.GetAsync(url);Assert((int)response.StatusCode==200,url+" returned "+(int)response.StatusCode);
                    if(url=="/Admin/CapPhat/Reconciliation")Assert((await response.Content.ReadAsStringAsync()).Contains("5 dòng phù hợp"),"Five unresolved rows visible");
                }
            });
        }
        finally{await app.StopAsync();}
        await StageCase("Primary read-only: zero data/schema changes after all MVC GETs",async()=>
        {
            Assert(before==await RunNativeDatabaseTool(connection,false),"Read-only MVC run changed database");
            Assert(await db.tblSuKienCapPhat.CountAsync()==0,"No audit fabricated");
            Assert(await db.tblCapPhatTaiSan.CountAsync(x=>x.CP_NguoiNhanID==null&&x.CP_LoaiPhieu==null)==5,"Five recipients unmapped");
            var asset=await db.tblTaiSan.AsNoTracking().SingleAsync(x=>x.TS_ID==5);Assert(asset.TS_TrangThai=="ChuaCapPhat"&&asset.TS_TinhTrang=="CanBaoTri","Asset 5 unchanged");
        });
        await File.WriteAllTextAsync(Path.Combine(".local-backups","sprint2-primary-readonly-results.json"),JsonSerializer.Serialize(new{Database=connection.Database,Port=connection.Port,ReadOnly=true,Passed=passed,Failed=failed,Pages=urls,DumpSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(before))),Results=stageOutcomes},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PRIMARY READ-ONLY RESULT: {passed} passed, {failed} failed; no DDL or DML executed");
        return failed==0?0:1;
    }
}
