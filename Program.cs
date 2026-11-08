using DoAn.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

var connection = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<DataContext>(options => options.UseMySql(
    connection,
    new MariaDbServerVersion(new Version(10, 4, 32))
));
builder.Services.AddDbContext<DBContext>(options => options.UseMySql(
    connection,
    new MariaDbServerVersion(new Version(10, 4, 32))
));

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// IIS and direct Kestrel runs honor the same temporary maintenance marker.
app.Use(async (http, next) =>
{
    var offline = Path.Combine(app.Environment.ContentRootPath, "app_offline.htm");
    if (File.Exists(offline))
    {
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync(await File.ReadAllTextAsync(offline));
        return;
    }
    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=DangNhap}/{action=Index}/{id?}");

app.Run();
