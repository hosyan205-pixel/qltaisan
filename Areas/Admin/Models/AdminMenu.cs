using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Areas.Admin.Models
{
    [Table("adminmenu")]
    public class AdminMenu
    {
        [Key]
        [Column("AdminMenuID")]
        public long AdminMenuID { get; set; }

        [Column("ItemName")]
        public string? ItemName { get; set; }

        [Column("ItemLevel")]
        public int ItemLevel { get; set; }

        [Column("ParentLevel")]
        public int ParentLevel { get; set; }

        [Column("ItemOrder")]
        public int ItemOrder { get; set; }

        [Column("IsActive")]
        public bool? IsActive { get; set; }

        [Column("ItemTarget")]
        public string? ItemTarget { get; set; }

        [Column("AreaName")]
        public string? AreaName { get; set; }

        [Column("ControllerName")]
        public string? ControllerName { get; set; }

        [Column("ActionName")]
        public string? ActionName { get; set; }

        [Column("Icon")]
        public string? Icon { get; set; }

        [Column("IdName")]
        public string? IdName { get; set; }
    }
}
