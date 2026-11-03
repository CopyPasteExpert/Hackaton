namespace Tracker.Data;

public class CompetitionResult
{
    public int Id { get; set; }
    public string Participant { get; set; } = string.Empty;
    public string Competition { get; set; } = string.Empty;
    public int Year { get; set; }
    public decimal? Score { get; set; }
    public int? Grade { get; set; }
    public int? Place { get; set; }
    public string? SourceUrl { get; set; }
}