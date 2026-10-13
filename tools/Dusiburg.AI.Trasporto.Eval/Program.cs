using Dusiburg.AI.Trasporto.Efti.Core.Documents;
using Dusiburg.AI.Trasporto.Efti.Core.Ecmr;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Eval;
using Dusiburg.AI.Trasporto.Eval.Bench;
using Dusiburg.AI.Trasporto.Eval.Cmr;
using Dusiburg.AI.Trasporto.Eval.Extraction;
using Dusiburg.AI.Trasporto.Eval.Security;
using Dusiburg.AI.Trasporto.Eval.Validation;

// CLI di valutazione e confronto dei modelli (PLAN §5, Fase 9).
//
// Uso: dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- <comando> [opzioni]
//
//   docs              rigenera i documenti di docs/ che nascono dal codice (regole di validazione, copertura, ruoli, regole dell'eCMR)
//   cmr               rigenera i CMR sintetici della demo in data/cmr (PDF e verità di riferimento)
//   golden            rigenera il golden set dell'estrazione: data/golden_cmr.json e i PDF di data/golden_cmr
//   eval validation   golden set della validazione e difetti del seed (nessun modello)
//   eval extraction   golden set dei CMR letto da un modello: accuratezza, calibrazione, revisione, sensibilità
//   eval security     documenti con iniezione, ruoli × tool con audit, agente autorità sollecitato (vuole lo stack avviato;
//                     --skip-live per i soli documenti, --repeat <n> per ripetere le richieste all'agente)
//   bench models      confronto dei modelli su estrazione da testo, estrazione da immagine e mappatura (la mappatura vuole
//                     lo stack avviato). --models a,b,c  --parts extraction,images,mapping  --repeat <n>  --reuse
//
// Opzioni dei comandi di valutazione:
//   --provider ollama|anthropic   --model <id>     il modello da misurare (default: quello della demo)
//   --limit <n>   --category <nome>                solo i primi n documenti, o solo una categoria
//   --threshold <0..1>   --passes <1..3>           soglia di revisione e letture per documento (senza: 2 in locale, 1 sul cloud)
//   --replay <file>                                ricalcola il report dalle letture salvate, senza chiamare il modello
//   --out <cartella>                               dove scrivere i report (default: eval/)
//   --root <cartella>                              radice del repository, se l'eseguibile non sta sotto di essa
//
// Le chiavi (modello cloud, ruoli) sono quelle degli user-secrets dell'AppHost. I modelli locali vogliono Ollama avviato.
var cli = Cli.Parse(args);
string root = cli.Value("root") is { } repository ? Path.GetFullPath(repository) : FindRepositoryRoot();
string output = cli.Value("out") is { } directory ? Path.GetFullPath(directory) : Path.Combine(root, "eval");

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    switch (cli.Command)
    {
        case "docs":
            string docs = Path.Combine(root, "docs");

            Write(Path.Combine(docs, GeneratedDocuments.ValidationRulesFile), GeneratedDocuments.RenderValidationRules());
            Write(Path.Combine(docs, GeneratedDocuments.CoverageFile), GeneratedDocuments.RenderCoverage(EftiSchemaCatalog.Default));
            Write(Path.Combine(docs, GeneratedDocuments.RolesFile), GeneratedDocuments.RenderRoles());
            Write(Path.Combine(docs, GeneratedDocuments.EcmrRulesFile), GeneratedDocuments.RenderEcmrRules(EcmrSchema.Default));

            return 0;

        case "cmr":
            Print(CmrGenerator.WriteAll(Path.Combine(root, "data", "cmr")));

            return 0;

        case "golden":
            Print(CmrGenerator.WriteGolden(Path.Combine(root, "data")));

            return 0;

        case "eval validation":
            return ValidationCommand.Run(EvalSettings.Load(root), output, Console.Out);

        case "eval extraction":
            return await ExtractionCommand.RunAsync(cli, EvalSettings.Load(root), output, Console.Out, cancellation.Token);

        case "eval security":
            return await SecurityCommand.RunAsync(cli, EvalSettings.Load(root), output, Console.Out, cancellation.Token);

        case "bench models":
            return await BenchCommand.RunAsync(cli, EvalSettings.Load(root), output, Console.Out, cancellation.Token);

        default:
            Console.Error.WriteLine("Comando non riconosciuto. Disponibili: docs, cmr, golden, eval validation, eval extraction, eval security, bench models.");

            return 1;
    }
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FileNotFoundException or HttpRequestException)
{
    Console.Error.WriteLine(exception.Message);

    return 1;
}

static void Write(string path, string content)
{
    File.WriteAllText(path, content);
    Console.WriteLine($"Scritto {path}");
}

static void Print(IEnumerable<string> files)
{
    foreach (string file in files)
    {
        Console.WriteLine($"Scritto {file}");
    }
}

// Le cartelle docs/, data/ ed eval/ sono quelle del repository, non quelle accanto all'eseguibile: si risale fino al file della solution.
static string FindRepositoryRoot()
{
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        if (directory.EnumerateFiles("*.slnx").Any())
        {
            return directory.FullName;
        }
    }

    throw new InvalidOperationException("Radice del repository non trovata: nessun file .slnx sopra la cartella dell'eseguibile.");
}
