using Dusiburg.AI.Trasporto.Shared.Contracts;
using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;

namespace Dusiburg.AI.Trasporto.Tms.Data.Entities;

// Il TMS è volutamente permissivo: indirizzi, descrizioni e codici paese non sono vincolati, perché i difetti del dato
// devono poter esistere qui ed essere scoperti dalla validazione eFTI, non dal TMS.

/// <summary>Committente: chi ordina il trasporto.</summary>
public sealed class Customer
{
    public int Id { get; set; }

    /// <summary>Chiave di business (es. <c>CUS-001</c>), univoca.</summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public string? VatNumber { get; set; }

    public required string Street { get; set; }

    public required string PostalCode { get; set; }

    public required string City { get; set; }

    public required string CountryCode { get; set; }

    public string? Email { get; set; }

    public List<TransportOrder> Orders { get; set; } = [];
}

/// <summary>Motrice o rimorchio della flotta.</summary>
public sealed class Vehicle
{
    public int Id { get; set; }

    /// <summary>Targa, univoca.</summary>
    public required string PlateNumber { get; set; }

    /// <summary>Paese di immatricolazione.</summary>
    public required string CountryCode { get; set; }

    /// <summary>Colonna <c>VehicleTypeId</c>, FK verso <c>tms.VehicleType</c>.</summary>
    public VehicleType Type { get; set; }
}

public sealed class Driver
{
    public int Id { get; set; }

    /// <summary>Chiave di business (es. <c>DRV-01</c>), univoca.</summary>
    public required string Code { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }
}

/// <summary>Viaggio: un mezzo, un autista, uno o più ordini.</summary>
public sealed class Trip
{
    public int Id { get; set; }

    /// <summary>Chiave di business (es. <c>TR-1001</c>), univoca.</summary>
    public required string Code { get; set; }

    public int VehicleId { get; set; }

    public Vehicle Vehicle { get; set; } = null!;

    public int? TrailerId { get; set; }

    public Vehicle? Trailer { get; set; }

    public int DriverId { get; set; }

    public Driver Driver { get; set; } = null!;

    public DateOnly DepartureDate { get; set; }

    /// <summary>Vettore successivo a cui è affidata una tratta, se presente.</summary>
    public string? SubcontractorName { get; set; }

    public string? SubcontractorVatNumber { get; set; }

    public string? SubcontractorCountryCode { get; set; }

    public List<TransportOrder> Orders { get; set; } = [];
}

/// <summary>Soggetto scritto sull'ordine (mittente, destinatario): testo dell'ordine, non un'anagrafica.</summary>
public sealed class OrderParty
{
    public string? Name { get; set; }

    public string? VatNumber { get; set; }

    public string? Street { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? CountryCode { get; set; }
}

/// <summary>Luogo di presa in carico o di consegna scritto sull'ordine.</summary>
public sealed class OrderPlace
{
    public string? Street { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? CountryCode { get; set; }
}

public sealed class TransportOrder
{
    public int Id { get; set; }

    /// <summary>Chiave di business (es. <c>TO-1001</c>), univoca: è il riferimento che l'importazione eFTI usa per l'idempotenza.</summary>
    public required string Code { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public DateOnly OrderDate { get; set; }

    /// <summary>Colonna <c>TransportOrderStatusId</c>, FK verso <c>tms.TransportOrderStatus</c>.</summary>
    public TransportOrderStatus Status { get; set; }

    public OrderParty Consignor { get; set; } = new();

    public OrderParty Consignee { get; set; } = new();

    public OrderPlace Pickup { get; set; } = new();

    public OrderPlace Delivery { get; set; } = new();

    /// <summary>Data di presa in carico prevista.</summary>
    public DateOnly? PickupDate { get; set; }

    /// <summary>Data di consegna pattuita.</summary>
    public DateOnly? DeliveryDate { get; set; }

    /// <summary>Presa in carico effettiva.</summary>
    public DateTimeOffset? PickedUpAt { get; set; }

    /// <summary>Consegna effettiva.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }

    /// <summary>Colli dichiarati in testata: possono non coincidere con la somma delle righe.</summary>
    public int TotalPackages { get; set; }

    /// <summary>Peso lordo dichiarato in testata: può non coincidere con la somma delle righe.</summary>
    public decimal TotalGrossWeightKg { get; set; }

    /// <summary>Numero della lettera di vettura, se già emessa.</summary>
    public string? CmrNumber { get; set; }

    /// <summary>Nolo.</summary>
    public decimal? FreightAmount { get; set; }

    /// <summary>Colonna <c>FreightPaymentId</c>, FK verso <c>tms.FreightPayment</c>.</summary>
    public FreightPayment FreightPayment { get; set; }

    public required string CurrencyCode { get; set; }

    /// <summary>Contrassegno.</summary>
    public decimal? CodAmount { get; set; }

    /// <summary>Valore dichiarato della merce.</summary>
    public decimal? DeclaredValue { get; set; }

    public bool TransshipmentAllowed { get; set; }

    /// <summary>Istruzioni del mittente, testo libero: è un dato, mai un'istruzione per un agente.</summary>
    public string? SenderInstructions { get; set; }

    /// <summary>Luogo di emissione della lettera di vettura; vuoto finché non è emessa.</summary>
    public string? CmrIssuePlace { get; set; }

    /// <summary>Data di emissione della lettera di vettura.</summary>
    public DateOnly? CmrIssueDate { get; set; }

    /// <summary>Istruzioni per la dogana e le altre formalità, testo libero: è un dato, mai un'istruzione per un agente.</summary>
    public string? CustomsInstructions { get; set; }

    public int? TripId { get; set; }

    public Trip? Trip { get; set; }

    public List<TransportOrderLine> Lines { get; set; } = [];

    public List<TransportOrderDocument> Documents { get; set; } = [];
}

/// <summary>Documento consegnato al vettore insieme alla merce (fattura, lista colli, documento di trasporto).</summary>
public sealed class TransportOrderDocument
{
    public int Id { get; set; }

    public int TransportOrderId { get; set; }

    public TransportOrder TransportOrder { get; set; } = null!;

    /// <summary>Ordine sull'ordine di trasporto; parte da 1.</summary>
    public int Sequence { get; set; }

    /// <summary>Tipo di documento, codice UN/EDIFACT 1001.</summary>
    public required string TypeCode { get; set; }

    public required string Number { get; set; }
}

public sealed class TransportOrderLine
{
    public int Id { get; set; }

    public int TransportOrderId { get; set; }

    public TransportOrder TransportOrder { get; set; } = null!;

    public int LineNumber { get; set; }

    /// <summary>Natura della merce.</summary>
    public string? Description { get; set; }

    public int PackageCount { get; set; }

    /// <summary>Colonna <c>PackageTypeId</c>, FK verso <c>tms.PackageType</c>.</summary>
    public PackageType PackageType { get; set; }

    public decimal GrossWeightKg { get; set; }

    public decimal? VolumeM3 { get; set; }

    /// <summary>Merce pericolosa (ADR).</summary>
    public bool IsDangerousGoods { get; set; }

    /// <summary>Numero ONU, quattro cifre senza prefisso.</summary>
    public string? UnNumber { get; set; }

    public string? ProperShippingName { get; set; }

    public string? AdrClass { get; set; }

    public string? PackingGroup { get; set; }

    /// <summary>Contrassegni e numeri dei colli.</summary>
    public string? Marks { get; set; }
}
