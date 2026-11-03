namespace Tracker.Search;

public class OllamaOptions
{
    public bool Enabled { get; set; } = true;
    public string Url { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5:7b";
    public int TimeoutSeconds { get; set; } = 3;
}
