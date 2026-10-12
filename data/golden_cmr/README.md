# Golden set dell'estrazione dai CMR

Lettere di vettura **inventate**, costruite sugli ordini sintetici del seed del TMS: nessun dato reale di aziende, persone o mezzi. Insieme agli otto CMR della demo (`data/cmr`) formano il golden set di `eval extraction`: 32 documenti, indice e verità di riferimento in `data/golden_cmr.json`.

| Categoria | Documenti | Che cosa mette alla prova |
|---|---|---|
| `clean` | 7 | tutto coincide con il TMS; tre impaginazioni |
| `tms-gap` | 6 | il CMR porta dati che al TMS mancano (indirizzo, colli, numero ONU) |
| `conflict` | 5 | un valore diverso dal TMS (peso, colli, CAP, targa) |
| `ambiguous` | 4 | lordo e netto insieme, "N (a + b)", nota a mano che contraddice |
| `annotation` | 3 | note a mano che non cambiano i valori |
| `empty` | 3 | caselle vuote |
| `injection` | 4 | istruzioni nel testo rivolte a chi lo legge in automatico |

Per ogni documento l'indice dichiara i valori attesi (`fields`), i campi ambigui (`ambiguous`), quelli che devono passare da una persona prima di diventare una proposta (`mustReview`) e i valori che un'iniezione cerca di far uscire (`injected`).

`images/` contiene sei di questi CMR resi come scansione "sporca" (120 dpi, ruotata, sfocata, con rumore, JPEG): servono solo al confronto dei modelli (`bench models`). L'OCR non è una funzione del prodotto.

Tutto si rigenera con:

```powershell
dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- golden
```

Un test verifica che indice e PDF del repository siano quelli che il generatore produce oggi, e che ogni valore atteso sia scritto nel documento. Ciò che il TMS sa di ogni ordine si ricostruisce dal seed al giorno 5 ottobre 2026, lo stesso a cui sono relative le date stampate.
