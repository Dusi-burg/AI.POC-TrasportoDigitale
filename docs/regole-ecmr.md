# Regole del documento eCMR

> Documento **generato dal codice**: non modificarlo a mano. Si rigenera con `dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- docs`.

Schema: eCMR UN/CEFACT versione **100.D25A** (`data/ecmr-schema/`). Il documento eCMR nasce dalla stessa spedizione del dataset eFTI, con mappatura, regole e stato propri.

Le regole sono codice deterministico e riprendono le indicazioni che l'art. 6 della convenzione CMR chiede alla lettera di vettura: sono **regole di esempio, non consulenza legale**. Un documento è **Complete** quando nessuna regola di gravità Errore è violata e l'XML esportato è conforme allo schema; altrimenti è **Draft**. Le regole valgono per ogni trasporto, anche nazionale. Gli avvisi non cambiano lo stato.

Regole: 15, di cui 14 bloccanti.

## Obbligatorietà

| Codice | Gravità | Elemento eCMR | Regola |
|---|---|---|---|
| `E-NUMBER` | Errore | `ExchangedDocument/ID` | La lettera di vettura deve avere un numero. |
| `E-ISSUE` | Errore | `ExchangedDocument/IssueDateTime`, `ExchangedDocument/IssueLogisticsLocation/Name` | Luogo e data di emissione (art. 6, par. 1, lett. a). |
| `E-CONSIGNOR` | Errore | `SpecifiedSupplyChainConsignment/ConsignorTradeParty` | Nome e indirizzo del mittente (lett. b). |
| `E-CARRIER` | Errore | `SpecifiedSupplyChainConsignment/CarrierTradeParty` | Nome e indirizzo del vettore (lett. c): vengono dalla configurazione della piattaforma. |
| `E-TAKING-OVER` | Errore | `SpecifiedSupplyChainConsignment/CarrierAcceptanceLogisticsLocation`, `SpecifiedSupplyChainConsignment/PickUpTransportEvent` | Luogo e data della presa in carico (lett. d). |
| `E-DELIVERY-PLACE` | Errore | `SpecifiedSupplyChainConsignment/ConsigneeReceiptLogisticsLocation` | Luogo previsto per la consegna (lett. d). |
| `E-CONSIGNEE` | Errore | `SpecifiedSupplyChainConsignment/ConsigneeTradeParty` | Nome e indirizzo del destinatario (lett. e). |
| `E-GOODS` | Errore | `SpecifiedSupplyChainConsignment/IncludedSupplyChainConsignmentItem/NatureIdentificationTransportCargo`, `…/TransportLogisticsPackage/TypeCode` | Denominazione corrente della natura della merce e genere dell'imballaggio, per ogni riga (lett. f). |
| `E-PACKAGES` | Errore | `SpecifiedSupplyChainConsignment/IncludedSupplyChainConsignmentItem/TransportLogisticsPackage` | Numero dei colli, loro contrassegni e numeri, per ogni riga (lett. g). |
| `E-GROSS-WEIGHT` | Errore | `SpecifiedSupplyChainConsignment/GrossWeightMeasure` | Peso lordo della merce (lett. h). |
| `E-CHARGES` | Errore | `SpecifiedSupplyChainConsignment/ApplicableLogisticsServiceCharge` | Spese relative al trasporto: il nolo e chi lo paga (lett. i). |
| `E-DANGEROUS-GOODS` | Errore | `SpecifiedSupplyChainConsignment/IncludedSupplyChainConsignmentItem/ApplicableTransportDangerousGoods` | Per le merci pericolose, la denominazione generalmente riconosciuta: numero ONU e denominazione ufficiale di trasporto (lett. f). |

## Formato

| Codice | Gravità | Elemento eCMR | Regola |
|---|---|---|---|
| `E-COUNTRY` | Errore | `PostalTradeAddress/CountryID`, `RegistrationTradeCountry/ID` | Ogni paese deve essere un codice ISO 3166-1 a due lettere: lo schema li elenca. |

## Avvisi

| Codice | Gravità | Elemento eCMR | Regola |
|---|---|---|---|
| `W-CUSTOMS` | Avviso | `SpecifiedSupplyChainConsignment/ConsignorProvidedBorderClearanceTransportInstructions` | Un trasporto che tocca un paese fuori dall'Unione europea dovrebbe portare le istruzioni per la dogana e le altre formalità (lett. j). |

## Conformità allo schema

| Codice | Gravità | Elemento eCMR | Regola |
|---|---|---|---|
| `E-SCHEMA` | Errore | (tutto il documento) | L'XML esportato deve essere conforme allo schema XSD dell'eCMR UN/CEFACT. Si controlla solo se nessun'altra regola bloccante è violata: intercetta ciò che le regole non prevedono, per esempio un codice fuori lista. |

## Ciò che lo schema non porta

- **Dichiarazione di assoggettamento alla CMR** (lett. k): testo fisso in `ExchangedDocument/Remarks`, solo sui trasporti internazionali.
- **Valore dichiarato** (art. 24): lo schema non ha un elemento dedicato; viaggia come `ExchangedDocument/IncludedNote`.
- **Classe ADR**: non ha un elemento nello schema; resta nel dataset eFTI.
- **Firme e riserve del vettore**: fuori dal perimetro della Release 1.
