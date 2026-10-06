using System.Text;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Security;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Efti.Core.Documents;

/// <summary>
/// Documenti di <c>docs/</c> generati dal codice (P9): regole di validazione e copertura del dataset. Si rigenerano con
/// <c>dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- docs</c>; un test confronta i file con ciò che esce da qui.
/// </summary>
public static class GeneratedDocuments
{
    public const string ValidationRulesFile = "regole-di-validazione.md";

    public const string CoverageFile = "copertura-dataset.md";

    public const string RolesFile = "ruoli.md";

    /// <summary>Sottoinsiemi che il POC mostra all'autorità (D6).</summary>
    public static readonly IReadOnlyList<string> AuthoritySubsets = EftiSubsets.Authority;

    private const string GeneratedNotice =
        "> Documento **generato dal codice**: non modificarlo a mano. Si rigenera con " +
        "`dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- docs`.";

    public static string RenderValidationRules()
    {
        var text = new StringBuilder();

        text.AppendLine("# Regole di validazione del dataset eFTI");
        text.AppendLine();
        text.AppendLine(GeneratedNotice);
        text.AppendLine();
        text.AppendLine("Le regole sono codice deterministico: nessun modello decide se un dataset è valido. Un dataset è **Valid** quando " +
            "nessuna regola di gravità Errore è violata e l'XML esportato è conforme allo schema; altrimenti è **Incomplete**. " +
            "Gli avvisi non cambiano lo stato.");
        text.AppendLine();
        text.AppendLine($"Regole: {ValidationRules.All.Count}, di cui {ValidationRules.All.Count(r => r.Severity == IssueSeverity.Error)} bloccanti.");

        foreach (IGrouping<RuleCategory, ValidationRule> category in ValidationRules.All.GroupBy(r => r.Category))
        {
            text.AppendLine();
            text.AppendLine($"## {CategoryTitle(category.Key)}");
            text.AppendLine();
            text.AppendLine("| Codice | Gravità | Elemento eFTI | Regola |");
            text.AppendLine("|---|---|---|---|");

            foreach (ValidationRule rule in category)
            {
                text.AppendLine($"| `{rule.Code}` | {SeverityLabel(rule.Severity)} | {CodeList(rule.Field)} | {rule.Explanation} |");
            }
        }

        return text.ToString().ReplaceLineEndings("\n");
    }

    public static string RenderRoles()
    {
        Role[] roles = Enum.GetValues<Role>();
        var text = new StringBuilder();

        text.AppendLine("# Ruoli e operazioni");
        text.AppendLine();
        text.AppendLine(GeneratedNotice);
        text.AppendLine();
        text.AppendLine("L'identità è **simulata**: ogni richiesta porta una chiave nell'intestazione `X-Api-Key`, e la chiave dice quale dei " +
            "quattro utenti dimostrativi sta chiamando. Non è autenticazione reale. Ciò che è reale è il controllo: ogni operazione passa " +
            "da un unico punto che consulta questa matrice e registra l'esito nell'audit, rifiuti compresi. Ciò che non è elencato è negato.");
        text.AppendLine();
        text.AppendLine("## Utenti dimostrativi");
        text.AppendLine();
        text.AppendLine("| Ruolo | Utente | Che cosa fa |");
        text.AppendLine("|---|---|---|");

        foreach (Role role in roles)
        {
            text.AppendLine($"| `{role}` | {DemoActors.For(role).Name} | {RoleDescription(role)} |");
        }

        text.AppendLine();
        text.AppendLine("## Matrice");
        text.AppendLine();
        text.AppendLine("Ogni cella dice **su che cosa** vale il permesso; una cella vuota è un rifiuto.");
        text.AppendLine();
        text.AppendLine($"| Operazione | {string.Join(" | ", roles.Select(r => $"`{r}`"))} |");
        text.AppendLine($"|---|{string.Concat(roles.Select(_ => "---|"))}");

        foreach (OperationInfo operation in AuthorizationPolicy.Operations)
        {
            string cells = string.Join(" | ", roles.Select(role => ScopeLabel(AuthorizationPolicy.ScopeFor(role, operation.Operation))));

            text.AppendLine($"| `{operation.Operation}` | {cells} |");
        }

        text.AppendLine();
        text.AppendLine("## Operazioni");
        text.AppendLine();
        text.AppendLine("| Operazione | Tipo | Endpoint | Che cosa permette |");
        text.AppendLine("|---|---|---|---|");

        foreach (OperationInfo operation in AuthorizationPolicy.Operations)
        {
            text.AppendLine($"| `{operation.Operation}` | {(operation.IsWrite ? "scrittura" : "lettura")} | `{operation.Endpoint}` | {operation.Description} |");
        }

        text.AppendLine();
        text.AppendLine("## Ambiti");
        text.AppendLine();

        foreach (AccessScope scope in Enum.GetValues<AccessScope>().Where(s => s != AccessScope.Denied))
        {
            text.AppendLine($"- **{ScopeLabel(scope)}**: {ScopeDescription(scope)}");
        }

        return text.ToString().ReplaceLineEndings("\n");
    }

    public static string RenderCoverage(EftiSchemaCatalog catalog)
    {
        var text = new StringBuilder();

        text.AppendLine("# Copertura del dataset eFTI");
        text.AppendLine();
        text.AppendLine(GeneratedNotice);
        text.AppendLine();
        text.AppendLine($"Schema: `{EftiSchemaCatalog.CommonSchemaFile}` versione **{catalog.Version}** (`data/efti-schema/`). " +
            "Identificativi, formati e sottoinsiemi sono letti dallo schema; l'origine del dato e la casella del CMR sono dichiarate nel codice (`DatasetCoverage`).");
        text.AppendLine();
        text.AppendLine("Il POC copre il trasporto su strada e solo una parte del dataset comune. Lo stato nei sottoinsiemi è riportato come scritto " +
            "nello schema (`M`, `C`, `O`, `SI` e combinazioni): il significato ufficiale di `SI` non è stato verificato sul Reg. delegato (UE) 2024/2024. " +
            "Le caselle del CMR seguono il modello IRU e sono da riconfermare.");
        text.AppendLine();
        text.AppendLine($"## Elementi coperti ({DatasetCoverage.Elements.Count})");
        text.AppendLine();
        text.AppendLine($"| Elemento | Id eFTI | Formato | {string.Join(" | ", AuthoritySubsets)} | Origine | CMR |");
        text.AppendLine($"|---|---|---|{string.Concat(AuthoritySubsets.Select(_ => "---|"))}---|---|");

        foreach (CoveredElement covered in DatasetCoverage.Elements)
        {
            SchemaElement element = catalog.Find(covered.Path)
                ?? throw new InvalidOperationException($"'{covered.Path}' non esiste nello schema eFTI {catalog.Version}.");
            string subsets = string.Join(" | ", AuthoritySubsets.Select(subset => element.StatusIn(subset) ?? "—"));

            text.AppendLine($"| `{covered.Path}` | {element.EftiId ?? "—"} | {element.Format ?? "—"} | {subsets} | {covered.Source} | {covered.CmrBox ?? "—"} |");
        }

        IReadOnlyList<(string Path, SchemaElement Element)> uncovered = UncoveredSubsetElements(catalog);

        text.AppendLine();
        text.AppendLine($"## Elementi dei sottoinsiemi non coperti ({uncovered.Count})");
        text.AppendLine();
        text.AppendLine($"Elementi che lo schema assegna a {string.Join(" o ", AuthoritySubsets)} e che il POC non valorizza, limitati ai contenitori " +
            "che il POC usa. Sono il perimetro dichiarato: un'autorità non li troverebbe nel dataset.");
        text.AppendLine();
        text.AppendLine($"| Elemento | Id eFTI | Formato | {string.Join(" | ", AuthoritySubsets)} |");
        text.AppendLine($"|---|---|---|{string.Concat(AuthoritySubsets.Select(_ => "---|"))}");

        foreach ((string path, SchemaElement element) in uncovered)
        {
            string subsets = string.Join(" | ", AuthoritySubsets.Select(subset => element.StatusIn(subset) ?? "—"));

            text.AppendLine($"| `{path}` | {element.EftiId ?? "—"} | {element.Format ?? "—"} | {subsets} |");
        }

        return text.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Figli diretti dei contenitori usati dal POC (la radice e ogni prefisso di un percorso coperto) che appartengono a un
    /// sottoinsieme dell'autorità e non sono né coperti né contenitori di un elemento coperto.
    /// </summary>
    public static IReadOnlyList<(string Path, SchemaElement Element)> UncoveredSubsetElements(EftiSchemaCatalog catalog)
    {
        string[] covered = [.. DatasetCoverage.Elements.Select(e => e.Path)];
        SortedSet<string> containers = new(covered.SelectMany(Prefixes), StringComparer.Ordinal) { string.Empty };
        List<(string, SchemaElement)> uncovered = [];

        foreach (string container in containers)
        {
            string typeName = container.Length == 0 ? catalog.RootTypeName : catalog.Find(container)!.ComplexTypeName;

            foreach (SchemaElement child in catalog.ElementsOf(typeName).Where(e => AuthoritySubsets.Any(e.IsInSubset)))
            {
                string path = container.Length == 0 ? child.Name : $"{container}/{child.Name}";

                if (!covered.Any(c => c == path || c.StartsWith(path + "/", StringComparison.Ordinal)))
                {
                    uncovered.Add((path, child));
                }
            }
        }

        return uncovered;
    }

    private static IEnumerable<string> Prefixes(string path)
    {
        for (int index = path.IndexOf('/', StringComparison.Ordinal); index >= 0; index = path.IndexOf('/', index + 1))
        {
            yield return path[..index];
        }
    }

    private static string CategoryTitle(RuleCategory category) => category switch
    {
        RuleCategory.Mandatory => "Obbligatorietà",
        RuleCategory.Format => "Formato",
        RuleCategory.Coherence => "Coerenza",
        RuleCategory.Advisory => "Avvisi",
        RuleCategory.Schema => "Conformità allo schema",
        _ => category.ToString()
    };

    private static string RoleDescription(Role role) => role switch
    {
        Role.Dispatcher => "Ufficio traffico del vettore: importa le spedizioni, le completa, vede chi le ha consultate.",
        Role.ClaimsManager => "Ufficio reclami del vettore: legge le spedizioni; le operazioni sui reclami arrivano con la Release 2.",
        Role.Authority => "Autorità di controllo: parte da una targa, ottiene un UIL e con quello legge un sottoinsieme.",
        Role.Admin => "Amministratore della piattaforma: legge spedizioni e audit, non modifica i dati di trasporto.",
        _ => role.ToString()
    };

    private static string ScopeLabel(AccessScope scope) => scope switch
    {
        AccessScope.Denied => "—",
        AccessScope.Self => "sé",
        AccessScope.AllShipments => "tutte le spedizioni",
        AccessScope.IdentifierRegistry => "registro degli identificatori",
        AccessScope.AuthoritySubset => "sottoinsieme per UIL",
        AccessScope.AuthorityAccesses => "accessi dell'autorità",
        AccessScope.AuditTrail => "tutto l'audit",
        AccessScope.Settings => "configurazione",
        _ => scope.ToString()
    };

    private static string ScopeDescription(AccessScope scope) => scope switch
    {
        AccessScope.Self => "solo la propria identità e l'elenco di ciò che il proprio ruolo può fare.",
        AccessScope.AllShipments => "tutte le spedizioni del vettore, con dataset completo e dati contrattuali.",
        AccessScope.IdentifierRegistry => "solo il registro degli identificatori, cercato per targa: UIL, targhe, date, modo di trasporto, indicatore di merci pericolose. Nessun soggetto, nessuna merce.",
        AccessScope.AuthoritySubset => $"solo i sottoinsiemi {string.Join(" ed ", AuthoritySubsets.Select(s => $"`{s}`"))} di una spedizione di cui si conosce l'UIL. Nessun elenco, nessun dato contrattuale.",
        AccessScope.AuthorityAccesses => "solo gli eventi di audit in cui un'autorità ha consultato una spedizione.",
        AccessScope.AuditTrail => "tutto il registro di audit, in sola lettura: il database rifiuta modifiche e cancellazioni.",
        AccessScope.Settings => "la configurazione in vigore, in sola lettura e senza segreti: modelli per compito, pesi e soglia dell'estrazione, permanenza sul gate.",
        _ => string.Empty
    };

    private static string SeverityLabel(IssueSeverity severity) => severity == IssueSeverity.Error ? "Errore" : "Avviso";

    /// <summary>Più elementi separati da virgola, ciascuno in formato codice.</summary>
    private static string CodeList(string field) =>
        field.StartsWith('(') ? field : string.Join(", ", field.Split(", ").Select(f => $"`{f}`"));
}
