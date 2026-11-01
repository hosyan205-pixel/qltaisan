using DoAn.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoAn.Areas.Admin.Components
{
    [ViewComponent(Name = "AdminMenu")]
    public class AdminMenuComponent : ViewComponent
    {
        private readonly DBContext _context;

        public AdminMenuComponent(DBContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var menu = await _context.AdminMenus
                .Where(m => m.IsActive == true)
                .OrderBy(m => m.ItemLevel)
                .ThenBy(m => m.ItemOrder)
                .ToListAsync();

            return View(menu);
        }
    }
}