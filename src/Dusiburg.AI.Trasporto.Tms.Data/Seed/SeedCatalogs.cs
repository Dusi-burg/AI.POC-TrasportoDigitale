using System.Globalization;
using Dusiburg.AI.Trasporto.Shared.Contracts;

namespace Dusiburg.AI.Trasporto.Tms.Data.Seed;

internal sealed record SeedCity(string City, string PostalCode);

/// <param name="NamePattern">Composizione della ragione sociale: <c>{0}</c> parola di attività, <c>{1}</c> cognome.</param>
/// <param name="VatPrefix">Prefisso della partita IVA; il blocco <c>999</c> che segue la marca come fittizia.</param>
/// <param name="VatDigits">Cifre casuali dopo il blocco <c>999</c>, per arrivare alla lunghezza prevista dal paese.</param>
internal sealed record SeedCountry(
    string Code, string NamePattern, string VatPrefix, int VatDigits, string VatSuffix, string[] Words, string[] Surnames, string[] Streets, SeedCity[] Cities);

/// <param name="KgPerPackage">Peso lordo minimo e massimo di un collo.</param>
internal sealed record SeedGoods(
    string Description, PackageType PackageType, int MinPackages, int MaxPackages, (int Min, int Max) KgPerPackage, decimal M3PerPackage);

/// <summary>Voce ADR: numero ONU, denominazione ufficiale di trasporto, classe, gruppo di imballaggio.</summary>
internal sealed record SeedDangerousGoods(
    string Description, PackageType PackageType, string UnNumber, string ProperShippingName, string AdrClass, string? PackingGroup);

/// <summary>Liste da cui il generatore compone anagrafiche, luoghi e merci: tutte inventate o di dominio pubblico.</summary>
internal static class SeedCatalogs
{
    public const string Italy = "IT";

    public static readonly string[] ItalianActivities =
    [
        "Meccanica", "Plastica", "Arredamenti", "Alimentari", "Ceramiche", "Imballaggi",
        "Tessile", "Chimica", "Elettrotecnica", "Legnami", "Vetraria", "Carpenteria"
    ];

    public static readonly string[] ItalianLegalForms = ["S.r.l.", "S.p.A.", "S.n.c."];

    public static readonly string[] ItalianStreets =
    [
        "Via dell'Industria", "Via Roma", "Viale Europa", "Via degli Artigiani", "Via Marconi",
        "Via del Commercio", "Via Enrico Fermi", "Strada Statale 11"
    ];

    public static readonly SeedCity[] ItalianCities =
    [
        new("Milano", "20141"), new("Bergamo", "24126"), new("Brescia", "25125"), new("Verona", "37135"),
        new("Padova", "35129"), new("Bologna", "40128"), new("Modena", "41123"), new("Parma", "43122"),
        new("Torino", "10156"), new("Genova", "16152"), new("Firenze", "50127"), new("Prato", "59100"),
        new("Ancona", "60131"), new("Pescara", "65128"), new("Bari", "70132"), new("Napoli", "80146"),
        new("Udine", "33100"), new("Trento", "38121"), new("Piacenza", "29122"), new("Novara", "28100")
    ];

    public static readonly SeedCountry[] ForeignCountries =
    [
        new("DE", "{1} {0} GmbH", "DE", 6, "", ["Maschinenbau", "Logistik", "Technik", "Handel"], ["Bauer", "Lindner", "Hoffmann", "Krüger"],
            ["Industriestraße", "Hafenstraße", "Am Gewerbepark"],
            [new("München", "80939"), new("Stuttgart", "70565"), new("Nürnberg", "90431"), new("Köln", "50769")]),
        new("FR", "{0} {1} SAS", "FR99", 6, "", ["Établissements", "Distribution", "Société Industrielle"], ["Moreau", "Girard", "Lefèvre", "Roussel"],
            ["Rue de l'Industrie", "Avenue de la Gare", "Zone Artisanale du Parc"],
            [new("Lyon", "69007"), new("Lille", "59000"), new("Toulouse", "31200"), new("Strasbourg", "67100")]),
        new("AT", "{1} {0} GmbH", "ATU", 5, "", ["Handel", "Technik", "Bau"], ["Gruber", "Steiner", "Pichler"],
            ["Industriestraße", "Gewerbestraße"],
            [new("Wien", "1100"), new("Graz", "8020"), new("Linz", "4020")]),
        new("ES", "{0} {1} S.L.", "ESB", 5, "", ["Suministros", "Industrias", "Comercial"], ["Navarro", "Ortega", "Vidal"],
            ["Calle de la Industria", "Avenida del Puerto"],
            [new("Barcelona", "08040"), new("Zaragoza", "50197"), new("València", "46014")]),
        new("SI", "{0} {1} d.o.o.", "SI", 5, "", ["Trgovina", "Industrija"], ["Novak", "Kovač", "Zupan"],
            ["Industrijska cesta", "Tržaška cesta"],
            [new("Ljubljana", "1000"), new("Maribor", "2000")]),
        new("CH", "{1} {0} AG", "CHE", 6, "", ["Technik", "Handel"], ["Keller", "Meier", "Bernasconi"],
            ["Industriestrasse", "Via Cantonale"],
            [new("Zürich", "8048"), new("Lugano", "6900")]),
        new("NL", "{1} {0} B.V.", "NL", 6, "B01", ["Logistiek", "Techniek"], ["de Vries", "Jansen", "Bakker"],
            ["Havenweg", "Industrieweg"],
            [new("Rotterdam", "3089"), new("Venlo", "5928")]),
        new("PL", "{0} {1} Sp. z o.o.", "PL", 7, "", ["Handel", "Przemysł"], ["Nowak", "Kowalski", "Wiśniewski"],
            ["ulica Przemysłowa", "ulica Fabryczna"],
            [new("Poznań", "61-001"), new("Wrocław", "50-001")]),
        new("BE", "{1} {0} NV", "BE0", 6, "", ["Logistiek", "Handel"], ["Peeters", "Janssens", "Dubois"],
            ["Havenlaan", "Industrieweg"],
            [new("Antwerpen", "2030"), new("Liège", "4000")])
    ];

    public static readonly SeedGoods[] Goods =
    [
        new("Ricambi meccanici", PackageType.Pallet, 1, 8, (180, 420), 1.2m),
        new("Piastrelle in ceramica", PackageType.Pallet, 4, 20, (800, 1_100), 0.9m),
        new("Mobili smontati", PackageType.Carton, 10, 60, (18, 45), 0.25m),
        new("Pasta alimentare secca", PackageType.Pallet, 6, 24, (450, 700), 1.4m),
        new("Vino in bottiglie", PackageType.Pallet, 4, 18, (600, 850), 1.1m),
        new("Tessuti in pezza", PackageType.Roll, 10, 40, (35, 70), 0.2m),
        new("Granulato plastico", PackageType.Bag, 20, 80, (25, 25), 0.04m),
        new("Profilati in alluminio", PackageType.Bundle, 4, 16, (220, 480), 0.8m),
        new("Elettrodomestici", PackageType.Carton, 12, 48, (30, 75), 0.45m),
        new("Carta in bobine", PackageType.Roll, 4, 14, (500, 900), 1.3m),
        new("Macchinari industriali", PackageType.Crate, 1, 4, (600, 2_400), 3.5m),
        new("Componenti elettronici", PackageType.Carton, 8, 40, (8, 22), 0.08m),
        new("Lamiere in acciaio", PackageType.Bundle, 2, 10, (900, 1_800), 0.6m),
        new("Prodotti cosmetici", PackageType.Carton, 20, 80, (6, 14), 0.06m)
    ];

    public static readonly SeedDangerousGoods[] DangerousGoods =
    [
        new("Vernici a solvente", PackageType.Drum, "1263", "PITTURE", "3", "II"),
        new("Diluenti industriali", PackageType.Drum, "1993", "LIQUIDO INFIAMMABILE, N.A.S.", "3", "III"),
        new("Bombolette aerosol", PackageType.Carton, "1950", "AEROSOL", "2", null),
        new("Additivi per lubrificanti", PackageType.Drum, "3082", "MATERIA PERICOLOSA PER L'AMBIENTE, LIQUIDA, N.A.S.", "9", "III"),
        new("Detergenti acidi industriali", PackageType.Drum, "1805", "ACIDO FOSFORICO IN SOLUZIONE", "8", "III"),
        new("Additivo sbiancante", PackageType.Drum, "2014", "PEROSSIDO DI IDROGENO IN SOLUZIONE ACQUOSA", "5.1", "II")
    ];

    public static readonly string[] SenderInstructions =
    [
        "Scarico con sponda idraulica.",
        "Avvisare il destinatario un giorno prima della consegna.",
        "Merce fragile: non sovrapporre.",
        "Consegna solo al mattino, entro le 12.",
        "Ritiro presso il magazzino 3, baia B."
    ];

    /// <summary>Valori sbagliati tipici in un campo paese compilato a mano.</summary>
    public static readonly string[] InvalidCountryCodes = ["XX", "ITA", "UK", "D"];

    /// <summary>Lettere usate nelle targhe italiane (mancano I, O, Q, U).</summary>
    public const string PlateLetters = "ABCDEFGHJKLMNPRSTVWXYZ";
}

/// <summary>Partite IVA italiane fittizie ma formalmente corrette.</summary>
public static class ItalianVat
{
    /// <summary>Codice dell'ufficio provinciale: <c>000</c> non esiste, quindi il numero non può appartenere a un'azienda reale.</summary>
    public const string FictitiousOfficeCode = "000";

    /// <summary><c>IT</c> + 7 cifre di matricola + ufficio <c>000</c> + cifra di controllo.</summary>
    public static string Create(int serial)
    {
        string body = serial.ToString("D7", CultureInfo.InvariantCulture) + FictitiousOfficeCode;

        return string.Create(CultureInfo.InvariantCulture, $"IT{body}{CheckDigit(body)}");
    }

    /// <summary>Formato <c>IT</c> + 11 cifre con cifra di controllo corretta (algoritmo di Luhn sulle prime dieci).</summary>
    public static bool IsValid(string? vatNumber) =>
        vatNumber is { Length: 13 }
        && vatNumber.StartsWith("IT", StringComparison.Ordinal)
        && vatNumber.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0
        && CheckDigit(vatNumber.Substring(2, 10)) == vatNumber[12] - '0';

    private static int CheckDigit(string firstTenDigits)
    {
        int sum = 0;

        for (int i = 0; i < firstTenDigits.Length; i++)
        {
            int digit = firstTenDigits[i] - '0';

            // Le cifre in posizione pari (seconda, quarta, …) si raddoppiano e, sopra il 9, si riducono di 9.
            if (i % 2 == 1)
            {
                digit *= 2;

                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
        }

        return (10 - (sum % 10)) % 10;
    }
}
