using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoAn.Models
{
    [Table("tblmenu")]
    public class tblMenu
    {
        [Key]
        [Column("MenuID")]
        public int MenuID { get; set; }

        [Column("MenuName")]
        public string? MenuName { get; set; }

        [Column("IsActive")]
        public bool? IsActive { get; set; }

        [Column("ControllerName")]
        public string? ControllerName { get; set; }

        [Column("ActionName")]
        public string? ActionName { get; set; }

        [Column("Levels")]
        public int Levels { get; set; }

        [Column("ParentID")]
        public int ParentID { get; set; }

        [Column("Link")]
        public string? Link { get; set; }

        [Column("MenuOrder")]
        public int MenuOrder { get; set; }

        [Column("Position")]
        public int Position { get; set; }
    }
}
