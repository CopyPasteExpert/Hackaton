using Tracker.Importer;

// Usage: dotnet run --project Tracker.Importer -- extract | import
var command = args.FirstOrDefault();

var root = ImporterConfig.FindRoot();
var config = ImporterConfig.Load(root);

switch (command)
{
    case "extract":
    case "import":
        Console.Error.WriteLine($"'{command}' is not implemented yet.");
        return 1;

    default:
        Console.Error.WriteLine("Usage: dotnet run --project Tracker.Importer -- extract | import");
        return 1;
}
