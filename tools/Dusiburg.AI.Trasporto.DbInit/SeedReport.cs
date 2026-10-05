using Dusiburg.AI.Trasporto.Shared.Contracts.Tms;
using Dusiburg.AI.Trasporto.Shared.Demo;
using Dusiburg.AI.Trasporto.Tms.Data;
using Dusiburg.AI.Trasporto.Tms.Data.Seed;
using Microsoft.EntityFrameworkCore;

namespace Dusiburg.AI.Trasporto.DbInit;

/// <summary>
/// Riepilogo finale del seed: conteggi per tabella e ordini per stato letti dal database, distribuzione dei difetti presa dal
/// risultato del generatore (i difetti assegnati non sono una tabella).
/// </summary>
public static class SeedReport
{
    public static async Task WriteAsync(TmsDbContext db, TmsSeedData data, TextWriter output, CancellationToken cancellationToken = default)
    {
        int customers = await db.Customers.CountAsync(cancellationToken);
        int vehicles = await db.Vehicles.CountAsync(cancellationToken);
        int drivers = await db.Drivers.CountAsync(cancellationToken);
        int trips = await db.Trips.CountAsync(cancellationToken);
        int orders = await db.TransportOrders.CountAsync(cancellationToken);
        int lines = await db.TransportOrderLines.CountAsync(cancellationToken);

        output.WriteLine();
        output.WriteLine($"Dati sintetici: RandomSeed={data.RandomSeed}, data di riferimento {data.Today:yyyy-MM-dd}.");
        output.WriteLine($"Clienti {customers} · Mezzi {vehicles} · Autisti {drivers} · Viaggi {trips} · Ordini {orders} · Righe {lines}");
        output.WriteLine($"Ordini demo: {string.Join(", ", DemoCatalog.Orders.Select(o => o.Code))}");

        var byStatus = await db.TransportOrders
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Orders = g.Count() })
            .ToListAsync(cancellationToken);

        output.WriteLine();
        output.WriteLine("Ordini per stato");

        foreach (TransportOrderStatus status in Enum.GetValues<TransportOrderStatus>())
        {
            output.WriteLine($"  {status,-12}{byStatus.SingleOrDefault(s => s.Status == status)?.Orders ?? 0,5}");
        }

        WriteScope(data, output);
        WriteDefects(data, output);
    }

    private static void WriteScope(TmsSeedData data, TextWriter output)
    {
        int international = data.Orders.Count(o => o.Pickup.CountryCode != o.Delivery.CountryCode);
        int dangerous = data.Orders.Count(o => o.Lines.Any(l => l.IsDangerousGoods));

        output.WriteLine();
        output.WriteLine($"Ambito: nazionali {data.Orders.Count - international}, internazionali {international}; con merci pericolose {dangerous}");
    }

    private static void WriteDefects(TmsSeedData data, TextWriter output)
    {
        int generated = data.Orders.Count - DemoCatalog.Orders.Count;
        int defective = data.DefectsByOrderCode.Count;

        output.WriteLine();
        output.WriteLine($"Difetti voluti: {defective} ordini generati su {generated} ({(double)defective / generated:P0}), " +
            $"{data.DefectsByOrderCode.Values.Sum(d => d.Count)} difetti in tutto");

        foreach (SeedDefect defect in Enum.GetValues<SeedDefect>())
        {
            output.WriteLine($"  {defect,-26}{data.DefectsByOrderCode.Values.Count(d => d.Contains(defect)),4}");
        }
    }
}
