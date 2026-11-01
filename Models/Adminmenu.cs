using System;
using System.Collections.Generic;

namespace DoAn.Models;

public partial class Adminmenu
{
    public long AdminMenuId { get; set; }

    public string ItemName { get; set; } = null!;

    public int ItemLevel { get; set; }

    public int ParentLevel { get; set; }

    public int ItemOrder { get; set; }

    public int IsActive { get; set; }

    public string ItemTarget { get; set; } = null!;

    public string AreaName { get; set; } = null!;

    public string ControllerName { get; set; } = null!;

    public string ActionName { get; set; } = null!;

    public string Icon { get; set; } = null!;

    public string IdName { get; set; } = null!;
}
