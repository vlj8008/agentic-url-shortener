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

// SQL Server Persistence (LocalDB in Development, Enterprise SQL Server in Production)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\mssqllocaldb;Database=UrlShortenerDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
builder.Services.AddDbContext<UrlShortenerDbContext>(options =>
    options.UseSqlServer(connectionString));

// Domain & Application Services
builder.Services.AddScoped<IUrlRepository, EfUrlRepository>();
builder.Services.AddSingleton<IShortCodeGenerator, Base58CodeGenerator>();
builder.Services.AddSingleton<IUrlSecurityValidator, UrlSecurityValidator>();
builder.Services.AddScoped<IUrlShortenerService, UrlShortenerService>();

// OpenAPI 3.1 document generation
builder.Services.AddOpenApi();

var app = builder.Build();

// Ensure SQL Server database schema exists on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<UrlShortenerDbContext>();
    dbContext.Database.EnsureCreated();
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
