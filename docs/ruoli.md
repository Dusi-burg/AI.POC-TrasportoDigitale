# Ruoli e operazioni

> Documento **generato dal codice**: non modificarlo a mano. Si rigenera con `dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- docs`.

L'identità è **simulata**: ogni richiesta porta una chiave nell'intestazione `X-Api-Key`, e la chiave dice quale dei quattro utenti dimostrativi sta chiamando. Non è autenticazione reale. Ciò che è reale è il controllo: ogni operazione passa da un unico punto che consulta questa matrice e registra l'esito nell'audit, rifiuti compresi. Ciò che non è elencato è negato.

## Utenti dimostrativi

| Ruolo | Utente | Che cosa fa |
|---|---|---|
| `Dispatcher` | Giulia Ferri | Ufficio traffico del vettore: importa le spedizioni, le completa, vede chi le ha consultate. |
| `ClaimsManager` | Marco Colombo | Ufficio reclami del vettore: legge le spedizioni; le operazioni sui reclami arrivano con la Release 2. |
| `Authority` | Pattuglia 12 — controllo su strada | Autorità di controllo: parte da una targa, ottiene un UIL e con quello legge un sottoinsieme. |
| `Admin` | Elena Riva | Amministratore della piattaforma: legge spedizioni e audit, non modifica i dati di trasporto. |

## Matrice

Ogni cella dice **su che cosa** vale il permesso; una cella vuota è un rifiuto.

| Operazione | `Dispatcher` | `ClaimsManager` | `Authority` | `Admin` |
|---|---|---|---|---|
| `IdentityRead` | sé | sé | sé | sé |
| `ShipmentList` | tutte le spedizioni | tutte le spedizioni | — | tutte le spedizioni |
| `ShipmentRead` | tutte le spedizioni | tutte le spedizioni | — | tutte le spedizioni |
| `ShipmentImport` | tutte le spedizioni | — | — | — |
| `DatasetExport` | tutte le spedizioni | tutte le spedizioni | — | tutte le spedizioni |
| `IdentifierSearch` | — | — | registro degli identificatori | — |
| `SubsetRead` | — | — | sottoinsieme per UIL | — |
| `AuthorityAccessRead` | accessi dell'autorità | — | — | accessi dell'autorità |
| `AuditRead` | — | — | — | tutto l'audit |
| `TmsOrderRead` | tutte le spedizioni | — | — | — |
| `FieldProposalCreate` | tutte le spedizioni | — | — | — |
| `FieldProposalRead` | tutte le spedizioni | — | — | tutte le spedizioni |
| `FieldProposalDecide` | tutte le spedizioni | — | — | — |
| `DocumentUpload` | tutte le spedizioni | — | — | — |
| `DocumentList` | tutte le spedizioni | — | — | tutte le spedizioni |
| `DocumentRead` | tutte le spedizioni | — | — | tutte le spedizioni |
| `ReviewQueueRead` | tutte le spedizioni | — | — | tutte le spedizioni |
| `ReviewDecide` | tutte le spedizioni | — | — | — |
| `SettingsRead` | configurazione | — | — | configurazione |
| `ControlMomentSuggest` | — | — | registro degli identificatori | — |

## Operazioni

| Operazione | Tipo | Endpoint | Che cosa permette |
|---|---|---|---|
| `IdentityRead` | lettura | `GET /api/me` | Sapere con quale identità e quale ruolo si sta lavorando. |
| `ShipmentList` | lettura | `GET /api/views/shipments` | Elencare le spedizioni con lo stato del dataset. |
| `ShipmentRead` | lettura | `GET /api/views/shipments/{code}` | Leggere una spedizione con dataset completo e problemi di validazione. |
| `ShipmentImport` | scrittura | `POST /api/shipments/import` | Importare ordini dal TMS e creare le spedizioni. |
| `DatasetExport` | lettura | `GET /api/shipments/{code}/dataset.xml` | Scaricare il dataset completo in XML eFTI. |
| `IdentifierSearch` | lettura | `GET /api/gate/identifiers?plate=` | Cercare una targa nel registro degli identificatori: restituisce UIL e identificatori delle spedizioni a bordo in quel momento, nessun altro dato. |
| `SubsetRead` | lettura | `GET /api/gate/subset?uil=&subsetId=` | Leggere un sottoinsieme del dataset di una spedizione, dati UIL e sottoinsieme. |
| `AuthorityAccessRead` | lettura | `GET /api/shipments/{code}/authority-access` | Vedere quando un'autorità ha consultato una spedizione. |
| `AuditRead` | lettura | `GET /api/audit` | Leggere il registro di audit. |
| `TmsOrderRead` | lettura | `GET /api/shipments/{code}/tms-order` | Leggere l'ordine di origine nel TMS, per motivare una proposta di completamento. |
| `FieldProposalCreate` | scrittura | `POST /api/shipments/{code}/proposals` | Proporre il valore di un campo del dataset: la proposta non modifica il dataset. |
| `FieldProposalRead` | lettura | `GET /api/shipments/{code}/proposals` | Leggere le proposte di completamento di una spedizione e il loro esito. |
| `FieldProposalDecide` | scrittura | `POST /api/proposals/{id}/decision` | Accettare o rifiutare una proposta: l'accettazione crea una nuova versione del dataset. È un'azione umana, nessun tool la espone. |
| `DocumentUpload` | scrittura | `POST /api/shipments/{code}/documents` | Caricare il CMR in PDF di una spedizione ed estrarne i campi: l'estrazione lascia proposte e voci di revisione, non modifica il dataset. |
| `DocumentList` | lettura | `GET /api/shipments/{code}/documents` | Elencare i documenti caricati su una spedizione. |
| `DocumentRead` | lettura | `GET /api/documents/{id}` | Leggere un documento: testo letto, campi estratti, segnali e confidenza. |
| `ReviewQueueRead` | lettura | `GET /api/review-tasks` | Leggere la coda di revisione dei campi estratti sotto soglia. |
| `ReviewDecide` | scrittura | `POST /api/review-tasks/{id}/decision` | Confermare, correggere o rifiutare un campo in revisione: la conferma crea una nuova versione del dataset. È un'azione umana. |
| `SettingsRead` | lettura | `GET /api/settings` | Vedere la configurazione in vigore: modello per compito, pesi e soglia dell'estrazione, permanenza sul gate. Le manopole si cambiano sull'AppHost. |
| `ControlMomentSuggest` | lettura | `GET /api/gate/control-moments?plate=` | Aiuto per la demo: i momenti recenti in cui simulare un controllo su strada per quella targa. Restituisce solo date e numero di spedizioni a bordo. |

## Ambiti

- **sé**: solo la propria identità e l'elenco di ciò che il proprio ruolo può fare.
- **tutte le spedizioni**: tutte le spedizioni del vettore, con dataset completo e dati contrattuali.
- **registro degli identificatori**: solo il registro degli identificatori, cercato per targa: UIL, targhe, date, modo di trasporto, indicatore di merci pericolose. Nessun soggetto, nessuna merce.
- **sottoinsieme per UIL**: solo i sottoinsiemi `EU03` ed `EU05a` di una spedizione di cui si conosce l'UIL. Nessun elenco, nessun dato contrattuale.
- **accessi dell'autorità**: solo gli eventi di audit in cui un'autorità ha consultato una spedizione.
- **tutto l'audit**: tutto il registro di audit, in sola lettura: il database rifiuta modifiche e cancellazioni.
- **configurazione**: la configurazione in vigore, in sola lettura e senza segreti: modelli per compito, pesi e soglia dell'estrazione, permanenza sul gate.
