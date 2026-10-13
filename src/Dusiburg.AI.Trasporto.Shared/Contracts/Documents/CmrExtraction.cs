using System.ComponentModel;

namespace Dusiburg.AI.Trasporto.Shared.Contracts.Documents;

// Ciò che il modello restituisce leggendo un CMR: lo schema JSON della risposta nasce da questi tipi, e le descrizioni sono
// la sola spiegazione che il modello riceve di ogni campo. Tutto è facoltativo: una casella vuota resta null. I pesi sono
// testo copiato dal documento: come si legge "8.840" lo decide il codice (CmrFieldMap), non il modello.

public sealed record CmrParty(
    [property: Description("Company name.")] string? Name,
    [property: Description("Street and house number.")] string? Street,
    [property: Description("Postal code.")] string? PostalCode,
    [property: Description("City.")] string? City,
    [property: Description("Two-letter ISO country code, e.g. IT, FR, DE.")] string? CountryCode,
    [property: Description("VAT number with country prefix, if written.")] string? VatNumber);

public sealed record CmrPlace(
    [property: Description("Street and house number.")] string? Street,
    [property: Description("Postal code.")] string? PostalCode,
    [property: Description("City.")] string? City,
    [property: Description("Two-letter ISO country code.")] string? CountryCode);

public sealed record CmrGoods(
    [property: Description("Nature of the goods, as written.")] string? Description,
    [property: Description("Number of packages of this line.")] int? PackageCount,
    [property: Description("Gross weight of this line, copied exactly as written, e.g. \"1.840\". Do not convert it to a number.")] string? GrossWeight,
    [property: Description("UN number of dangerous goods: four digits, without the UN prefix. Null if the line is not dangerous goods.")] string? UnNumber,
    [property: Description("Proper shipping name of dangerous goods.")] string? ProperShippingName,
    [property: Description("ADR class, e.g. 3 or 5.1.")] string? AdrClass,
    [property: Description("Packing group: I, II or III.")] string? PackingGroup);

public sealed record CmrExtraction(
    [property: Description("Number of the consignment note.")] string? CmrNumber,
    [property: Description("Box 1: sender.")] CmrParty? Sender,
    [property: Description("Box 2: consignee.")] CmrParty? Consignee,
    [property: Description("Box 3: place of delivery of the goods.")] CmrPlace? PlaceOfDelivery,
    [property: Description("Box 4: place of taking over of the goods.")] CmrPlace? PlaceOfTakingOver,
    [property: Description("Boxes 6-12: one item per line of goods, in the order of the document.")] IReadOnlyList<CmrGoods>? Goods,
    [property: Description("Box 7: total number of packages.")] int? TotalPackages,
    [property: Description("Box 11: total gross weight (gross, not net), copied exactly as written, e.g. \"8.840\". Do not convert it to a number.")] string? TotalGrossWeight,
    [property: Description("Registration plate of the motor vehicle.")] string? VehiclePlate,
    [property: Description("Registration plate of the trailer.")] string? TrailerPlate);
