# CMR sintetici

Lettere di vettura **inventate**, generate dal catalogo demo: nessun dato reale di aziende, persone o mezzi. Non sono facsimili del modulo CMR: ne riprendono le caselle e la numerazione del modello IRU.

Ogni documento ha due file:

- `<codice>.pdf` — il CMR, con testo estraibile;
- `<codice>.expected.json` — la verità di riferimento: il valore atteso di ogni campo, per percorso nel dataset eFTI, e i campi che il documento rende ambigui.

Si rigenerano con:

```powershell
dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- cmr
```

Un test verifica che i file del repository siano quelli che il generatore produce oggi.

| Documento | Spedizione | Che cosa mette alla prova |
|---|---|---|
| `CMR-1001` | SH-1001 | caso pulito: tutto coincide con il TMS |
| `CMR-1002` | SH-1002 | porta i dati che al TMS mancano (CAP, partita IVA, dati ADR) e un peso diverso |
| `CMR-1003` | SH-1003 | scenario 3: colli e pesi che il TMS non ha, caselle ambigue (lordo e netto, "14 (12 pallet + 2 casse)") e un'annotazione a mano che contraddice lo stampato |
| `CMR-1004` | SH-1004 | merci pericolose, impaginazione a elenco senza numeri di casella |
| `CMR-1005` | SH-1005 | caselle vuote |
| `CMR-1006` | SH-1006 | riserve del destinatario scritte a mano |
| `CMR-1001-DIFF` | SH-1001 | un valore diverso dal TMS: il peso lordo |
| `CMR-1004-INJ` | SH-1004 | prompt injection nella casella 13 |

Le date stampate sono relative al 5 ottobre 2026; l'estrazione non confronta le date con il TMS, le cui date seguono il giorno in cui si ricrea il database. Le annotazioni "a mano" sono testo in corsivo blu: restano testo estraibile.
