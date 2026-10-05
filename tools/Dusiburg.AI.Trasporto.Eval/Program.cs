using Dusiburg.AI.Trasporto.Efti.Core.Documents;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;

// CLI di valutazione e confronto dei modelli. I comandi di valutazione (eval extraction, eval validation, eval security,
// bench models) arrivano con la Fase 9.
//
// Uso: dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- docs
//   docs   rigenera i documenti di docs/ che nascono dal codice (regole di validazione, copertura del dataset)
if (args is ["docs"])
{
    string docs = Path.Combine(FindRepositoryRoot(), "docs");

    Write(Path.Combine(docs, GeneratedDocuments.ValidationRulesFile), GeneratedDocuments.RenderValidationRules());
    Write(Path.Combine(docs, GeneratedDocuments.CoverageFile), GeneratedDocuments.RenderCoverage(EftiSchemaCatalog.Default));

    return 0;
}

Console.Error.WriteLine("Comando non riconosciuto. Disponibile: docs. I comandi di valutazione arrivano con la Fase 9.");

return 1;

static void Write(string path, string content)
{
    File.WriteAllText(path, content);
    Console.WriteLine($"Scritto {path}");
}

// La cartella docs/ è quella del repository, non quella accanto all'eseguibile: si risale fino al file della solution.
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
