var builder = WebApplication.CreateBuilder(args);

// OpenAPI 3.1 document generation (contract defined in specs/spec.md).
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Endpoints are added in Phase 3 (T010–T016) per specs/tasks.md.

app.Run();

/// <summary>
/// Exposes the implicit top-level Program class so integration tests can host the API
/// via WebApplicationFactory&lt;Program&gt; (specs/tasks.md T002).
/// </summary>
public partial class Program { }
