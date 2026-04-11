using SQLite;

namespace Sufni.App.Models;

[Table("app_setting")]
public class AppSetting
{
    [PrimaryKey, Column("key")]
    public string Key { get; set; } = null!;

    [Column("value")]
    public string Value { get; set; } = null!;
}
