namespace CyberBonsaiManager.Models;

public class AppTaskBase
{
    public string? Type { get; set; } = default;
    public string? FilePath { get; set; } = default;
    public string? Args { get; set; } = default;
    public string? WorkPath { get; set; } = default;
    public string Encoding { get; set; } = string.Empty;

    public static AppTaskBase Parse(AppTask appTask)
    {
        var appTaskBase = new AppTaskBase
        {
            Type = appTask.Type,
            FilePath = appTask.FilePath,
            Args = appTask.Args,
            WorkPath = appTask.WorkPath,
        };
        foreach (var v in appTask.Variants)
        {
            if (v.Condition is null || v.Condition.IsTrue)
            {
                if (v.Type is not null)
                    appTaskBase.Type = v.Type;
                if (v.FilePath is not null)
                    appTaskBase.FilePath = v.FilePath;
                if (v.Args is not null)
                    appTaskBase.Args = v.Args;
                break;
            }
        }
        return appTaskBase;
    }
}