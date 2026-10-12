# Valutazione della validazione eFTI — 2026-10-12 12:21

> Generato da `Eval -- eval validation`. Nessun modello: sono regole dichiarative, e l'esito atteso è il 100%.

| Parametro | Valore |
|---|---|
| Schema eFTI | 0.8 |
| Regole dichiarative | 29 |
| Casi del golden set | 34 |
| Ordini del seed valutati | 149 (seed 20261005, giorno 2026-10-05) |

## Sintesi

| Misura | Valore |
|---|---|
| Casi del golden set con le regole attese, tutte e sole | **34 su 34 (100,0 %)** |
| Difetti del seed riconosciuti | **60 su 60 (100,0 %)** |
| Spedizioni del seed valide / incomplete | 100 / 49 |
| Spedizioni senza difetti voluti ma incomplete | 0 |

## Golden set

| Caso | Regole attese | Regole scattate | Esito |
|---|---|---|---|
| Internazionale completa | nessuna | nessuna | ✓ |
| Nazionale ADR completa, non ancora partita | nessuna | nessuna | ✓ |
| Mittente senza nome | `M-CONSIGNOR-NAME` | `M-CONSIGNOR-NAME` | ✓ |
| Mittente senza CAP e città | `M-CONSIGNOR-ADDRESS` | `M-CONSIGNOR-ADDRESS` | ✓ |
| Destinatario con nome vuoto | `M-CONSIGNEE-NAME` | `M-CONSIGNEE-NAME` | ✓ |
| Destinatario senza CAP e senza partita IVA | `M-CONSIGNEE-ADDRESS`, `W-PARTY-VAT` | `M-CONSIGNEE-ADDRESS`, `W-PARTY-VAT` | ✓ |
| Vettore senza nome | `M-CARRIER-NAME` | `M-CARRIER-NAME` | ✓ |
| Luogo di presa in carico senza città | `M-PICKUP-LOCATION` | `M-PICKUP-LOCATION` | ✓ |
| Luogo di consegna senza paese | `M-DELIVERY-LOCATION` | `M-DELIVERY-LOCATION` | ✓ |
| Nessuna data di presa in carico | `M-PICKUP-DATE` | `M-PICKUP-DATE` | ✓ |
| Nessuna data di consegna pattuita | `M-DELIVERY-DATE` | `M-DELIVERY-DATE` | ✓ |
| Riga senza descrizione | `M-GOODS-DESCRIPTION` | `M-GOODS-DESCRIPTION` | ✓ |
| Riga con colli a zero, totale coerente | `M-PACKAGES` | `M-PACKAGES` | ✓ |
| Riga con colli a zero, totale non aggiornato | `C-PACKAGES-SUM`, `M-PACKAGES` | `C-PACKAGES-SUM`, `M-PACKAGES` | ✓ |
| Peso lordo a zero | `M-GROSS-WEIGHT` | `M-GROSS-WEIGHT` | ✓ |
| Presa in carico senza mezzo | `M-VEHICLE` | `M-VEHICLE` | ✓ |
| Non ancora partita, senza mezzo | `W-VEHICLE-NOT-ASSIGNED` | `W-VEHICLE-NOT-ASSIGNED` | ✓ |
| ADR senza numero ONU | `M-ADR-UN-NUMBER` | `M-ADR-UN-NUMBER` | ✓ |
| ADR senza denominazione ufficiale | `M-ADR-SHIPPING-NAME` | `M-ADR-SHIPPING-NAME` | ✓ |
| ADR senza classe | `M-ADR-CLASS` | `M-ADR-CLASS` | ✓ |
| Paese del destinatario 'UK' | `F-COUNTRY` | `F-COUNTRY` | ✓ |
| Paese a tre lettere su soggetto e luogo | `F-COUNTRY` | `F-COUNTRY` | ✓ |
| Partita IVA italiana con cifra di controllo sbagliata | `F-VAT` | `F-VAT` | ✓ |
| Partita IVA senza prefisso del paese | `F-VAT` | `F-VAT` | ✓ |
| Targa troppo corta | `F-PLATE` | `F-PLATE` | ✓ |
| Numero ONU con una lettera | `F-UN-NUMBER` | `F-UN-NUMBER` | ✓ |
| Gruppo di imballaggio inesistente | `F-PACKING-GROUP` | `F-PACKING-GROUP` | ✓ |
| Peso dichiarato molto diverso dalle righe | `C-WEIGHT-SUM` | `C-WEIGHT-SUM` | ✓ |
| Peso dichiarato entro la tolleranza | nessuna | nessuna | ✓ |
| Colli dichiarati diversi dalle righe | `C-PACKAGES-SUM` | `C-PACKAGES-SUM` | ✓ |
| Consegna pattuita prima della presa | `C-DELIVERY-BEFORE-PICKUP` | `C-DELIVERY-BEFORE-PICKUP` | ✓ |
| Consegna effettiva prima della presa effettiva | `C-DELIVERED-BEFORE-PICKED-UP` | `C-DELIVERED-BEFORE-PICKED-UP` | ✓ |
| Internazionale in viaggio senza numero di CMR | `W-CMR-NUMBER` | `W-CMR-NUMBER` | ✓ |
| Via del destinatario più lunga del formato | `X-SCHEMA` | `X-SCHEMA` | ✓ |

## Difetti voluti del seed

| Difetto | Regole che devono scattare | Ordini | Riconosciuti |
|---|---|---|---|
| `MissingConsigneeAddress` | `M-CONSIGNEE-ADDRESS`, `M-DELIVERY-LOCATION` | 9 | 9 su 9 (100,0 %) |
| `MissingGoodsDescription` | `M-GOODS-DESCRIPTION` | 13 | 13 su 13 (100,0 %) |
| `MissingPackageCount` | `M-PACKAGES` | 12 | 12 su 12 (100,0 %) |
| `WeightMismatch` | `C-WEIGHT-SUM` | 6 | 6 su 6 (100,0 %) |
| `AdrWithoutUnNumber` | `M-ADR-SHIPPING-NAME`, `M-ADR-UN-NUMBER` | 8 | 8 su 8 (100,0 %) |
| `InvalidCountryCode` | `F-COUNTRY` | 7 | 7 su 7 (100,0 %) |
| `DeliveryBeforePickup` | `C-DELIVERY-BEFORE-PICKUP` | 5 | 5 su 5 (100,0 %) |

## Regole scattate sul seed

| Regola | Gravità | Spedizioni |
|---|---|---|
| `W-VEHICLE-NOT-ASSIGNED` | avviso | 15 |
| `M-GOODS-DESCRIPTION` | bloccante | 13 |
| `M-PACKAGES` | bloccante | 13 |
| `M-CONSIGNEE-ADDRESS` | bloccante | 10 |
| `M-ADR-SHIPPING-NAME` | bloccante | 9 |
| `M-ADR-UN-NUMBER` | bloccante | 9 |
| `M-DELIVERY-LOCATION` | bloccante | 9 |
| `C-WEIGHT-SUM` | bloccante | 7 |
| `F-COUNTRY` | bloccante | 7 |
| `C-DELIVERY-BEFORE-PICKUP` | bloccante | 5 |
| `M-GROSS-WEIGHT` | bloccante | 1 |
| `W-PARTY-VAT` | avviso | 1 |
