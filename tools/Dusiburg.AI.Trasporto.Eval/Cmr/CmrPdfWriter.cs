using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Dusiburg.AI.Trasporto.Eval.Cmr;

/// <summary>
/// Disegna un CMR sintetico in PDF con testo estraibile (G4). Tre impaginazioni: la griglia di caselle numerate del modello
/// IRU con etichette in due lingue, la stessa in francese, e un elenco etichetta–valore senza numeri. Le annotazioni "a mano"
/// sono testo in corsivo blu. Non è un facsimile del modulo: ne riprende caselle e numerazione.
/// </summary>
public static class CmrPdfWriter
{
    private const string PrintFont = "Arial";
    private const string ListFont = "Courier New";

    private static readonly Color Handwriting = new(20, 60, 160);

    static CmrPdfWriter()
    {
        // PDFsharp 6 non cerca da solo i font di sistema: su Windows (sviluppo e CI) si usano quelli installati.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    public static byte[] Write(CmrContent content)
    {
        var document = new Document();

        document.Info.Title = $"{content.Code} — CMR sintetico";
        document.Info.Subject = "Documento di prova, dati inventati";

        Section section = document.AddSection();

        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);

        if (content.Layout == CmrLayout.LabelList)
        {
            WriteLabelList(section, content);
        }
        else
        {
            WriteBoxes(section, content, Labels.For(content.Layout));
        }

        var renderer = new PdfDocumentRenderer { Document = document };

        renderer.RenderDocument();

        using var stream = new MemoryStream();

        renderer.PdfDocument.Save(stream, closeStream: false);

        return stream.ToArray();
    }

    private static void WriteBoxes(Section section, CmrContent content, Labels labels)
    {
        Paragraph title = section.AddParagraph();

        title.Format.Font = new Font(PrintFont, 11) { Bold = true };
        title.AddText(labels.Title);
        title.Format.SpaceAfter = Unit.FromPoint(2);

        Paragraph number = section.AddParagraph($"{labels.Number} {content.CmrNumber}");

        number.Format.Font = new Font(PrintFont, 9);
        number.Format.SpaceAfter = Unit.FromPoint(6);

        Table parties = NewTable(section, 9, 9);

        Row row = parties.AddRow();

        Box(row.Cells[0], 1, labels.Sender, PartyLines(content.Sender), content);
        Box(row.Cells[1], 16, labels.Carrier, PartyLines(content.Carrier), content);
        row = parties.AddRow();
        Box(row.Cells[0], 2, labels.Consignee, PartyLines(content.Consignee), content);
        Box(row.Cells[1], 17, labels.SuccessiveCarriers, [], content);
        row = parties.AddRow();
        Box(row.Cells[0], 3, labels.Delivery, PlaceLines(content.Delivery), content);
        Box(row.Cells[1], 18, labels.Reservations, [], content);
        row = parties.AddRow();
        Box(row.Cells[0], 4, labels.TakingOver, [.. PlaceLines(content.TakingOver), content.TakingOverDate], content);
        Box(row.Cells[1], 5, labels.Documents, [], content);

        Table goods = NewTable(section, 2.6, 3.2, 2, 5.4, 2.8, 2);

        row = goods.AddRow();
        Header(row.Cells[0], 6, labels.Marks);
        Header(row.Cells[1], 7, labels.Packages);
        Header(row.Cells[2], 8, labels.Packing);
        Header(row.Cells[3], 9, labels.Nature);
        Header(row.Cells[4], 11, labels.GrossWeight);
        Header(row.Cells[5], 12, labels.Volume);

        foreach (CmrGoodsRow line in content.Goods)
        {
            row = goods.AddRow();
            Value(row.Cells[0], line.Marks);
            Value(row.Cells[1], line.Packages);
            Value(row.Cells[2], line.Packing);
            Value(row.Cells[3], line.Description);
            Value(row.Cells[4], line.GrossWeight);
            Value(row.Cells[5], line.Volume);
        }

        row = goods.AddRow();
        Value(row.Cells[0], labels.Total, bold: true);
        Value(row.Cells[1], content.TotalPackages, bold: true);
        Value(row.Cells[4], content.TotalGrossWeight, bold: true);

        Table footer = NewTable(section, 9, 9);

        row = footer.AddRow();
        Box(row.Cells[0], 13, labels.Instructions, [content.SenderInstructions], content);
        Box(row.Cells[1], 19, labels.SpecialAgreements, [], content);
        row = footer.AddRow();
        Box(row.Cells[0], 14, labels.Payment, [content.Payment], content);
        Box(row.Cells[1], 15, labels.CashOnDelivery, [content.CashOnDelivery], content);
        row = footer.AddRow();
        Box(row.Cells[0], 21, labels.Established, [content.EstablishedAt], content);
        Box(row.Cells[1], 0, labels.Vehicle, [$"{labels.Tractor}: {content.VehiclePlate}", $"{labels.Trailer}: {content.TrailerPlate}"], content);
        row = footer.AddRow();
        Box(row.Cells[0], 22, labels.SenderSignature, [], content);
        Box(row.Cells[1], 24, labels.Received, [], content);
    }

    /// <summary>Elenco etichetta–valore: stessi dati, nessun numero di casella, merce prima dei soggetti.</summary>
    private static void WriteLabelList(Section section, CmrContent content)
    {
        Paragraph title = section.AddParagraph("INTERNATIONAL CONSIGNMENT NOTE (CMR) — LETTERA DI VETTURA");

        title.Format.Font = new Font(ListFont, 10) { Bold = true };
        title.Format.SpaceAfter = Unit.FromPoint(8);

        void Line(string label, string? value)
        {
            Paragraph paragraph = section.AddParagraph();

            paragraph.Format.Font = new Font(ListFont, 8.5);
            paragraph.Format.SpaceAfter = Unit.FromPoint(2);
            paragraph.AddFormattedText($"{label}: ", TextFormat.Bold);
            paragraph.AddText(string.IsNullOrWhiteSpace(value) ? "—" : value);
        }

        Line("CONSIGNMENT NOTE No", content.CmrNumber);
        Line("VEHICLE / TARGA MOTRICE", content.VehiclePlate);
        Line("TRAILER / TARGA RIMORCHIO", content.TrailerPlate);

        for (int index = 0; index < content.Goods.Count; index++)
        {
            CmrGoodsRow line = content.Goods[index];

            Line($"GOODS LINE {index + 1} / MERCE", line.Description);
            Line($"  PACKAGES LINE {index + 1} / COLLI", $"{line.Packages} {line.Packing}");
            Line($"  GROSS WEIGHT LINE {index + 1} KG / PESO LORDO", line.GrossWeight);
            Line($"  VOLUME LINE {index + 1} M3", line.Volume);
        }

        Line("TOTAL PACKAGES / TOTALE COLLI", content.TotalPackages);
        Line("TOTAL GROSS WEIGHT / PESO LORDO TOTALE", content.TotalGrossWeight);
        Line("SENDER / MITTENTE", string.Join(", ", PartyLines(content.Sender)));
        Line("CONSIGNEE / DESTINATARIO", string.Join(", ", PartyLines(content.Consignee)));
        Line("CARRIER / VETTORE", string.Join(", ", PartyLines(content.Carrier)));
        Line("PLACE OF TAKING OVER / PRESA IN CARICO", string.Join(", ", PlaceLines(content.TakingOver)));
        Line("DATE OF TAKING OVER / DATA", content.TakingOverDate);
        Line("PLACE OF DELIVERY / CONSEGNA", string.Join(", ", PlaceLines(content.Delivery)));
        Line("SENDER'S INSTRUCTIONS / ISTRUZIONI DEL MITTENTE", content.SenderInstructions);
        Line("PAYMENT / PAGAMENTO DEL NOLO", content.Payment);
        Line("CASH ON DELIVERY / CONTRASSEGNO", content.CashOnDelivery);
        Line("ESTABLISHED IN / COMPILATO A", content.EstablishedAt);

        foreach (CmrHandwrittenNote note in content.Handwritten)
        {
            Paragraph paragraph = section.AddParagraph();

            paragraph.Format.SpaceBefore = Unit.FromPoint(6);
            paragraph.Format.Font = new Font(ListFont, 8.5);
            paragraph.AddFormattedText(note.Box == 24 ? "GOODS RECEIVED / RICEVUTA DEL DESTINATARIO: " : "CARRIER'S RESERVATIONS / RISERVE DEL VETTORE: ", TextFormat.Bold);
            AddHandwriting(paragraph, note.Text);
        }
    }

    private static Table NewTable(Section section, params double[] widthsInCentimeters)
    {
        Table table = section.AddTable();

        table.Borders.Width = 0.5;
        table.TopPadding = Unit.FromPoint(2);
        table.BottomPadding = Unit.FromPoint(3);

        foreach (double width in widthsInCentimeters)
        {
            table.AddColumn(Unit.FromCentimeter(width));
        }

        return table;
    }

    /// <summary>Una casella: numero ed etichetta in piccolo, poi il contenuto, poi le eventuali annotazioni a mano.</summary>
    private static void Box(Cell cell, int number, string label, IReadOnlyList<string?> lines, CmrContent content)
    {
        Header(cell, number, label);

        foreach (string? line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            Value(cell, line!);
        }

        foreach (CmrHandwrittenNote note in content.Handwritten.Where(n => n.Box == number))
        {
            Paragraph paragraph = cell.AddParagraph();

            paragraph.Format.Font = new Font(PrintFont, 9);
            AddHandwriting(paragraph, note.Text);
        }
    }

    private static void Header(Cell cell, int number, string label)
    {
        Paragraph paragraph = cell.AddParagraph(number > 0 ? $"{number} {label}" : label);

        paragraph.Format.Font = new Font(PrintFont, 6.5) { Color = Colors.DimGray };
        paragraph.Format.SpaceAfter = Unit.FromPoint(1);
    }

    private static void Value(Cell cell, string text, bool bold = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Paragraph paragraph = cell.AddParagraph(text);

        paragraph.Format.Font = new Font(PrintFont, 9) { Bold = bold };
    }

    private static void AddHandwriting(Paragraph paragraph, string text)
    {
        FormattedText handwriting = paragraph.AddFormattedText(text, TextFormat.Italic);

        handwriting.Color = Handwriting;
    }

    private static IReadOnlyList<string?> PartyLines(CmrPartyBlock party) =>
        [party.Name, .. PlaceLines(party), party.VatNumber is null ? null : $"P.IVA / VAT {party.VatNumber}"];

    private static IReadOnlyList<string?> PlaceLines(CmrPartyBlock place) =>
        [place.Street, string.Join(' ', new[] { place.PostalCode, place.City, place.CountryCode is null ? null : $"({place.CountryCode})" }.Where(p => !string.IsNullOrWhiteSpace(p)))];

    /// <summary>Etichette delle caselle nelle due versioni a griglia.</summary>
    private sealed record Labels(
        string Title, string Number, string Sender, string Consignee, string Delivery, string TakingOver, string Documents, string Marks,
        string Packages, string Packing, string Nature, string GrossWeight, string Volume, string Instructions, string Payment,
        string CashOnDelivery, string Carrier, string SuccessiveCarriers, string Reservations, string SpecialAgreements, string Established,
        string SenderSignature, string Received, string Vehicle, string Tractor, string Trailer, string Total)
    {
        public static Labels For(CmrLayout layout) => layout == CmrLayout.BoxesFrench ? French : Italian;

        private static readonly Labels Italian = new(
            "LETTERA DI VETTURA INTERNAZIONALE (CMR) — INTERNATIONAL CONSIGNMENT NOTE", "N.",
            "Mittente (nome, indirizzo, paese) / Sender", "Destinatario (nome, indirizzo, paese) / Consignee",
            "Luogo di consegna della merce / Place of delivery", "Luogo e data di presa in carico / Place and date of taking over",
            "Documenti allegati / Documents attached", "Contrassegni e numeri / Marks and Nos", "Numero dei colli / Number of packages",
            "Imballaggio / Method of packing", "Natura della merce / Nature of the goods", "Peso lordo kg / Gross weight", "Volume m3",
            "Istruzioni del mittente / Sender's instructions", "Prescrizioni di affrancazione / Instructions as to payment for carriage",
            "Rimborso / Cash on delivery", "Vettore (nome, indirizzo, paese) / Carrier", "Vettori successivi / Successive carriers",
            "Riserve e osservazioni del vettore / Carrier's reservations and observations", "Convenzioni particolari / Special agreements",
            "Compilato a / Established in", "Firma e timbro del mittente / Signature of the sender", "Ricevuta della merce / Goods received",
            "Veicolo / Vehicle", "Targa motrice", "Targa rimorchio", "Totale / Total");

        private static readonly Labels French = new(
            "LETTRE DE VOITURE INTERNATIONALE (CMR) — INTERNATIONAL CONSIGNMENT NOTE", "No",
            "Expéditeur (nom, adresse, pays) / Sender", "Destinataire (nom, adresse, pays) / Consignee",
            "Lieu prévu pour la livraison / Place of delivery", "Lieu et date de la prise en charge / Place and date of taking over",
            "Documents annexés / Documents attached", "Marques et numéros / Marks and Nos", "Nombre des colis / Number of packages",
            "Mode d'emballage / Method of packing", "Nature de la marchandise / Nature of the goods", "Poids brut kg / Gross weight", "Cubage m3",
            "Instructions de l'expéditeur / Sender's instructions", "Prescriptions d'affranchissement / Instructions as to payment for carriage",
            "Remboursement / Cash on delivery", "Transporteur (nom, adresse, pays) / Carrier", "Transporteurs successifs / Successive carriers",
            "Réserves et observations du transporteur / Carrier's reservations and observations", "Conventions particulières / Special agreements",
            "Établie à / Established in", "Signature et timbre de l'expéditeur / Signature of the sender", "Marchandises reçues / Goods received",
            "Véhicule / Vehicle", "Tracteur", "Remorque", "Total");
    }
}
