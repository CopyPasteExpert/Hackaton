using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using MySqlConnector;
using Tracker.Data;
using Tracker.Search;

var builder = WebApplication.CreateBuilder(args);
// Get the MySQL connection string
var connectionString =
    builder.Configuration.GetConnectionString("Tracker");

// Configure the database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        ServerVersion.AutoDetect(connectionString)
    ));

// Configure Identity
builder.Services.AddIdentityApiEndpoints<IdentityUser>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Enable authorization
builder.Services.AddAuthorization();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Smart name search (local LLM via Ollama + fuzzy matching)
builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection("Ollama"));
builder.Services.AddHttpClient<QueryInterpreter>();
builder.Services.AddScoped<SearchService>();

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
{
    var scope = app.Services.CreateScope();
    var interpreter = scope.ServiceProvider.GetRequiredService<QueryInterpreter>();
    _ = interpreter.WarmUpAsync().ContinueWith(_ => scope.Dispose());
});
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGroup("/api/account")
   .MapIdentityApi<IdentityUser>();

app.MapGet("/api/account/me", async (
    ClaimsPrincipal principal,
    UserManager<IdentityUser> userManager) =>
{
    var user = await userManager.GetUserAsync(principal);

    if (user == null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        email = user.Email
    });
}).RequireAuthorization();

app.MapPost("/api/account/logout", async (
    SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();

    return Results.Ok();
}).RequireAuthorization();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/api/search", async (
    string? q,
    SearchService search,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q) || NameNormalizer.Normalize(q).Length == 0)
    {
        return Results.BadRequest("Please enter a name.");
    }

    try
    {
        return Results.Ok(await search.SearchAsync(q, ct));
    }
    catch (MySqlException ex)
    {
        return Results.Problem($"Database error: {ex.Message}");
    }
});


app.MapGet("/api/results", async (
    string? participant,
    string? competition,
    int? year) =>
{
    string? connectionString =
        app.Configuration.GetConnectionString("Tracker");

    if (string.IsNullOrEmpty(connectionString))
    {
        return Results.Problem("Database connection string not found.");
    }

    string sql = @"
        SELECT id, participant_name, competition_name,
               competition_year, score
        FROM competition_results
        WHERE 1 = 1";

    if (!string.IsNullOrWhiteSpace(participant))
    {
        sql += " AND participant_name LIKE @participant";
    }

    if (!string.IsNullOrWhiteSpace(competition))
    {
        sql += " AND competition_name LIKE @competition";
    }

    if (year.HasValue)
    {
        sql += " AND competition_year = @year";
    }

    var results = new List<object>();

    await using var connection =
        new MySqlConnection(connectionString);

    await connection.OpenAsync();

    await using var command =
        new MySqlCommand(sql, connection);

    if (!string.IsNullOrWhiteSpace(participant))
    {
        command.Parameters.AddWithValue(
            "@participant", $"%{participant}%");
    }

    if (!string.IsNullOrWhiteSpace(competition))
    {
        command.Parameters.AddWithValue(
            "@competition", $"%{competition}%");
    }

    if (year.HasValue)
    {
        command.Parameters.AddWithValue("@year", year.Value);
    }

    await using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        results.Add(new
        {
            id = reader.GetInt32("id"),
            participant = reader.GetString("participant_name"),
            competition = reader.GetString("competition_name"),
            year = reader.GetInt32("competition_year"),
            score = reader.IsDBNull(reader.GetOrdinal("score"))
                ? (decimal?)null
                : reader.GetDecimal("score")
        });
    }

    return Results.Ok(results);
});

app.MapPost("/api/saved-results/{resultId}", async (
    int resultId,
    ClaimsPrincipal principal,
    UserManager<IdentityUser> userManager,
    ApplicationDbContext db) =>
{
    var user = await userManager.GetUserAsync(principal);

    if (user == null)
    {
        return Results.Unauthorized();
    }

    var resultExists = await db.Database
        .SqlQuery<int>($"SELECT id AS Value FROM competition_results WHERE id = {resultId}")
        .FirstOrDefaultAsync();

    if (resultExists == 0)
    {
        return Results.NotFound("Competition result not found.");
    }

    var alreadySaved = await db.SavedResults
        .AnyAsync(s => s.UserId == user.Id && s.ResultId == resultId);

    if (alreadySaved)
    {
        return Results.Conflict("You have already saved this result.");
    }

    db.SavedResults.Add(new SavedResult
    {
        UserId = user.Id,
        ResultId = resultId
    });

    await db.SaveChangesAsync();

    return Results.Ok("Result saved successfully!");
}).RequireAuthorization();

app.MapGet("/api/saved-results", async (
    ClaimsPrincipal principal,
    UserManager<IdentityUser> userManager,
    ApplicationDbContext db) =>
{
    var user = await userManager.GetUserAsync(principal);

    if (user == null)
    {
        return Results.Unauthorized();
    }

    var savedResults = await db.SavedResults
        .Where(s => s.UserId == user.Id)
        .Join(
            db.Database
                .SqlQueryRaw<CompetitionResult>(
                    @"SELECT id AS Id,
                             participant_name AS Participant,
                             competition_name AS Competition,
                             competition_year AS Year,
                             score AS Score
                      FROM competition_results"),
            saved => saved.ResultId,
            result => result.Id,
            (saved, result) => new
            {
                saved.Id,
                resultId = result.Id,
                participant = result.Participant,
                competition = result.Competition,
                year = result.Year,
                score = result.Score
            })
        .ToListAsync();

    return Results.Ok(savedResults);
}).RequireAuthorization();

app.MapDelete("/api/saved-results/{savedId}", async (
    int savedId,
    ClaimsPrincipal principal,
    UserManager<IdentityUser> userManager,
    ApplicationDbContext db) =>
{
    var user = await userManager.GetUserAsync(principal);

    if (user == null)
    {
        return Results.Unauthorized();
    }

    var savedResult = await db.SavedResults
        .FirstOrDefaultAsync(s =>
            s.Id == savedId && s.UserId == user.Id);

    if (savedResult == null)
    {
        return Results.NotFound("Saved result not found.");
    }

    db.SavedResults.Remove(savedResult);

    await db.SaveChangesAsync();

    return Results.Ok("Result removed successfully!");
}).RequireAuthorization();

app.Run();
