using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UrlShortener.Api.Endpoints;
using UrlShortener.Api.Infrastructure.Data;
using UrlShortener.Api.Infrastructure.Middleware;
using UrlShortener.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ProblemDetails & Global Exception Handling (RFC 7807)
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalProblemDetailsExceptionHandler>();

// Bounded in-memory cache to prevent memory exhaustion
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 50_000;
    options.CompactionPercentage = 0.20;
});

// Database Persistence:
// - In "Testing" environment (T002/T015/T016), use isolated EF Core InMemory database
// - In Development/Production, use SQL Server
if (builder.Environment.IsEnvironment("Testing"))
{
    var testDbName = builder.Configuration["TestDatabaseName"] ?? "UrlShortener_DefaultTestDb";
    builder.Services.AddDbContext<UrlShortenerDbContext>(options =>
        options.UseInMemoryDatabase(testDbName));
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Server=(localdb)\\mssqllocaldb;Database=UrlShortenerDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
    builder.Services.AddDbContext<UrlShortenerDbContext>(options =>
        options.UseSqlServer(connectionString));
}

// Domain & Application Services
builder.Services.AddScoped<IUrlRepository, EfUrlRepository>();
builder.Services.AddSingleton<IShortCodeGenerator, Base58CodeGenerator>();
builder.Services.AddSingleton<IUrlSecurityValidator, UrlSecurityValidator>();
builder.Services.AddSingleton<IIpPrivacyHasher, IpPrivacyHasher>();
builder.Services.AddScoped<IUrlShortenerService, UrlShortenerService>();

// OpenAPI 3.1 document generation
builder.Services.AddOpenApi();

var app = builder.Build();

// Ensure database schema exists on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
    if (!dbContext.Database.IsRelational())
    {
        dbContext.Database.EnsureCreated();
    }
    else
    {
        var retryCount = 0;
        while (true)
        {
            try
            {
                dbContext.Database.EnsureCreated();
                break;
            }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1801)
            {
                // Safe concurrent initialization: database was already created
                break;
            }
            catch (Microsoft.Data.SqlClient.SqlException) when (retryCount < 3)
            {
                retryCount++;
                System.Threading.Thread.Sleep(500);
            }
        }
    }
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Register Greenfield Minimal API routes (T013)
app.MapUrlEndpoints();

app.Run();

/// <summary>
/// Exposes the implicit top-level Program class so integration tests can host the API
/// via WebApplicationFactory&lt;Program&gt; (specs/tasks.md T002).
/// </summary>
public partial class Program { }
