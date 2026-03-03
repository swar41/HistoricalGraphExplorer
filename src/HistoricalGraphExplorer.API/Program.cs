using HistoricalGraphExplorer.API.Middleware;
using HistoricalGraphExplorer.Application.Interfaces;
using HistoricalGraphExplorer.Application.Services;
using HistoricalGraphExplorer.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title       = "Historical Graph Explorer API",
        Version     = "v1",
        Description = "Query 780 historical events using graph traversal or natural language AI."
    });
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath)) c.IncludeXmlComments(xmlPath);
});

var config = builder.Configuration;

// Config section "CosmosGremlin" matches the C# uploader's appsettings structure
var endpoint   = config["CosmosGremlin:Endpoint"]   ?? throw new Exception("Missing CosmosGremlin:Endpoint");
var primaryKey = config["CosmosGremlin:PrimaryKey"] ?? throw new Exception("Missing CosmosGremlin:PrimaryKey");
var database   = config["CosmosGremlin:Database"]   ?? throw new Exception("Missing CosmosGremlin:Database");
var graph      = config["CosmosGremlin:Graph"]      ?? throw new Exception("Missing CosmosGremlin:Graph");

var gremlinClient = CosmosGremlinClientFactory.Create(endpoint, primaryKey, database, graph);

builder.Services.AddSingleton(gremlinClient);
builder.Services.AddScoped<IGremlinRepository,     GremlinRepository>();
builder.Services.AddScoped<IGraphTraversalService, GraphTraversalService>();

var kernel = SemanticKernelFactory.Create(
    config["AzureOpenAI:Endpoint"]       ?? throw new Exception("Missing AzureOpenAI:Endpoint"),
    config["AzureOpenAI:Key"]            ?? throw new Exception("Missing AzureOpenAI:Key"),
    config["AzureOpenAI:DeploymentName"] ?? throw new Exception("Missing AzureOpenAI:DeploymentName"));

builder.Services.AddSingleton(kernel);
builder.Services.AddScoped<IGraphCopilotService, GraphCopilotService>();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Historical Graph Explorer v1"));

app.MapRazorPages();
app.MapControllers();

// Health endpoint — checks Cosmos + OpenAI connectivity
app.MapGet("/health", async (IGremlinRepository repo, IGraphCopilotService _) =>
{
    var checks = new Dictionary<string, string>();
    try
    {
        await repo.ExecuteAsync("g.V().limit(1)");
        checks["cosmos"] = "ok";
    }
    catch (Exception ex)
    {
        checks["cosmos"] = $"error: {ex.Message}";
    }

    // OpenAI: just verify kernel was wired (no live call to avoid cost)
    checks["openai"] = "configured";

    var healthy = checks.Values.All(v => v == "ok" || v == "configured");
    return healthy ? Results.Ok(checks) : Results.Json(checks, statusCode: 503);
});

app.Run();
