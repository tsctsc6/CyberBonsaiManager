using Microsoft.Extensions.Configuration;

namespace CyberBonsaiManager.Utilities;

public class VariantsAnalyzer
{
    public IConfigurationSection Analyze(IConfigurationSection section)
    {
        int index = -1;
        foreach ((var item, var i) in section.GetSection("variants").GetChildren().Select((s, i) => (s, i)))
        {
            try
            {
                if (!AnalyzeCondition(item.GetRequiredSection("condition"))) continue;
            }
            catch (InvalidOperationException)
            {
                continue;
            }
            index = i;
            break;
        }
        Dictionary<string, string?> dict = new(section.AsEnumerable().Where(s =>
            !s.Key.StartsWith($"{section.Key}:variants")));
        IConfigurationRoot configurationRoot;
        if (index == -1)
        {
            configurationRoot = new ConfigurationBuilder()
                .AddInMemoryCollection(dict)
                .Build();
            return configurationRoot.GetRequiredSection(section.Key);
        }
        var prefix = $"{section.Key}:variants:{index}";
        foreach (var item in section.GetSection($"variants:{index}").AsEnumerable()
                     .Where(s => !(s.Key.StartsWith($"{prefix}:condition") || s.Key == prefix)))
        {
            dict[$"{section.Key}:{item.Key[(prefix.Length + 1)..]}"] = item.Value;
        }
        configurationRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
        return configurationRoot.GetRequiredSection(section.Key);
    }

    private bool AnalyzeCondition(IConfigurationSection section)
    {
        var result = section["type"] switch
        {
            "And" => AndConditions(section.GetSection("conditions").GetChildren()),
            "Or" => OrConditions(section.GetSection("conditions").GetChildren()),
            "Time" => TimeCondition(section["start"], section["end"]),
            "True" => true,
            _ => false
        };
        if (section.GetValue<bool>("invert")) return !result;
        return result;
    }

    private bool OrConditions(IEnumerable<IConfigurationSection> children)
    {
        return children.Aggregate(false, (current, item) => current || AnalyzeCondition(item));
    }

    private bool AndConditions(IEnumerable<IConfigurationSection> children)
    {
        return children.Aggregate(true, (current, item) => current && AnalyzeCondition(item));
    }

    private bool TimeCondition(string? startString, string? endString)
    {
        var time = TimeOnly.FromDateTime(DateTime.Now);
        if (!TimeOnly.TryParse(startString, out var start)) start = default;
        if (!TimeOnly.TryParse(endString, out var end)) end = default;
        if (start > end)
        {
            if (start <= time || time <= end) return true;
        }
        else
        {
            if (start <= time && time <= end) return true;
        }
        return false;
    }
}