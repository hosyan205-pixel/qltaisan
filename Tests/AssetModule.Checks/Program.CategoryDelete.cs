using System.Data.Common;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoAn.Areas.Admin.Controllers;
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
    private const string CategoryDeleteTrigger = """
        CREATE OR REPLACE TRIGGER category_before_delete
        BEFORE DELETE ON tbldanhmuctaisan
        FOR EACH ROW
        BEGIN
            IF EXISTS (SELECT 1 FROM tbltaisan WHERE DMTS_ID = OLD.DMTS_ID) THEN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Category has assets';
            END IF;
            IF EXISTS (SELECT 1 FROM tbldanhmuctaisan WHERE ParentId = OLD.DMTS_ID) THEN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Category has child categories';
            END IF;
            IF EXISTS (SELECT 1 FROM categoryattributedefinition WHERE CategoryId = OLD.DMTS_ID) THEN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Category has attribute definitions';
            END IF;
            IF EXISTS (SELECT 1 FROM categoryauditlog WHERE CategoryId = OLD.DMTS_ID) THEN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Category has audit history';
            END IF;
        END
        """;

    private static async Task<int> RunCategoryDeleteChecks()
    {
        using var config=JsonDocument.Parse(await File.ReadAllTextAsync("appsettings.json"));
        var source=new MySqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!);
        Assert(source.Database.Equals("QuanLyTaiSan",StringComparison.OrdinalIgnoreCase)&&source.Port==3307&&source.Server is "localhost" or "127.0.0.1","Expected local primary read source");
        var evidence=Path.GetFullPath(Path.Combine(".local-backups","category-delete-fix-"+DateTime.UtcNow.AddHours(7).ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(evidence);
        await File.WriteAllTextAsync(Path.Combine(".local-backups","latest-category-delete-path.txt"),evidence,new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(evidence,"category-before-delete.sql"),"DELIMITER $$\n"+CategoryDeleteTrigger+"$$\nDELIMITER ;\n",new UTF8Encoding(false));
        var baseline=await RunNativeDatabaseTool(source,false);
        await File.WriteAllTextAsync(Path.Combine(evidence,"source-before-tests.sql"),baseline,new UTF8Encoding(false));
        var scratch="qltaisan_sprint2_checks_"+Guid.NewGuid().ToString("N");
        var connection=new MySqlConnectionStringBuilder(source.ConnectionString){Database=scratch};
        var adminConnection=new MySqlConnectionStringBuilder(source.ConnectionString){Database=""};
        await using var admin=new MySqlConnection(adminConnection.ConnectionString);await admin.OpenAsync();
        var created=false;
        try
        {
            await using(var cmd=admin.CreateCommand()){cmd.CommandText="CREATE DATABASE "+scratch+" CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci";await cmd.ExecuteNonQueryAsync();created=true;}
            await RunNativeDatabaseTool(connection,true,baseline);stageChecksConnection=connection.ConnectionString;
            await StageCase("Category backup restores all data, schema, indexes, FK and seven triggers exactly",async()=>
            {
                Assert(await RunNativeDatabaseTool(connection,false)==baseline,"Restored full dump differs");
                Assert(await Scalar("SELECT COUNT(*) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE()")==7,"Seven triggers restored");
            });
            if(failed>0)return 1;
            var triggers=await CategoryTriggerRows();
            int actor,employee,locked;
            await using(var db=StageDb())
            {
                var accounts=new[]{new tblTaiKhoan{TK_TenDangNhap="cd_a_"+Guid.NewGuid().ToString("N")[..16],TK_HoTen="Category checks admin",TK_VaiTro="Admin",TK_MatKhau="unused"},new tblTaiKhoan{TK_TenDangNhap="cd_e_"+Guid.NewGuid().ToString("N")[..16],TK_HoTen="Category checks employee",TK_MatKhau="unused"},new tblTaiKhoan{TK_TenDangNhap="cd_l_"+Guid.NewGuid().ToString("N")[..16],TK_HoTen="Category checks locked admin",TK_VaiTro="Admin",TK_TrangThai=false,TK_MatKhau="unused"}};
                db.AddRange(accounts);await db.SaveChangesAsync();actor=accounts[0].TK_ID;employee=accounts[1].TK_ID;locked=accounts[2].TK_ID;
            }
            var empty=await NewDeleteCategory();
            await StageCase("Old unconditional delete trigger returns a friendly message instead of an exception page",async()=>
            {
                await CategoryExecute("CREATE OR REPLACE TRIGGER category_before_delete BEFORE DELETE ON tbldanhmuctaisan FOR EACH ROW BEGIN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Use Active/Inactive instead of deleting a category'; END");
                var result=await DeleteCategory(empty,actor);
                Assert(result.Error?.Contains("Database hiện đang chặn") == true&&await CategoryExists(empty),"Old database policy is handled without deleting");
            });
            await CategoryExecute(CategoryDeleteTrigger);
            await StageCase("Unreferenced category is physically deleted after trigger fix",async()=>
            {
                var result=await DeleteCategory(empty,actor);Assert(result.Error==null&&!await CategoryExists(empty),"Empty category deleted");
            });
            await StageCase("Original empty category DM006 can be deleted on the restored copy only",async()=>
            {
                await using var db=StageDb();var original=await db.tblDanhMucTaiSan.AsNoTracking().SingleOrDefaultAsync(x=>x.DMTS_MaDanhMuc=="DM006");
                Assert(original!=null,"Reproducing current empty category");
                var result=await DeleteCategory(original!.DMTS_ID,actor);Assert(result.Error==null&&!await CategoryExists(original.DMTS_ID),"Restored DM006 deleted");
            });
            await StageCase("Active and inactive empty categories are both deletable",async()=>
            {
                var id=await NewDeleteCategory();await Exec($"UPDATE tbldanhmuctaisan SET DMTS_TrangThai=0 WHERE DMTS_ID={id}");
                Assert((await DeleteCategory(id,actor)).Error==null&&!await CategoryExists(id),"Inactive empty category deleted");
            });
            await StageCase("Category with assets remains protected by controller, trigger and FK",async()=>
            {
                var id=await NewDeleteCategory();await AddDeleteAsset(id);
                var result=await DeleteCategory(id,actor);Assert(result.Error?.Contains("tài sản")==true&&await CategoryExists(id),"Existing assets prevent deletion");
                await RejectCategorySqlDelete(id,"Category has assets");
            });
            await StageCase("Parent with an active or inactive child cannot be deleted",async()=>
            {
                var parent=await NewDeleteCategory();var child=await NewDeleteCategory();await Exec($"UPDATE tbldanhmuctaisan SET ParentId={parent} WHERE DMTS_ID={child}");
                Assert((await DeleteCategory(parent,actor)).Error?.Contains("danh mục con")==true,"Active child protected");
                await Exec($"UPDATE tbldanhmuctaisan SET DMTS_TrangThai=0 WHERE DMTS_ID={child}");
                Assert((await DeleteCategory(parent,actor)).Error?.Contains("danh mục con")==true,"Inactive child protected");
                Assert((await DeleteCategory(child,actor)).Error==null&&(await DeleteCategory(parent,actor)).Error==null,"Empty leaf can be removed before empty parent");
            });
            await StageCase("Category attribute definitions prevent deletion even without assets",async()=>
            {
                var id=await NewDeleteCategory();await Exec($"INSERT INTO categoryattributedefinition(CategoryId,Code,Name,DataType) VALUES({id},{"CHECK_ATTR"},{"Check attribute"},{"Text"})");
                Assert((await DeleteCategory(id,actor)).Error?.Contains("thuộc tính")==true&&await CategoryExists(id),"Attribute definition preserved");
                await RejectCategorySqlDelete(id,"Category has attribute definitions");
            });
            await StageCase("Category audit history prevents deletion and is never erased",async()=>
            {
                var id=await NewDeleteCategory();await Exec($"INSERT INTO categoryauditlog(CategoryId,ActorId,Action,OccurredAt,AfterJson) VALUES({id},{actor},{"Create"},{DateTime.UtcNow},{"{}"})");
                Assert((await DeleteCategory(id,actor)).Error?.Contains("lịch sử")==true&&await CategoryExists(id),"Audit category protected");
                await RejectCategorySqlDelete(id,"Category has audit history");
                Assert(await Scalar("SELECT COUNT(*) FROM categoryauditlog WHERE CategoryId="+id)==1,"Audit row preserved");
            });
            await StageCase("Asset inserted after application precheck still blocks category deletion safely",async()=>
            {
                var id=await NewDeleteCategory();var race=new BeforeCategoryDelete(async()=>await AddDeleteAsset(id));
                await using var db=StageDb(race);var controller=CategoryController(db,actor);var result=await controller.Delete(id);
                Assert(result is RedirectToActionResult&&controller.TempData["Error"] is string message&&message.Contains("tài sản")&&await CategoryExists(id),"Concurrent relation is handled without exception");
            });
            await StageCase("Missing or already deleted category returns 404",async()=>
            {
                var id=await NewDeleteCategory();await DeleteCategory(id,actor);await using var db=StageDb();
                Assert(await CategoryController(db,actor).Delete(id) is NotFoundResult,"Repeated delete is safe");
            });
            await StageCase("Category MVC HTTP enforces CSRF, real account roles and route IDs",()=>CategoryDeleteHttp(actor,employee,locked));
            await StageCase("Only category delete trigger changed; other six trigger definitions remain identical",async()=>
            {
                var now=await CategoryTriggerRows();Assert(now.Length==7&&triggers.Where(x=>!x.StartsWith("category_before_delete\t")).SequenceEqual(now.Where(x=>!x.StartsWith("category_before_delete\t"))),"Other triggers unchanged");
            });
            await StageCase("Primary database data and schema were not modified by category checks",async()=>Assert(await RunNativeDatabaseTool(source,false)==baseline,"Primary changed during checks"));
            await File.WriteAllTextAsync(Path.Combine(evidence,"checks.json"),JsonSerializer.Serialize(new{Passed=passed,Failed=failed,BackupRestoredExactly=true,Database=scratch,Results=stageOutcomes},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"CATEGORY DELETE RESULT: {passed} passed, {failed} failed. Evidence: {evidence}");
            return failed==0?0:1;
        }
        finally
        {
            if(created){Assert(Regex.IsMatch(scratch,"^qltaisan_sprint2_checks_[0-9a-f]{32}$"),"Unsafe category cleanup");await using var cmd=admin.CreateCommand();cmd.CommandText="DROP DATABASE "+scratch;await cmd.ExecuteNonQueryAsync();Console.WriteLine("Removed only the disposable category checks database.");}
        }
    }

    private static async Task CategoryExecute(string sql){await using var db=StageDb();await db.Database.ExecuteSqlRawAsync(sql);}
    private static async Task<bool> CategoryExists(int id){await using var db=StageDb();return await db.tblDanhMucTaiSan.AsNoTracking().AnyAsync(x=>x.DMTS_ID==id);}
    private static async Task<int> NewDeleteCategory()
    {
        await using var db=StageDb();var item=new tblDanhMucTaiSan{DMTS_MaDanhMuc="CD"+Guid.NewGuid().ToString("N")[..24],DMTS_TenDanhMuc="Delete check "+Guid.NewGuid().ToString("N")[..12]};db.Add(item);await db.SaveChangesAsync();return item.DMTS_ID;
    }
    private static async Task AddDeleteAsset(int category)
    {
        await using var db=StageDb();db.Add(new tblTaiSan{TS_MaTaiSan="CD"+Guid.NewGuid().ToString("N")[..24],TS_TenTaiSan="Category relation check",DMTS_ID=category,TS_NguyenGia=1});await db.SaveChangesAsync();
    }
    private static DanhMucController CategoryController(DataContext db,int actor)
    {
        var http=new DefaultHttpContext{Session=new MemorySession()};http.Session.SetInt32("TaiKhoanID",actor);http.Session.SetString("VaiTro","Admin");
        return new(db,NullLogger<DanhMucController>.Instance){ControllerContext=new ControllerContext(new ActionContext(http,new RouteData(),new ControllerActionDescriptor())),TempData=new TempDataDictionary(http,new MemoryTempData())};
    }
    private static async Task<(IActionResult Result,string? Error)> DeleteCategory(int id,int actor)
    {
        await using var db=StageDb();var controller=CategoryController(db,actor);var result=await controller.Delete(id);return(result,controller.TempData["Error"] as string);
    }
    private static async Task RejectCategorySqlDelete(int id,string reason)
    {
        try{await Exec($"DELETE FROM tbldanhmuctaisan WHERE DMTS_ID={id}");throw new InvalidOperationException("Related category was deleted");}
        catch(MySqlException ex)when(ex.Number==1644){Assert(ex.Message==reason,"Expected relationship rejection");}
    }
    private static async Task<string[]> CategoryTriggerRows()
    {
        await using var db=new MySqlConnection(stageChecksConnection);await db.OpenAsync();await using var cmd=db.CreateCommand();cmd.CommandText="SELECT TRIGGER_NAME,ACTION_STATEMENT,SQL_MODE,DEFINER FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=DATABASE() ORDER BY TRIGGER_NAME";await using var reader=await cmd.ExecuteReaderAsync();var rows=new List<string>();while(await reader.ReadAsync())rows.Add(string.Join("\t",Enumerable.Range(0,4).Select(reader.GetString)));return rows.ToArray();
    }
    private sealed class BeforeCategoryDelete(Func<Task> insert) : DbCommandInterceptor
    {
        private bool ran;
        private async Task Run(DbCommand command){if(!ran&&command.CommandText.Contains("DELETE FROM `tbldanhmuctaisan`",StringComparison.OrdinalIgnoreCase)){ran=true;await insert();}}
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken token=default){await Run(command);return result;}
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<int> result,CancellationToken token=default){await Run(command);return result;}
    }
    private static async Task CategoryDeleteHttp(int actor,int employee,int locked)
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(DanhMucController).Assembly.GetName().Name,ContentRootPath=Directory.GetCurrentDirectory(),EnvironmentName="Development"});builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<DataContext>(o=>o.UseMySql(stageChecksConnection,new MariaDbServerVersion(new Version(10,4,32))));builder.Services.AddDbContext<DBContext>(o=>o.UseMySql(stageChecksConnection,new MariaDbServerVersion(new Version(10,4,32))));builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();builder.Services.AddControllersWithViews().AddApplicationPart(typeof(DanhMucController).Assembly);
        await using var app=builder.Build();app.Use(async(http,next)=>{http.Session=new MemorySession();var id=int.TryParse(http.Request.Headers["X-Category-Actor"],out var value)?value:actor;http.Session.SetInt32("TaiKhoanID",id);http.Session.SetString("VaiTro","Admin");await next();});app.MapControllerRoute("areas","{area:exists}/{controller=Home}/{action=Index}/{id?}");await app.StartAsync();
        try
        {
            using var client=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false,CookieContainer=new CookieContainer()}){BaseAddress=new Uri(app.Urls.Single())};
            var html=await client.GetStringAsync("/Admin/DanhMuc");var token=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);Assert(token.Length>0,"Real CSRF form");
            var id=await NewDeleteCategory();var spoof=await NewDeleteCategory();
            Assert((int)(await client.PostAsync("/Admin/DanhMuc/Delete/"+id,new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode==400,"CSRF required");
            foreach(var denied in new[]{employee,locked}){client.DefaultRequestHeaders.Add("X-Category-Actor",denied.ToString());Assert((int)(await client.PostAsync("/Admin/DanhMuc/Delete/"+id,new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token}}))).StatusCode==403,"Stale Admin session denied");client.DefaultRequestHeaders.Remove("X-Category-Actor");}
            Assert((int)(await client.GetAsync("/Admin/DanhMuc/Delete/"+id)).StatusCode==405,"GET cannot delete");
            Assert((int)(await client.PostAsync("/Admin/DanhMuc/Delete/"+id,new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"id",spoof.ToString()}}))).StatusCode==302,"HTTP deletion succeeds");
            Assert(!await CategoryExists(id)&&await CategoryExists(spoof),"Form cannot override route ID");
            var related=await NewDeleteCategory();await AddDeleteAsset(related);
            var blocked=await client.PostAsync("/Admin/DanhMuc/Delete/"+related,new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token}}));Assert((int)blocked.StatusCode==302,"Relation rejection uses normal redirect");
            var message=WebUtility.HtmlDecode(await client.GetStringAsync(blocked.Headers.Location));Assert(message.Contains("Không thể xóa danh mục")&&!message.Contains("unhandled exception"),"Friendly HTML error");
        }
        finally{await app.StopAsync();}
    }
}
