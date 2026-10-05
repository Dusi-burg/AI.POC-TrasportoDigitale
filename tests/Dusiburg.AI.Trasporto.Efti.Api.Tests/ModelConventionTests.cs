using Dusiburg.AI.Trasporto.DbInit;
using Dusiburg.AI.Trasporto.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests;

/// <summary>
/// Convenzioni del modello dati su tutti i <c>DbContext</c> del POC, lette dai metadati di EF: non serve un database.
/// Una tabella nuova che le viola fa fallire questi test.
/// </summary>
public class ModelConventionTests
{
    private const string UnusedConnectionString = @"Server=(localdb)\non-usato;Database=Trasporto_Model";

    private static IEnumerable<TestCaseData> Contexts()
    {
        yield return new TestCaseData(new Func<DbContext>(() => TrasportoDatabaseInitializer.CreateTms(UnusedConnectionString))).SetArgDisplayNames("tms");
        yield return new TestCaseData(new Func<DbContext>(() => TrasportoDatabaseInitializer.CreateEfti(UnusedConnectionString))).SetArgDisplayNames("efti");
        yield return new TestCaseData(new Func<DbContext>(() => TrasportoDatabaseInitializer.CreateAudit(UnusedConnectionString))).SetArgDisplayNames("audit");
    }

    [TestCaseSource(nameof(Contexts))]
    public void Tables_AreNamedAfterTheSingleRow(Func<DbContext> createContext)
    {
        //SETUP
        using DbContext context = createContext();

        //SUT
        foreach (IEntityType entity in Tables(context))
        {
            // Il nome è quello della classe (o dell'enum, per le lookup): mai il plurale del DbSet.
            Assert.That(entity.GetTableName(), Is.EqualTo(ExpectedTableName(entity)), entity.DisplayName());
        }
    }

    [TestCaseSource(nameof(Contexts))]
    public void PrimaryKeys_AreNumericAndCalledId(Func<DbContext> createContext)
    {
        //SETUP
        using DbContext context = createContext();

        //SUT
        foreach (IEntityType entity in Tables(context))
        {
            IReadOnlyList<IProperty> key = entity.FindPrimaryKey()!.Properties;

            Assert.That(key.Select(p => p.GetColumnName()), Is.EqualTo(new[] { "Id" }), entity.DisplayName());
            Assert.That(key[0].ClrType, Is.Not.EqualTo(typeof(Guid)), $"{entity.DisplayName()}: PK GUID");
        }
    }

    [TestCaseSource(nameof(Contexts))]
    public void EnumColumns_AreForeignKeysToTheirLookup(Func<DbContext> createContext)
    {
        //SETUP
        using DbContext context = createContext();

        //SUT
        foreach (IEntityType entity in Tables(context).Where(e => !IsLookup(e)))
        {
            foreach (IProperty property in entity.GetProperties().Where(p => EnumTypeOf(p) is not null))
            {
                Type enumType = EnumTypeOf(property)!;
                string column = $"{entity.DisplayName()}.{property.Name}";

                Assert.That(property.GetColumnName(), Is.EqualTo(enumType.Name + "Id"), column);
                Assert.That(
                    property.GetContainingForeignKeys().Select(fk => fk.PrincipalEntityType.ClrType),
                    Is.EqualTo(new[] { typeof(EnumLookup<>).MakeGenericType(enumType) }),
                    $"{column}: manca la FK verso la lookup");
            }
        }
    }

    [TestCaseSource(nameof(Contexts))]
    public void Lookups_HoldOneRowPerEnumMemberWithExplicitValues(Func<DbContext> createContext)
    {
        //SETUP
        using DbContext context = createContext();

        //SUT
        // I dati di HasData stanno solo nel modello di design-time, non in quello ottimizzato per l'esecuzione.
        IModel designTimeModel = context.GetService<IDesignTimeModel>().Model;

        foreach (IEntityType lookup in designTimeModel.GetEntityTypes().Where(IsLookup))
        {
            Type enumType = lookup.ClrType.GetGenericArguments()[0];
            int[] members = [.. Enum.GetValues(enumType).Cast<object>().Select(Convert.ToInt32)];

            // Nei dati del modello l'Id può comparire come enum o già convertito in byte: si confronta il valore numerico.
            int[] seededIds = [.. lookup.GetSeedData().Select(row => Convert.ToInt32(row["Id"]))];

            Assert.That(seededIds, Is.EquivalentTo(members), enumType.Name);
            Assert.That(members, Has.None.EqualTo(0), $"{enumType.Name}: valori espliciti da 1");
        }
    }

    /// <summary>Le entità con una tabella propria: i tipi posseduti sono colonne della tabella che li contiene.</summary>
    private static IEnumerable<IEntityType> Tables(DbContext context) => context.Model.GetEntityTypes().Where(e => !e.IsOwned());

    private static bool IsLookup(IEntityType entity) =>
        entity.ClrType.IsGenericType && entity.ClrType.GetGenericTypeDefinition() == typeof(EnumLookup<>);

    private static string ExpectedTableName(IEntityType entity) =>
        IsLookup(entity) ? entity.ClrType.GetGenericArguments()[0].Name : entity.ClrType.Name;

    private static Type? EnumTypeOf(IProperty property)
    {
        Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        return type.IsEnum ? type : null;
    }
}
