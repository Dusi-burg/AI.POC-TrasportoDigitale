using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Formatting;
using Dusiburg.AI.Trasporto.Shared.Grounding;

namespace Dusiburg.AI.Trasporto.Agents.Tests;

/// <summary>Misure e momenti del dataset scritti per una persona, e il controllo dei valori citati in quella forma.</summary>
public class ReadableValuesTests
{
    [TestCase("5300.000", "KGM", "5.300 kg")]
    [TestCase("18.200", "MTQ", "18,2 m³")]
    [TestCase("0.5", "KGM", "0,5 kg")]
    [TestCase("1234567.891", "KGM", "1.234.567,891 kg")]
    [TestCase("12", "TNE", "12 t")]
    [TestCase("40", "XYZ", "40 XYZ")]
    [TestCase("40", null, "40")]
    public void Measure_IsWrittenTheItalianWay(string value, string? unitId, string expected)
    {
        //SETUP
        decimal number = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        //SUT
        string text = ReadableValues.Measure(number, unitId);

        Assert.That(text, Is.EqualTo(expected));
    }

    [TestCase("202610050830+0100", "05/10/2026 08:30 (UTC+01:00)")]
    [TestCase("202601312359-0500", "31/01/2026 23:59 (UTC-05:00)")]
    [TestCase("2026-10-05", null)]
    [TestCase(null, null)]
    public void Moment_ReadsFormat205(string? value, string? expected)
    {
        //SETUP
        string? input = value;

        //SUT
        string? text = ReadableValues.Moment(input);

        Assert.That(text, Is.EqualTo(expected));
    }

    [Test]
    public void Humanize_ReplacesMeasuresAndMoments_AndLeavesTheRest()
    {
        //SETUP
        JsonElement dataset = JsonSerializer.SerializeToElement(new
        {
            grossWeight = new[] { new { value = 5300.000m, unitId = "KGM" } },
            grossVolume = new[] { new { value = 18.200m, unitId = "MTQ" } },
            carrierAcceptanceDateTime = new { value = "202610050830+0100", formatId = "205" },
            usedTransportMeans = new { id = "ZZ104AA", typeCode = "33" },
            item = new { itemQuantity = 20, uNDGID = "1263", flag = true, missing = (string?)null },
            other = new { value = "testo", formatId = "999" }
        });

        //SUT
        string json = ReadableValues.Humanize(dataset).GetRawText();

        Assert.That(json, Does.Contain("\"grossWeight\":[\"5.300 kg\"]").And.Contain("\"grossVolume\":[\"18,2 m³\"]"), "senza sequenze di escape");
        Assert.That(json, Does.Contain("\"carrierAcceptanceDateTime\":\"05/10/2026 08:30 (UTC+01:00)\""));
        Assert.That(json, Does.Contain("\"usedTransportMeans\":{\"id\":\"ZZ104AA\",\"typeCode\":\"33\"}"));
        Assert.That(json, Does.Contain("\"itemQuantity\":20").And.Contain("\"uNDGID\":\"1263\"").And.Contain("\"flag\":true").And.Contain("\"missing\":null"));
        Assert.That(json, Does.Contain("\"other\":{\"value\":\"testo\",\"formatId\":\"999\"}"), "un formato non previsto resta com'è");
        Assert.That(json, Does.Not.Contain("KGM").And.Not.Contain("5300"));
    }

    [TestCase("5.300 kg", true)]
    [TestCase("5.300", true, Description = "in una frase per una persona 5.300 è cinquemilatrecento")]
    [TestCase("5300", true)]
    [TestCase("18,2 m³", true)]
    [TestCase("18,2", true)]
    [TestCase("05/10/2026", true)]
    [TestCase("PITTURE", true)]
    [TestCase("5,3 kg", false)]
    [TestCase("5.400", false)]
    [TestCase("18.200", false, Description = "diciottomiladuecento non è il volume letto")]
    [TestCase("", false)]
    public void IsQuotedFrom_ReadsNumbersTheItalianWay(string quoted, bool expected)
    {
        //SETUP
        JsonElement raw = JsonSerializer.SerializeToElement(new
        {
            grossWeight = new[] { new { value = 5300.000m, unitId = "KGM" } },
            grossVolume = new[] { new { value = 18.200m, unitId = "MTQ" } },
            carrierAcceptanceDateTime = new { value = "202610050830+0100", formatId = "205" },
            properShippingName = new[] { "PITTURE" }
        });
        JsonElement[] read = [raw, ReadableValues.Humanize(raw)];

        //SUT
        bool grounded = SourceGrounding.IsQuotedFrom(quoted, read);

        Assert.That(grounded, Is.EqualTo(expected));
    }
}
