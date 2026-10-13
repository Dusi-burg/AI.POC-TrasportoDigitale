# eCMR XSD schemas (UN/CEFACT)

The files under `uncefact/` are **unmodified copies** of the UN/CEFACT eCMR XML schemas.

| | |
|---|---|
| Standard | UN/CEFACT eCMR (electronic consignment note), based on the Multi-Modal Transport Reference Data Model |
| Release | **D25A**, schema version `100.D25A` |
| Root schema | `uncefact/eCMR_100pD25A.xsd`, root element `rsm:eCMR`, namespace `urn:un:unece:uncefact:data:standard:eCMR:100` |
| Files | 28: the root schema, three data type schemas (`ReusableAggregateBusinessInformationEntity`, `QualifiedDataType`, `UnqualifiedDataType`) and 24 code lists |
| Source | UNECE, <https://unece.org/trade/uncefact/mainstandards> (eCMR). Added to the repository by the project owner on 2026-10-07 |
| Licence | **Not verified.** The UN/CEFACT terms of use have not been checked: verify them before publishing the repository |

The official package `eCMR_D25A.zip` downloaded from unece.org on 2026-10-07 contained an **empty** `XSD/` folder (only the
`GuidelineStructure.xlsx` and the UML diagram); the schema files come from a separate download.

The files are stored without end-of-line normalisation (`.gitattributes`) so that they stay byte-identical to the source.

## How the POC uses them

- The eCMR document of a shipment is saved as JSON **with the element names of the schema**; the XML is produced from that
  JSON by walking the compiled schema (element order, namespaces, attributes) and is validated against these files.
- Almost every element is optional in the schema: conformity checks structure, order, types and code lists. What a
  consignment note must contain is checked by the declarative rules in `docs/regole-ecmr.md`.
- The schema has no element for the declared value (CMR art. 24), for the ADR class and for signatures: see
  `docs/regole-ecmr.md`.

The eCMR is **not** an eFTI subset: it is the contract of carriage between private parties, while the eFTI dataset
(`data/efti-schema`) is what authorities read.
