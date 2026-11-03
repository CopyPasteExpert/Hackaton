namespace Tracker.Importer;

// One MON protocol to import, picked by hand from the "Класиране" page.
public record ProtocolSource(string Url, string Competition, int Year);
