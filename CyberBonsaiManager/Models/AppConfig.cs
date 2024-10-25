namespace CyberBonsaiManager.Models;

public class AppConfig
{
    public Emulator Emulator { get; set; } = new();
    public Tasks Tasks { get; set; } = new();
}

public class Emulator
{
    public string Type { get; set; } = string.Empty;
    public string ConfigPath { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Args { get; set; } = string.Empty;
}

public class Tasks
{
    public string ConfigPath { get; set; } = string.Empty;
}