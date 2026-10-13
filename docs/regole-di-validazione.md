# Regole di validazione del dataset eFTI

> Documento **generato dal codice**: non modificarlo a mano. Si rigenera con `dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- docs`.

Le regole sono codice deterministico: nessun modello decide se un dataset è valido. Un dataset è **Valid** quando nessuna regola di gravità Errore è violata e l'XML esportato è conforme allo schema; altrimenti è **Incomplete**. Gli avvisi non cambiano lo stato.

Regole: 29, di cui 26 bloccanti.

## Obbligatorietà

| Codice | Gravità | Elemento eFTI | Regola |
|---|---|---|---|
| `M-CONSIGNOR-NAME` | Errore | `consignor/name` | Il mittente deve avere un nome. |
| `M-CONSIGNOR-ADDRESS` | Errore | `consignor/postalAddress` | L'indirizzo del mittente deve avere città, CAP e paese. |
| `M-CONSIGNEE-NAME` | Errore | `consignee/name` | Il destinatario deve avere un nome. |
| `M-CONSIGNEE-ADDRESS` | Errore | `consignee/postalAddress` | L'indirizzo del destinatario deve avere città, CAP e paese. |
| `M-CARRIER-NAME` | Errore | `carrier/name` | Il vettore deve avere un nome: viene dalla configurazione della piattaforma. |
| `M-PICKUP-LOCATION` | Errore | `carrierAcceptanceLocation/postalAddress` | Il luogo di presa in carico deve avere città e paese. |
| `M-DELIVERY-LOCATION` | Errore | `consigneeReceiptLocation/postalAddress` | Il luogo di consegna deve avere città e paese. |
| `M-PICKUP-DATE` | Errore | `carrierAcceptanceDateTime` | Serve la data di presa in carico, effettiva o prevista. |
| `M-DELIVERY-DATE` | Errore | `deliveryEvent/scheduledOccurrenceDateTime` | Serve la data di consegna pattuita. |
| `M-GOODS-DESCRIPTION` | Errore | `natureIdentificationCargo/identificationText` | Ogni riga di merce deve dire che cosa si trasporta. |
| `M-PACKAGES` | Errore | `numberOfPackages`, `transportPackage/itemQuantity` | Il numero di colli deve essere maggiore di zero, in totale e su ogni riga. |
| `M-GROSS-WEIGHT` | Errore | `grossWeight` | Il peso lordo della spedizione deve essere maggiore di zero. |
| `M-VEHICLE` | Errore | `mainCarriageTransportMovement/usedTransportMeans` | Una spedizione già presa in carico deve indicare targa e paese del mezzo. |
| `M-ADR-UN-NUMBER` | Errore | `includedConsignmentItem/transportDangerousGoods/uNDGID` | Una riga di merce pericolosa deve avere il numero ONU. |
| `M-ADR-SHIPPING-NAME` | Errore | `includedConsignmentItem/transportDangerousGoods/properShippingName` | Una riga di merce pericolosa deve avere la denominazione ufficiale di trasporto. |
| `M-ADR-CLASS` | Errore | `includedConsignmentItem/transportDangerousGoods/hazardClassificationID` | Una riga di merce pericolosa deve avere la classe ADR. |

## Formato

| Codice | Gravità | Elemento eFTI | Regola |
|---|---|---|---|
| `F-COUNTRY` | Errore | `postalAddress/countryCode`, `registrationCountry/code` | Ogni paese deve essere un codice ISO 3166-1 a due lettere presente nella lista dello schema eFTI. |
| `F-VAT` | Errore | `taxRegistration/id` | Una partita IVA, se presente, deve avere il prefisso del paese e una forma plausibile; per l'Italia anche la cifra di controllo. |
| `F-PLATE` | Errore | `usedTransportMeans/id`, `usedTransportEquipment/id` | Una targa, se presente, deve avere da 5 a 10 lettere e cifre. |
| `F-UN-NUMBER` | Errore | `includedConsignmentItem/transportDangerousGoods/uNDGID` | Il numero ONU, se presente, deve avere quattro cifre. |
| `F-PACKING-GROUP` | Errore | `includedConsignmentItem/transportDangerousGoods/packagingDangerLevelCode` | Il gruppo di imballaggio, se presente, deve essere I, II o III. |

## Coerenza

| Codice | Gravità | Elemento eFTI | Regola |
|---|---|---|---|
| `C-WEIGHT-SUM` | Errore | `grossWeight` | Il peso lordo dichiarato deve coincidere con la somma dei pesi delle righe, con una tolleranza del 2%. |
| `C-PACKAGES-SUM` | Errore | `numberOfPackages` | Il numero di colli dichiarato deve coincidere con la somma dei colli delle righe. |
| `C-DELIVERY-BEFORE-PICKUP` | Errore | `deliveryEvent/scheduledOccurrenceDateTime` | La data di consegna pattuita non può precedere la presa in carico. |
| `C-DELIVERED-BEFORE-PICKED-UP` | Errore | `deliveryEvent/actualOccurrenceDateTime` | La consegna effettiva non può precedere la presa in carico effettiva. |

## Avvisi

| Codice | Gravità | Elemento eFTI | Regola |
|---|---|---|---|
| `W-VEHICLE-NOT-ASSIGNED` | Avviso | `mainCarriageTransportMovement/usedTransportMeans` | Una spedizione non ancora partita senza mezzo assegnato: il dato servirà alla presa in carico. |
| `W-PARTY-VAT` | Avviso | `consignor/taxRegistration/id`, `consignee/taxRegistration/id` | Mittente e destinatario dovrebbero avere la partita IVA: non è nei sottoinsiemi dell'autorità ma serve alla lettera di vettura. |
| `W-CMR-NUMBER` | Avviso | `transportContractDocument/id` | Un trasporto internazionale già preso in carico dovrebbe avere il numero della lettera di vettura. |

## Conformità allo schema

| Codice | Gravità | Elemento eFTI | Regola |
|---|---|---|---|
| `X-SCHEMA` | Errore | (tutto il dataset) | L'XML esportato deve essere conforme allo schema XSD eFTI. Si controlla solo se nessun'altra regola bloccante è violata: intercetta ciò che le regole non prevedono, per esempio un testo più lungo del formato ammesso. |
