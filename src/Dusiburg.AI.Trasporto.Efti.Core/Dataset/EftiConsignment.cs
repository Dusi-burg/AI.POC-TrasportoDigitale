using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dusiburg.AI.Trasporto.Efti.Core.Dataset;

// Il dataset eFTI della spedizione, limitato agli elementi che il POC copre (docs/copertura-dataset.md). I nomi JSON sono
// quelli degli elementi dello schema, così il JSON salvato e l'XML esportato parlano la stessa lingua. Un elemento ripetibile
// nello schema è un array; un valore con attributo (unità, valuta, formato) è un oggetto { value, <attributo> }.

public sealed record EftiAmount(decimal Value, string CurrencyId);

public sealed record EftiMeasure(decimal Value, string UnitId);

/// <param name="FormatId">UN/EDIFACT 2379: <c>102</c> = CCYYMMDD, <c>205</c> = CCYYMMDDHHMMZHHMM.</param>
public sealed record EftiDateTime(string Value, string FormatId);

public sealed record EftiTaxRegistration(string Id);

public sealed record EftiTradeCountry(string Code);

public sealed record EftiTradeAddress
{
    public IReadOnlyList<string>? CityName { get; init; }

    public string? CountryCode { get; init; }

    public IReadOnlyList<string>? Postcode { get; init; }

    public IReadOnlyList<string>? StreetName { get; init; }
}

public sealed record EftiTradeParty
{
    public IReadOnlyList<string>? Name { get; init; }

    public EftiTradeAddress? PostalAddress { get; init; }

    public IReadOnlyList<EftiTaxRegistration>? TaxRegistration { get; init; }
}

public sealed record EftiLogisticsLocation
{
    public EftiTradeAddress? PostalAddress { get; init; }
}

public sealed record EftiTransportEvent
{
    public EftiDateTime? ActualOccurrenceDateTime { get; init; }

    public EftiDateTime? ScheduledOccurrenceDateTime { get; init; }
}

public sealed record EftiTransportCargo
{
    public IReadOnlyList<string>? IdentificationText { get; init; }
}

public sealed record EftiLogisticsPackage
{
    public int? ItemQuantity { get; init; }

    public IReadOnlyList<string>? TypeCode { get; init; }
}

public sealed record EftiDangerousGoods
{
    public IReadOnlyList<EftiLogisticsPackage>? DangerousGoodsLogisticsPackage { get; init; }

    public EftiMeasure? GrossWeight { get; init; }

    /// <summary>Classe ADR.</summary>
    public string? HazardClassificationID { get; init; }

    /// <summary>Gruppo di imballaggio.</summary>
    public string? PackagingDangerLevelCode { get; init; }

    public IReadOnlyList<string>? ProperShippingName { get; init; }

    /// <summary>Numero ONU.</summary>
    [JsonPropertyName("uNDGID")]
    public string? UnDgId { get; init; }
}

public sealed record EftiConsignmentItem
{
    public IReadOnlyList<EftiMeasure>? GrossVolume { get; init; }

    public IReadOnlyList<EftiDangerousGoods>? TransportDangerousGoods { get; init; }
}

public sealed record EftiTransportMeans
{
    /// <summary>Targa.</summary>
    public string? Id { get; init; }

    public EftiTradeCountry? RegistrationCountry { get; init; }

    /// <summary>Tipo di mezzo, UN/ECE Rec. 28.</summary>
    public string? TypeCode { get; init; }
}

public sealed record EftiTransportMovement
{
    public bool? DangerousGoodsIndicator { get; init; }

    public string? ModeCode { get; init; }

    public EftiTransportMeans? UsedTransportMeans { get; init; }
}

public sealed record EftiTransportEquipment
{
    public string? CategoryCode { get; init; }

    /// <summary>Targa.</summary>
    public string? Id { get; init; }

    public EftiTradeCountry? RegistrationCountry { get; init; }

    public int? SequenceNumber { get; init; }
}

public sealed record EftiReferencedDocument
{
    public string? Id { get; init; }

    public string? TypeCode { get; init; }
}

public sealed record EftiServiceCharge
{
    public IReadOnlyList<EftiAmount>? AppliedAmount { get; init; }

    public string? PaymentArrangementCode { get; init; }
}

/// <summary>Radice del dataset: elemento <c>consignment</c> dello schema comune.</summary>
public sealed record EftiConsignment
{
    public IReadOnlyList<EftiServiceCharge>? ApplicableServiceCharge { get; init; }

    [JsonPropertyName("cODAmount")]
    public EftiAmount? CodAmount { get; init; }

    public EftiTradeParty? Carrier { get; init; }

    public EftiDateTime? CarrierAcceptanceDateTime { get; init; }

    public EftiLogisticsLocation? CarrierAcceptanceLocation { get; init; }

    public IReadOnlyList<EftiTradeParty>? ConnectingCarrier { get; init; }

    public EftiTradeParty? Consignee { get; init; }

    public EftiLogisticsLocation? ConsigneeReceiptLocation { get; init; }

    public EftiTradeParty? Consignor { get; init; }

    public IReadOnlyList<string>? ConsignorProvidedInformationText { get; init; }

    public EftiAmount? DeclaredValueForCarriageAmount { get; init; }

    public EftiTransportEvent? DeliveryEvent { get; init; }

    public IReadOnlyList<EftiMeasure>? GrossVolume { get; init; }

    public IReadOnlyList<EftiMeasure>? GrossWeight { get; init; }

    public IReadOnlyList<EftiConsignmentItem>? IncludedConsignmentItem { get; init; }

    public IReadOnlyList<EftiTransportMovement>? MainCarriageTransportMovement { get; init; }

    public EftiTransportCargo? NatureIdentificationCargo { get; init; }

    public int? NumberOfPackages { get; init; }

    public string? PaymentArrangementCode { get; init; }

    public EftiReferencedDocument? TransportContractDocument { get; init; }

    public IReadOnlyList<EftiLogisticsPackage>? TransportPackage { get; init; }

    public bool? TransshipmentPermittedIndicator { get; init; }

    public IReadOnlyList<EftiTransportEquipment>? UsedTransportEquipment { get; init; }
}

/// <summary>JSON del dataset: nomi degli elementi dello schema (camelCase, salvo le due eccezioni dichiarate), niente <c>null</c>.</summary>
public static class EftiDatasetJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        // Il JSON finisce in una colonna nvarchar e in pagine web: i caratteri accentati restano leggibili.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(EftiConsignment consignment) => JsonSerializer.Serialize(consignment, Options);

    public static EftiConsignment Deserialize(string json) =>
        JsonSerializer.Deserialize<EftiConsignment>(json, Options) ?? throw new JsonException("Dataset eFTI vuoto.");
}
