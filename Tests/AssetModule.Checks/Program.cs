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
using AssetContext = DoAn.Models.DataContext;

namespace AssetModule.Checks;

internal static partial class Program
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
            if (args.Contains("--kiemke-reports")) return await RunInventoryReportChecks();
            if (args.Contains("--sprint3")) return await RunSprint3();
            if (args.Contains("--sprint2-stage")) return await RunSprint2Stage();
            if (args.Contains("--primary-readonly")) return await RunPrimaryReadOnly();
            if (args.Contains("--category-delete")) return await RunCategoryDeleteChecks();
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
        using var db = new AssetContext(new DbContextOptionsBuilder<AssetContext>().UseMySql(mysqlTestConnection, new MariaDbServerVersion(new Version(10, 4, 32))).Options);
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
            var asset = await f.Db.tblTaiSan.SingleAsync(x => x.TS_MaTaiSan == "NEW001");
            Assert(asset.TS_TenTaiSan == "Máy tính mới" && asset.TS_MoTa == "mô tả", "Trim");
            Assert(asset.TS_TrangThai == AssetStatuses.Unallocated && asset.TS_NgayTao != default, "Server fields");
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
                    case "duplicate-code": model.Ma = f.First.TS_MaTaiSan; break;
                    case "unknown-category": model.DanhMucId = int.MaxValue; break;
                    case "inactive-category": model.DanhMucId = f.Inactive.DMTS_ID; break;
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
                var before = await f.Db.tblTaiSan.CountAsync();
                var controller = f.Controller(); controller.TryValidateModel(model);
                Assert(await controller.Create(model) is ViewResult && !controller.ModelState.IsValid, "Expected field error");
                Assert(await f.Db.tblTaiSan.CountAsync() == before, "No insert");
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
            var asset = await f.Db.tblTaiSan.SingleAsync(x => x.TS_MaTaiSan == "NEW001");
            Assert(asset.TS_TrangThai == AssetStatuses.Unallocated && asset.TS_ID != 999 && asset.TS_NgayTao.Year >= 2026, "Spoof ignored");
        });
        await Check("Edit updates allowed fields, preserves code/status/ID/date against spoofing", async f =>
        {
            var c = f.Controller(); var model = new AssetEditViewModel();
            var values = f.Form(); values["Ma"] = "ATTACK"; values["AssetId"] = "999"; values["TrangThai"] = "DaThanhLy";
            values["TS_MaTaiSan"] = "ATTACK"; values["TsTrangThai"] = "DaThanhLy"; values["TS_NgayTao"] = "1900-01-01";
            values["Ten"] = "Tên đã sửa"; values["DanhMucId"] = f.SecondCategory.DMTS_ID.ToString(); values["TinhTrang"] = AssetConditions.Degraded;
            Assert(await c.TryUpdateModelAsync(model, "", f.Values(values)), "Binding");
            Assert(model.AssetId == 0 && model.Ma == "" && model.TrangThai == "", "BindNever");
            var before = (f.First.TS_ID, f.First.TS_MaTaiSan, f.First.TS_TrangThai, f.First.TS_NgayTao);
            Assert(await c.Edit(f.First.TS_ID, model) is RedirectToActionResult, "Edit");
            Assert(before == (f.First.TS_ID, f.First.TS_MaTaiSan, f.First.TS_TrangThai, f.First.TS_NgayTao), "Immutable fields");
            Assert(f.First.TS_TenTaiSan == "Tên đã sửa" && f.First.DMTS_ID == f.SecondCategory.DMTS_ID && f.First.TS_TinhTrang == AssetConditions.Degraded, "Editable fields");
        });
        await Check("Edit invalid/inactive category returns field error and authoritative display", async f =>
        {
            var c = f.Controller(); var model = f.ValidEdit(); model.DanhMucId = f.Inactive.DMTS_ID;
            model.Ma = "ATTACK"; model.TrangThai = AssetStatuses.Disposed; model.AssetId = 999;
            c.TryValidateModel(model);
            Assert(await c.Edit(f.First.TS_ID, model) is ViewResult && !c.ModelState.IsValid, "Rejected");
            Assert(model.Ma == f.First.TS_MaTaiSan && model.TrangThai == f.First.TS_TrangThai && model.AssetId == f.First.TS_ID, "DB display");
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
            var result = (ViewResult)await f.Controller().Details(f.First.TS_ID);
            var asset = (tblTaiSan)result.Model!;
            Assert(asset.tblDanhMucTaiSan?.DMTS_TenDanhMuc == f.Category.DMTS_TenDanhMuc && asset.TS_NguyenGia > 0, "Details");
        });
        await Check("Delete GET confirms without mutation; POST deletes unused profile", async f =>
        {
            var c = f.Controller(); var count = await f.Db.tblTaiSan.CountAsync();
            Assert(await c.Delete(f.First.TS_ID) is ViewResult && await f.Db.tblTaiSan.CountAsync() == count, "GET is safe");
            Assert(await c.DeleteConfirmed(f.First.TS_ID) is RedirectToActionResult, "POST delete");
            Assert(await f.Db.tblTaiSan.CountAsync() == count - 1, "One deleted");
        });
        foreach (var history in new[] { "allocation", "maintenance", "inventory", "disposal" })
        {
            await Check("Delete blocked by " + history, async f =>
            {
                await f.AddHistory(history);
                var c = f.Controller();
                Assert(await c.DeleteConfirmed(f.First.TS_ID) is ViewResult && !c.ModelState.IsValid, "Blocked");
                Assert(await f.Db.tblTaiSan.AnyAsync(x => x.TS_ID == f.First.TS_ID), "Preserved asset");
            });
        }
        foreach (var filter in new[] { "code", "name", "category", "condition", "status", "combined", "reset" })
        {
            await Check("Search/filter " + filter + " and filtered totals", async f =>
            {
                string? keyword = filter switch { "code" => f.First.TS_MaTaiSan, "name" => "Dell", "combined" => "Dell", _ => null };
                int? category = filter is "category" or "combined" ? f.Category.DMTS_ID : null;
                string? condition = filter is "condition" or "combined" ? AssetConditions.Good : null;
                string? status = filter is "status" or "combined" ? AssetStatuses.Allocated : null;
                var result = (ViewResult)await f.Controller().Index(keyword, category, status, condition);
                var model = (TaiSanIndex)result.Model!;
                var expected = filter == "reset" ? 3 : filter == "combined" || filter == "code" || filter == "status" || filter == "name" ? 1 : 2;
                Assert(model.Total == expected, $"Expected {expected}, got {model.Total}");
                Assert(model.TongNguyenGia == model.Items.Sum(x => x.TS_NguyenGia), "Sum matches filtered records");
            });
        }
        await Check("Pagination retains filters/sort, totals cover all pages; page bounds", async f =>
        {
            for (var i = 0; i < 22; i++) f.Db.tblTaiSan.Add(f.Asset("EX" + i.ToString("D2"), "Dell extra", f.Category.DMTS_ID, AssetStatuses.Allocated, AssetConditions.Good, 100));
            await f.Db.SaveChangesAsync();
            var result = (ViewResult)await f.Controller().Index("Dell", f.Category.DMTS_ID, AssetStatuses.Allocated, AssetConditions.Good, "code_asc", 2, 10);
            var m = (TaiSanIndex)result.Model!;
            Assert(m.Total == 23 && m.Items.Count == 10 && m.Page == 2 && m.FirstItem == 11 && m.LastItem == 20 && m.TotalPages == 3, "Paging");
            Assert(m.Keyword == "Dell" && m.DanhMucId == f.Category.DMTS_ID && m.TrangThai == AssetStatuses.Allocated && m.TinhTrang == AssetConditions.Good && m.Sort == "code_asc", "Filter state");
            Assert(m.TongNguyenGia == f.First.TS_NguyenGia + 2200, "Sum across all pages");
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
                for (var i = 0; i < 12; i++) f.Db.tblTaiSan.Add(f.Asset("EX" + i.ToString("D2"), "Name" + i, f.Category.DMTS_ID, AssetStatuses.Unallocated, AssetConditions.Good, i));
                await f.Db.SaveChangesAsync();
                var m = (TaiSanIndex)((ViewResult)await f.Controller().Index(null, null, null, sort: sort, page: 2)).Model!;
                Assert(m.Sort == sort && m.Page == 2 && m.Items.Count == 5, "State");
                Func<tblTaiSan, IComparable?> key = sort.Split('_')[0] switch
                {
                    "code" => x => x.TS_MaTaiSan, "name" => x => x.TS_TenTaiSan, "price" => x => x.TS_NguyenGia,
                    "purchase" => x => x.TS_NgayMua, _ => x => x.TS_NgayTao
                };
                var all = await f.Db.tblTaiSan.ToListAsync();
                var expected = (sort.EndsWith("asc") ? all.OrderBy(key) : all.OrderByDescending(key)).ThenBy(x => x.TS_ID).Skip(10).Select(x => x.TS_ID);
                Assert(m.Items.Select(x => x.TS_ID).SequenceEqual(expected), "Ordering");
            });
        }
        await Check("Legacy Hong displays/filters as HuHong", async f =>
        {
            f.First.TS_TinhTrang = AssetConditions.LegacyDamaged; await f.Db.SaveChangesAsync();
            var m = (TaiSanIndex)((ViewResult)await f.Controller().Index(null, null, null, AssetConditions.Damaged)).Model!;
            Assert(m.Total == 1 && AssetConditions.Normalize(m.Items[0].TS_TinhTrang) == AssetConditions.Damaged, "Legacy compatibility");
            var edit = (AssetEditViewModel)((ViewResult)await f.Controller().Edit(f.First.TS_ID)).Model!;
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
            using var db = new AssetContext(new DbContextOptionsBuilder<AssetContext>().UseMySql("Server=localhost;Database=checks;User ID=checks", new MariaDbServerVersion(new Version(10, 4, 32))).Options);
            var entity = db.Model.FindEntityType(typeof(tblTaiSan))!;
            Assert(entity.GetIndexes().Any(x => x.IsUnique && x.Properties.Single().Name == nameof(tblTaiSan.TS_MaTaiSan)), "Unique code");
            var price = entity.FindProperty(nameof(tblTaiSan.TS_NguyenGia))!;
            Assert(price.GetPrecision() == 15 && price.GetScale() == 2, "Decimal 15,2");
            Assert(entity.FindProperty(nameof(tblTaiSan.TS_TrangThai))!.IsConcurrencyToken, "Status concurrency");
            Assert(db.Model.GetEntityTypes().SelectMany(x => x.GetForeignKeys()).Where(x => x.PrincipalEntityType.ClrType == typeof(tblTaiSan) || x.PrincipalEntityType.ClrType == typeof(tblDanhMucTaiSan)).All(x => x.DeleteBehavior == DeleteBehavior.Restrict), "Restrict references");
            foreach (var sort in AssetQueries.Sorts.Keys)
            {
                var query = AssetQueries.Filter(db.tblTaiSan, "dell", f.Category.DMTS_ID, AssetConditions.Damaged, AssetStatuses.Allocated);
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
                Assert(m.Total == 1 && m.Items[0].TS_ID == f.First.TS_ID, "Case insensitive name");
            });
            await Check("MySQL UNIQUE rejects duplicate codes regardless of case", async f =>
            {
                f.Db.tblTaiSan.Add(f.Asset("ts001", "Duplicate", f.Category.DMTS_ID, AssetStatuses.Unallocated, AssetConditions.Good, 1));
                try { await f.Db.SaveChangesAsync(); throw new InvalidOperationException("Unique constraint missing"); }
                catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 }) { }
            });
            await Check("MySQL FK blocks deleting asset even if application history guard is bypassed", async f =>
            {
                await f.AddHistory("allocation");
                try
                {
                    await f.Db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM tblTaiSan WHERE TS_ID = {f.First.TS_ID}");
                    throw new InvalidOperationException("FK restriction missing");
                }
                catch (MySqlException ex) when (ex.Number == 1451) { }
            });
            await Check("MySQL workflow status changed during edit produces friendly concurrency error", async f =>
            {
                await f.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tblTaiSan SET TS_TrangThai = {AssetStatuses.UnderMaintenance} WHERE TS_ID = {f.First.TS_ID}");
                var c = f.Controller(); var model = f.ValidEdit(); c.TryValidateModel(model);
                Assert(await c.Edit(f.First.TS_ID, model) is ViewResult && !c.ModelState.IsValid, "Concurrent save rejected");
                Assert((await f.Db.tblTaiSan.AsNoTracking().SingleAsync(x => x.TS_ID == f.First.TS_ID)).TS_TrangThai == AssetStatuses.UnderMaintenance, "Workflow state preserved");
            });
            await Check("HTTP renders all Razor views, preserves query links, enforces CSRF/auth and immutable fields", RenderHttp);
        }
        await RunAllocationChecks();
        if (args.Contains("--mysql-check")) await InspectMySql();
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    private static async Task RunAllocationChecks()
    {
        foreach (var filter in new[] { "code", "asset", "receiver", "department", "inactive-department", "status", "date", "none", "partial", "all", "combined", "reset" })
        {
            await Check("Allocation search/filter " + filter, async f =>
            {
                var seed = await f.SeedReceipts();
                var c = f.AllocationController();
                var view = (ViewResult)(filter switch
                {
                    "code" => await c.Index(keyword: "  CP-A  "),
                    "asset" => await c.Index(keyword: f.First.TS_MaTaiSan),
                    "receiver" => await c.Index(nguoiNhan: " Lan "),
                    "department" => await c.Index(phongBanId: seed.Department.PB_ID),
                    "inactive-department" => await c.Index(phongBanNgungHoatDong: true),
                    "status" => await c.Index(trangThai: "Nhap"),
                    "date" => await c.Index(tuNgay: AssetDisplay.Today.AddDays(-10), denNgay: AssetDisplay.Today.AddDays(-9)),
                    "none" or "partial" or "all" => await c.Index(thuHoi: filter),
                    "combined" => await c.Index(keyword: "CP-A", nguoiNhan: "Lan", phongBanId: seed.Department.PB_ID, trangThai: "DaCapPhat", thuHoi: "partial", tuNgay: AssetDisplay.Today.AddDays(-10), denNgay: AssetDisplay.Today),
                    _ => await c.Index()
                });
                var m = (CapPhatIndex)view.Model!;
                var expected = filter switch { "asset" or "department" => 4, "receiver" or "date" or "none" => 2, "reset" => 5, _ => 1 };
                Assert(m.Total == expected && m.Items.Count == expected, "Filtered count: " + m.Total);
                Assert(m.PhongBans.Count == 2 && m.PhongBans.Any(x => x.Text.Contains("ngừng")), "Inactive departments remain visible");
                Assert(!f.Db.ChangeTracker.HasChanges(), "Read must not mutate tracked records");
            });
        }
        await Check("Allocation totals, independent row returns and no fabricated custody", async f =>
        {
            var seed = await f.SeedReceipts(); var c = f.AllocationController();
            var details = (CapPhatDetails)((ViewResult)await c.Details(seed.Receipts[0].CP_ID)).Model!;
            Assert(details.Total == 2 && details.Phieu.SoDaThuHoi == 1 && details.Items.Count(x => x.NgayThuHoi.HasValue) == 1, "Partial count from lines");
            var history = (CapPhatHistory)((ViewResult)await c.History(f.First.TS_ID)).Model!;
            Assert(history.Total == 4 && history.Items.Count == 4, "All original allocation rows retained");
            Assert(history.Items[0].PhieuId == seed.Receipts[3].CP_ID, "Unknown date remains unknown, not replaced by today");
            Assert(history.Items.Any(x => x.NgayThuHoi == null) && history.Items.Any(x => x.NgayThuHoi != null), "Return dates retained individually");
            Assert(!f.Db.ChangeTracker.HasChanges(), "Details/history do not change records");
            Assert(await c.Details(int.MaxValue) is NotFoundResult && await c.History(int.MaxValue) is NotFoundResult, "Unknown IDs");
            var unused = await f.Db.tblTaiSan.SingleAsync(x => x.TS_MaTaiSan == "TS003");
            var empty = (CapPhatHistory)((ViewResult)await c.History(unused.TS_ID, int.MaxValue)).Model!;
            Assert(empty.Total == 0 && empty.Page == 1 && empty.FirstItem == 0, "Empty history");
        });
        foreach (var sort in new[] { "date_asc", "date_desc", "code_asc", "code_desc" })
        {
            await Check("Allocation stable server pagination " + sort, async f =>
            {
                var seed = await f.SeedReceipts();
                for (var i = 0; i < 15; i++) f.Db.tblCapPhatTaiSan.Add(new tblCapPhatTaiSan
                {
                    CP_MaPhieu = "EX" + i.ToString("D2"), PB_ID = seed.Department.PB_ID, CP_NguoiGiaoID = seed.Admin.TK_ID,
                    CP_NgayCapPhat = AssetDisplay.Today, CP_TrangThai = "Nhap"
                });
                await f.Db.SaveChangesAsync();
                var c = f.AllocationController();
                var m = (CapPhatIndex)((ViewResult)await c.Index(sort: sort, page: 2)).Model!;
                var all = await f.Db.tblCapPhatTaiSan.ToListAsync();
                IEnumerable<tblCapPhatTaiSan> sorted = sort switch
                {
                    "code_asc" => all.OrderBy(x => x.CP_MaPhieu).ThenBy(x => x.CP_ID),
                    "code_desc" => all.OrderByDescending(x => x.CP_MaPhieu).ThenBy(x => x.CP_ID),
                    "date_asc" => all.OrderBy(x => x.CP_NgayCapPhat).ThenBy(x => x.CP_ID),
                    _ => all.OrderByDescending(x => x.CP_NgayCapPhat).ThenBy(x => x.CP_ID)
                };
                Assert(m.Total == 20 && m.Page == 2 && m.Items.Count == 10 && m.FirstItem == 11 && m.LastItem == 20, "Paging bounds");
                Assert(m.Items.Select(x => x.Id).SequenceEqual(sorted.Skip(10).Take(10).Select(x => x.CP_ID)), "Stable sort");
                m = (CapPhatIndex)((ViewResult)await c.Index(sort: "invalid", page: int.MaxValue, pageSize: int.MaxValue, thuHoi: "invalid")).Model!;
                Assert(m.Sort == "date_desc" && m.PageSize == 10 && m.Page == 2 && m.ThuHoi == null, "Invalid options clamp");
            });
        }
        await Check("Allocation invalid date range and preserved pagination filters", async f =>
        {
            await f.SeedReceipts(); var c = f.AllocationController();
            await c.Index(tuNgay: AssetDisplay.Today, denNgay: AssetDisplay.Today.AddDays(-1));
            Assert(!c.ModelState.IsValid && c.ModelState.ContainsKey("denNgay"), "Date range error");
            var model = new CapPhatIndex { Keyword = "CP", NguoiNhan = "Lan", PhongBanId = 2, TrangThai = "DaCapPhat", ThuHoi = "partial", PhongBanNgungHoatDong = true, TuNgay = new(2026, 1, 1), DenNgay = new(2026, 2, 1), Sort = "code_asc", PageSize = 20 };
            var routes = model.Routes(3);
            Assert(routes.Count == 14 && routes["page"] == "3" && routes["pageSize"] == "20" && routes["keyword"] == "CP" && routes["nguoiNhan"] == "Lan" && routes["phongBanId"] == "2" && routes["trangThai"] == "DaCapPhat" && routes["thuHoi"] == "partial" && routes["tuNgay"] == "2026-01-01" && routes["denNgay"] == "2026-02-01" && routes["sort"] == "code_asc" && routes["phongBanNgungHoatDong"] == "True", "All filter routes");
        });
        await Check("Allocation revalidates account status and role on every action", async f =>
        {
            var seed = await f.SeedReceipts();
            foreach (var scenario in new[] { "anonymous", "employee", "locked", "deleted", "stale-session", "admin" })
            {
                foreach (var action in new[] { "Index", "Details", "History" })
                {
                    var c = f.AllocationController(); var reached = false;
                    seed.Admin.TK_VaiTro = scenario is "employee" or "stale-session" ? "NhanVien" : "Admin";
                    seed.Admin.TK_TrangThai = scenario != "locked"; await f.Db.SaveChangesAsync();
                    if (scenario != "anonymous") c.HttpContext.Session.SetInt32("TaiKhoanID", scenario == "deleted" ? int.MaxValue : seed.Admin.TK_ID);
                    c.HttpContext.Session.SetString("VaiTro", scenario == "admin" ? "NhanVien" : "Admin");
                    c.ControllerContext.ActionDescriptor.ActionName = action;
                    var filter = new ActionExecutingContext(c.ControllerContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), c);
                    await c.OnActionExecutionAsync(filter, () => { reached = true; return Task.FromResult(new ActionExecutedContext(c.ControllerContext, new List<IFilterMetadata>(), c)); });
                    Assert(scenario switch { "anonymous" => filter.Result is RedirectToActionResult, "admin" => reached && filter.Result == null, _ => !reached && filter.Result is StatusCodeResult { StatusCode: 403 } }, scenario + " " + action);
                }
            }
        });
        await Check("Allocation projections translate to MySQL with joins/counts/limits", f =>
        {
            using var db = new AssetContext(new DbContextOptionsBuilder<AssetContext>().UseMySql("Server=localhost;Database=checks;User ID=checks", new MariaDbServerVersion(new Version(10, 4, 32))).Options);
            var receipt = (System.Linq.Expressions.Expression<Func<tblCapPhatTaiSan, CapPhatRow>>)typeof(CapPhatController).GetField("ReceiptProjection", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var line = (System.Linq.Expressions.Expression<Func<tblChiTietCapPhat, CapPhatLine>>)typeof(CapPhatController).GetField("LineProjection", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var sql = db.tblCapPhatTaiSan.AsNoTracking().Where(x => x.tblChiTietCapPhats.Any(d => d.TaiSan!.TS_MaTaiSan.Contains("TS"))).OrderByDescending(x => x.CP_NgayCapPhat).ThenBy(x => x.CP_ID).Skip(10).Take(10).Select(receipt).ToQueryString();
            Assert(sql.Contains("LIMIT") && sql.Contains("COUNT") && sql.Contains("JOIN") && sql.Contains("EXISTS"), "Receipt SQL");
            sql = db.tblChiTietCapPhat.AsNoTracking().Where(x => x.TS_ID == 1).OrderBy(x => x.CapPhat!.CP_NgayCapPhat).ThenBy(x => x.CTCP_ID).Take(20).Select(line).ToQueryString();
            Assert(sql.Contains("LIMIT") && sql.Contains("ORDER BY") && sql.Contains("JOIN") && !sql.Contains("TK_MatKhau"), "History SQL projects no account secrets");
            return Task.CompletedTask;
        });
        await Check("Allocation HTTP views, encoding, pagination, current roles and read-only routes", RenderAllocationHttp);
        await Check("Allocation database outage denies access with a safe 503 response", async f =>
        {
            using var db = new AssetContext(new DbContextOptionsBuilder<AssetContext>().UseMySql(
                "Server=127.0.0.1;Port=1;Database=checks;User ID=checks;Connection Timeout=1",
                new MariaDbServerVersion(new Version(10, 4, 32))).Options);
            var c = new CapPhatController(db, NullLogger<CapPhatController>.Instance) { ControllerContext = f.AllocationController().ControllerContext };
            c.HttpContext.Session.SetInt32("TaiKhoanID", 1);
            var filter = new ActionExecutingContext(c.ControllerContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), c);
            await c.OnActionExecutionAsync(filter, () => throw new InvalidOperationException("Unauthorized action reached"));
            Assert(filter.Result is ObjectResult { StatusCode: 503, Value: string message } && !message.Contains("127.0.0.1") && !message.Contains("checks"), "Safe outage response");
        });
    }

    private static async Task Check(string name, Func<Fixture, Task> body)
    {
        try { using var f = new Fixture(); await body(f); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    private static async Task RenderHttp(Fixture f)
    {
        for (var i = 0; i < 22; i++) f.Db.tblTaiSan.Add(f.Asset("WEB" + i.ToString("D2"), "Dell web", f.Category.DMTS_ID, AssetStatuses.Allocated, AssetConditions.Good, 100));
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
        builder.Services.AddDbContext<DoAn.Models.DBContext>(options => options.UseMySql(mysqlTestConnection!, new MariaDbServerVersion(new Version(10, 4, 32))));
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
            var index = await client.GetStringAsync($"/Admin/TaiSan?keyword=Dell&danhMucId={f.Category.DMTS_ID}&tinhTrang=Tot&trangThai=DaCapPhat&sort=code_asc&page=2&pageSize=10");
            Assert(index.Contains("Hiển thị 11–20") && index.Contains("23"), "Rendered pagination");
            var links = System.Text.RegularExpressions.Regex.Matches(index, "href=\"([^\"]+)\"").Select(x => System.Net.WebUtility.HtmlDecode(x.Groups[1].Value)).ToList();
            var next = links.Single(x => x.Contains("page=3"));
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(next[(next.IndexOf('?'))..]);
            Assert(query["keyword"] == "Dell" && query["danhMucId"] == f.Category.DMTS_ID.ToString() && query["tinhTrang"] == "Tot" && query["trangThai"] == "DaCapPhat" && query["sort"] == "code_asc" && query["pageSize"] == "10", "All pagination filters");
            Assert(links.Contains("/Admin/TaiSan"), "Reset URL has no filters/page");
            var create = await client.GetStringAsync("/Admin/TaiSan/Create");
            Assert(create.Contains("name=\"Ma\"") && !create.Contains("name=\"TrangThai\""), "Create allowed fields");
            var createToken = System.Text.RegularExpressions.Regex.Match(create, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            var createForm = f.Form().ToDictionary(x => x.Key, x => x.Value.ToString());
            createForm["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(createToken);
            createForm["TrangThai"] = AssetStatuses.Disposed; createForm["TS_TrangThai"] = AssetStatuses.Disposed;
            var created = await client.PostAsync("/Admin/TaiSan/Create", new FormUrlEncodedContent(createForm));
            Assert((int)created.StatusCode == 302 && (await f.Db.tblTaiSan.SingleAsync(x => x.TS_MaTaiSan == "NEW001")).TS_TrangThai == AssetStatuses.Unallocated, "HTTP spoofed Create status ignored");
            var edit = await client.GetStringAsync("/Admin/TaiSan/Edit/" + f.First.TS_ID);
            Assert(edit.Contains("readonly") && !edit.Contains("name=\"Ma\"") && !edit.Contains("name=\"TrangThai\"") && !edit.Contains("name=\"AssetId\""), "Edit immutable fields");
            var details = await client.GetStringAsync("/Admin/TaiSan/Details/" + f.First.TS_ID);
            Assert(details.Contains(f.First.TS_MaTaiSan) && details.Contains("Bảo hành"), "Details renders");
            var delete = await client.GetStringAsync("/Admin/TaiSan/Delete/" + f.First.TS_ID);
            Assert(delete.Contains(f.First.TS_MaTaiSan) && delete.Contains("__RequestVerificationToken"), "Delete confirmation");
            var noToken = await client.PostAsync("/Admin/TaiSan/Create", new FormUrlEncodedContent(new Dictionary<string, string>()));
            Assert((int)noToken.StatusCode == 400, "Missing anti forgery token");
            var token = System.Text.RegularExpressions.Regex.Match(edit, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            Assert(token.Length > 0, "Form anti forgery token");
            var form = f.Form().ToDictionary(x => x.Key, x => x.Value.ToString());
            form["__RequestVerificationToken"] = System.Net.WebUtility.HtmlDecode(token);
            form["Id"] = int.MaxValue.ToString(); form["AssetId"] = int.MaxValue.ToString(); form["Ma"] = "SPOOF"; form["TrangThai"] = AssetStatuses.Disposed; form["Ten"] = "HTTP edited";
            var before = (f.First.TS_ID, f.First.TS_MaTaiSan, f.First.TS_TrangThai, f.First.TS_NgayTao);
            var posted = await client.PostAsync("/Admin/TaiSan/Edit/" + f.First.TS_ID, new FormUrlEncodedContent(form));
            Assert((int)posted.StatusCode == 302, "Valid POST redirect");
            Assert(f.First.TS_TenTaiSan == "HTTP edited" && before == (f.First.TS_ID, f.First.TS_MaTaiSan, f.First.TS_TrangThai, f.First.TS_NgayTao), "HTTP overposting protection");
            client.DefaultRequestHeaders.Add("X-Test-Role", "NhanVien");
            foreach (var route in new[] { "Create", "Edit/" + f.First.TS_ID, "Delete/" + f.First.TS_ID })
                Assert((int)(await client.GetAsync("/Admin/TaiSan/" + route)).StatusCode == 403, "Employee direct URL " + route);
            Assert((int)(await client.PostAsync("/Admin/TaiSan/Edit/" + f.First.TS_ID, new FormUrlEncodedContent(form))).StatusCode == 403, "Employee POST blocked");
            client.DefaultRequestHeaders.Remove("X-Test-Role"); client.DefaultRequestHeaders.Add("X-Test-Role", "anonymous");
            Assert((int)(await client.GetAsync("/Admin/TaiSan")).StatusCode == 302, "Anonymous redirect");
        }
        finally { await app.StopAsync(); }
    }

    private static async Task RenderAllocationHttp(Fixture f)
    {
        var seed = await f.SeedReceipts();
        seed.Receipts[0].CP_GhiChu = "<script>alert('unsafe')</script>";
        seed.Receipts[0].tblChiTietCapPhats.First().CTCP_GhiChu = "<img src=x onerror=alert(1)>";
        for (var i = 0; i < 55; i++) seed.Receipts[0].tblChiTietCapPhats.Add(new tblChiTietCapPhat
        {
            TaiSan = f.Asset("DETAIL" + i.ToString("D2"), "Tài sản chi tiết", f.Category.DMTS_ID, AssetStatuses.Allocated, AssetConditions.Good, 1)
        });
        for (var i = 0; i < 22; i++) f.Db.tblCapPhatTaiSan.Add(new tblCapPhatTaiSan
        {
            CP_MaPhieu = "WEB" + i.ToString("D2"), CP_NguoiNhan = "Lan", CP_NguoiGiaoID = seed.Admin.TK_ID,
            PB_ID = seed.Department.PB_ID, CP_TrangThai = "Nhap", CP_NgayCapPhat = AssetDisplay.Today,
            tblChiTietCapPhats = [new tblChiTietCapPhat { TS_ID = f.First.TS_ID }]
        });
        await f.Db.SaveChangesAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(CapPhatController).Assembly.GetName().Name,
            ContentRootPath = Directory.GetCurrentDirectory(), EnvironmentName = "Development"
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(f.Db);
        var menuDatabase = "allocation-menu-" + Guid.NewGuid();
        builder.Services.AddDbContext<DBContext>(options => options.UseInMemoryDatabase(menuDatabase));
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(CapPhatController).Assembly);
        await using var app = builder.Build();
        var expectedMenu = new (string Name, string Controller)[]
        {
            ("Tổng quan", "Home"), ("Danh mục tài sản", "DanhMuc"), ("Quản lý tài sản", "TaiSan"),
            ("Quản lý phòng ban", "PhongBan"), ("Cấp phát và bàn giao", "BanGiao"),
            ("Bảo trì và sửa chữa", "BaoTri"), ("Kiểm kê tài sản", "KiemKe"),
            ("Thanh lý tài sản", "ThanhLy"), ("Quản lý tài khoản", "TaiKhoan")
        };
        using (var scope = app.Services.CreateScope())
        {
            var menu = scope.ServiceProvider.GetRequiredService<DBContext>();
            menu.AdminMenus.AddRange(expectedMenu.Select((item, i) => new AdminMenu
            {
                AdminMenuID = i + 1, ItemName = item.Name, ControllerName = item.Controller,
                AreaName = "Admin", ActionName = "Index", ParentLevel = 0, ItemLevel = i == 0 ? 0 : 1,
                ItemOrder = i + 1, IsActive = true, Icon = "bi bi-folder"
            }).Reverse());
            menu.AdminMenus.Add(new AdminMenu { AdminMenuID = 100, ItemName = "Hidden menu", ItemOrder = 0, ItemLevel = 1, ParentLevel = 0, IsActive = false });
            await menu.SaveChangesAsync();
        }
        // Chỉ host kiểm thử gán session. Quyền thật vẫn lấy từ tài khoản seed trong DB kiểm thử.
        app.Use(async (http, next) =>
        {
            http.Session = new MemorySession();
            if (!http.Request.Headers.ContainsKey("X-Test-Anonymous"))
            {
                http.Session.SetInt32("TaiKhoanID", seed.Admin.TK_ID);
                http.Session.SetString("VaiTro", "Admin");
            }
            await next();
        });
        app.MapControllerRoute("areas", "{area:exists}/{controller=Home}/{action=Index}/{id?}");
        app.MapControllerRoute("default", "{controller=DangNhap}/{action=Index}/{id?}");
        await app.StartAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { BaseAddress = new Uri(app.Urls.Single()) };
            var html = await client.GetStringAsync("/Admin/CapPhat?nguoiNhan=Lan&sort=code_asc&page=2&pageSize=10");
            Assert(html.Contains("Hiển thị 11–20"), "Rendered page range");
            var sidebar = System.Text.RegularExpressions.Regex.Match(html, "<aside\\b[^>]*id=\"sidebar\"[^>]*>.*?</aside>", System.Text.RegularExpressions.RegexOptions.Singleline).Value;
            var menuLabels = System.Text.RegularExpressions.Regex.Matches(sidebar, "<span>\\s*([^<]+?)\\s*</span>")
                .Select(x => System.Net.WebUtility.HtmlDecode(x.Groups[1].Value).Trim()).ToArray();
            Assert(menuLabels.SequenceEqual(expectedMenu.Select(x => x.Name)), "Sidebar follows database order, without extra Home or allocation entries");
            var allocationMenu = System.Text.RegularExpressions.Regex.Matches(sidebar, "<a\\b[^>]*>.*?</a>", System.Text.RegularExpressions.RegexOptions.Singleline)
                .Select(x => x.Value).Single(x => System.Net.WebUtility.HtmlDecode(x).Contains("Cấp phát và bàn giao"));
            Assert((allocationMenu.Contains("href=\"/Admin/CapPhat\"") || allocationMenu.Contains("href=\"/Admin/CapPhat/Index\"")) && !allocationMenu.Contains("collapsed"), "Legacy BanGiao entry opens and highlights the current allocation module");
            var links = System.Text.RegularExpressions.Regex.Matches(html, "href=\"([^\"]+)\"").Select(x => System.Net.WebUtility.HtmlDecode(x.Groups[1].Value)).ToList();
            var nextLink = links.Single(x => x.Contains("page=3"));
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(nextLink[nextLink.IndexOf('?')..]);
            Assert(query["nguoiNhan"] == "Lan" && query["sort"] == "code_asc" && query["pageSize"] == "10", "HTTP pagination retains filters");
            Assert(links.Contains("/Admin/CapPhat"), "Reset filter URL");
            foreach (var route in new[] { "Details/" + seed.Receipts[0].CP_ID, "History/" + f.First.TS_ID })
            {
                var response = await client.GetAsync("/Admin/CapPhat/" + route);
                var body = await response.Content.ReadAsStringAsync();
                Assert(response.IsSuccessStatusCode, "Render " + route + ": " + body[..Math.Min(300, body.Length)]);
                Assert(!body.Contains("<script>alert") && !body.Contains("<img src=x"), "Razor encodes user fields");
                if (route.StartsWith("Details")) Assert(body.Contains("&lt;script&gt;") && body.Contains("&lt;img"), "Stored markup shown as text");
                var pageLink = System.Text.RegularExpressions.Regex.Matches(body, "href=\"([^\"]+)\"")
                    .Select(x => System.Net.WebUtility.HtmlDecode(x.Groups[1].Value)).Single(x => x.Contains("page=2"));
                Assert(pageLink.Contains(route), "Details/history pagination retains route ID");
                var secondPage = await client.GetStringAsync(pageLink);
                Assert(secondPage.Contains(route.StartsWith("Details") ? "Hiển thị 51–57" : "Hiển thị 21–26"), "Details/history second page");
            }
            Assert(!f.Db.ChangeTracker.HasChanges(), "HTTP GETs leave records unchanged");
            foreach (var action in new[] { "Index", "Details", "History" })
                Assert((int)(await client.PostAsync("/Admin/CapPhat/" + action, new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode == 405, "Read action rejects POST " + action);
            Assert((int)(await client.GetAsync("/Admin/CapPhat/Schema")).StatusCode == 404, "Technical schema report is not an application page");
            foreach (var action in new[] { "Create", "CreateReturn", "CreateTransfer", "EditDraft", "Submit", "Cancel", "ConfirmHandover", "ConfirmReturn", "ConfirmTransfer" })
                Assert((int)(await client.PostAsync("/Admin/CapPhat/" + action, new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode == 400, "Missing anti-forgery token rejected " + action);
            foreach (var action in new[] { "Return", "Transfer", "Schema" })
                Assert((int)(await client.PostAsync("/Admin/CapPhat/" + action, new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode == 404, "Unimplemented transaction has no route " + action);
            seed.Admin.TK_VaiTro = "NhanVien"; await f.Db.SaveChangesAsync();
            Assert((int)(await client.GetAsync("/Admin/CapPhat")).StatusCode == 200,"Employee can view a scoped list");
            foreach (var route in new[] { "Details/" + seed.Receipts[0].CP_ID, "History/" + f.First.TS_ID })
                Assert((int)(await client.GetAsync("/Admin/CapPhat/" + route)).StatusCode == 404, "Employee cannot read another holder's records " + route);
            Assert((int)(await client.GetAsync("/Admin/CapPhat/Create")).StatusCode == 403,"Stale Admin session cannot manage receipts");
            seed.Admin.TK_VaiTro = "Admin"; seed.Admin.TK_TrangThai = false; await f.Db.SaveChangesAsync();
            Assert((int)(await client.GetAsync("/Admin/CapPhat")).StatusCode == 403, "Account locked after page opened");
            client.DefaultRequestHeaders.Add("X-Test-Anonymous", "1");
            var anonymous = await client.GetAsync("/Admin/CapPhat");
            Assert((int)anonymous.StatusCode == 302 && anonymous.Headers.Location != null, "Anonymous redirect, status=" + (int)anonymous.StatusCode + ", location=" + anonymous.Headers.Location);
            var redirected = await client.GetAsync(anonymous.Headers.Location);
            Assert(redirected.IsSuccessStatusCode, "Redirect resolves login endpoint");
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
        public tblDanhMucTaiSan Category { get; } = new() { DMTS_MaDanhMuc = "PC", DMTS_TenDanhMuc = "Máy tính", DMTS_TrangThai = true };
        public tblDanhMucTaiSan SecondCategory { get; } = new() { DMTS_MaDanhMuc = "OFFICE", DMTS_TenDanhMuc = "Văn phòng", DMTS_TrangThai = true };
        public tblDanhMucTaiSan Inactive { get; } = new() { DMTS_MaDanhMuc = "OLD", DMTS_TenDanhMuc = "Cũ", DMTS_TrangThai = false };
        public tblTaiSan First { get; }

        public Fixture()
        {
            var collection = new ServiceCollection(); collection.AddLogging();
            collection.AddSingleton<DiagnosticListener>(new DiagnosticListener("AssetChecks"));
            collection.AddSingleton<DiagnosticSource>(sp => sp.GetRequiredService<DiagnosticListener>());
            collection.AddControllersWithViews(); services = collection.BuildServiceProvider();
            var options = new DbContextOptionsBuilder<AssetContext>();
            if (mysqlTestConnection == null) options.UseInMemoryDatabase(Guid.NewGuid().ToString());
            else options.UseMySql(mysqlTestConnection, new MariaDbServerVersion(new Version(10, 4, 32)));
            Db = new AssetContext(options.Options);
            if (mysqlTestConnection != null) transaction = Db.Database.BeginTransaction();
            Db.tblDanhMucTaiSan.AddRange(Category, SecondCategory, Inactive); Db.SaveChanges();
            First = Asset("TS001", "Máy tính Dell OptiPlex", Category.DMTS_ID, AssetStatuses.Allocated, AssetConditions.Good, 15000000);
            Db.tblTaiSan.AddRange(First,
                Asset("TS002", "Máy tính HP", Category.DMTS_ID, AssetStatuses.Unallocated, AssetConditions.Good, 10000000),
                Asset("TS003", "Bàn", SecondCategory.DMTS_ID, AssetStatuses.Unallocated, AssetConditions.Degraded, 1000000));
            Db.SaveChanges();
        }
        public tblTaiSan Asset(string code, string name, int category, string status, string condition, decimal cost) => new()
        {
            TS_MaTaiSan = code, TS_TenTaiSan = name, DMTS_ID = category, TS_TrangThai = status, TS_TinhTrang = condition,
            TS_NguyenGia = cost, TS_NgayMua = AssetDisplay.Today.AddDays(-10), TS_NgayTao = new DateTime(2026, 1, 1)
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
        public CapPhatController AllocationController()
        {
            var http = new DefaultHttpContext { RequestServices = services, Session = new MemorySession() };
            return new CapPhatController(Db, NullLogger<CapPhatController>.Instance)
            {
                ControllerContext = new ControllerContext(new ActionContext(http, new RouteData(), new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor())),
                TempData = new TempDataDictionary(http, new MemoryTempData())
            };
        }
        public async Task<(tblTaiKhoan Admin, tblPhongBan Department, List<tblCapPhatTaiSan> Receipts)> SeedReceipts()
        {
            var admin = new tblTaiKhoan { TK_TenDangNhap = "allocation-admin", TK_MatKhau = "unused", TK_HoTen = "Quản trị", TK_VaiTro = "Admin", TK_TrangThai = true };
            var department = new tblPhongBan { PB_MaPhongBan = "ACTIVE", PB_TenPhongBan = "Phòng đang hoạt động", PB_TrangThai = true };
            var inactive = new tblPhongBan { PB_MaPhongBan = "INACTIVE", PB_TenPhongBan = "Phòng đã đóng", PB_TrangThai = false };
            Db.AddRange(admin, department, inactive); await Db.SaveChangesAsync();
            var receipts = new List<tblCapPhatTaiSan>();
            for (var i = 0; i < 5; i++)
            {
                var receipt = new tblCapPhatTaiSan
                {
                    CP_MaPhieu = "CP-" + (char)('A' + i), CP_NguoiNhan = i is 0 or 2 ? "Lan" : "Minh",
                    CP_NguoiGiaoID = admin.TK_ID, PB_ID = i == 3 ? inactive.PB_ID : department.PB_ID,
                    CP_NgayCapPhat = i == 3 ? null : AssetDisplay.Today.AddDays(-10 + i), CP_TrangThai = i == 4 ? "Nhap" : "DaCapPhat"
                };
                if (i != 4) receipt.tblChiTietCapPhats.Add(new tblChiTietCapPhat
                {
                    TS_ID = First.TS_ID, CTCP_TinhTrangKhiGiao = "Tot",
                    CTCP_NgayThuHoi = i == 1 ? AssetDisplay.Today.AddDays(-2) : null,
                    CTCP_TinhTrangKhiThuHoi = i == 1 ? "Hong" : null
                });
                receipts.Add(receipt);
            }
            var second = await Db.tblTaiSan.SingleAsync(x => x.TS_MaTaiSan == "TS002");
            receipts[0].tblChiTietCapPhats.Add(new tblChiTietCapPhat { TS_ID = second.TS_ID, CTCP_NgayThuHoi = AssetDisplay.Today.AddDays(-5), CTCP_TinhTrangKhiGiao = "Tot", CTCP_TinhTrangKhiThuHoi = "Tot" });
            Db.tblCapPhatTaiSan.AddRange(receipts); await Db.SaveChangesAsync();
            return (admin, department, receipts);
        }
        public AssetCreateViewModel ValidCreate() => new() { Ma = "NEW001", Ten = "Máy tính mới", DanhMucId = Category.DMTS_ID, NguyenGia = 100, NgayMua = AssetDisplay.Today.AddDays(-1) };
        public AssetEditViewModel ValidEdit() => new() { Ten = "Tên mới", DanhMucId = Category.DMTS_ID, NguyenGia = 100, TinhTrang = AssetConditions.Good };
        public Dictionary<string, StringValues> Form() => new() { ["Ma"] = "NEW001", ["Ten"] = "Máy tính mới", ["DanhMucId"] = Category.DMTS_ID.ToString(), ["NguyenGia"] = "100", ["TinhTrang"] = AssetConditions.Good };
        public FormValueProvider Values(Dictionary<string, StringValues> values) => new(BindingSource.Form, new FormCollection(values), CultureInfo.InvariantCulture);
        public async Task AddHistory(string history)
        {
            var account = new tblTaiKhoan { TK_TenDangNhap = "tester", TK_MatKhau = "unused", TK_HoTen = "Kiểm thử", TK_VaiTro = "Admin", TK_TrangThai = true };
            Db.tblTaiKhoan.Add(account);
            var department = new tblPhongBan { PB_MaPhongBan = "CHECK", PB_TenPhongBan = "Kiểm thử", PB_TrangThai = true };
            Db.tblPhongBan.Add(department);
            await Db.SaveChangesAsync();
            switch (history)
            {
                case "allocation":
                    var allocation = new tblCapPhatTaiSan { CP_MaPhieu = "CP", PB_ID = department.PB_ID, CP_NguoiNhan = "Kiểm thử", CP_NguoiGiaoID = account.TK_ID, CP_NgayCapPhat = AssetDisplay.Today, CP_TrangThai = "DaCapPhat" };
                    Db.tblChiTietCapPhat.Add(new tblChiTietCapPhat { TS_ID = First.TS_ID, CapPhat = allocation }); break;
                case "maintenance":
                    Db.tblBaoTriTaiSan.Add(new tblBaoTriTaiSan { TS_ID = First.TS_ID, BT_NguoiLapID = account.TK_ID, BT_MaPhieu = "BT", BT_NgayBaoTri = AssetDisplay.Today, BT_LyDo = "Test", BT_TrangThai = "ChoXuLy" }); break;
                case "inventory":
                    var inventory = new tblKiemKeTaiSan { KK_MaPhieu = "KK", KK_NguoiKiemKeID = account.TK_ID, KK_NgayKiemKe = AssetDisplay.Today, KK_TrangThai = "DangKiemKe", KK_LoaiPhamVi = "ToanBo" };
                    Db.tblChiTietKiemKe.Add(new tblChiTietKiemKe { TS_ID = First.TS_ID, KiemKe = inventory, CTKK_KetQua = "BinhThuong" }); break;
                case "disposal":
                    var disposal = new tblThanhLyTaiSan { TL_MaPhieu = "TL", TL_NguoiLapID = account.TK_ID, TL_NgayThanhLy = AssetDisplay.Today, TL_LyDo = "Test", TL_TrangThai = "ChoDuyet" };
                    Db.tblChiTietThanhLy.Add(new tblChiTietThanhLy { TS_ID = First.TS_ID, ThanhLy = disposal }); break;
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
