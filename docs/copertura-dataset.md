# Copertura del dataset eFTI

> Documento **generato dal codice**: non modificarlo a mano. Si rigenera con `dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- docs`.

Schema: `consignment-common.xsd` versione **0.8** (`data/efti-schema/`). Identificativi, formati e sottoinsiemi sono letti dallo schema; l'origine del dato e la casella del CMR sono dichiarate nel codice (`DatasetCoverage`).

Il POC copre il trasporto su strada e solo una parte del dataset comune. Lo stato nei sottoinsiemi è riportato come scritto nello schema (`M`, `C`, `O`, `SI` e combinazioni): il significato ufficiale di `SI` non è stato verificato sul Reg. delegato (UE) 2024/2024. Le caselle del CMR seguono il modello IRU e sono da riconfermare.

## Elementi coperti (64)

| Elemento | Id eFTI | Formato | EU03 | EU05a | Origine | CMR |
|---|---|---|---|---|---|---|
| `consignor/name` | eFTI51 | an..35 | O | O | ordine del TMS: mittente, nome | 1 |
| `consignor/postalAddress/streetName` | eFTI56 | an..35 | M | M | ordine del TMS: mittente, via | 1 |
| `consignor/postalAddress/postcode` | eFTI54 | an..17 | M | M | ordine del TMS: mittente, CAP | 1 |
| `consignor/postalAddress/cityName` | eFTI57 | an..35 | M | M | ordine del TMS: mittente, città | 1 |
| `consignor/postalAddress/countryCode` | eFTI58 | a2 | M | M | ordine del TMS: mittente, paese | 1 |
| `consignor/taxRegistration/id` | eFTI859 | an..35 | — | — | ordine del TMS: mittente, partita IVA | 1 |
| `consignee/name` | eFTI51 | an..35 | O | O | ordine del TMS: destinatario, nome | 2 |
| `consignee/postalAddress/streetName` | eFTI56 | an..35 | M | M | ordine del TMS: destinatario, via | 2 |
| `consignee/postalAddress/postcode` | eFTI54 | an..17 | M | M | ordine del TMS: destinatario, CAP | 2 |
| `consignee/postalAddress/cityName` | eFTI57 | an..35 | M | M | ordine del TMS: destinatario, città | 2 |
| `consignee/postalAddress/countryCode` | eFTI58 | a2 | M | M | ordine del TMS: destinatario, paese | 2 |
| `consignee/taxRegistration/id` | eFTI859 | an..35 | — | — | ordine del TMS: destinatario, partita IVA | 2 |
| `carrier/name` | eFTI51 | an..35 | O | O | configurazione della piattaforma: vettore, nome | 16 |
| `carrier/postalAddress/streetName` | eFTI56 | an..35 | M | M | configurazione della piattaforma: vettore, via | 16 |
| `carrier/postalAddress/postcode` | eFTI54 | an..17 | M | M | configurazione della piattaforma: vettore, CAP | 16 |
| `carrier/postalAddress/cityName` | eFTI57 | an..35 | M | M | configurazione della piattaforma: vettore, città | 16 |
| `carrier/postalAddress/countryCode` | eFTI58 | a2 | M | M | configurazione della piattaforma: vettore, paese | 16 |
| `carrier/taxRegistration/id` | eFTI859 | an..35 | — | — | configurazione della piattaforma: vettore, partita IVA | 16 |
| `connectingCarrier/name` | eFTI51 | an..35 | O | O | viaggio: vettore successivo, nome | 17 |
| `connectingCarrier/postalAddress/countryCode` | eFTI58 | a2 | M | M | viaggio: vettore successivo, paese | 17 |
| `connectingCarrier/taxRegistration/id` | eFTI859 | an..35 | — | — | viaggio: vettore successivo, partita IVA | 17 |
| `carrierAcceptanceLocation/postalAddress/streetName` | eFTI56 | an..35 | M | M | ordine del TMS: luogo di presa in carico, via | 4 |
| `carrierAcceptanceLocation/postalAddress/postcode` | eFTI54 | an..17 | M | M | ordine del TMS: luogo di presa in carico, CAP | 4 |
| `carrierAcceptanceLocation/postalAddress/cityName` | eFTI57 | an..35 | M | M | ordine del TMS: luogo di presa in carico, città | 4 |
| `carrierAcceptanceLocation/postalAddress/countryCode` | eFTI58 | a2 | M | M | ordine del TMS: luogo di presa in carico, paese | 4 |
| `carrierAcceptanceDateTime` | eFTI39 | an..20 | M | — | ordine del TMS: presa in carico effettiva, altrimenti prevista | 4 |
| `consigneeReceiptLocation/postalAddress/streetName` | eFTI56 | an..35 | M | M | ordine del TMS: luogo di consegna, via | 3 |
| `consigneeReceiptLocation/postalAddress/postcode` | eFTI54 | an..17 | M | M | ordine del TMS: luogo di consegna, CAP | 3 |
| `consigneeReceiptLocation/postalAddress/cityName` | eFTI57 | an..35 | M | M | ordine del TMS: luogo di consegna, città | 3 |
| `consigneeReceiptLocation/postalAddress/countryCode` | eFTI58 | a2 | M | M | ordine del TMS: luogo di consegna, paese | 3 |
| `deliveryEvent/scheduledOccurrenceDateTime` | eFTI190 | an..20 | — | — | ordine del TMS: consegna pattuita | — |
| `deliveryEvent/actualOccurrenceDateTime` | eFTI188 | an..20 | C | — | ordine del TMS: consegna effettiva | 24 |
| `natureIdentificationCargo/identificationText` | eFTI699 | an..512 | M | — | righe dell'ordine: descrizione | 9 |
| `numberOfPackages` | eFTI47 | n..8 | — | — | ordine del TMS: colli dichiarati | 7 |
| `transportPackage/itemQuantity` | eFTI296 | n..8 | — | C/C | righe dell'ordine: colli | 7 |
| `transportPackage/typeCode` | eFTI297 | an..6 | — | O | righe dell'ordine: tipo di imballo | 8 |
| `grossWeight` | eFTI41 | n..16,6 | C | — | ordine del TMS: peso lordo dichiarato | 11 |
| `grossVolume` | eFTI45 | n..16,6 | C | — | righe dell'ordine: somma dei volumi | 12 |
| `includedConsignmentItem/grossVolume` | eFTI228 | n..16,6 | — | — | righe dell'ordine: volume | 12 |
| `includedConsignmentItem/transportDangerousGoods/uNDGID` | eFTI232 | n4 | M | C/M | righe dell'ordine: numero ONU | 9 |
| `includedConsignmentItem/transportDangerousGoods/properShippingName` | eFTI251 | an..128 | O | C | righe dell'ordine: denominazione ufficiale di trasporto | 9 |
| `includedConsignmentItem/transportDangerousGoods/hazardClassificationID` | eFTI242 | an..7 | — | C | righe dell'ordine: classe ADR | 9 |
| `includedConsignmentItem/transportDangerousGoods/packagingDangerLevelCode` | eFTI236 | an..3 | — | C/C | righe dell'ordine: gruppo di imballaggio | 9 |
| `includedConsignmentItem/transportDangerousGoods/grossWeight` | eFTI238 | n..16,6 | — | C | righe dell'ordine: peso della riga ADR | 11 |
| `includedConsignmentItem/transportDangerousGoods/dangerousGoodsLogisticsPackage/itemQuantity` | eFTI296 | n..8 | — | C/C | righe dell'ordine: colli della riga ADR | 7 |
| `includedConsignmentItem/transportDangerousGoods/dangerousGoodsLogisticsPackage/typeCode` | eFTI297 | an..6 | — | O | righe dell'ordine: tipo di imballo della riga ADR | 8 |
| `mainCarriageTransportMovement/modeCode` | eFTI581 | n1 | — | M | fisso: strada | — |
| `mainCarriageTransportMovement/dangerousGoodsIndicator` | eFTI1451 | — | — | — | righe dell'ordine: almeno una riga ADR | — |
| `mainCarriageTransportMovement/usedTransportMeans/id` | eFTI618 | an..17 | M | C | viaggio: targa della motrice | — |
| `mainCarriageTransportMovement/usedTransportMeans/registrationCountry/code` | eFTI578 | a2 | M | — | viaggio: paese della motrice | — |
| `mainCarriageTransportMovement/usedTransportMeans/typeCode` | eFTI617 | an..3 | M | — | viaggio: tipo di mezzo (autocarro o trattore) | — |
| `usedTransportEquipment/id` | eFTI374 | an..17 | M | O/C | viaggio: targa del rimorchio | — |
| `usedTransportEquipment/categoryCode` | eFTI378 | an..3 | M | C | fisso: semirimorchio | — |
| `usedTransportEquipment/registrationCountry/code` | eFTI578 | a2 | M | — | viaggio: paese del rimorchio | — |
| `usedTransportEquipment/sequenceNumber` | eFTI987 | n..16 | — | O | fisso: 1 | — |
| `transportContractDocument/id` | eFTI170 | an..17 | — | — | ordine del TMS: numero della lettera di vettura | — |
| `transportContractDocument/typeCode` | eFTI169 | an..3 | — | — | fisso: lettera di vettura stradale | — |
| `applicableServiceCharge/appliedAmount` | eFTI951 | n..16,3 | — | — | ordine del TMS: nolo | 20 |
| `applicableServiceCharge/paymentArrangementCode` | eFTI945 | an..3 | — | — | ordine del TMS: pagamento del nolo | 14 |
| `paymentArrangementCode` | eFTI1307 | an..3 | — | — | ordine del TMS: pagamento del nolo | 14 |
| `cODAmount` | eFTI854 | n..16,3 | — | — | ordine del TMS: contrassegno | 15 |
| `declaredValueForCarriageAmount` | eFTI1149 | n..16,3 | — | — | ordine del TMS: valore dichiarato | 19 |
| `transshipmentPermittedIndicator` | eFTI857 | n1 | — | — | ordine del TMS: trasbordo ammesso | 19 |
| `consignorProvidedInformationText` | eFTI858 | an..256 | — | — | ordine del TMS: istruzioni del mittente | 13 |

## Elementi dei sottoinsiemi non coperti (92)

Elementi che lo schema assegna a EU03 o EU05a e che il POC non valorizza, limitati ai contenitori che il POC usa. Sono il perimetro dichiarato: un'autorità non li troverebbe nel dataset.

| Elemento | Id eFTI | Formato | EU03 | EU05a |
|---|---|---|---|---|
| `dangerousGoods` | ASBIE1127 | — | — | SI |
| `carrier/confirmedDocumentAuthentication` | ASBIE1033 | — | SI | — |
| `carrier/definedContactDetails` | ASBIE1029 | — | SI | — |
| `carrier/id` | eFTI49 | an..17 | O | O |
| `carrier/roleCode` | eFTI88 | an..3 | O | — |
| `carrier/postalAddress/additionalStreetName` | eFTI62 | an..35 | M | M |
| `carrier/postalAddress/buildingNumber` | eFTI60 | an..17 | M | M |
| `carrier/postalAddress/countrySubDivisionName` | eFTI59 | an..35 | M | M |
| `carrier/postalAddress/departmentName` | eFTI61 | an..35 | — | M |
| `carrier/postalAddress/postOfficeBox` | eFTI55 | an..17 | M | M |
| `carrierAcceptanceLocation/geographicalCoordinates` | ASBIE1049 | — | SI | — |
| `carrierAcceptanceLocation/id` | eFTI136 | an..17 | C | — |
| `carrierAcceptanceLocation/name` | eFTI138 | an..35 | C | — |
| `carrierAcceptanceLocation/postalAddress/additionalStreetName` | eFTI62 | an..35 | M | M |
| `carrierAcceptanceLocation/postalAddress/buildingNumber` | eFTI60 | an..17 | M | M |
| `carrierAcceptanceLocation/postalAddress/countrySubDivisionName` | eFTI59 | an..35 | M | M |
| `carrierAcceptanceLocation/postalAddress/departmentName` | eFTI61 | an..35 | — | M |
| `carrierAcceptanceLocation/postalAddress/postOfficeBox` | eFTI55 | an..17 | M | M |
| `connectingCarrier/confirmedDocumentAuthentication` | ASBIE1033 | — | SI | — |
| `connectingCarrier/definedContactDetails` | ASBIE1029 | — | SI | — |
| `connectingCarrier/id` | eFTI49 | an..17 | O | O |
| `connectingCarrier/roleCode` | eFTI88 | an..3 | O | — |
| `connectingCarrier/postalAddress/additionalStreetName` | eFTI62 | an..35 | M | M |
| `connectingCarrier/postalAddress/buildingNumber` | eFTI60 | an..17 | M | M |
| `connectingCarrier/postalAddress/cityName` | eFTI57 | an..35 | M | M |
| `connectingCarrier/postalAddress/countrySubDivisionName` | eFTI59 | an..35 | M | M |
| `connectingCarrier/postalAddress/departmentName` | eFTI61 | an..35 | — | M |
| `connectingCarrier/postalAddress/postOfficeBox` | eFTI55 | an..17 | M | M |
| `connectingCarrier/postalAddress/postcode` | eFTI54 | an..17 | M | M |
| `connectingCarrier/postalAddress/streetName` | eFTI56 | an..35 | M | M |
| `consignee/confirmedDocumentAuthentication` | ASBIE1033 | — | SI | — |
| `consignee/definedContactDetails` | ASBIE1029 | — | SI | — |
| `consignee/id` | eFTI49 | an..17 | O | O |
| `consignee/roleCode` | eFTI88 | an..3 | O | — |
| `consignee/postalAddress/additionalStreetName` | eFTI62 | an..35 | M | M |
| `consignee/postalAddress/buildingNumber` | eFTI60 | an..17 | M | M |
| `consignee/postalAddress/countrySubDivisionName` | eFTI59 | an..35 | M | M |
| `consignee/postalAddress/departmentName` | eFTI61 | an..35 | — | M |
| `consignee/postalAddress/postOfficeBox` | eFTI55 | an..17 | M | M |
| `consigneeReceiptLocation/geographicalCoordinates` | ASBIE1049 | — | SI | — |
| `consigneeReceiptLocation/id` | eFTI136 | an..17 | C | — |
| `consigneeReceiptLocation/name` | eFTI138 | an..35 | C | — |
| `consigneeReceiptLocation/postalAddress/additionalStreetName` | eFTI62 | an..35 | M | M |
| `consigneeReceiptLocation/postalAddress/buildingNumber` | eFTI60 | an..17 | M | M |
| `consigneeReceiptLocation/postalAddress/countrySubDivisionName` | eFTI59 | an..35 | M | M |
| `consigneeReceiptLocation/postalAddress/departmentName` | eFTI61 | an..35 | — | M |
| `consigneeReceiptLocation/postalAddress/postOfficeBox` | eFTI55 | an..17 | M | M |
| `consignor/confirmedDocumentAuthentication` | ASBIE1033 | — | SI | — |
| `consignor/definedContactDetails` | ASBIE1029 | — | SI | — |
| `consignor/id` | eFTI49 | an..17 | O | O |
| `consignor/roleCode` | eFTI88 | an..3 | O | — |
| `consignor/postalAddress/additionalStreetName` | eFTI62 | an..35 | M | M |
| `consignor/postalAddress/buildingNumber` | eFTI60 | an..17 | M | M |
| `consignor/postalAddress/countrySubDivisionName` | eFTI59 | an..35 | M | M |
| `consignor/postalAddress/departmentName` | eFTI61 | an..35 | — | M |
| `consignor/postalAddress/postOfficeBox` | eFTI55 | an..17 | M | M |
| `includedConsignmentItem/associatedTransportEquipment` | ASBIE1082 | — | — | SI |
| `includedConsignmentItem/goodsUnitQuantity` | eFTI971 | n..8 | — | C |
| `includedConsignmentItem/transportDangerousGoods/applicableRegulatoryExemption` | ASBIE1750 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/controlTemperature` | ASBIE1067 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/emergencyTemperature` | ASBIE1068 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/explosiveCargoNetWeight` | eFTI249 | n..16,6 | — | C |
| `includedConsignmentItem/transportDangerousGoods/grossVolume` | eFTI243 | n..16,6 | — | C |
| `includedConsignmentItem/transportDangerousGoods/hazardCategoryCode` | eFTI258 | an..3 | — | C |
| `includedConsignmentItem/transportDangerousGoods/includedFuel` | ASBIE1080 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/netWeight` | eFTI247 | n..16,6 | — | C/C |
| `includedConsignmentItem/transportDangerousGoods/radioactiveMaterial` | ASBIE1069 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/regulatoryAuthorityName` | eFTI388 | an..35 | — | C |
| `includedConsignmentItem/transportDangerousGoods/relatedDocument` | ASBIE1079 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/reportableQuantity` | eFTI399 | n..8 | — | C |
| `includedConsignmentItem/transportDangerousGoods/specialProvisionID` | eFTI256 | an..17 | — | C/C/C/C/C/C/C/C/C/C |
| `includedConsignmentItem/transportDangerousGoods/statedCondition` | ASBIE1077 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/supplementaryInformation` | eFTI397 | an..128 | — | C |
| `includedConsignmentItem/transportDangerousGoods/technicalName` | eFTI235 | an..128 | O | — |
| `includedConsignmentItem/transportDangerousGoods/technicalNameNote` | ASBIE1140 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/tunnelRestrictionCode` | eFTI255 | an..3 | — | C |
| `includedConsignmentItem/transportDangerousGoods/dangerousGoodsLogisticsPackage/referencedDocument` | ASBIE1075 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/dangerousGoodsLogisticsPackage/shippingMarks` | ASBIE1074 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/dangerousGoodsLogisticsPackage/statedCondition` | ASBIE1076 | — | — | SI |
| `includedConsignmentItem/transportDangerousGoods/dangerousGoodsLogisticsPackage/typeText` | eFTI298 | an..35 | — | C |
| `natureIdentificationCargo/operationalCategoryCode` | eFTI700 | an..3 | O | — |
| `natureIdentificationCargo/statisticalClassificationCode` | eFTI701 | an..3 | O | — |
| `natureIdentificationCargo/typeCode` | eFTI698 | an..3 | O | — |
| `transportContractDocument/attachedBinaryObject` | eFTI329 | — | — | C |
| `transportPackage/referencedDocument` | ASBIE1075 | — | — | SI |
| `transportPackage/shippingMarks` | ASBIE1074 | — | — | SI |
| `transportPackage/statedCondition` | ASBIE1076 | — | — | SI |
| `transportPackage/typeText` | eFTI298 | an..35 | — | C |
| `usedTransportEquipment/carriedTransportEquipment` | ASBIE1092 | — | — | SI |
| `usedTransportEquipment/fullEmptyCode` | eFTI382 | an..3 | — | C/C/C |
| `usedTransportEquipment/loadedDangerousGoods` | ASBIE1087 | — | — | SI |
| `usedTransportEquipment/reportableQuantity` | eFTI1014 | n..8 | — | C |
