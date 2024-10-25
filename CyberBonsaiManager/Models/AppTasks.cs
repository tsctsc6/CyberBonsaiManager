namespace CyberBonsaiManager.Models;

public class AppTasks
{
    public List<AppTask> Tasks { get; set; } = [];
}

public class AppTask : AppTaskBase
{
    public List<Variants> Variants { get; set; } = [];
}

public class Variants : AppTaskBase
{
    public Condition? Condition { get; set; } = default;
}

public class Condition
{
    public bool IsTrue
    {
        get
        {
            bool result = Type switch
            {
                "And" => AndHandler(),
                "Or" => OrHandler(),
                "Time" => TimeHandler(),
                _ => false,
            };
            if (IsInvert is true)
                return !result;
            return result;
        }
    }

    public bool? IsInvert = default;
    public string? Type { get; set; } = default;
    public List<Condition>? Conditions { get; set; } = default;
    public string? Start { get; set; } = default;
    public string? End { get; set; } = default;

    private bool AndHandler()
    {
        if (Conditions is null || Conditions.Count == 0) return false;
        var result = true;
        foreach (var c in Conditions)
            result = result && c.IsTrue;
        return result;
    }

    private bool OrHandler()
    {
        if (Conditions is null || Conditions.Count == 0) return false;
        var result = false;
        foreach (var c in Conditions)
            result = result || c.IsTrue;
        return result;
    }

    private bool TimeHandler()
    {
        if (Start is null || End is null) return false;
        var startTime = DateTime.Parse(Start);
        var endTime = DateTime.Parse(End);
        if (endTime < startTime) endTime = endTime.AddDays(1);
        var nowTime = DateTime.Now;
        if (startTime < nowTime && nowTime < endTime) return true;
        return false;
    }
}