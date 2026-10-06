using System.Text.Json;
using Dusiburg.AI.Trasporto.Agents.Grounding;
using Dusiburg.AI.Trasporto.Agents.Tests.Support;

namespace Dusiburg.AI.Trasporto.Agents.Tests;

/// <summary>
/// Riconducibilità di un valore a una fonte: il controllo che sta fra ciò che il modello dice di aver letto e ciò che ha letto.
/// </summary>
public class SourceGroundingTests
{
    /// <summary>L'ordine del TMS dello scenario 2, come lo restituisce il tool.</summary>
    private static readonly JsonElement Order = JsonSerializer.SerializeToElement(new
    {
        order = new { code = "TO-1002", totalPackages = 22, totalGrossWeightKg = 8480.0m, deliveryDate = "2026-10-08" },
        consignee = new { name = "Papeteries du Rhône SAS", city = "Lyon", countryCode = "FR" },
        pickedUpAt = "2026-10-06T08:30:00+02:00",
        senderInstructions = FakeEftiTools.SenderInstructions + " Ritiro il 07/10/2026, altezza massima 2,5 m. Volume 1.250,75 litri.",
        transshipmentAllowed = true
    });

    [TestCase("69007", TestName = "CAP nel testo libero")]
    [TestCase("2014", TestName = "numero ONU nel testo libero")]
    [TestCase("PEROSSIDO DI IDROGENO IN SOLUZIONE ACQUOSA", TestName = "denominazione alla lettera")]
    [TestCase("perossido  di idrogeno in soluzione acquosa", TestName = "maiuscole e spazi non contano")]
    [TestCase("PEROSSIDO DI IDROGENO", TestName = "parte di un testo più lungo")]
    [TestCase("FR99999000022", TestName = "partita IVA")]
    [TestCase("II", TestName = "gruppo di imballaggio come parola intera")]
    [TestCase("5.1", TestName = "classe ADR: punto seguito da una cifra è un decimale")]
    [TestCase("8840", TestName = "8.840 nel testo italiano è 8840")]
    [TestCase("8480", TestName = "numero JSON")]
    [TestCase("8480.0", TestName = "stesso numero scritto con il decimale")]
    [TestCase("22", TestName = "intero JSON")]
    [TestCase("2.5", TestName = "virgola decimale nel testo")]
    [TestCase("1250.75", TestName = "migliaia e decimali all'italiana")]
    [TestCase("2026-10-08", TestName = "data ISO in un campo")]
    [TestCase("2026-10-06", TestName = "data dentro un istante ISO")]
    [TestCase("2026-10-07", TestName = "data scritta gg/mm/aaaa nel testo")]
    [TestCase("Lyon", TestName = "valore di un campo")]
    [TestCase("FR", TestName = "codice paese")]
    [TestCase("true", TestName = "booleano")]
    public void IsGrounded_ValueWrittenInTheSource_IsAccepted(string value)
    {
        //SUT
        bool grounded = SourceGrounding.IsGrounded(value, [Order], out string? reason);

        Assert.That((grounded, reason), Is.EqualTo((true, (string?)null)));
    }

    [TestCase("8.840", "8840", TestName = "8.840 letto come 8,84")]
    [TestCase("8.84", "non compare", TestName = "peso mai scritto")]
    [TestCase("69008", "non compare", TestName = "CAP diverso")]
    [TestCase("9999", "non compare", TestName = "le cifre dentro la partita IVA non sono un numero")]
    [TestCase("PEROSSIDO DI AZOTO", "non compare", TestName = "denominazione diversa")]
    [TestCase("Marseille", "non compare", TestName = "città mai scritta")]
    [TestCase("I", "non compare", TestName = "una lettera dentro una parola non conta")]
    [TestCase("2026-10-09", "non compare", TestName = "data mai scritta")]
    [TestCase("", "vuoto", TestName = "valore vuoto")]
    public void IsGrounded_ValueNotWrittenInTheSource_IsRefusedWithAReason(string value, string expectedInReason)
    {
        //SUT
        bool grounded = SourceGrounding.IsGrounded(value, [Order], out string? reason);

        Assert.That(grounded, Is.False);
        Assert.That(reason, Does.Contain(expectedInReason));
    }

    [Test]
    public void IsGrounded_WithSeveralSources_LooksInAllOfThem()
    {
        //SETUP
        JsonElement identifiers = JsonSerializer.SerializeToElement(new { usedTransportMeans = new { id = "ZZ104AA" } });
        JsonElement subset = JsonSerializer.SerializeToElement(new { transportDangerousGoods = new[] { new { uNDGID = "1263", grossWeight = new { value = 4200m } } } });

        //SUT
        bool plate = SourceGrounding.IsGrounded("ZZ104AA", [identifiers, subset], out _);
        bool unNumber = SourceGrounding.IsGrounded("1263", [identifiers, subset], out _);
        bool weight = SourceGrounding.IsGrounded("4200", [identifiers, subset], out _);
        bool nothingRead = SourceGrounding.IsGrounded("1263", [], out string? reason);

        Assert.That((plate, unNumber, weight), Is.EqualTo((true, true, true)));
        Assert.That(nothingRead, Is.False);
        Assert.That(reason, Does.Contain("non compare"));
    }
}
