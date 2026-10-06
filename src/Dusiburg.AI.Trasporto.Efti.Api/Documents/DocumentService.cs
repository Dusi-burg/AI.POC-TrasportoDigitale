using System.Globalization;
using System.Security.Cryptography;
using Dusiburg.AI.Trasporto.Documents.Extraction;
using Dusiburg.AI.Trasporto.Efti.Api.Proposals;
using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Efti.Data;
using Dusiburg.AI.Trasporto.Efti.Data.Configurations;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dusiburg.AI.Trasporto.Efti.Api.Documents;

/// <summary>Il modello non ha restituito un'estrazione utilizzabile: il documento va riletto o trattato a mano.</summary>
public sealed class ExtractionFailedException(string message) : Exception(message);

/// <summary>
/// Dal PDF caricato ai campi con la loro sorte. Il modello legge, <see cref="ExtractionAssessor"/> giudica, e qui si esegue ciò
/// che il giudizio comporta: nulla, una proposta, una voce di revisione. Il dataset non si tocca: lo cambia la decisione su una
/// proposta o la conferma di una revisione, che sono azioni di una persona. L'unica eccezione è l'accettazione automatica, una
/// regola esplicita e spenta di default.
/// </summary>
public sealed class DocumentService(
    EftiDbContext db,
    CmrExtractor extractor,
    ExtractionAssessor assessor,
    FieldProposalService proposals,
    IOptions<ExtractionOptions> extractionOptions,
    TimeProvider timeProvider,
    ILogger<DocumentService> logger)
{
    public const int MaxFileBytes = 2 * 1024 * 1024;

    public const int MaxRows = 200;

    private static readonly byte[] PdfSignature = "%PDF"u8.ToArray();

    /// <summary>Carica un documento su una spedizione ed esegue l'estrazione nella stessa chiamata.</summary>
    public async Task<DocumentDetailView> UploadAsync(string shipmentCode, string fileName, byte[] content, Actor actor, CancellationToken cancellationToken)
    {
        Shipment shipment = await db.Shipments
            .AsNoTracking()
            .Include(s => s.Parties).ThenInclude(p => p.Party)
            .Include(s => s.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Code == shipmentCode, cancellationToken)
            ?? throw new ProposalException(ProposalFailure.NotFound, $"Spedizione {shipmentCode} non trovata.");

        PdfText pdf = ReadText(content);
        string text = pdf.Text;
        ExtractionOptions options = extractionOptions.Value;
        CmrExtractionRun run = await extractor.ExtractAsync(text, options.Passes, cancellationToken);

        if (run.Primary is not { } primary)
        {
            throw new ExtractionFailedException(run.Passes.Count > 0 && run.Passes[0].Refused
                ? "Il modello ha rifiutato di leggere il documento."
                : "Il modello non ha restituito un'estrazione valida del documento.");
        }

        CmrExtractionPass? second = run.Passes.Count > 1 ? run.Passes[1] : null;
        IReadOnlyList<FieldAssessment> assessments = assessor.Assess(shipment, text, pdf.Annotations, primary, second?.Extraction, options);
        DateTimeOffset now = timeProvider.GetUtcNow();

        var document = new Document
        {
            ShipmentId = shipment.Id,
            FileName = Shorten(Path.GetFileName(fileName), 200),
            SizeBytes = content.LongLength,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
            Content = content,
            Text = text,
            Annotations = pdf.Annotations.Count == 0 ? null : string.Join('\n', pdf.Annotations),
            UploadedById = actor.Id,
            UploadedByName = actor.Name,
            UploadedAt = now,
            Extraction = new ExtractionResult
            {
                ModelProvider = run.Provider,
                ModelId = run.Model,
                Passes = run.Passes.Count(p => p.Extraction is not null),
                Threshold = options.ReviewThreshold,
                LiteralWeight = options.Weights.Literal,
                FormatWeight = options.Weights.Format,
                TmsWeight = options.Weights.Tms,
                AgreementWeight = options.Weights.Agreement,
                AnnotationWeight = options.Weights.Annotation,
                ElapsedMs = run.ElapsedMs,
                InputTokens = run.InputTokens,
                OutputTokens = run.OutputTokens,
                RawJson = run.Passes[0].RawText,
                CreatedAt = now,
                Fields = [.. assessments.Select(a => ToEntity(a, now))]
            }
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync(cancellationToken);

        foreach (ExtractionField field in document.Extraction.Fields.Where(f => f.Outcome == ExtractionFieldOutcome.Proposed))
        {
            await ProposeAsync(shipmentCode, document, field, actor, options, now, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Documento {DocumentId} su {ShipmentCode}: {Fields} campi, {Proposed} proposte, {InReview} in revisione ({Provider}/{Model}, {ElapsedMs:0} ms)",
            document.Id, shipmentCode, document.Extraction.Fields.Count,
            document.Extraction.Fields.Count(f => f.Outcome == ExtractionFieldOutcome.Proposed),
            document.Extraction.Fields.Count(f => f.Outcome == ExtractionFieldOutcome.InReview), run.Provider, run.Model, run.ElapsedMs);

        return ToDetail(document, shipmentCode);
    }

    /// <summary>Documenti di una spedizione dal più recente (indice su <c>ShipmentId</c>); <c>null</c> se la spedizione non esiste.</summary>
    public async Task<IReadOnlyList<DocumentSummaryView>?> ListAsync(string shipmentCode, CancellationToken cancellationToken)
    {
        int? shipmentId = await db.Shipments.Where(s => s.Code == shipmentCode).Select(s => (int?)s.Id).SingleOrDefaultAsync(cancellationToken);

        if (shipmentId is null)
        {
            return null;
        }

        var rows = await db.Documents
            .AsNoTracking()
            .Where(d => d.ShipmentId == shipmentId)
            .OrderByDescending(d => d.Id)
            .Take(MaxRows)
            .Select(d => new
            {
                d.Id, d.FileName, d.SizeBytes, d.UploadedByName, d.UploadedAt,
                Outcomes = d.Extraction!.Fields.Select(f => f.Outcome).ToList()
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => Summary(r.Id, shipmentCode, r.FileName, r.SizeBytes, r.UploadedByName, r.UploadedAt, r.Outcomes))];
    }

    /// <summary>Un documento con il testo letto e i campi estratti (lettura per chiave primaria); <c>null</c> se non esiste.</summary>
    public async Task<DocumentDetailView?> FindAsync(int documentId, CancellationToken cancellationToken)
    {
        Document? document = await db.Documents
            .AsNoTracking()
            .Include(d => d.Shipment)
            .Include(d => d.Extraction!).ThenInclude(e => e.Fields).ThenInclude(f => f.ReviewTask)
            .AsSplitQuery()
            .SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        return document is null ? null : ToDetail(document, document.Shipment.Code);
    }

    /// <summary>La coda di revisione, dalla voce più vecchia (indice su <c>ReviewStatusId</c>).</summary>
    public async Task<IReadOnlyList<ReviewTaskView>> ListReviewTasksAsync(ReviewStatus? status, string? shipmentCode, CancellationToken cancellationToken)
    {
        IQueryable<ReviewTask> query = db.ReviewTasks.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(t => t.Status == wanted);
        }

        if (shipmentCode is not null)
        {
            query = query.Where(t => t.ExtractionField.ExtractionResult.Document.Shipment.Code == shipmentCode);
        }

        List<ReviewTask> tasks = await ReviewGraph(query).OrderBy(t => t.Id).Take(MaxRows).ToListAsync(cancellationToken);

        return [.. tasks.Select(ToView)];
    }

    /// <summary>
    /// Decide una voce della coda. Il rifiuto la chiude. La conferma è una decisione umana completa: il valore (quello estratto,
    /// o quello corretto dalla persona) diventa una proposta e la proposta viene accettata, quindi nasce la versione successiva
    /// del dataset.
    /// </summary>
    public async Task<ReviewDecisionResponse> DecideReviewAsync(int reviewTaskId, ReviewDecisionRequest request, Actor actor, CancellationToken cancellationToken)
    {
        if (request.Decision is not (ReviewStatus.Confirmed or ReviewStatus.Rejected))
        {
            throw new ProposalException(ProposalFailure.Invalid, $"Indicare la decisione (decision): {ReviewStatus.Confirmed} oppure {ReviewStatus.Rejected}.");
        }

        string? note = string.IsNullOrWhiteSpace(request.Note) ? null : Shorten(request.Note.Trim(), ColumnSizes.Rationale);

        ReviewTask task = await ReviewGraph(db.ReviewTasks).SingleOrDefaultAsync(t => t.Id == reviewTaskId, cancellationToken)
            ?? throw new ProposalException(ProposalFailure.NotFound, $"Voce di revisione {reviewTaskId} non trovata.");

        if (task.Status != ReviewStatus.Pending)
        {
            throw new ProposalException(ProposalFailure.Conflict, $"La voce di revisione {reviewTaskId} è già stata decisa: {task.Status}.");
        }

        ExtractionField field = task.ExtractionField;
        Document document = field.ExtractionResult.Document;
        ProposalDecisionResponse? dataset = null;

        if (request.Decision == ReviewStatus.Confirmed)
        {
            string? corrected = string.IsNullOrWhiteSpace(request.Value) ? null : request.Value.Trim();
            string value = corrected ?? field.NormalizedValue ?? field.Value;

            // Proposta e accettazione nella stessa azione: la persona ha visto il valore e lo ha confermato.
            ProposeFieldResponse proposed = await proposals.ProposeAsync(
                document.Shipment.Code,
                new ProposeFieldRequest(field.FieldPath, value, nameof(ProposalSource.Document), SourceReference(document, field),
                    corrected is null ? "Valore estratto dal documento e confermato in revisione." : "Valore corretto in revisione, a partire dal documento."),
                actor, cancellationToken);

            dataset = await proposals.DecideAsync(
                proposed.Proposal.Id, new ProposalDecisionRequest(ProposalStatus.Accepted, note ?? "Confermato in revisione."), actor, cancellationToken);

            field.FieldProposalId = proposed.Proposal.Id;
            task.CorrectedValue = corrected;
        }

        task.Status = request.Decision.Value;
        task.DecidedById = actor.Id;
        task.DecidedByName = actor.Name;
        task.DecidedAt = timeProvider.GetUtcNow();
        task.Note = note;

        await db.SaveChangesAsync(cancellationToken);

        return new ReviewDecisionResponse(ToView(task), dataset);
    }

    private static PdfText ReadText(byte[] content)
    {
        if (content.Length == 0 || content.Length > MaxFileBytes)
        {
            throw new ProposalException(ProposalFailure.Invalid, $"Il file deve essere un PDF non vuoto di al massimo {MaxFileBytes / (1024 * 1024)} MB.");
        }

        if (!content.AsSpan().StartsWith(PdfSignature))
        {
            throw new ProposalException(ProposalFailure.Invalid, "Il file non è un PDF.");
        }

        try
        {
            return PdfTextReader.Read(content);
        }
        catch (PdfNotReadableException exception)
        {
            throw new ProposalException(ProposalFailure.Invalid, exception.Message);
        }
    }

    /// <summary>
    /// Un campo sopra soglia diventa una proposta come quelle dell'agente. Se la proposta non si può registrare (per esempio il
    /// dataset è firmato) il campo passa alla revisione, con il motivo.
    /// </summary>
    private async Task ProposeAsync(
        string shipmentCode, Document document, ExtractionField field, Actor actor, ExtractionOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            ProposeFieldResponse proposed = await proposals.ProposeAsync(
                shipmentCode,
                new ProposeFieldRequest(field.FieldPath, field.NormalizedValue, nameof(ProposalSource.Document), SourceReference(document, field),
                    string.Create(CultureInfo.InvariantCulture, $"Estratto dal documento con confidenza {field.Confidence:0.00} (soglia {options.ReviewThreshold:0.00}).")),
                actor, cancellationToken);

            field.FieldProposalId = proposed.Proposal.Id;

            if (options.AutoAccept)
            {
                await proposals.DecideAsync(
                    proposed.Proposal.Id,
                    new ProposalDecisionRequest(ProposalStatus.Accepted, string.Create(CultureInfo.InvariantCulture,
                        $"Accettazione automatica: confidenza {field.Confidence:0.00}, soglia {options.ReviewThreshold:0.00}.")),
                    actor, cancellationToken);
            }
        }
        catch (ProposalException exception)
        {
            field.Outcome = ExtractionFieldOutcome.InReview;
            field.Note = Shorten(exception.Message, 500);
            field.ReviewTask = new ReviewTask { Status = ReviewStatus.Pending, CreatedAt = now };
        }
    }

    private static ExtractionField ToEntity(FieldAssessment assessment, DateTimeOffset now) => new()
    {
        FieldPath = assessment.CanonicalPath,
        CmrBox = assessment.Field.CmrBox,
        Label = assessment.Field.Label,
        Value = Shorten(assessment.Field.Value, ColumnSizes.ExtractedValue),
        NormalizedValue = assessment.NormalizedValue,
        CurrentValue = assessment.CurrentValue,
        LiteralMatch = assessment.Signals.Literal,
        FormatValid = assessment.Signals.Format,
        TmsMatch = assessment.Signals.Tms,
        PassesAgree = assessment.Signals.Agreement,
        AnnotationAgrees = assessment.Signals.Annotation,
        Confidence = assessment.Confidence,
        Outcome = assessment.Outcome,
        Note = assessment.Note is null ? null : Shorten(assessment.Note, 500),
        ReviewTask = assessment.Outcome == ExtractionFieldOutcome.InReview ? new ReviewTask { Status = ReviewStatus.Pending, CreatedAt = now } : null
    };

    private static IQueryable<ReviewTask> ReviewGraph(IQueryable<ReviewTask> tasks) =>
        tasks.Include(t => t.ExtractionField).ThenInclude(f => f.ExtractionResult).ThenInclude(r => r.Document).ThenInclude(d => d.Shipment);

    private static string SourceReference(Document document, ExtractionField field) =>
        Shorten($"documento {document.Id} ({document.FileName})" + (field.CmrBox is null ? string.Empty : $", casella {field.CmrBox}"), ColumnSizes.SourceReference);

    private static DocumentDetailView ToDetail(Document document, string shipmentCode)
    {
        ExtractionResult extraction = document.Extraction!;

        return new DocumentDetailView(
            Summary(document.Id, shipmentCode, document.FileName, document.SizeBytes, document.UploadedByName, document.UploadedAt,
                [.. extraction.Fields.Select(f => f.Outcome)]),
            document.Text,
            document.Annotations?.Split('\n') ?? [],
            new ExtractionResultView(
                extraction.ModelProvider, extraction.ModelId, extraction.Passes, extraction.Threshold, extraction.ElapsedMs,
                extraction.InputTokens, extraction.OutputTokens, extraction.CreatedAt,
                [.. extraction.Fields.OrderBy(f => f.Id).Select(ToView)]));
    }

    private static DocumentSummaryView Summary(
        int id, string shipmentCode, string fileName, long sizeBytes, string uploadedByName, DateTimeOffset uploadedAt, IReadOnlyList<ExtractionFieldOutcome> outcomes) =>
        new(id, shipmentCode, fileName, sizeBytes, uploadedByName, uploadedAt, outcomes.Count,
            outcomes.Count(o => o == ExtractionFieldOutcome.Matches),
            outcomes.Count(o => o == ExtractionFieldOutcome.Proposed),
            outcomes.Count(o => o == ExtractionFieldOutcome.InReview));

    private static ExtractionFieldView ToView(ExtractionField field) => new(
        field.Id, field.FieldPath, field.CmrBox, field.Label, field.Value, field.NormalizedValue, field.CurrentValue,
        new ConfidenceSignals(field.LiteralMatch, field.FormatValid, field.TmsMatch, field.PassesAgree, field.AnnotationAgrees),
        field.Confidence, field.Outcome, field.FieldProposalId, field.ReviewTask?.Id, field.Note);

    private static ReviewTaskView ToView(ReviewTask task)
    {
        Document document = task.ExtractionField.ExtractionResult.Document;

        return new ReviewTaskView(
            task.Id, document.Id, document.Shipment.Code, document.FileName, ToView(task.ExtractionField), task.Status,
            task.DecidedByName, task.DecidedAt, task.CorrectedValue, task.Note);
    }

    private static string Shorten(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];
}
