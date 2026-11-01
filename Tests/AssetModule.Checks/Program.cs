using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using DoAn.Areas.Admin.Controllers;
using DoAn.Areas.Admin.Models;
using DoAn.Models;
using DoAn.Services.Assets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using MySqlConnector;
using AssetContext = DoAn.Data.DataContext;

namespace AssetModule.Checks;

internal static class Program
{
    private static int passed;
    private static int failed;
    private static string? mysqlTestConnection;
    private static string? mysqlTestDatabase;
    private static MySqlConnection? mysqlAdmin;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--mysql-integration")) await CreateMySqlSandbox();
            return await Run(args);
        }
        finally
        {
            if (mysqlTestDatabase != null && mysqlAdmin != null)
            {
                // Chỉ xóa database có tên ngẫu nhiên do chính lần chạy này tạo.
                if (!System.Text.RegularExpressions.Regex.IsMatch(mysqlTestDatabase, "^qltaisan_checks_[0-9a-f]{32}$"))
                    throw new InvalidOperationException("Unsafe test database name");
                await using var command = mysqlAdmin.CreateCommand();
                command.CommandText = "DROP DATABASE `" + mysqlTestDatabase + "`";
                await command.ExecuteNonQueryAsync();
                Console.WriteLine("MYSQL INTEGRATION: removed isolated test database");
            }
            if (mysqlAdmin != null) await mysqlAdmin.DisposeAsync();
        }
    }

    private static async Task CreateMySqlSandbox()
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync("appsettings.json"));
        var builder = new MySqlConnectionStringBuilder(json.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!);
        var name = "qltaisan_checks_" + Guid.NewGuid().ToString("N");
        builder.Database = "";
        mysqlAdmin = new MySqlConnection(builder.ConnectionString);
        await mysqlAdmin.OpenAsync();
        await using var command = mysqlAdmin.CreateCommand();
        command.CommandText = "CREATE DATABASE `" + name + "` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci";
        await command.ExecuteNonQueryAsync();
        mysqlTestDatabase = name;
        builder.Database = name;
        mysqlTestConnection = builder.ConnectionString;
        using var db = new AssetContext(new DbContextOptionsBuilder<AssetContext>().UseMySql(mysqlTestConnection, new MySqlServerVersion(new Version(8, 0, 0))).Options);
        await db.Database.EnsureCreatedAsync();
        Console.WriteLine("MYSQL INTEGRATION: isolated schema, per-test transaction rollback");
    }

    private static async Task<int> Run(string[] args)
    {
        await Check("Create valid profile, trimmed fields, server status/date", async f =>
        {
            var model = f.ValidCreate(); model.Ma = " NEW001 "; model.Ten = " Máy tính mới "; model.MoTa = " mô tả ";
            var controller = f.Controller(); controller.TryValidateModel(model);
            Assert(await controller.Create(model) is RedirectToActionResult, "Create must redirect");
            var asset = await f.Db.Tbltaisans.SingleAsync(x => x.TsMaTaiSan == "NEW001");
            Assert(asset.TsTenTaiSan == "Máy tính mới" && asset.TsMoTa == "mô tả", "Trim");
            Assert(asset.TsTrangThai == AssetStatuses.Unallocated && asset.TsNgayTao != default, "Server fields");
        });
        foreach (var name in new[] { "empty-code", "empty-name", "duplicate-code", "unknown-category", "inactive-category", "negative-price", "missing-price", "warranty-before-purchase", "future-purchase", "ancient-purchase", "price-precision", "code-length", "name-length", "unknown-condition" })
        {
            await Check("Create rejects " + name, async f =>
            {
                var model = f.ValidCreate();
                switch (name)
                {
                    case "empty-code": model.Ma = " "; break;
                    case "empty-name": model.Ten = " "; break;
                    case "duplicate-code": model.Ma = f.First.TsMaTaiSan; break;
                    case "unknown-category": model.DanhMucId = int.MaxValue; break;
                    case "inactive-category": model.DanhMucId = f.Inactive.DmtsId; break;
                    case "negative-price": model.NguyenGia = -1; break;
                    case "missing-price": model.NguyenGia = null; break;
                    case "warranty-before-purchase": model.HanBaoHanh = model.NgayMua!.Value.AddDays(-1); break;
                    case "future-purchase": model.NgayMua = AssetDisplay.Today.AddDays(1); break;
                    case "ancient-purchase": model.NgayMua = new DateOnly(1800, 1, 1); break;
                    case "price-precision": model.NguyenGia = 1.001m; break;
                    case "code-length": model.Ma = new string('X', 31); break;
                    case "name-length": model.Ten = new string('X', 151); break;
                    case "unknown-condition": model.TinhTrang = "Unknown"; break;
                }
                var before = await f.Db.Tbltaisans.CountAsync();
                var controller = f.Controller(); controller.TryValidateModel(model);
                Assert(await controller.Create(model) is ViewResult && !controller.ModelState.IsValid, "Expected field error");
                Assert(await f.Db.Tbltaisans.CountAsync() == before, "No insert");
            });
        }
        await Check("Warranty without purchase date is accepted", async f =>
        {
            var model = f.ValidCreate(); model.NgayMua = null; model.HanBaoHanh = AssetDisplay.Today.AddDays(30);
            var c = f.Controller(); Assert(c.TryValidateModel(model), "Optional purchase date");
            Assert(await c.Create(model) is RedirectToActionResult, "Create");
        });
        await Check("MVC binding ignores spoofed workflow/identity/date on Create", async f =>
        {
            var c = f.Controller(); var model = new AssetCreateViewModel();
            var values = f.Form(); values["TrangThai"] = "DaThanhLy"; values["TS_TrangThai"] = "DaThanhLy";
            values["TsTrangThai"] = "DaThanhLy"; values["Id"] = "999"; values["TsNgayTao"] = "1900-01-01";
            Assert(await c.TryUpdateModelAsync(model, "", f.Values(values)), "Binding");
            Assert(await c.Create(model) is RedirectToActionResult, "Create");
            var asset = await f.Db.Tbltaisans.SingleAsync(x => x.TsMaTaiSan == "NEW001");
            Assert(asset.TsTrangThai == AssetStatuses.Unallocated && asset.TsId != 999 && asset.TsNgayTao.Year >= 2026, "Spoof ignored");
        });
        await Check("Edit updates allowed fields, preserves code/status/ID/date against spoofing", async f =>
        {
            var c = f.Controller(); var model = new AssetEditViewModel();
            var values = f.Form(); values["Ma"] = "ATTACK"; values["AssetId"] = "999"; values["TrangThai"] = "DaThanhLy";
            values["TS_MaTaiSan"] = "ATTACK"; values["TsTrangThai"] = "DaThanhLy"; values["TS_NgayTao"] = "1900-01-01";
            values["Ten"] = "Tên đã sửa"; values["DanhMucId"] = f.SecondCategory.DmtsId.ToString(); values["TinhTrang"] = AssetConditions.Degraded;
            Assert(await c.TryUpdateModelAsync(model, "", f.Values(values)), "Binding");
            Assert(model.AssetId == 0 && model.Ma == "" && model.TrangThai == "", "BindNever");
            var before = (f.First.TsId, f.First.TsMaTaiSan, f.First.TsTrangThai, f.First.TsNgayTao);
            Assert(await c.Edit(f.First.TsId, model) is RedirectToActionResult, "Edit");
            Assert(before == (f.First.TsId, f.First.TsMaTaiSan, f.First.TsTrangThai, f.First.TsNgayTao), "Immutable fields");
            Assert(f.First.TsTenTaiSan == "Tên đã sửa" && f.First.DmtsId == f.SecondCategory.DmtsId && f.First.TsTinhTrang == AssetConditions.Degraded, "Editable fields");
        });
        await Check("Edit invalid/inactive category returns field error and authoritative display", async f =>
        {
            var c = f.Controller(); var model = f.ValidEdit(); model.DanhMucId = f.Inactive.DmtsId;
            model.Ma = "ATTACK"; model.TrangThai = AssetStatuses.Disposed; model.AssetId = 999;
            c.TryValidateModel(model);
            Assert(await c.Edit(f.First.TsId, model) is ViewResult && !c.ModelState.IsValid, "Rejected");
            Assert(model.Ma == f.First.TsMaTaiSan && model.TrangThai == f.First.TsTrangThai && model.AssetId == f.First.TsId, "DB display");
        });
        await Check("Unknown ID safely returns 404 on Edit/Details/Delete", async f =>
        {
            var c = f.Controller();
            Assert(await c.Edit(int.MaxValue) is NotFoundResult, "Edit GET");
            Assert(await c.Edit(int.MaxValue, f.ValidEdit()) is NotFoundResult, "Edit POST");
            Assert(await c.Details(int.MaxValue) is NotFoundResult, "Details");
            Assert(await c.Delete(int.MaxValue) is NotFoundResult, "Delete GET");
            Assert(await c.DeleteConfirmed(int.MaxValue) is NotFoundResult, "Delete POST");
        });
        await Check("Details includes category and all entity fields", async f =>
        {
            var result = (ViewResult)await f.Controller().Details(f.First.TsId);
            var asset = (Tbltaisan)result.Model!;
            Assert(asset.Dmts.DmtsTenDanhMuc == f.Category.DmtsTenDanhMuc && asset.TsNguyenGia > 0, "Details");
        });
        await Check("Delete GET confirms without mutation; POST deletes unused profile", async f =>
        {
            var c = f.Controller(); var count = await f.Db.Tbltaisans.CountAsync();
            Assert(await c.Delete(f.First.TsId) is ViewResult && await f.Db.Tbltaisans.CountAsync() == count, "GET is safe");
            Assert(await c.DeleteConfirmed(f.First.TsId) is RedirectToActionResult, "POST delete");
            Assert(await f.Db.Tbltaisans.CountAsync() == count - 1, "One deleted");
        });
        foreach (var history in new[] { "allocation", "maintenance", "inventory", "disposal" })
        {
            await Check("Delete blocked by " + history, async f =>
            {
                await f.AddHistory(history);
                var c = f.Controller();
                Assert(await c.DeleteConfirmed(f.First.TsId) is ViewResult && !c.ModelState.IsValid, "Blocked");
                Assert(await f.Db.Tbltaisans.AnyAsync(x => x.TsId == f.First.TsId), "Preserved asset");
            });
        }
        foreach (var filter in new[] { "code", "name", "category", "condition", "status", "combined", "reset" })
        {
            await Check("Search/filter " + filter + " and filtered totals", async f =>
            {
                string? keyword = filter switch { "code" => f.First.TsMaTaiSan, "name" => "Dell", "combined" => "Dell", _ => null };
                int? category = filter is "category" or "combined" ? f.Category.DmtsId : null;
                string? condition = filter is "condition" or "combined" ? AssetConditions.Good : null;
                string? status = filter is "status" or "combined" ? AssetStatuses.Allocated : null;
                var result = (ViewResult)await f.Controller().Index(keyword, category, status, condition);
                var model = (TaiSanIndex)result.Model!;
                var expected = filter == "reset" ? 3 : filter == "combined" || filter == "code" || filter == "status" || filter == "name" ? 1 : 2;
                Assert(model.Total == expected, $"Expected {expected}, got {model.Total}");
                Assert(model.TongNguyenGia == model.Items.Sum(x => x.TsNguyenGia), "Sum matches filtered records");
            });
        }
        await Check("Pagination retains filters/sort, totals cover all pages; page bounds", async f =>
        {
            for (var i = 0; i < 22; i++) f.Db.Tbltaisans.Add(f.Asset("EX" + i.ToString("D2"), "Dell extra", f.Category.DmtsId, AssetStatuses.Allocated, AssetConditions.Good, 100));
            await f.Db.SaveChangesAsync();
            var result = (ViewResult)await f.Controller().Index("Dell", f.Category.DmtsId, AssetStatuses.Allocated, AssetConditions.Good, "code_asc", 2, 10);
            var m = (TaiSanIndex)result.Model!;
            Assert(m.Total == 23 && m.Items.Count == 10 && m.Page == 2 && m.FirstItem == 11 && m.LastItem == 20 && m.TotalPages == 3, "Paging");
            Assert(m.Keyword == "Dell" && m.DanhMucId == f.Category.DmtsId && m.TrangThai == AssetStatuses.Allocated && m.TinhTrang == AssetConditions.Good && m.Sort == "code_asc", "Filter state");
            Assert(m.TongNguyenGia == f.First.TsNguyenGia + 2200, "Sum across all pages");
            foreach (var size in new[] { 10, 20, 50 })
            {
                m = (TaiSanIndex)((ViewResult)await f.Controller().Index(null, null, null, page: int.MaxValue, pageSize: size)).Model!;
                Assert(m.PageSize == size && m.Page == m.TotalPages, "Page clamp");
            }
            m = (TaiSanIndex)((ViewResult)await f.Controller().Index("no-result", null, null, sort: "bad", page: -9, pageSize: int.MaxValue)).Model!;
            Assert(m.Total == 0 && m.TongNguyenGia == 0 && m.Page == 1 && m.PageSize == 10 && m.Sort == "created_desc" && m.FirstItem == 0, "Empty and invalid inputs");
        });
        foreach (var sort in AssetQueries.Sorts.Keys)
        {
            await Check("Sort " + sort + " preserves requested page", async f =>
            {
                for (var i = 0; i < 12; i++) f.Db.Tbltaisans.Add(f.Asset("EX" + i.ToString("D2"), "Name" + i, f.Category.DmtsId, AssetStatuses.Unallocated, AssetConditions.Good, i));
                await f.Db.SaveChangesAsync();
                var m = (TaiSanIndex)((ViewResult)await f.Controller().Index(null, null, null, sort: sort, page: 2)).Model!;
                Assert(m.Sort == sort && m.Page == 2 && m.Items.Count == 5, "State");
                Func<Tbltaisan, IComparable?> key = sort.Split('_')[0] switch
                {
                    "code" => x => x.TsMaTaiSan, "name" => x => x.TsTenTaiSan, "price" => x => x.TsNguyenGia,
                    "purchase" => x => x.TsNgayMua, _ => x => x.TsNgayTao
                };
                var all = await f.Db.Tbltaisans.ToListAsync();
                var expected = (sort.EndsWith("asc") ? all.OrderBy(key) : all.OrderByDescending(key)).ThenBy(x => x.TsId).Skip(10).Select(x => x.TsId);
                Assert(m.Items.Select(x => x.TsId).SequenceEqual(expected), "Ordering");
            });
        }
        await Check("Legacy Hong displays/filters as HuHong", async f =>
        {
            f.First.TsTinhTrang = AssetConditions.LegacyDamaged; await f.Db.SaveChangesAsync();
            var m = (TaiSanIndex)((ViewResult)await f.Controller().Index(null, null, null, AssetConditions.Damaged)).Model!;
            Assert(m.Total == 1 && AssetConditions.Normalize(m.Items[0].TsTinhTrang) == AssetConditions.Damaged, "Legacy compatibility");
            var edit = (AssetEditViewModel)((ViewResult)await f.Controller().Edit(f.First.TsId)).Model!;
            Assert(edit.TinhTrang == AssetConditions.Damaged, "Legacy edit");
        });
        foreach (var role in new[] { "anonymous", "NhanVien", "Admin" })
        {
            await Check("Server authorization for " + role + " on all direct actions", f =>
            {
                foreach (var action in new[] { "Index", "Details", "Create", "Edit", "Delete", "DeleteConfirmed" })
                {
                    var c = f.Controller();
                    if (role != "anonymous") { c.HttpContext.Session.SetInt32("TaiKhoanID", 1); c.HttpContext.Session.SetString("VaiTro", role); }
                    var filter = new ActionExecutingContext(c.ControllerContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), c);
                    c.ControllerContext.ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor { ActionName = action };
                    c.OnActionExecuting(filter);
                    Assert(role switch { "anonymous" => filter.Result is RedirectToActionResult, "NhanVien" => filter.Result is StatusCodeResult { StatusCode: 403 }, _ => filter.Result == null }, action);
                }
                return Task.CompletedTask;
            });
        }
        await Check("POST actions have anti forgery; route ID excludes form override", f =>
        {
            foreach (var method in typeof(TaiSanController).GetMethods().Where(x => x.GetCustomAttribute<HttpPostAttribute>() != null))
                Assert(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>() != null, method.Name);
            foreach (var method in typeof(TaiSanController).GetMethods().Where(x => x.Name is "Edit" or "Details" or "Delete" or "DeleteConfirmed"))
                Assert(method.GetParameters()[0].GetCustomAttribute<FromRouteAttribute>() != null, method.Name + " route ID");
            return Task.CompletedTask;
        });
        await Check("Warranty boundary dates and Vietnamese decimal display", f =>
        {
            var today = new DateOnly(2026, 11, 2);
            Assert(AssetDisplay.Warranty(null, today).Label == "Không có thông tin bảo hành", "Unknown");
            Assert(AssetDisplay.Warranty(today.AddDays(-1), today).Label == "Hết bảo hành", "Expired");
            Assert(AssetDisplay.Warranty(today, today).Label == "Sắp hết bảo hành", "Today inclusive");
            Assert(AssetDisplay.Warranty(today.AddDays(30), today).Label == "Sắp hết bảo hành", "30 days inclusive");
            Assert(AssetDisplay.Warranty(today.AddDays(31), today).Label == "Còn bảo hành", "31 days");
            Assert(AssetDisplay.Money(15000000.25m) == "15.000.000,25 đ", "Decimal display");
            Assert(AssetDisplay.Money(15000000m) == "15.000.000 đ", "Whole dong display");
            return Task.CompletedTask;
        });
        await Check("MySQL model preserves unique code/decimal/FKs; SQL translation for filters/sorts", f =>
        {
            using var db = new AssetContext(new DbContextOptionsBuilder<AssetContext>().UseMySql("Server=localhost;Database=checks;User ID=checks", new MySqlServerVersion(new Version(8, 0, 0))).Options);
            var entity = db.Model.FindEntityType(typeof(Tbltaisan))!;
            Assert(entity.GetIndexes().Any(x => x.IsUnique && x.Properties.Single().Name == nameof(Tbltaisan.TsMaTaiSan)), "Unique code");
            var price = entity.FindProperty(nameof(Tbltaisan.TsNguyenGia))!;
            Assert(price.GetPrecision() == 15 && price.GetScale() == 2, "Decimal 15,2");
            Assert(entity.FindProperty(nameof(Tbltaisan.TsTrangThai))!.IsConcurrencyToken, "Status concurrency");
            Assert(db.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()).Where(x => x.PrincipalEntityType.ClrType == typeof(Tbltaisan) || x.PrincipalEntityType.ClrType == typeof(Tbldanhmuctaisan)).All(x => x.DeleteBehavior == DeleteBehavior.Restrict), "Restrict references");
            foreach (var sort in AssetQueries.Sorts.Keys)
            {
                var query = AssetQueries.Filter(db.Tbltaisans, "dell", f.Category.DmtsId, AssetConditions.Damaged, AssetStatuses.Allocated);
                var sql = AssetQueries.Sort(query, sort).Skip(10).Take(10).ToQueryString();
                Assert(sql.Contains("LIMIT") && sql.Contains("ORDER BY") && sql.Contains("TS_TinhTrang"), "Server SQL " + sort);
            }
            return Task.CompletedTask;
        });
        if (mysqlTestConnection != null)
        {
            await Check("MySQL search respects case-insensitive collation", async f =>
            {
                var m = (TaiSanIndex)((ViewResult)await f.Controller().Index("dell", null, null)).Model!;
                Assert(m.Total == 1 && m.Items[0].TsId == f.First.TsId, "Case insensitive name");
            });
            await Check("MySQL UNIQUE rejects duplicate codes regardless of case", async f =>
            {
                f.Db.Tbltaisans.Add(f.Asset("ts001", "Duplicate", f.Category.DmtsId, AssetStatuses.Unallocated, AssetConditions.Good, 1));
                try { await f.Db.SaveChangesAsync(); throw new InvalidOperationException("Unique constraint missing"); }
                catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 }) { }
            });
            await Check("MySQL FK blocks deleting asset even if application history guard is bypassed", async f =>
            {
                await f.AddHistory("allocation");
                try
                {
                    await f.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tbltaisan WHERE TS_ID = {f.First.TsId}");
                    throw new InvalidOperationException("FK restriction missing");
                }
                catch (MySqlException ex) when (ex.Number == 1451) { }
            });
            await Check("MySQL workflow status changed during edit produces friendly concurrency error", async f =>
            {
                await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tbltaisan SET TS_TrangThai = {AssetStatuses.UnderMaintenance} WHERE TS_ID = {f.First.TsId}");
                var c = f.Controller(); var model = f.ValidEdit(); c.TryValidateModel(model);
                Assert(await c.Edit(f.First.TsId, model) is ViewResult && !c.ModelState.IsValid, "Concurrent save rejected");
                Assert((await f.Db.Tbltaisans.AsNoTracking().SingleAsync(x => x.TsId == f.First.TsId)).TsTrangThai == AssetStatuses.UnderMaintenance, "Workflow state preserved");
            });
            await Check("HTTP renders all Razor views, preserves query links, enforces CSRF/auth and immutable fields", RenderHttp);
        }
        if (args.Contains("--mysql-check")) await InspectMySql();
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static async Task Check(string name, Func<Fixture, Task> body)
    {
        try { using var f = new Fixture(); await body(f); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private static async Task RenderHttp(Fixture f)
    {
        for (var i = 0; i < 22; i++) f.Db.Tbltaisans.Add(f.Asset("WEB" + i.ToString("D2"), "Dell web", f.Category.DmtsId, AssetStatuses.Allocated, AssetConditions.Good, 100));
        await f.Db.SaveChangesAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(TaiSanController).Assembly.GetName().Name,
            ContentRootPath = Directory.GetCurrentDirectory(), EnvironmentName = "Development"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton(f.Db);
        builder.Services.AddDbContext<DoAn.Models.DBContext>(options => options.UseMySql(mysqlTestConnection!, new MySqlServerVersion(new Version(8, 0, 0))));
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(TaiSanController).Assembly);
        await using var app = builder.Build();
        // Danh tính giả chỉ tồn tại trong HTTP host kiểm thử, không thay đổi app thật.
        app.Use(async (http, next) =>
        {
            http.Session = new MemorySession();
            var role = http.Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Admin";
            if (role != "anonymous") { http.Session.SetInt32("TaiKhoanID", 1); http.Session.SetString("VaiTro", role); }
            await next();
        });
        app.MapControllerRoute("areas", "{area:exists}/{controller=Home}/{action=Index}/{id?}");
        app.MapControllerRoute("default", "{controller=DangNhap}/{action=Index}/{id?}");
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, CookieContainer = new System.Net.CookieContainer() }) { BaseAddress = new Uri(app.Urls.Single()) };
            var index = await client.GetStringAsync($"/Admin/TaiSan?keyword=Dell&danhMucId={f.Category.DmtsId}&tinhTrang=Tot&trangThai=DaCapPhat&sort=code_asc&page=2&pageSize=10");
            Assert(index.Contains("Hiển thị 11–20") && index.Contains("23"), "Rendered pagination");
            var links = System.Text.RegularExpressions.Regex.Matches(index, "href=\"([^\"]+)\"").Select(x => System.Net.WebUtility.HtmlDecode(x.Groups[1].Value)).ToList();
            var next = links.Single(x => x.Contains("page=3"));
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(next[(next.IndexOf('?'))..]);
            Assert(query["keyword"] == "Dell" && query["danhMucId"] == f.Category.DmtsId.ToString() && query["tinhTrang"] == "Tot" && query["trangThai"] == "DaCapPhat" && query["sort"] == "code_asc" && query["pageSize"] == "10", "All pagination filters");
            Assert(links.Contains("/Admin/TaiSan"), "Reset URL has no filters/page");
            var create = await client.GetStringAsync("/Admin/TaiSan/Create");
            Assert(create.Contains("name=\"Ma\"") && !create.Contains("name=\"TrangThai\""), "Create allowed fields");
            var createToken = System.Text.RegularExpressions.Regex.Match(create, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var createForm = f.Form().ToDictionary(x => x.Key, x => x.Value.ToString());
            createForm["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(createToken);
            createForm["TrangThai"] = AssetStatuses.Disposed; createForm["TS_TrangThai"] = AssetStatuses.Disposed;
            var created = await client.PostAsync("/Admin/TaiSan/Create", new FormUrlEncodedContent(createForm));
            Assert((int)created.StatusCode == 302 && (await f.Db.Tbltaisans.SingleAsync(x => x.TsMaTaiSan == "NEW001")).TsTrangThai == AssetStatuses.Unallocated, "HTTP spoofed Create status ignored");
            var edit = await client.GetStringAsync("/Admin/TaiSan/Edit/" + f.First.TsId);
            Assert(edit.Contains("readonly") && !edit.Contains("name=\"Ma\"") && !edit.Contains("name=\"TrangThai\"") && !edit.Contains("name=\"AssetId\""), "Edit immutable fields");
            var details = await client.GetStringAsync("/Admin/TaiSan/Details/" + f.First.TsId);
            Assert(details.Contains(f.First.TsMaTaiSan) && details.Contains("Bảo hành"), "Details renders");
            var delete = await client.GetStringAsync("/Admin/TaiSan/Delete/" + f.First.TsId);
            Assert(delete.Contains(f.First.TsMaTaiSan) && delete.Contains("__RequestVerificationToken"), "Delete confirmation");
            var noToken = await client.PostAsync("/Admin/TaiSan/Create", new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert((int)noToken.StatusCode == 400, "Missing anti forgery token");
            var token = System.Text.RegularExpressions.Regex.Match(edit, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert(token.Length > 0, "Form anti forgery token");
            var form = f.Form().ToDictionary(x => x.Key, x => x.Value.ToString());
            form["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(token);
            form["Id"] = int.MaxValue.ToString(); form["AssetId"] = int.MaxValue.ToString(); form["Ma"] = "SPOOF"; form["TrangThai"] = AssetStatuses.Disposed; form["Ten"] = "HTTP edited";
            var before = (f.First.TsId, f.First.TsMaTaiSan, f.First.TsTrangThai, f.First.TsNgayTao);
            var posted = await client.PostAsync("/Admin/TaiSan/Edit/" + f.First.TsId, new FormUrlEncodedContent(form));
            Assert((int)posted.StatusCode == 302, "Valid POST redirect");
            Assert(f.First.TsTenTaiSan == "HTTP edited" && before == (f.First.TsId, f.First.TsMaTaiSan, f.First.TsTrangThai, f.First.TsNgayTao), "HTTP overposting protection");
            client.DefaultRequestHeaders.Add("X-Test-Role", "NhanVien");
            foreach (var route in new[] { "Create", "Edit/" + f.First.TsId, "Delete/" + f.First.TsId })
                Assert((int)(await client.GetAsync("/Admin/TaiSan/" + route)).StatusCode == 403, "Employee direct URL " + route);
            Assert((int)(await client.PostAsync("/Admin/TaiSan/Edit/" + f.First.TsId, new FormUrlEncodedContent(form))).StatusCode == 403, "Employee POST blocked");
            client.DefaultRequestHeaders.Remove("X-Test-Role"); client.DefaultRequestHeaders.Add("X-Test-Role", "anonymous");
            Assert((int)(await client.GetAsync("/Admin/TaiSan")).StatusCode == 302, "Anonymous redirect");
        }
        finally { await app.StopAsync(); }
    }

    private static async Task InspectMySql()
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync("appsettings.json"));
        var builder = new MySqlConnectionStringBuilder(json.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!) { ConnectionTimeout = 3 };
        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT TABLE_NAME, CONSTRAINT_NAME, DELETE_RULE FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND REFERENCED_TABLE_NAME IN ('tbltaisan','tbldanhmuctaisan')";
            await using var reader = await command.ExecuteReaderAsync();
            var count = 0; var cascade = false;
            while (await reader.ReadAsync()) { count++; var rule = reader.GetString(2); Console.WriteLine($"DB FK {reader.GetString(0)} {reader.GetString(1)} DELETE {rule}"); cascade |= rule == "CASCADE"; }
            Console.WriteLine($"MYSQL READ-ONLY: connected, {count} asset/category FKs, cascade={cascade}");
            if (cascade) { failed++; Console.WriteLine("FAIL Existing database has cascade rules; EF metadata alone does not change existing MySQL constraints."); }
        }
        catch (MySqlException ex) { Console.WriteLine($"MYSQL READ-ONLY: unavailable (MySQL error {ex.Number}); no database was modified."); }
    }

    private sealed class Fixture : IDisposable
    {
        public AssetContext Db { get; }
        private readonly ServiceProvider services;
        private readonly Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction;
        public Tbldanhmuctaisan Category { get; } = new() { DmtsMaDanhMuc = "PC", DmtsTenDanhMuc = "Máy tính", DmtsTrangThai = true };
        public Tbldanhmuctaisan SecondCategory { get; } = new() { DmtsMaDanhMuc = "OFFICE", DmtsTenDanhMuc = "Văn phòng", DmtsTrangThai = true };
        public Tbldanhmuctaisan Inactive { get; } = new() { DmtsMaDanhMuc = "OLD", DmtsTenDanhMuc = "Cũ", DmtsTrangThai = false };
        public Tbltaisan First { get; }

        public Fixture()
        {
            var collection = new ServiceCollection(); collection.AddLogging();
            collection.AddSingleton<DiagnosticListener>(new DiagnosticListener("AssetChecks"));
            collection.AddSingleton<DiagnosticSource>(sp => sp.GetRequiredService<DiagnosticListener>());
            collection.AddControllersWithViews(); services = collection.BuildServiceProvider();
            var options = new DbContextOptionsBuilder<AssetContext>();
            if (mysqlTestConnection == null) options.UseInMemoryDatabase(Guid.NewGuid().ToString());
            else options.UseMySql(mysqlTestConnection, new MySqlServerVersion(new Version(8, 0, 0)));
            Db = new AssetContext(options.Options);
            if (mysqlTestConnection != null) transaction = Db.Database.BeginTransaction();
            Db.Tbldanhmuctaisans.AddRange(Category, SecondCategory, Inactive); Db.SaveChanges();
            First = Asset("TS001", "Máy tính Dell OptiPlex", Category.DmtsId, AssetStatuses.Allocated, AssetConditions.Good, 15000000);
            Db.Tbltaisans.AddRange(First,
                Asset("TS002", "Máy tính HP", Category.DmtsId, AssetStatuses.Unallocated, AssetConditions.Good, 10000000),
                Asset("TS003", "Bàn", SecondCategory.DmtsId, AssetStatuses.Unallocated, AssetConditions.Degraded, 1000000));
            Db.SaveChanges();
        }
        public Tbltaisan Asset(string code, string name, int category, string status, string condition, decimal cost) => new()
        {
            TsMaTaiSan = code, TsTenTaiSan = name, DmtsId = category, TsTrangThai = status, TsTinhTrang = condition,
            TsNguyenGia = cost, TsNgayMua = AssetDisplay.Today.AddDays(-10), TsNgayTao = new DateTime(2026, 1, 1)
        };
        public TaiSanController Controller()
        {
            var http = new DefaultHttpContext { RequestServices = services, Session = new MemorySession() };
            var context = new ControllerContext(new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()));
            return new TaiSanController(Db, NullLogger<TaiSanController>.Instance)
            {
                ControllerContext = context, TempData = new TempDataDictionary(http, new MemoryTempData())
            };
        }
        public AssetCreateViewModel ValidCreate() => new() { Ma = "NEW001", Ten = "Máy tính mới", DanhMucId = Category.DmtsId, NguyenGia = 100, NgayMua = AssetDisplay.Today.AddDays(-1) };
        public AssetEditViewModel ValidEdit() => new() { Ten = "Tên mới", DanhMucId = Category.DmtsId, NguyenGia = 100, TinhTrang = AssetConditions.Good };
        public Dictionary<string, StringValues> Form() => new() { ["Ma"] = "NEW001", ["Ten"] = "Máy tính mới", ["DanhMucId"] = Category.DmtsId.ToString(), ["NguyenGia"] = "100", ["TinhTrang"] = AssetConditions.Good };
        public FormValueProvider Values(Dictionary<string, StringValues> values) => new(BindingSource.Form, new FormCollection(values), CultureInfo.InvariantCulture);
        public async Task AddHistory(string history)
        {
            var account = new Tbltaikhoan { TkTenDangNhap = "tester", TkMatKhau = "unused", TkHoTen = "Kiểm thử", TkVaiTro = "Admin", TkTrangThai = true };
            Db.Tbltaikhoans.Add(account);
            var department = new Tblphongban { PbMaPhongBan = "CHECK", PbTenPhongBan = "Kiểm thử", PbTrangThai = true };
            Db.Tblphongbans.Add(department);
            await Db.SaveChangesAsync();
            switch (history)
            {
                case "allocation":
                    var allocation = new Tblcapphattaisan { CpMaPhieu = "CP", PbId = department.PbId, CpNguoiNhan = "Kiểm thử", CpNguoiGiaoId = account.TkId, CpNgayCapPhat = AssetDisplay.Today, CpTrangThai = "DaCapPhat" };
                    Db.Tblchitietcapphats.Add(new Tblchitietcapphat { TsId = First.TsId, Cp = allocation }); break;
                case "maintenance":
                    Db.Tblbaotritaisans.Add(new Tblbaotritaisan { TsId = First.TsId, BtNguoiLapId = account.TkId, BtMaPhieu = "BT", BtNgayBaoTri = AssetDisplay.Today, BtLyDo = "Test", BtTrangThai = "ChoXuLy" }); break;
                case "inventory":
                    var inventory = new Tblkiemketaisan { KkMaPhieu = "KK", KkNguoiKiemKeId = account.TkId, KkNgayKiemKe = AssetDisplay.Today, KkTrangThai = "DangKiemKe" };
                    Db.Tblchitietkiemkes.Add(new Tblchitietkiemke { TsId = First.TsId, Kk = inventory, CtkkKetQua = "BinhThuong" }); break;
                case "disposal":
                    var disposal = new Tblthanhlytaisan { TlMaPhieu = "TL", TlNguoiLapId = account.TkId, TlNgayThanhLy = AssetDisplay.Today, TlLyDo = "Test", TlTrangThai = "ChoDuyet" };
                    Db.Tblchitietthanhlies.Add(new Tblchitietthanhly { TsId = First.TsId, Tl = disposal }); break;
            }
            await Db.SaveChangesAsync();
        }
        public void Dispose() { transaction?.Rollback(); transaction?.Dispose(); Db.Dispose(); services.Dispose(); }
    }

    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class MemorySession : ISession
    {
        private readonly Dictionary<string, byte[]> values = new();
        public bool IsAvailable => true;
        public string Id { get; } = Guid.NewGuid().ToString();
        public IEnumerable<string> Keys => values.Keys;
        public void Clear() => values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => values.Remove(key);
        public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => values.TryGetValue(key, out value!);
    }
}
