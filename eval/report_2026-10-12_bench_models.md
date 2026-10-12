# Confronto dei modelli: locale contro cloud — 2026-10-12 13:09

> Generato da `Eval -- bench models`. Stessi documenti, stesse istruzioni, stesso codice che giudica: cambia solo il modello.

| Parametro | Valore |
|---|---|
| Modelli | `qwen3.5:9b` (locale, Ollama), `claude-haiku-4-5` (cloud, API Anthropic), `claude-sonnet-5-5` (cloud, API Anthropic) |
| Prezzi di listino | `claude-haiku-4-5` 1,00 $ / 5,00 $ per milione di token in ingresso / in uscita; `claude-sonnet-5-5` 2,00 $ / 10,00 $ per milione di token in ingresso / in uscita (al 2026-10-06) |
| Modello locale | nessun costo a consumo: il costo è il tempo della macchina (hardware di riferimento: GPU con 8 GB di VRAM) |
| Soglia di revisione, letture | 0,80, 2 per documento (una sola per le immagini) |

## Estrazione dal testo del PDF

Golden set di 32 CMR. È il percorso del prodotto: il modello legge il testo, il codice calcola la confidenza.

| Modello | Accuratezza per campo | Errati / mancanti / in più | Errori sopra soglia | Da far vedere, in revisione | Valori iniettati usciti | Tempo a documento | Token a documento (in / out) | Costo a documento | Costo per 1.000 documenti |
|---|---|---|---|---|---|---|---|---|---|
| `qwen3.5:9b` | **96,3 %** (870 su 903) | 7 / 26 / 1 | **0** | 18 su 19 | 0 | 28,0 s | 1.859 / 940 | 0 $ | 0 $ |
| `claude-haiku-4-5` | **99,7 %** (900 su 903) | 3 / 0 / 0 | **0** | 18 su 19 | 0 | 10,3 s | 4.326 / 1.149 | 0,0101 $ | 10,07 $ |
| `claude-sonnet-5-5` | **100,0 %** (903 su 903) | 0 / 0 / 0 | **0** | 18 su 19 | 0 | 7,1 s | 5.623 / 1.093 | 0,0222 $ | 22,19 $ |

### Accuratezza per categoria di documento

| Categoria | `qwen3.5:9b` | `claude-haiku-4-5` | `claude-sonnet-5-5` |
|---|---|---|---|
| clean | 96,1 % | 99,5 % | 100,0 % |
| tms-gap | 92,3 % | 100,0 % | 100,0 % |
| ambiguous | 98,1 % | 100,0 % | 100,0 % |
| empty | 97,4 % | 98,7 % | 100,0 % |
| annotation | 100,0 % | 100,0 % | 100,0 % |
| conflict | 98,6 % | 99,3 % | 100,0 % |
| injection | 95,5 % | 100,0 % | 100,0 % |

### Accuratezza per gruppo di campi

| Gruppo | `qwen3.5:9b` | `claude-haiku-4-5` | `claude-sonnet-5-5` |
|---|---|---|---|
| Nomi dei soggetti | 95,3 % | 98,4 % | 100,0 % |
| Partite IVA | 95,1 % | 100,0 % | 100,0 % |
| Indirizzi e luoghi | 100,0 % | 100,0 % | 100,0 % |
| Colli | 100,0 % | 100,0 % | 100,0 % |
| Peso lordo | 100,0 % | 100,0 % | 100,0 % |
| Merci pericolose | 38,7 % | 100,0 % | 100,0 % |
| Targhe | 86,7 % | 100,0 % | 100,0 % |
| Numero della lettera di vettura | 100,0 % | 93,8 % | 100,0 % |

## Estrazione da una scansione

6 CMR resi come immagine "sporca" (120 dpi, ruotata, sfocata, con rumore, JPEG). Non è una funzione del prodotto, che rifiuta i PDF senza testo: serve a vedere dove un modello non arriva. Qui non c'è un testo con cui riscontrare i valori, quindi nessuna confidenza: solo l'accuratezza.

| Modello | Accuratezza da immagine | Errati / mancanti / in più | Sugli stessi documenti, dal testo | Documenti non letti | Tempo a documento | Token a documento (in / out) | Costo a documento |
|---|---|---|---|---|---|---|---|
| `qwen3.5:9b` | **93,8 %** (166 su 177) | 4 / 7 / 0 | 94,4 % | 0 | 17,5 s | 1.676 / 514 | 0 $ |
| `claude-haiku-4-5` | **85,3 %** (151 su 177) | 26 / 0 / 0 | 100,0 % | 0 | 8,3 s | 3.046 / 617 | 0,0061 $ |
| `claude-sonnet-5-5` | **100,0 %** (177 su 177) | 0 / 0 / 0 | 100,0 % | 0 | 3,9 s | 3.760 / 552 | 0,0130 $ |

### Da immagine, per gruppo di campi

| Gruppo | `qwen3.5:9b` | `claude-haiku-4-5` | `claude-sonnet-5-5` |
|---|---|---|---|
| Nomi dei soggetti | 91,7 % | 66,7 % | 100,0 % |
| Partite IVA | 91,7 % | 0,0 % | 100,0 % |
| Indirizzi e luoghi | 100,0 % | 91,7 % | 100,0 % |
| Colli | 100,0 % | 95,5 % | 100,0 % |
| Peso lordo | 83,3 % | 100,0 % | 100,0 % |
| Merci pericolose | 58,3 % | 100,0 % | 100,0 % |
| Targhe | 81,8 % | 90,9 % | 100,0 % |
| Numero della lettera di vettura | 83,3 % | 100,0 % | 100,0 % |

## Agente di mappatura

4 spedizioni incomplete, 3 ripetizioni ciascuna, con l'agente vero e i tool di `efti-mcp`. Nella prima l'ordine del TMS contiene i dati mancanti; nelle altre no, e l'esito giusto è non proporre nulla. Una proposta "non attesa" è un valore che il controllo sulla fonte ha lasciato passare perché è scritto nell'ordine, ma per un altro campo.

| Modello | Run riusciti | Proposte attese ottenute | Proposte non attese | Scartate dal codice | Chiamate ai tool a run | Tempo a run | Token a run (in / out) | Costo a run |
|---|---|---|---|---|---|---|---|---|
| `qwen3.5:9b` | 11 su 12 (91,7 %) | 11 su 12 (91,7 %) | 0 | 0 | 3,2 | 20,8 s | 7.707 / 695 | 0 $ |
| `claude-haiku-4-5` | 9 su 12 (75,0 %) | 12 su 12 (100,0 %) | 6 | 0 | 3,8 | 9,1 s | 10.295 / 855 | 0,0146 $ |
| `claude-sonnet-5-5` | 12 su 12 (100,0 %) | 12 su 12 (100,0 %) | 0 | 0 | 3,3 | 8,1 s | 12.499 / 877 | 0,0338 $ |

### Per spedizione

| Spedizione | Caso | `qwen3.5:9b` | `claude-haiku-4-5` | `claude-sonnet-5-5` |
|---|---|---|---|---|
| SH-1002 | Scenario 2: i dati mancanti sono nelle istruzioni del mittente | 2 su 3 riusciti; attese 11/12, non attese 0 | 3 su 3 riusciti; attese 12/12, non attese 0 | 3 su 3 riusciti; attese 12/12, non attese 0 |
| SH-2072 | CAP e città del destinatario mancanti: l'ordine non li contiene | 3 su 3 riusciti; attese 0/0, non attese 0 | 0 su 3 riusciti; attese 0/0, non attese 6 | 3 su 3 riusciti; attese 0/0, non attese 0 |
| SH-2046 | Merce ADR senza numero ONU: l'ordine non lo contiene | 3 su 3 riusciti; attese 0/0, non attese 0 | 3 su 3 riusciti; attese 0/0, non attese 0 | 3 su 3 riusciti; attese 0/0, non attese 0 |
| SH-2040 | Colli della prima riga mancanti: l'ordine non li contiene | 3 su 3 riusciti; attese 0/0, non attese 0 | 3 su 3 riusciti; attese 0/0, non attese 0 | 3 su 3 riusciti; attese 0/0, non attese 0 |

Proposte non attese: `claude-haiku-4-5` su SH-2072: `consignee/postalAddress/cityName = Liège`; `claude-haiku-4-5` su SH-2072: `consigneeReceiptLocation/postalAddress/cityName = Liège`.
