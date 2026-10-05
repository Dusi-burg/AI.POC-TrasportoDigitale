# AI.POC-TrasportoDigitale

> 🇬🇧 **English** · 🇮🇹 [Italiano](README.it.md)

**"Digital transport"** POC: from a road carrier's point of view, an eFTI-ready layer on top of a simulated TMS — eFTI dataset, validation, roles, audit, MCP tools, agents and CMR extraction — followed by exception and claim handling.

- Architecture and decisions (Italian): [docs/architettura.md](docs/architettura.md)

> **Status**: Phases 1-2 complete. The solution starts under Aspire and `/api/health` checks SQL Server, Ollama, the models and the cloud model key; the simulated TMS has its data model, 6 demo orders plus 150 synthetic orders with deliberate defects, and read APIs (`/api/views/orders`). Next: Phase 3 — eFTI import, mapping and validation.

All data is **synthetic and fictitious**. The POC is not a certified eFTI platform and gives no legal or insurance advice.

## Running it

Prerequisites: .NET SDK 10.0.400 or later, SQL Server 2025 LocalDB with the `localdev` instance (`sqllocaldb create localdev -s`), Ollama with `qwen3.5:9b` and `embeddinggemma`.

```powershell
# 1. Connection string in the AppHost user-secrets
dotnet user-secrets set "ConnectionStrings:sql" "Server=(localdb)\localdev;Database=Trasporto;Trusted_Connection=True;TrustServerCertificate=True" --project src/Dusiburg.AI.Trasporto.AppHost

# 2. Optional: cloud model key
dotnet user-secrets set "Parameters:anthropic-api-key" "<key>" --project src/Dusiburg.AI.Trasporto.AppHost

# 3. Database (drops and recreates it, with the TMS data; --no-seed leaves it empty)
dotnet run --project tools/Dusiburg.AI.Trasporto.DbInit

# 4. Start
dotnet run --project src/Dusiburg.AI.Trasporto.AppHost
```

The status page is at <http://localhost:5306>; the Aspire dashboard address is printed at startup.

## Build and test

```powershell
dotnet build Dusiburg.AI.Trasporto.slnx
dotnet test --solution Dusiburg.AI.Trasporto.slnx
```

Tests never call a real model. The SQL check tests create and drop a dedicated database on `(localdb)\localdev`.

## License

[MIT](LICENSE)
