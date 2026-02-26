# Historical Graph Explorer

Enterprise .NET 9 app — 780 historical events in Azure Cosmos DB (Gremlin) + Azure OpenAI natural language queries.

## Graph Schema (produced by the C# uploader)

| Vertex | id format | key properties |
|---|---|---|
| Event | `wd:Q<number>` | name, startTime, startYear, pk=event |
| Participant | `participant:<slug>` | name, pk=participant |
| Place | `place:<slug>` | name, pk=place |

**Edges:** `Participant -[INVOLVED_IN]-> Event` · `Event -[OCCURRED_AT]-> Place`

## Quick Start

### 1. Upload data to Cosmos DB
Run the C# uploader (`Dec_05_2025.cs`) — it reads `cleaned_events.json` and seeds the graph.

### 2. Configure secrets
Edit `appsettings.json` (or use User Secrets):
```json
{
  "CosmosGremlin": {
    "Endpoint":    "wss://YOUR_ACCOUNT.gremlin.cosmos.azure.com:443/",
    "PrimaryKey":  "YOUR_KEY",
    "Database":    "YOUR_DATABASE",
    "Graph":       "YOUR_GRAPH"
  },
  "AzureOpenAI": {
    "Endpoint":        "https://YOUR_RESOURCE.openai.azure.com/",
    "Key":             "YOUR_KEY",
    "DeploymentName":  "gpt-4o-mini"
  }
}
```
> ⚠️ The config section is `CosmosGremlin` (not `Cosmos`) — matches the C# uploader.

### 3. Run
Set `HistoricalGraphExplorer.API` as startup → F5

- **UI**: `https://localhost:PORT/`
- **Swagger**: `https://localhost:PORT/swagger`

### 4. Test
```bash
dotnet test
```

## API Endpoints

| Method | URL | Description |
|---|---|---|
| GET | `/api/events` | List events (optional: `?fromYear=1600&toYear=1700&limit=50`) |
| GET | `/api/events/{id}` | Event details + participants + places |
| GET | `/api/events/{id}/neighbors` | Graph neighbors (optional: `?depth=2`) |
| GET | `/api/participants/{slug}/events` | All events for a participant |
| GET | `/api/places/{slug}/events` | All events at a place |
| POST | `/api/copilot/ask` | Natural language → AI answer |
****


HistoricalGraphExplorer/
│
├── HistoricalGraphExplorer.sln
│
├── src/
│   ├── HistoricalGraphExplorer.API/
│   │   ├── Controllers/
│   │   │   ├── EventsController.cs
│   │   │   └── CopilotController.cs
│   │   ├── Pages/
│   │   │   ├── Index.cshtml
│   │   │   ├── Index.cshtml.cs
│   │   │   └── _ViewImports.cshtml
│   │   ├── Middleware/
│   │   │   └── ExceptionMiddleware.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   │
│   ├── HistoricalGraphExplorer.Application/
│   │   ├── Interfaces/
│   │   │   ├── IGremlinRepository.cs
│   │   │   ├── IGraphTraversalService.cs
│   │   │   └── IGraphCopilotService.cs
│   │   ├── Services/
│   │   │   ├── GraphTraversalService.cs
│   │   │   └── GraphCopilotService.cs
│   │
│   ├── HistoricalGraphExplorer.Infrastructure/
│   │   ├── CosmosGremlinClientFactory.cs
│   │   ├── GremlinRepository.cs
│   │   └── SemanticKernelFactory.cs
│   │
│   └── HistoricalGraphExplorer.Domain/
│       └── GraphResult.cs
│
├── tests/
│   └── HistoricalGraphExplorer.Tests/
│       └── GraphCopilotTests.cs
│
└── README.md
