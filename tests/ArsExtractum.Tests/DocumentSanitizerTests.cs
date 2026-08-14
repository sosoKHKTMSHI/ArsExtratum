using ArsExtractum.Core.Documents;
using ArsExtractum.Core.Sanitization;
using Xunit;

namespace ArsExtractum.Tests;

public sealed class DocumentSanitizerTests
{
    [Fact]
    public void SanitizeStructuresHeaderAndKeepsOnlyActiveClinicalTextVisible()
    {
        var source = Document(
            HeaderLines()
                .Concat(
                [
                    Line(10, "CREATININA............................: | Valores de Referencia",
                        Cell(10, 0, "CREATININA............................:", 44, 255),
                        Cell(10, 1, "Valores de Referencia", 435, 510)),
                    Line(11, "1,50 mg/dl", Cell(11, 0, "1,50 mg/dl", 259, 313)),
                    Line(12, "Homens: 0,66 - 1,25 mg/dL", Cell(12, 0, "Homens: 0,66 - 1,25 mg/dL", 435, 532)),
                    Line(13, "Material...: SORO", Cell(13, 0, "Material...: SORO", 44, 120)),
                    Line(14, "Método.....: Colorimétrico (automação -", Cell(14, 0, "Método.....: Colorimétrico (automação -", 44, 280)),
                    Line(15, "equipamento)", Cell(15, 0, "equipamento)", 44, 180)),
                    Line(16, "NOTA: O uso de dipirona pode interferir no resultado", Cell(16, 0, "NOTA: O uso de dipirona pode interferir no resultado", 44, 400)),
                    Line(17, "Texto padronizado de continuação", Cell(17, 0, "Texto padronizado de continuação", 44, 280)),
                    Line(18, "Observações.:Anisocitose", Cell(18, 0, "Observações.:Anisocitose", 44, 190)),
                    Line(19, "Resultados Anteriores", Cell(19, 0, "Resultados Anteriores", 44, 150)),
                    Line(20, "01/01/2025", Cell(20, 0, "01/01/2025", 44, 100)),
                    Line(21, "1,20", Cell(21, 0, "1,20", 44, 70)),
                ])
                .Concat(FooterLines())
                .ToArray());

        var result = DocumentSanitizer.Sanitize(source);
        var page = Assert.Single(result.Pages);
        var active = page.Lines
            .Where(static line => line.Disposition == SanitizedDisposition.Active)
            .Select(static line => line.Text)
            .ToArray();

        Assert.True(page.Header.IsComplete);
        Assert.Equal("PACIENTE TESTE", page.Header.PatientName);
        Assert.Equal("030-0000001", page.Header.RequestNumber);
        Assert.True(page.FooterRecognized);
        Assert.Equal(
            ["CREATININA:", "1,50 mg/dl", "Material: SORO", "Observações:Anisocitose"],
            active);
        Assert.Contains(page.Lines, static line => line.Disposition == SanitizedDisposition.Reference);
        Assert.Contains(page.Lines, static line => line.Disposition == SanitizedDisposition.Method);
        Assert.Contains(page.Lines, static line => line.Disposition == SanitizedDisposition.BoilerplateNote);
        Assert.Contains(page.Lines, static line => line.Disposition == SanitizedDisposition.History);
        Assert.Contains(page.Lines, static line => line.Disposition == SanitizedDisposition.Footer);

        var sourceWordIds = source.Pages[0].Lines.SelectMany(static line => line.WordIds).Order().ToArray();
        var sanitizedWordIds = page.Lines.SelectMany(static line => line.SourceWordIds).Order().ToArray();
        Assert.Equal(sourceWordIds, sanitizedWordIds);
        Assert.Equal(sourceWordIds.Length, sanitizedWordIds.Distinct().Count());
    }

    [Fact]
    public void SanitizeReassemblesKnownHeaderContinuationsWithoutChangingBodyOrder()
    {
        var texts = new[]
        {
            "Fundação de Saúde Pública de Novo Hamburgo",
            "Laboratório Público Municipal",
            "AV. Pedro Adams Filho, 6520 - Bairro: Operário - Cep: 93315-550 Novo",
            "Hamburgo - RS - Fone: (51) 3272-3261",
            "Nome do Paciente.: PACIENTE TESTE Sexo....: Feminino",
            "Data Nascimento..: 01/01/2000 Idade...: 26",
            "Solicitante......: NOME INCOMPLETO",
            "Registro: 123",
            "SOBRENOME",
            "Nr Requisição....: 030-0000001 Data Req: 01/08/2026",
            "Origem...........: UNIDADE TESTE Hora Req: 10:00:00",
            "Data Col.........: 01/08/2026 Hora Col: 10:05:00",
        };
        var lines = texts.Select(SimpleLine).ToList();
        lines.Add(SimpleLine("SÓDIO.................................: 140 mEq/L", 12));
        lines.AddRange(FooterLines(13));
        var source = Document(lines.ToArray());

        var page = Assert.Single(DocumentSanitizer.Sanitize(source).Pages);

        Assert.Equal("NOME INCOMPLETO SOBRENOME", page.Header.Requester);
        Assert.Equal("123", page.Header.RequesterRegistration);
        Assert.Empty(page.Header.UnresolvedFragments);
        Assert.Equal(
            "SÓDIO: 140 mEq/L",
            Assert.Single(page.Lines, static line => line.Disposition == SanitizedDisposition.Active).Text);
    }

    [Fact]
    public void SanitizeReattachesNumericScriptSeparatedByReferenceColumn()
    {
        var lines = HeaderLines().ToList();
        lines.Add(Line(
            10,
            "2 | Valor de referência (acima de 18",
            Cell(10, 0, "2", 362, 365),
            Cell(10, 1, "Valor de referência (acima de 18", 435, 550)));
        lines.Add(Line(
            11,
            "TAXA: 145,77 mL/min/1,73m",
            Cell(11, 0, "TAXA: 145,77 mL/min/1,73m", 44, 362)));
        lines.AddRange(FooterLines(12));

        var page = Assert.Single(DocumentSanitizer.Sanitize(Document(lines.ToArray())).Pages);

        Assert.Equal(
            "TAXA: 145,77 mL/min/1,73m²",
            Assert.Single(page.Lines, static line => line.Disposition == SanitizedDisposition.Active).Text);
        Assert.Contains(
            page.Lines,
            static line => line.Disposition == SanitizedDisposition.TypographicAttachment);
    }

    [Fact]
    public void SanitizeDoesNotCarryReferenceColumnIntoUppercaseResultFields()
    {
        var lines = HeaderLines().ToList();
        lines.Add(Line(10, "Valores de Referencia:", Cell(10, 0, "Valores de Referencia:", 44, 180)));
        lines.Add(Line(11, "ATIVIDADE: 61,0 %", Cell(11, 0, "ATIVIDADE: 61,0 %", 44, 180)));
        lines.Add(Line(12, "Material: SANGUE/CITRATO", Cell(12, 0, "Material: SANGUE/CITRATO", 44, 220)));
        lines.AddRange(FooterLines(13));

        var active = Assert.Single(DocumentSanitizer.Sanitize(Document(lines.ToArray())).Pages)
            .Lines.Where(static line => line.Disposition == SanitizedDisposition.Active)
            .Select(static line => line.Text)
            .ToArray();

        Assert.Equal(["ATIVIDADE: 61,0 %", "Material: SANGUE/CITRATO"], active);
    }

    [Fact]
    public void SanitizeStopsKnownNoteBeforeSplitReferenceHeadingAndNextExam()
    {
        var lines = HeaderLines().ToList();
        lines.Add(Line(10, "Nota: valores fora da faixa de 4% a 25%", Cell(10, 0, "Nota: valores fora da faixa de 4% a 25%", 44, 300)));
        lines.Add(Line(11, "continuacao da nota", Cell(11, 0, "continuacao da nota", 44, 220)));
        lines.Add(Line(12, "Valores de", Cell(12, 0, "Valores de", 435, 490)));
        lines.Add(Line(13, "TROPONINA I (ultrassensivel): 858,1 ng/L", Cell(13, 0, "TROPONINA I (ultrassensivel): 858,1 ng/L", 44, 350)));
        lines.Add(Line(14, "Referencia:", Cell(14, 0, "Referencia:", 435, 500)));
        lines.AddRange(FooterLines(15));

        var page = Assert.Single(DocumentSanitizer.Sanitize(Document(lines.ToArray())).Pages);

        Assert.Contains(page.Lines, static line =>
            line.Disposition == SanitizedDisposition.Active &&
            line.Text.StartsWith("TROPONINA I", StringComparison.Ordinal));
        Assert.Contains(page.Lines, static line =>
            line.Disposition == SanitizedDisposition.Reference && line.Text == "Valores de");
    }

    [Fact]
    public void SanitizeKeepsResultsAndMaterialWhileRemovingRightReferenceBand()
    {
        var lines = HeaderLines().ToList();
        lines.Add(Line(
            10,
            "BILIRRUBINA DIRETA................: 0,40 mg/dl | Valores de Referencia:",
            Cell(10, 0, "BILIRRUBINA DIRETA................: 0,40 mg/dl", 44, 333),
            Cell(10, 1, "Valores de Referencia:", 411, 511)));
        lines.Add(Line(11, "ADULTOS", Cell(11, 0, "ADULTOS", 411, 436)));
        lines.Add(Line(12, "Bilirrubina Direta: ate 0,3 mg/dL", Cell(12, 0, "Bilirrubina Direta: ate 0,3 mg/dL", 411, 530)));
        lines.Add(Line(13, "BILIRRUBINA TOTAL................: 0,40 mg/dl", Cell(13, 0, "BILIRRUBINA TOTAL................: 0,40 mg/dl", 44, 333)));
        lines.Add(Line(
            14,
            "Material...: SORO | 0,2 a 1,3 mg/dL",
            Cell(14, 0, "Material...: SORO", 44, 105),
            Cell(14, 1, "0,2 a 1,3 mg/dL", 411, 490)));
        lines.AddRange(FooterLines(15));

        var active = Assert.Single(DocumentSanitizer.Sanitize(Document(lines.ToArray())).Pages)
            .Lines.Where(static line => line.Disposition == SanitizedDisposition.Active)
            .Select(static line => line.Text)
            .ToArray();

        Assert.Equal(
            ["BILIRRUBINA DIRETA: 0,40 mg/dl", "BILIRRUBINA TOTAL: 0,40 mg/dl", "Material: SORO"],
            active);
    }

    [Fact]
    public void SanitizeRemovesConfirmedHematologyTableAndRightObservationReference()
    {
        var lines = HeaderLines().ToList();
        lines.Add(Line(
            10,
            "BETA HCG........................: SUPERIOR A 25 mUI/mL | Observacao:",
            Cell(10, 0, "BETA HCG........................: SUPERIOR A 25 mUI/mL", 44, 372),
            Cell(10, 1, "Observacao:", 427, 466)));
        lines.Add(Line(11, "inferior a 25 mUI/mL - mulheres", Cell(11, 0, "inferior a 25 mUI/mL - mulheres", 427, 539)));
        lines.Add(Line(12, "Material...: SORO", Cell(12, 0, "Material...: SORO", 44, 105)));
        lines.Add(Line(13, "nao gravidas.", Cell(13, 0, "nao gravidas.", 427, 474)));
        lines.Add(Line(14, "HEMOGRAMA COMPLETO", Cell(14, 0, "HEMOGRAMA COMPLETO", 44, 200)));
        lines.Add(Line(15, "Hemoglobina ................: 11,50 g/dl", Cell(15, 0, "Hemoglobina ................: 11,50 g/dl", 44, 280)));
        lines.Add(Line(16, "Valores Hematologicos de Referencia - Serie Vermelha", Cell(16, 0, "Valores Hematologicos de Referencia - Serie Vermelha", 44, 400)));
        lines.Add(Line(17, "IDADE | Hemoglobina (g/dL)", Cell(17, 0, "IDADE | Hemoglobina (g/dL)", 44, 300)));
        lines.Add(Line(18, "Nascimento | 18,0 +/- 4,0", Cell(18, 0, "Nascimento | 18,0 +/- 4,0", 44, 260)));
        lines.AddRange(FooterLines(19));

        var page = Assert.Single(DocumentSanitizer.Sanitize(Document(lines.ToArray())).Pages);
        var active = page.Lines
            .Where(static line => line.Disposition == SanitizedDisposition.Active)
            .Select(static line => line.Text)
            .ToArray();

        Assert.Equal(
            ["BETA HCG: SUPERIOR A 25 mUI/mL", "Material: SORO", "HEMOGRAMA COMPLETO", "Hemoglobina: 11,50 g/dl"],
            active);
        Assert.Contains(page.Lines, static line =>
            line.Disposition == SanitizedDisposition.BoilerplateNote &&
            line.Text.StartsWith("Valores Hematologicos", StringComparison.Ordinal));
    }

    [Fact]
    public void SanitizeRecognizesHematologyReferenceRowsOnContinuationPage()
    {
        var lines = HeaderLines().ToList();
        lines.Add(Line(10, "12 | 9 | CHCM | 9", Cell(10, 0, "12 | 9 | CHCM | 9", 44, 200)));
        lines.Add(Line(11, "Hemacias (x10 /L) Hemoglobina (g/dL)", Cell(11, 0, "Hemacias (x10 /L) Hemoglobina (g/dL)", 44, 340)));
        lines.Add(Line(12, "(g/dL)", Cell(12, 0, "(g/dL)", 44, 90)));
        lines.Add(Line(13, "Homens | 5,0 +/- 0,5", Cell(13, 0, "Homens | 5,0 +/- 0,5", 44, 220)));
        lines.Add(Line(14, "Mulheres | 4,3 +/- 0,5", Cell(14, 0, "Mulheres | 4,3 +/- 0,5", 44, 220)));
        lines.AddRange(FooterLines(15));

        var page = Assert.Single(DocumentSanitizer.Sanitize(Document(lines.ToArray())).Pages);

        Assert.DoesNotContain(
            page.Lines,
            static line => line.Disposition == SanitizedDisposition.Active);
        Assert.Equal(
            5,
            page.Lines.Count(static line => line.Disposition == SanitizedDisposition.BoilerplateNote));
    }

    [Fact]
    public void SanitizeRemovesTechnicalFieldSeparatorAndKnownHba1cGuidance()
    {
        var lines = HeaderLines().ToList();
        lines.Add(Line(
            10,
            "TRANSAMINASE GLUTAMICO-OXALACETICA (TGO): | 29,00 U/L | complemento",
            Cell(10, 0, "TRANSAMINASE GLUTAMICO-OXALACETICA (TGO):", 44, 280),
            Cell(10, 1, "29,00 U/L", 285, 350),
            Cell(10, 2, "complemento", 355, 430)));
        lines.Add(SimpleLine(
            "Em casos de resultado superior a 14%, considerar o valor de glicemia média superior ao valor reportado.",
            11));
        lines.Add(SimpleLine(
            "base nos valores referenciais destas (The A1c-Derived Average Glucose(ADAG) Study Group: Translating the",
            12));
        lines.Add(SimpleLine(
            "A1C assay into estimated average glucose. Diabetes Care, june 2008).",
            13));
        lines.AddRange(FooterLines(14));

        var page = Assert.Single(DocumentSanitizer.Sanitize(Document(lines.ToArray())).Pages);
        var active = page.Lines
            .Where(static line => line.Disposition == SanitizedDisposition.Active)
            .Select(static line => line.Text)
            .ToArray();

        Assert.Equal(
            ["TRANSAMINASE GLUTAMICO-OXALACETICA (TGO): 29,00 U/L | complemento"],
            active);
        Assert.Equal(
            3,
            page.Lines.Count(static line => line.Disposition == SanitizedDisposition.BoilerplateNote));
    }

    private static ReconstructedDocument Document(params ReconstructedLine[] lines) =>
        new(
            "1.2",
            "doc-test",
            "synthetic.pdf",
            [new ReconstructedPage(1, 595, 842, lines, [], [], [])]);

    private static IEnumerable<ReconstructedLine> HeaderLines() =>
    [
        Line(0, "Fundação de Saúde Pública de Novo Hamburgo", Cell(0, 0, "Fundação de Saúde Pública de Novo Hamburgo", 140, 425)),
        Line(1, "Laboratório Público Municipal", Cell(1, 0, "Laboratório Público Municipal", 200, 390)),
        Line(2, "AV. Pedro Adams Filho, 6520 - Bairro: Operário - Cep: 93315-550 Novo", Cell(2, 0, "AV. Pedro Adams Filho, 6520 - Bairro: Operário - Cep: 93315-550 Novo", 42, 500)),
        Line(3, "Hamburgo - RS - Fone: (51) 3272-3261", Cell(3, 0, "Hamburgo - RS - Fone: (51) 3272-3261", 42, 300)),
        Line(4, "Nome do Paciente.: PACIENTE TESTE Sexo....: Feminino", Cell(4, 0, "Nome do Paciente.: PACIENTE TESTE Sexo....: Feminino", 42, 450)),
        Line(5, "Data Nascimento..: 01/01/2000 Idade...: 26", Cell(5, 0, "Data Nascimento..: 01/01/2000 Idade...: 26", 42, 350)),
        Line(6, "Solicitante......: MEDICO TESTE Registro: 123", Cell(6, 0, "Solicitante......: MEDICO TESTE Registro: 123", 42, 400)),
        Line(7, "Nr Requisição....: 030-0000001 Data Req: 01/08/2026", Cell(7, 0, "Nr Requisição....: 030-0000001 Data Req: 01/08/2026", 42, 400)),
        Line(8, "Origem...........: UNIDADE TESTE Hora Req: 10:00:00", Cell(8, 0, "Origem...........: UNIDADE TESTE Hora Req: 10:00:00", 42, 430)),
        Line(9, "Data Col.........: 01/08/2026 Hora Col: 10:05:00", Cell(9, 0, "Data Col.........: 01/08/2026 Hora Col: 10:05:00", 42, 430)),
    ];

    private static IEnumerable<ReconstructedLine> FooterLines(int start = 22) =>
    [
        Line(start, "Laudo conferido e liberado eletronicamente pelo(a) Analista:", Cell(start, 0, "Laudo conferido e liberado eletronicamente pelo(a) Analista:", 44, 370)),
        Line(start + 1, "Responsável Técnico: Farmacêutico Teste", Cell(start + 1, 0, "Responsável Técnico: Farmacêutico Teste", 44, 300)),
        Line(start + 2, "Data e hora liberação: 01/08/2026 11:00:00", Cell(start + 2, 0, "Data e hora liberação: 01/08/2026 11:00:00", 44, 310)),
        Line(start + 3, "Impresso por: TESTE", Cell(start + 3, 0, "Impresso por: TESTE", 44, 150)),
        Line(start + 4, "Este laudo possui caráter meramente informativo", Cell(start + 4, 0, "Este laudo possui caráter meramente informativo", 44, 350)),
    ];

    private static ReconstructedLine Line(
        int index,
        string text,
        params ReconstructedCell[] cells) =>
        new(
            $"p0001-l{index:D4}",
            index,
            text.Replace(" | ", " ", StringComparison.Ordinal),
            text,
            new PdfBounds(40, 700 - index * 10, 540, 708 - index * 10),
            700 - index * 10,
            cells.SelectMany(static cell => cell.WordIds).ToArray(),
            cells);

    private static ReconstructedCell Cell(
        int line,
        int index,
        string text,
        double left,
        double right) =>
        new(
            $"p0001-l{line:D4}-c{index:D2}",
            index,
            text,
            new PdfBounds(left, 0, right, 8),
            [$"p0001-w{line:D4}-{index:D2}"]);

    private static ReconstructedLine SimpleLine(string text, int index) =>
        Line(index, text, Cell(index, 0, text, 42, 500));
}
