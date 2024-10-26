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
                if (!AnalyzeConditions(item.GetRequiredSection("conditions"))) continue;
                index = i;
                break;
            }
            catch (InvalidOperationException)
            {
            }
            try
            {
                if (!AnalyzeCondition(item.GetRequiredSection("condition"))) continue;
            }
            catch (InvalidOperationException)
            {
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
        return true;
    }
    
    private bool AnalyzeConditions(IConfigurationSection section)
    {
        return true;
    }
}