# eFTI XSD schemas

The files under `v0/` are **unmodified copies** of the eFTI consignment schemas published by the eFTI reference implementation.

| | |
|---|---|
| Source repository | <https://github.com/fintraffic-efti/efti-data-tools> (development continued from <https://github.com/EFTI4EU/efti-data-tools>) |
| Path in the source | `xsd/v0/` |
| Commit | `474df96073b2317e2bbc78b5941c6fcaeb507d40` (2026-09-25) |
| Schema versions | `consignment-common.xsd` 0.8, `consignment-identifier.xsd` 0.6 |
| Licence | Apache License 2.0, see [LICENSE](LICENSE) |
| Copied on | 2026-10-05 |

Copied files: `consignment-common.xsd`, `consignment-identifier.xsd`, `types/*.xsd`, `codes/*.xsd`. The example documents, the eDelivery message schemas and the test cases of the source folder are not copied.

The rest of this repository is under the MIT licence; these files keep their own licence. They are stored without end-of-line normalisation (`.gitattributes`) so that they stay byte-identical to the source.

## How the POC uses them

- `consignment-common.xsd` is the normative source for element names, eFTI identifiers, formats and subsets: the dataset saved by the POC uses the same element names, and the exported XML is validated against this schema.
- An element belongs to a subset when its annotation lists that subset id, as the reference tools do (`SubsetUtil` in the source repository). The `status` attribute (`M`, `C`, `O`, `SI` and combinations) is reported as written; its official meaning has not been verified against Delegated Regulation (EU) 2024/2024.
- `consignment-identifier.xsd` describes the identifier subset registered on the eFTI Gate.

The POC covers only part of the common dataset: see `docs/copertura-dataset.md`.
