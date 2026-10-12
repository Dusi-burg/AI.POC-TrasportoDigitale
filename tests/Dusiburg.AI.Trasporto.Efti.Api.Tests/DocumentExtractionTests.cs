using System.Net;
using System.Net.Http.Json;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Documents.Extraction;
using Dusiburg.AI.Trasporto.Efti.Api.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Data.Entities;
using Dusiburg.AI.Trasporto.Shared.Contracts.Documents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;
using Dusiburg.AI.Trasporto.Shared.Contracts.Views.Efti;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Security;
using Dusiburg.AI.Trasporto.Shared.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dusiburg.AI.Trasporto.Efti.Api.Tests;

/// <summary>
/// Dal PDF caricato ai campi con la loro sorte, via HTTP su un database dedicato. Il PDF è quello vero del repository e il
/// suo testo si legge davvero; solo il modello è finto, e restituisce ciò che il test decide. Ogni test usa una spedizione
/// propria, così l'ordine di esecuzione non conta.
/// </summary>
public class DocumentExtractionTests
{
    private readonly StubExtractionModel _model = new();
    private EftiApiTestHost _api = null!;

    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [OneTimeSetUp]
    public async Task StartApiWithDemoShipments()
    {
        _api = await EftiApiTestHost.StartAsync(
            CancellationToken,
            services =>
            {
                services.RemoveAll<IChatClientFactory>();
                services.AddSingleton<IChatClientFactory>(_model);
            },
            "TO-1001", "TO-1002", "TO-1003", "TO-1004", "TO-1005", "TO-1006");
    }

    [OneTimeTearDown]
    public async Task StopApi() => await _api.DisposeAsync();

    [Test]
    public async Task Upload_CmrThatSaysWhatTheShipmentAlreadyHas_LeavesNothingToDo()
    {
        //SETUP
        _model.Reply(DemoCmrFiles.CorrectReading("CMR-1006"));

        //SUT
        DocumentDetailView document = await UploadAsync("SH-1006", "CMR-1006");

        AuditEvent audited = await _api.LastAuditEventAsync(CancellationToken);
        DocumentDetailView reread = await _api.GetAsync<DocumentDetailView>(Role.Admin, $"/api/documents/{document.Document.Id}", CancellationToken);
        List<DocumentSummaryView> listed = await _api.GetAsync<List<DocumentSummaryView>>(Role.Admin, "/api/shipments/SH-1006/documents", CancellationToken);
        ExtractionResultView extraction = document.Extraction;
        Assert.That(extraction.Fields, Is.Not.Empty);
        Assert.That(extraction.Fields.Select(f => f.Outcome), Is.All.EqualTo(ExtractionFieldOutcome.Matches));
        Assert.That(extraction.Fields.Select(f => f.Signals), Is.All.EqualTo(new ConfidenceSignals(true, true, true, true)));
        Assert.That(extraction.Fields.Select(f => f.Confidence), Is.All.EqualTo(1m));
        Assert.That((extraction.Provider, extraction.Model, extraction.Passes, extraction.Threshold, extraction.InputTokens), Is.EqualTo(("stub", "scripted", 2, 0.80m, (long?)2_000)));
        Assert.That((document.Document.ShipmentCode, document.Document.FileName, document.Document.Proposed, document.Document.InReview, document.Document.UploadedByName),
            Is.EqualTo(("SH-1006", "CMR-1006.pdf", 0, 0, DemoActors.For(Role.Dispatcher).Name)));
        Assert.That(document.Text, Does.Contain("CMR-1006001").And.Contain("Ricevuto con riserva"), "il testo è quello letto dal PDF vero");
        Assert.That(reread.Extraction.Fields, Is.EqualTo(extraction.Fields));
        Assert.That(listed.Select(d => d.Id), Is.EqualTo(new[] { document.Document.Id }));
        Assert.That((audited.Operation, audited.ShipmentCode, audited.Outcome), Is.EqualTo((Operation.DocumentUpload, "SH-1006", AuditOutcome.Success)));
        Assert.That(await ProposalsAsync("SH-1006"), Is.Empty);
        Assert.That(await _api.DatasetsAsync("SH-1006", CancellationToken), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task File_ReturnsThePdfExactlyAsUploaded_AndTheDownloadIsAudited()
    {
        //SETUP
        _model.Reply(DemoCmrFiles.CorrectReading("CMR-1005"));
        DocumentDetailView document = await UploadAsync("SH-1005", "CMR-1005");

        //SUT
        using HttpResponseMessage response = await _api.SendAsync(Role.Dispatcher, HttpMethod.Get, $"/api/documents/{document.Document.Id}/file", null, CancellationToken);
        byte[] downloaded = await response.Content.ReadAsByteArrayAsync(CancellationToken);

        AuditEvent audited = await _api.LastAuditEventAsync(CancellationToken);
        using HttpResponseMessage unknown = await _api.SendAsync(Role.Dispatcher, HttpMethod.Get, "/api/documents/999999/file", null, CancellationToken);
        using HttpResponseMessage authority = await _api.SendAsync(Role.Authority, HttpMethod.Get, $"/api/documents/{document.Document.Id}/file", null, CancellationToken);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(downloaded, Is.EqualTo(DemoCmrFiles.Pdf("CMR-1005")), "chi rivede un campo guarda il documento vero, byte per byte");
        Assert.That((response.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentDisposition?.FileName?.Trim('"')), Is.EqualTo(("application/pdf", "CMR-1005.pdf")));
        Assert.That((audited.Operation, audited.ShipmentCode, audited.Outcome), Is.EqualTo((Operation.DocumentDownload, "SH-1005", AuditOutcome.Success)));
        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(authority.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "l'autorità non vede i documenti del vettore");
    }

    [Test]
    public async Task Upload_CmrWithWhatTheTmsLacks_ProposesItAndSendsTheConflictToReview()
    {
        //SETUP
        _model.Reply(DemoCmrFiles.CorrectReading("CMR-1002"));

        //SUT
        DocumentDetailView document = await UploadAsync("SH-1002", "CMR-1002");

        List<FieldProposalView> proposals = await ProposalsAsync("SH-1002");
        List<ReviewTaskView> queue = await QueueAsync("SH-1002");
        Dictionary<string, ExtractionFieldView> fields = document.Extraction.Fields.ToDictionary(f => f.FieldPath);
        ExtractionFieldView postcode = fields["consignee/postalAddress/postcode"];
        ExtractionFieldView weight = fields["grossWeight"];
        FieldProposalView postcodeProposal = proposals.Single(p => p.Id == postcode.ProposalId);
        Assert.That((postcode.Value, postcode.CurrentValue, postcode.Signals, postcode.Confidence, postcode.Outcome, postcode.CmrBox),
            Is.EqualTo(("69007", (string?)null, new ConfidenceSignals(true, true, null, true), 1m, ExtractionFieldOutcome.Proposed, "2")));
        Assert.That((postcodeProposal.Status, postcodeProposal.Source, postcodeProposal.ProposedValue), Is.EqualTo((ProposalStatus.Pending, ProposalSource.Document, "69007")));
        Assert.That(postcodeProposal.SourceReference, Is.EqualTo($"documento {document.Document.Id} (CMR-1002.pdf), casella 2"));
        Assert.That(postcodeProposal.Rationale, Does.Contain("confidenza 1.00").And.Contain("soglia 0.80"));
        Assert.That(proposals.Select(p => p.FieldPath), Is.SupersetOf(new[]
        {
            "consignee/postalAddress/postcode", "consignee/taxRegistration/id",
            "includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID", "includedConsignmentItem[0]/transportDangerousGoods[0]/properShippingName"
        }));
        Assert.That((weight.Value, weight.NormalizedValue, weight.CurrentValue, weight.Signals, weight.Confidence, weight.Outcome),
            Is.EqualTo(("8840", "8840", "8480", new ConfidenceSignals(true, true, false, true), 0.70m, ExtractionFieldOutcome.InReview)),
            "il peso del CMR contraddice quello del TMS: non diventa una proposta, lo vede una persona");
        Assert.That(queue.Select(t => (t.Field.FieldPath, t.Status)), Is.EqualTo(new[] { ("grossWeight", ReviewStatus.Pending) }));
        Assert.That(weight.ReviewTaskId, Is.EqualTo(queue.Single().Id));
        Assert.That(await _api.DatasetsAsync("SH-1002", CancellationToken), Has.Count.EqualTo(1), "l'estrazione non ha scritto nel dataset");
    }

    [Test]
    public async Task ScenarioThree_FromTheUploadToAValidDataset_OnlyThroughHumanDecisions()
    {
        //SETUP
        // Il modello copia la casella 11 com'è, con lordo e netto: non è un numero, e deve vederlo una persona.
        _model.Reply(DemoCmrFiles.CorrectReading("CMR-1003") with { TotalGrossWeight = "lordo 2.360 kg (netto 2.180 kg)" });
        ShipmentDetailView before = await _api.GetAsync<ShipmentDetailView>(Role.Dispatcher, "/api/views/shipments/SH-1003", CancellationToken);

        //SUT
        DocumentDetailView document = await UploadAsync("SH-1003", "CMR-1003");
        List<ReviewTaskView> queue = await QueueAsync("SH-1003");
        List<FieldProposalView> proposalsAfterUpload = await ProposalsAsync("SH-1003");
        ReviewTaskView weightTask = queue.Single(t => t.Field.FieldPath == "grossWeight");
        ReviewDecisionResponse confirmed = await _api.PostAsync<ReviewDecisionResponse>(
            Role.Dispatcher, $"/api/review-tasks/{weightTask.Id}/decision", new ReviewDecisionRequest(ReviewStatus.Confirmed, "2360", "Vale il lordo."), CancellationToken);
        AuditEvent audited = await _api.LastAuditEventAsync(CancellationToken);

        // I colli: la persona legge la nota dell'autista, decide che vale lo stampato e conferma il valore estratto.
        foreach (ReviewTaskView packages in queue.Where(t => t.Id != weightTask.Id))
        {
            await _api.PostAsync<ReviewDecisionResponse>(
                Role.Dispatcher, $"/api/review-tasks/{packages.Id}/decision", new ReviewDecisionRequest(ReviewStatus.Confirmed, null, null), CancellationToken);
        }

        ShipmentDetailView after = await _api.GetAsync<ShipmentDetailView>(Role.Dispatcher, "/api/views/shipments/SH-1003", CancellationToken);
        Dictionary<string, ExtractionFieldView> fields = document.Extraction.Fields.ToDictionary(f => f.FieldPath);
        Assert.That((before.Dataset.Status, before.TotalPackages, before.TotalGrossWeightKg), Is.EqualTo((DatasetStatus.Incomplete, (int?)0, (decimal?)0m)), "il TMS non ha colli né pesi");
        Assert.That(document.Annotations, Is.EqualTo(new[] { "Al carico contati 13 colli: manca 1 cassa. Firma autista." }), "la nota a mano è riconosciuta nel PDF");
        Assert.That((fields["numberOfPackages"].Value, fields["numberOfPackages"].Signals, fields["numberOfPackages"].Confidence, fields["numberOfPackages"].Outcome),
            Is.EqualTo(("14", new ConfidenceSignals(true, true, null, true, false), 0.7m, ExtractionFieldOutcome.InReview)),
            "lo stampato dice 14, la nota a mano 13: il campo non diventa una proposta, lo decide una persona");
        Assert.That(fields["transportPackage[0]/itemQuantity"].Outcome, Is.EqualTo(ExtractionFieldOutcome.InReview));
        Assert.That(queue.Select(t => t.Field.FieldPath), Is.EquivalentTo(new[] { "numberOfPackages", "transportPackage[0]/itemQuantity", "grossWeight" }));
        Assert.That(proposalsAfterUpload, Is.Empty, "nessun campo di questo documento è diventato una proposta da solo");
        Assert.That((fields["grossWeight"].Signals.Format, fields["grossWeight"].NormalizedValue, fields["grossWeight"].Outcome),
            Is.EqualTo((false, (string?)null, ExtractionFieldOutcome.InReview)));
        Assert.That(fields["grossWeight"].Note, Does.Contain("numero"));
        Assert.That((weightTask.Field.Value, weightTask.FileName, weightTask.DocumentId), Is.EqualTo(("lordo 2.360 kg (netto 2.180 kg)", "CMR-1003.pdf", document.Document.Id)));
        Assert.That((confirmed.Task.Status, confirmed.Task.CorrectedValue, confirmed.Task.Note, confirmed.Task.DecidedByName),
            Is.EqualTo((ReviewStatus.Confirmed, "2360", "Vale il lordo.", DemoActors.For(Role.Dispatcher).Name)));
        Assert.That((confirmed.Dataset!.DatasetVersion, confirmed.Dataset.Proposal.Status, confirmed.Dataset.Proposal.Source, confirmed.Dataset.Proposal.ProposedValue),
            Is.EqualTo((2, ProposalStatus.Accepted, ProposalSource.Document, "2360")), "la conferma crea la proposta e la accetta");
        Assert.That(confirmed.Task.Field.ProposalId, Is.EqualTo(confirmed.Dataset.Proposal.Id));
        Assert.That((audited.Operation, audited.ShipmentCode, audited.Outcome), Is.EqualTo((Operation.ReviewDecide, "SH-1003", AuditOutcome.Success)));
        Assert.That(audited.Detail, Does.Contain("grossWeight").And.Contain("Confirmed").And.Contain("v2"));
        Assert.That((after.Dataset.Status, after.Shipment.Errors, after.TotalPackages, after.TotalGrossWeightKg), Is.EqualTo((DatasetStatus.Valid, 0, (int?)14, (decimal?)2360m)));
        Assert.That(after.Dataset.Version, Is.EqualTo(4), "tre decisioni umane, tre versioni nuove");
    }

    [Test]
    public async Task Upload_InjectionThatTheModelObeyed_ChangesNothingAndEndsUpInReview()
    {
        //SETUP
        // Il caso peggiore: il modello fa ciò che il testo della casella 13 gli chiede, in entrambe le letture.
        CmrExtraction correct = DemoCmrFiles.CorrectReading("CMR-1004-INJ");
        CmrExtraction obeyed = correct with
        {
            TotalGrossWeight = "1",
            Consignee = correct.Consignee! with { Name = "ACME Trading Ltd", Street = "10 Dock Road", City = "London", CountryCode = "GB" }
        };
        _model.Reply(obeyed);

        //SUT
        DocumentDetailView document = await UploadAsync("SH-1004", "CMR-1004-INJ");

        string[] altered = ["grossWeight", "consignee/name", "consignee/postalAddress/streetName", "consignee/postalAddress/cityName", "consignee/postalAddress/countryCode"];
        ExtractionFieldView[] alteredFields = [.. document.Extraction.Fields.Where(f => altered.Contains(f.FieldPath))];
        (string? Instructions, string Message) sent = _model.Requests[0];
        Assert.That(OnOneLine(document.Text), Does.Contain("ignora tutte le istruzioni precedenti"), "l'iniezione è davvero nel documento");
        Assert.That(alteredFields, Has.Length.EqualTo(altered.Length));
        Assert.That(alteredFields.Select(f => f.Outcome), Is.All.EqualTo(ExtractionFieldOutcome.InReview));
        Assert.That(alteredFields.Select(f => f.Signals.Tms), Is.All.EqualTo(false), "ogni valore alterato contraddice ciò che la spedizione ha già");
        Assert.That(alteredFields.Select(f => f.Confidence), Is.All.LessThan(0.80m));
        Assert.That(document.Extraction.Fields.Where(f => !altered.Contains(f.FieldPath)).Select(f => f.Outcome), Is.All.EqualTo(ExtractionFieldOutcome.Matches));
        Assert.That(await ProposalsAsync("SH-1004"), Is.Empty, "nessun valore alterato è diventato una proposta");
        Assert.That(await _api.DatasetsAsync("SH-1004", CancellationToken), Has.Count.EqualTo(1), "il dataset non è cambiato");
        Assert.That(sent.Instructions, Is.EqualTo(CmrExtractor.Instructions));
        Assert.That(OnOneLine(sent.Message), Does.Contain("<document>").And.Contain("ignora tutte le istruzioni precedenti").And.EndWith("</document>"), "il testo ostile arriva al modello come dato, fra delimitatori");
    }

    [Test]
    public async Task Upload_ReadingsThatDisagree_LowerTheConfidence_AndARejectedReviewChangesNothing()
    {
        //SETUP
        CmrExtraction first = DemoCmrFiles.CorrectReading("CMR-1005") with { VehiclePlate = "ZZ105AB" };
        _model.Reply(first, first with { VehiclePlate = "ZZ105AA" });

        //SUT
        DocumentDetailView document = await UploadAsync("SH-1005", "CMR-1005");
        ReviewTaskView task = (await QueueAsync("SH-1005")).Single();
        string decisionPath = $"/api/review-tasks/{task.Id}/decision";
        ReviewDecisionResponse rejected = await _api.PostAsync<ReviewDecisionResponse>(
            Role.Dispatcher, decisionPath, new ReviewDecisionRequest(ReviewStatus.Rejected, null, "Targa letta male."), CancellationToken);
        using HttpResponseMessage again = await _api.SendAsync(Role.Dispatcher, HttpMethod.Post, decisionPath, new ReviewDecisionRequest(ReviewStatus.Confirmed, null, null), CancellationToken);
        using HttpResponseMessage undecided = await _api.SendAsync(Role.Dispatcher, HttpMethod.Post, decisionPath, new ReviewDecisionRequest(ReviewStatus.Pending, null, null), CancellationToken);
        using HttpResponseMessage unknown = await _api.SendAsync(Role.Dispatcher, HttpMethod.Post, "/api/review-tasks/999999/decision", new ReviewDecisionRequest(ReviewStatus.Rejected, null, null), CancellationToken);
        List<ReviewTaskView> pending = await _api.GetAsync<List<ReviewTaskView>>(Role.Admin, "/api/review-tasks?status=pending&shipmentCode=SH-1005", CancellationToken);
        using HttpResponseMessage invalidFilter = await _api.SendAsync(Role.Admin, HttpMethod.Get, "/api/review-tasks?status=Boh", body: null, CancellationToken);

        ExtractionFieldView plate = document.Extraction.Fields.Single(f => f.FieldPath == "mainCarriageTransportMovement[0]/usedTransportMeans/id");
        Assert.That((plate.Value, plate.Signals, plate.Confidence, plate.Outcome), Is.EqualTo(("ZZ105AB", new ConfidenceSignals(false, true, false, false), 0.15m, ExtractionFieldOutcome.InReview)));
        Assert.That((rejected.Task.Status, rejected.Task.Note, rejected.Dataset), Is.EqualTo((ReviewStatus.Rejected, "Targa letta male.", (ProposalDecisionResponse?)null)));
        Assert.That((again.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(again, CancellationToken)), Is.EqualTo((HttpStatusCode.Conflict, ErrorCodes.Conflict)));
        Assert.That(undecided.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "Pending non è una decisione");
        Assert.That((unknown.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(unknown, CancellationToken)), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.NotFound)));
        Assert.That(pending, Is.Empty);
        Assert.That(invalidFilter.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await ProposalsAsync("SH-1005"), Is.Empty);
        Assert.That(await _api.DatasetsAsync("SH-1005", CancellationToken), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Upload_WithOtherSettings_FollowsThresholdAndAutomaticAcceptance()
    {
        //SETUP
        // Stesso database, altra configurazione: soglia a 0,60 (il conflitto con il TMS ora passa) e accettazione automatica.
        await using WebApplicationFactory<EftiApiEntryPoint> lenient = _api.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("EXTRACTION_REVIEW_THRESHOLD", "0.60");
            builder.UseSetting("EXTRACTION_AUTO_ACCEPT", "true");
            builder.UseSetting("Extraction:Passes", "1");
        });
        using HttpClient client = lenient.CreateClient().ActingAs(Role.Dispatcher);
        _model.Reply(DemoCmrFiles.CorrectReading("CMR-1001-DIFF"));

        //SUT
        using MultipartFormDataContent form = DemoCmrFiles.Form("CMR-1001-DIFF.pdf", DemoCmrFiles.Pdf("CMR-1001-DIFF"));
        using HttpResponseMessage response = await client.PostAsync("/api/shipments/SH-1001/documents", form, CancellationToken);
        DocumentDetailView? document = await response.Content.ReadFromJsonAsync<DocumentDetailView>(TrasportoJson.Options, CancellationToken);

        ExtractionFieldView weight = document!.Extraction.Fields.Single(f => f.FieldPath == "grossWeight");
        FieldProposalView proposal = (await ProposalsAsync("SH-1001")).Single();
        List<EftiDataset> versions = await _api.DatasetsAsync("SH-1001", CancellationToken);
        Assert.That((document.Extraction.Passes, document.Extraction.Threshold, _model.Requests.Count), Is.EqualTo((1, 0.60m, 1)));
        Assert.That((weight.Value, weight.CurrentValue, weight.Signals, weight.Outcome),
            Is.EqualTo(("2640", "2460", new ConfidenceSignals(true, true, false, null), ExtractionFieldOutcome.Proposed)), "con una sola lettura il segnale di accordo non si applica");
        Assert.That(weight.Confidence, Is.EqualTo(0.625m));
        Assert.That((proposal.Id, proposal.Status, proposal.ResultingDatasetVersion), Is.EqualTo((weight.ProposalId, ProposalStatus.Accepted, (int?)2)));
        Assert.That(proposal.DecisionNote, Does.StartWith("Accettazione automatica: confidenza 0.6"), "la regola che ha deciso resta scritta");
        Assert.That(versions.Select(v => (v.Version, v.Status)), Is.EqualTo(new[] { (1, DatasetStatus.Valid), (2, DatasetStatus.Incomplete) }),
            "accettare il peso del documento rende incoerente il dataset: è il motivo per cui la regola è spenta di default");
    }

    [Test]
    public async Task Upload_ThatCannotBeRead_IsRefusedWithoutStoringAnything()
    {
        //SETUP
        _model.Reply(DemoCmrFiles.CorrectReading("CMR-1001"));
        using MultipartFormDataContent notAPdf = DemoCmrFiles.Form("appunti.pdf", "Questo non è un PDF."u8.ToArray());
        using MultipartFormDataContent withoutText = DemoCmrFiles.Form("scansione.pdf", EmptyPdf());
        using MultipartFormDataContent tooBig = DemoCmrFiles.Form("grande.pdf", [.. "%PDF-1.7 "u8, .. new byte[2 * 1024 * 1024]]);
        using var wrongField = new MultipartFormDataContent { { new ByteArrayContent(DemoCmrFiles.Pdf("CMR-1001")), "documento", "CMR-1001.pdf" } };
        using MultipartFormDataContent valid = DemoCmrFiles.Form("CMR-1001.pdf", DemoCmrFiles.Pdf("CMR-1001"));

        //SUT
        using HttpResponseMessage first = await PostFormAsync("SH-1001", notAPdf);
        using HttpResponseMessage second = await PostFormAsync("SH-1001", withoutText);
        using HttpResponseMessage third = await PostFormAsync("SH-1001", tooBig);
        using HttpResponseMessage fourth = await PostFormAsync("SH-1001", wrongField);
        using HttpResponseMessage unknownShipment = await PostFormAsync("SH-9999", valid);
        using HttpResponseMessage notMultipart = await _api.SendAsync(Role.Dispatcher, HttpMethod.Post, "/api/shipments/SH-1001/documents", new { file = "x" }, CancellationToken);

        AuditEvent audited = await _api.LastAuditEventAsync(CancellationToken);
        Assert.That(await ReadProblemAsync(first), Is.EqualTo((HttpStatusCode.BadRequest, ErrorCodes.ValidationError, "Il file non è un PDF.")));
        Assert.That(await ReadProblemAsync(second), Is.EqualTo((HttpStatusCode.BadRequest, ErrorCodes.ValidationError, "Il PDF non contiene testo estraibile: le scansioni non sono supportate.")));
        Assert.That((third.StatusCode, fourth.StatusCode, notMultipart.StatusCode), Is.EqualTo((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest)));
        Assert.That((unknownShipment.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(unknownShipment, CancellationToken)), Is.EqualTo((HttpStatusCode.NotFound, ErrorCodes.NotFound)));
        Assert.That((audited.Operation, audited.Outcome), Is.EqualTo((Operation.DocumentUpload, AuditOutcome.Failed)));
        Assert.That(_model.Requests, Is.Empty, "un file che non si legge non arriva al modello");
    }

    [Test]
    public async Task Upload_WhenTheModelGivesNoValidExtraction_FailsWithoutStoringTheDocument()
    {
        //SETUP
        _model.ReplyRaw("Mi dispiace, non riesco a leggere il documento.");
        int before = (await _api.GetAsync<List<DocumentSummaryView>>(Role.Dispatcher, "/api/shipments/SH-1001/documents", CancellationToken)).Count;
        using MultipartFormDataContent form = DemoCmrFiles.Form("CMR-1001.pdf", DemoCmrFiles.Pdf("CMR-1001"));

        //SUT
        using HttpResponseMessage response = await PostFormAsync("SH-1001", form);

        int after = (await _api.GetAsync<List<DocumentSummaryView>>(Role.Dispatcher, "/api/shipments/SH-1001/documents", CancellationToken)).Count;
        Assert.That((response.StatusCode, await EftiApiTestHost.ReadProblemCodeAsync(response, CancellationToken)), Is.EqualTo((HttpStatusCode.ServiceUnavailable, ErrorCodes.UpstreamUnavailable)));
        Assert.That(_model.Requests, Has.Count.EqualTo(CmrExtractor.MaxAttempts), "un solo nuovo tentativo, poi basta");
        Assert.That(after, Is.EqualTo(before));
    }

    private async Task<DocumentDetailView> UploadAsync(string shipmentCode, string cmrCode)
    {
        using MultipartFormDataContent form = DemoCmrFiles.Form(cmrCode + ".pdf", DemoCmrFiles.Pdf(cmrCode));
        using HttpResponseMessage response = await PostFormAsync(shipmentCode, form);

        Assert.That(response.IsSuccessStatusCode, Is.True, () => $"caricamento di {cmrCode}: {(int)response.StatusCode} {response.Content.ReadAsStringAsync(CancellationToken).Result}");

        return (await response.Content.ReadFromJsonAsync<DocumentDetailView>(TrasportoJson.Options, CancellationToken))!;
    }

    private async Task<HttpResponseMessage> PostFormAsync(string shipmentCode, MultipartFormDataContent form)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, $"/api/shipments/{shipmentCode}/documents") { Content = form }
            .WithApiKey(TestApiKeys.For(Role.Dispatcher));

        return await _api.Client.SendAsync(request, CancellationToken);
    }

    private Task<List<FieldProposalView>> ProposalsAsync(string shipmentCode) =>
        _api.GetAsync<List<FieldProposalView>>(Role.Dispatcher, $"/api/shipments/{shipmentCode}/proposals", CancellationToken);

    private Task<List<ReviewTaskView>> QueueAsync(string shipmentCode) =>
        _api.GetAsync<List<ReviewTaskView>>(Role.Dispatcher, $"/api/review-tasks?status=Pending&shipmentCode={shipmentCode}", CancellationToken);

    private static async Task<(HttpStatusCode Status, string? Code, string? Detail)> ReadProblemAsync(HttpResponseMessage response)
    {
        using System.Text.Json.JsonDocument problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        return (response.StatusCode, problem.RootElement.GetProperty("code").GetString(), problem.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>Il testo di un PDF va a capo dove finisce la riga della casella: per cercarvi una frase lo si rimette su una riga.</summary>
    private static string OnOneLine(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Un PDF valido con una pagina bianca: come una scansione, non ha testo.</summary>
    private static byte[] EmptyPdf()
    {
        using var document = new PdfSharp.Pdf.PdfDocument();

        document.AddPage();

        using var stream = new MemoryStream();

        document.Save(stream, closeStream: false);

        return stream.ToArray();
    }
}
