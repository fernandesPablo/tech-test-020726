using TechTest.Api.DependencyInjection;
using TechTest.Api.HealthChecks;
using TechTest.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.RegisterServices(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "TechTest API v1");
        options.RoutePrefix = string.Empty;
    });
}

app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseHttpsRedirection();
app.UseRateLimiter();

app.MapControllers();
app.MapApplicationHealthCheckEndpoints();

app.Run();

// Exposes Program as a partial class so integration tests can reference it via WebApplicationFactory<Program>.
public partial class Program { }
