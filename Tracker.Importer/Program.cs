using Tracker.Importer;

// Usage: dotnet run --project Tracker.Importer -- extract [--protocols <file>] | import
var command = args.FirstOrDefault();

var root = ImporterConfig.FindRoot();
var config = ImporterConfig.Load(root);

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : null;
}

switch (command)
{
    case "extract":
        return await ExtractCommand.RunAsync(config, root, Option("--protocols"));

    case "import":
        Console.Error.WriteLine("'import' is not implemented yet.");
        return 1;

    default:
        Console.Error.WriteLine("Usage: dotnet run --project Tracker.Importer -- extract [--protocols <file>] | import");
        return 1;
}
