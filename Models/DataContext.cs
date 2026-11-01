using Microsoft.EntityFrameworkCore;
using DoAn.Models;

namespace DoAn.Models
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options)
        {
        }
        public DbSet<tblMenu> Menus {get; set;}
        public DbSet<tblTaiKhoan> TaiKhoans {get; set;}
        
    }
}