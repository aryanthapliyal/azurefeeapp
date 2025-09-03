using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices(services =>
    {
        // Enable Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Configure System.Text.Json globally for Function responses
        services.Configure<JsonSerializerOptions>(options =>
        {
            options.PropertyNameCaseInsensitive = true; // Case-insensitive JSON parsing
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull; // Ignore nulls in output
            options.WriteIndented = true; // Optional: makes JSON pretty
        });
    })
    .Build();

host.Run();
