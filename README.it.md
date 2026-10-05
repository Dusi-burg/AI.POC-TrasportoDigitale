# AI.POC-TrasportoDigitale

> 🇮🇹 **Italiano** · 🇬🇧 [English](README.md)

POC **"Trasporto digitale"**: dal punto di vista del vettore su strada, uno strato eFTI-ready sopra un TMS simulato — dataset eFTI, validazione, ruoli, audit, tool MCP, agenti ed estrazione dai CMR — e poi la gestione di eccezioni e reclami.

- Architettura e decisioni: [docs/architettura.md](docs/architettura.md)

> **Stato**: Fase 1 completata (scaffolding). La solution si avvia sotto Aspire con i servizi vuoti e `/api/health` controlla SQL Server, Ollama, modelli e chiave del modello cloud. Prossima: Fase 2 — modello dati, TMS simulato e dati sintetici.

Tutti i dati sono **sintetici e fittizi**. Il POC non è una piattaforma eFTI certificata e non fornisce consulenza legale o assicurativa.

## Come eseguire

Prerequisiti: .NET SDK 10.0.400 o successivo, SQL Server 2025 LocalDB con l'istanza `localdev` (`sqllocaldb create localdev -s`), Ollama con `qwen3.5:9b` ed `embeddinggemma`.

```powershell
# 1. Connection string negli user-secrets dell'AppHost
dotnet user-secrets set "ConnectionStrings:sql" "Server=(localdb)\localdev;Database=Trasporto;Trusted_Connection=True;TrustServerCertificate=True" --project src/Dusiburg.AI.Trasporto.AppHost

# 2. Facoltativo: chiave del modello cloud
dotnet user-secrets set "Parameters:anthropic-api-key" "<chiave>" --project src/Dusiburg.AI.Trasporto.AppHost

# 3. Database (lo cancella e lo ricrea)
dotnet run --project tools/Dusiburg.AI.Trasporto.DbInit

# 4. Avvio
dotnet run --project src/Dusiburg.AI.Trasporto.AppHost
```

La pagina di stato è su <http://localhost:5306>, il dashboard di Aspire all'indirizzo stampato all'avvio.

## Build e test

```powershell
dotnet build Dusiburg.AI.Trasporto.slnx
dotnet test --solution Dusiburg.AI.Trasporto.slnx
```

I test non chiamano modelli reali. Quelli sui controlli SQL creano e cancellano un database dedicato su `(localdb)\localdev`.

## Licenza

[MIT](LICENSE)
