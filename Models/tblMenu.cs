using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Tblmenu
{
    public int MenuId { get; set; }

    public string MenuName { get; set; } = null!;

    public ulong IsActive { get; set; }

    public string ControllerName { get; set; } = null!;

    public string ActionName { get; set; } = null!;

    public int Levels { get; set; }

    public int ParentId { get; set; }

    public string Link { get; set; } = null!;

    public int MenuOrder { get; set; }

    public int Position { get; set; }
}
