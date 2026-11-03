using Microsoft.Extensions.Configuration;

namespace Tracker.Importer;

// The importer shares the web app's settings (connection string, Ollama model),
// so there is one place to configure both.
public static class ImporterConfig
{
    // The repository root: the first parent folder that contains Tracker.sln.
    public static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tracker.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not find Tracker.sln above " + AppContext.BaseDirectory);
    }

    public static IConfiguration Load(string root) =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "Tracker", "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(root, "Tracker", "appsettings.Development.json"), optional: true)
            .Build();
}
