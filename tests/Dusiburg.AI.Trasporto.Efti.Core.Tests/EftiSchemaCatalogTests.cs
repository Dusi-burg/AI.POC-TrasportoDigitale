using Dusiburg.AI.Trasporto.Efti.Core.Schema;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>Lettura dello schema XSD eFTI copiato in <c>data/efti-schema</c>: versione, struttura, annotazioni, liste di codici.</summary>
public class EftiSchemaCatalogTests
{
    private static readonly EftiSchemaCatalog Catalog = EftiSchemaCatalog.Default;

    [Test]
    public void Load_CommonSchema_ReadsVersionRootAndTypes()
    {
        //SUT
        EftiSchemaCatalog catalog = EftiSchemaCatalog.Load(EftiSchemaCatalog.DefaultDirectory);

        Assert.That(catalog.Version, Is.EqualTo("0.8"));
        Assert.That((catalog.RootElementName, catalog.RootTypeName), Is.EqualTo(("consignment", "SupplyChainConsignment")));
        Assert.That(catalog.TargetNamespace, Is.EqualTo("http://efti.eu/v1/consignment/common"));
        Assert.That(catalog.ComplexTypeNames.Count(), Is.EqualTo(54));
    }

    [Test]
    public void ElementsOf_Root_KeepsTheOrderOfTheSchema()
    {
        //SUT
        string[] names = [.. Catalog.ElementsOf(Catalog.RootTypeName).Select(e => e.Name)];

        Assert.That(names, Has.Length.EqualTo(43));
        Assert.That(names.Take(3), Is.EqualTo(new[] { "applicableServiceCharge", "associatedDocument", "associatedParty" }));
        Assert.That(Array.IndexOf(names, "cODAmount"), Is.LessThan(Array.IndexOf(names, "cargoInsuranceInstructions")), "l'ordine è quello dello schema, non alfabetico");
    }

    [Test]
    public void Find_DataElement_ReturnsIdentifierFormatDefinitionAndSubsets()
    {
        //SUT
        SchemaElement? postcode = Catalog.Find("consignor/postalAddress/postcode");
        SchemaElement? acceptance = Catalog.Find("carrierAcceptanceDateTime");

        Assert.That(postcode, Is.Not.Null);
        Assert.That((postcode!.EftiId, postcode.Format, postcode.IsComplex, postcode.IsRepeatable), Is.EqualTo(("eFTI54", "an..17", false, true)));
        Assert.That(postcode.Definition, Is.Not.Empty);
        Assert.That((postcode.StatusIn("EU03"), postcode.StatusIn("EU05a")), Is.EqualTo(("M", "M")));
        Assert.That((acceptance!.EftiId, acceptance.TypeName, acceptance.MaxOccurs), Is.EqualTo(("eFTI39", "types:DateTime", (int?)1)));
        Assert.That(acceptance.IsInSubset("EU03"), Is.True);
        Assert.That(acceptance.IsInSubset("EU05a"), Is.False);
    }

    [Test]
    public void Find_Association_IsComplexAndCarriesItsSubsets()
    {
        //SUT
        SchemaElement? consignor = Catalog.Find("consignor");
        SchemaElement? dangerousGoods = Catalog.Find("includedConsignmentItem/transportDangerousGoods");

        Assert.That((consignor!.EftiId, consignor.IsComplex, consignor.ComplexTypeName), Is.EqualTo(("ASBIE1028", true, "TradeParty")));
        Assert.That(consignor.StatusIn("EU03"), Is.EqualTo("SI"));
        Assert.That((dangerousGoods!.IsInSubset("EU03"), dangerousGoods.IsInSubset("EU05a"), dangerousGoods.MaxOccurs), Is.EqualTo((true, true, (int?)null)));
    }

    [TestCase("consignor/nonEsiste")]
    [TestCase("nonEsiste")]
    [TestCase("carrierAcceptanceDateTime/qualcosa")]
    public void Find_UnknownPath_ReturnsNull(string path)
    {
        //SUT
        Assert.That(Catalog.Find(path), Is.Null);
    }

    [Test]
    public void CountryCodes_ComeFromTheSchemaCodeList()
    {
        //SUT
        IReadOnlySet<string> codes = Catalog.CountryCodes;

        Assert.That(codes, Has.Count.EqualTo(249));
        Assert.That(codes, Is.SupersetOf(new[] { "IT", "DE", "FR", "GB", "SI", "CH" }));
        Assert.That(codes, Has.None.AnyOf("UK", "XX", "ITA", "D"));
    }

    [Test]
    public void ElementsOf_UnknownType_Throws()
    {
        //SUT
        Assert.That(() => Catalog.ElementsOf("TipoInesistente"), Throws.TypeOf<KeyNotFoundException>().With.Message.Contains("TipoInesistente"));
    }
}
