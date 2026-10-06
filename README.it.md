# AI.POC-TrasportoDigitale

> 🇮🇹 **Italiano** · 🇬🇧 [English](README.md)

POC **"Trasporto digitale"**: dal punto di vista del vettore su strada, uno strato eFTI-ready sopra un TMS simulato — dataset eFTI, validazione, ruoli, audit, tool MCP, agenti ed estrazione dai CMR — e poi la gestione di eccezioni e reclami.

- Architettura e decisioni: [docs/architettura.md](docs/architettura.md)
- Regole di validazione del dataset, una per una: [docs/regole-di-validazione.md](docs/regole-di-validazione.md)
- Quali elementi del dataset eFTI sono coperti e quali no: [docs/copertura-dataset.md](docs/copertura-dataset.md)
- Chi può fare che cosa: [docs/ruoli.md](docs/ruoli.md)

> **Stato**: Fasi 1-6 completate. Il TMS simulato espone 6 ordini demo e 150 ordini sintetici con difetti voluti; `Efti.Api` li importa, costruisce il dataset eFTI con i nomi dello schema XSD (v0, 0.8), lo valida con 29 regole dichiarative e lo esporta in XML conforme allo schema. Ogni operazione richiede un ruolo e finisce in un audit append-only; l'autorità arriva a una spedizione solo da una targa, attraverso un registro degli identificatori, e legge i sottoinsiemi `EU03` ed `EU05a` ricavati dallo schema. Un server MCP espone sette tool con gli stessi permessi dell'API: un agente vede solo i tool del ruolo di chi lo usa e può **proporre** il valore di un campo, ma il dataset cambia solo quando una persona accetta la proposta. Due agenti usano quei tool con la chiave di chi li avvia: uno propone i completamenti leggendo l'ordine nel TMS (una proposta parte solo se il valore si ritrova nell'ordine), l'altro risponde alle domande di un controllo su strada dai soli sottoinsiemi. Prossima: Fase 7 — estrazione dai CMR in PDF.

L'identità è **simulata**: una chiave per ruolo nell'intestazione `X-Api-Key`, nessuna autenticazione reale.

Gli schemi XSD eFTI in `data/efti-schema/` sono copie inalterate dell'implementazione di riferimento, sotto licenza Apache-2.0 ([attribuzione](data/efti-schema/README.md)).

Tutti i dati sono **sintetici e fittizi**. Il POC non è una piattaforma eFTI certificata e non fornisce consulenza legale o assicurativa.

## Come eseguire

Prerequisiti: .NET SDK 10.0.400 o successivo, SQL Server 2025 LocalDB con l'istanza `localdev` (`sqllocaldb create localdev -s`), Ollama con `qwen3.5:9b` ed `embeddinggemma`.

```powershell
# 1. Connection string negli user-secrets dell'AppHost
dotnet user-secrets set "ConnectionStrings:sql" "Server=(localdb)\localdev;Database=Trasporto;Trusted_Connection=True;TrustServerCertificate=True" --project src/Dusiburg.AI.Trasporto.AppHost

# 2. Facoltativo: chiave del modello cloud
dotnet user-secrets set "Parameters:anthropic-api-key" "<chiave>" --project src/Dusiburg.AI.Trasporto.AppHost

# 2b. Una chiave per ruolo (dispatcher, claimsmanager, authority, admin): un valore qualsiasi, lungo e diverso per ciascuno
dotnet user-secrets set "Parameters:api-key-dispatcher" "<chiave>" --project src/Dusiburg.AI.Trasporto.AppHost

# 3. Database (lo cancella e lo ricrea, con i dati del TMS; --no-seed per lasciarlo vuoto)
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
