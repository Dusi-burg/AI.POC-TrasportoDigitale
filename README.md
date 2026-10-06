# AI.POC-TrasportoDigitale

> 🇬🇧 **English** · 🇮🇹 [Italiano](README.it.md)

**"Digital transport"** POC: from a road carrier's point of view, an eFTI-ready layer on top of a simulated TMS — eFTI dataset, validation, roles, audit, MCP tools, agents and CMR extraction — followed by exception and claim handling.

- Architecture and decisions (Italian): [docs/architettura.md](docs/architettura.md)
- Dataset validation rules, one by one (Italian): [docs/regole-di-validazione.md](docs/regole-di-validazione.md)
- Which eFTI dataset elements are covered and which are not (Italian): [docs/copertura-dataset.md](docs/copertura-dataset.md)
- Who can do what (Italian): [docs/ruoli.md](docs/ruoli.md)

> **Status**: Phases 1-8 complete. The simulated TMS exposes 6 demo orders plus 150 synthetic orders with deliberate defects; `Efti.Api` imports them, builds the eFTI dataset with the element names of the XSD schema (v0, 0.8), validates it with 29 declarative rules and exports it as schema-conformant XML. Every operation requires a role and lands in an append-only audit trail; an authority reaches a consignment only from a licence plate, through an identifier registry, and reads the `EU03` and `EU05a` subsets derived from the schema. An MCP server exposes seven tools with the same permissions as the API: an agent sees only the tools of the role of whoever runs it and can **propose** a field value, but the dataset changes only when a person accepts the proposal. Two agents use those tools with the key of whoever runs them: one proposes completions by reading the order in the TMS (a proposal goes through only if the value is found in the order), the other answers roadside-check questions from the subsets alone. From the CMR PDF of a consignment a model extracts the fields and **the code** computes their confidence from five verifiable signals, handwritten notes included: above the threshold they become proposals, below it they go to a review queue, and nothing reaches the dataset without a human decision. A web UI makes scenarios 1-4 runnable from the browser: a role selector picks one of the four demo users, every downstream call carries that role's key, and what the role may not do is refused by the API and shown on the page. Next: Phase 9 — evaluation, documentation and Release 1.

Identity is **simulated**: one key per role in the `X-Api-Key` header, no real authentication.

The eFTI XSD schemas in `data/efti-schema/` are unmodified copies from the reference implementation, under the Apache-2.0 licence ([attribution](data/efti-schema/README.md)).

All data is **synthetic and fictitious**. The POC is not a certified eFTI platform and gives no legal or insurance advice.

## Running it

Prerequisites: .NET SDK 10.0.400 or later, SQL Server 2025 LocalDB with the `localdev` instance (`sqllocaldb create localdev -s`), Ollama with `qwen3.5:9b` and `embeddinggemma`.

```powershell
# 1. Connection string in the AppHost user-secrets
dotnet user-secrets set "ConnectionStrings:sql" "Server=(localdb)\localdev;Database=Trasporto;Trusted_Connection=True;TrustServerCertificate=True" --project src/Dusiburg.AI.Trasporto.AppHost

# 2. Optional: cloud model key
dotnet user-secrets set "Parameters:anthropic-api-key" "<key>" --project src/Dusiburg.AI.Trasporto.AppHost

# 2b. One key per role (dispatcher, claimsmanager, authority, admin): any value, long and different for each
dotnet user-secrets set "Parameters:api-key-dispatcher" "<key>" --project src/Dusiburg.AI.Trasporto.AppHost

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
