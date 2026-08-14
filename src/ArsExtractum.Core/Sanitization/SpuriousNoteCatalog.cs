using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ArsExtractum.Core.Sanitization;

public sealed record SpuriousNoteRule(
    string Id,
    string Prefix,
    bool ContinuesUntilFooter = false);

public static class SpuriousNoteCatalog
{
    public const string Version = "1.2";

    public static IReadOnlyList<SpuriousNoteRule> Rules { get; } =
    [
        Rule("dipyrone-interference", "NOTA: O USO DE DIPIRONA"),
        Rule("idms-definition", "*IDMS: ISOTOPE DILUTION MASS SPECTROMETRY"),
        Rule("antibiogram-legend", "LEGENDA: S - SENSIVEL I - INTERMEDIARIO R - RESISTENTE"),
        Rule("lipid-source", "FONTE: FALUDI AA, IZAR MCO, SARAIVA JFK"),
        Rule("diabetes-source", "FONTE: SOCIEDADE BRASILEIRA DE DIABETES"),
        Rule("hematology-bands", "AS CELULAS BASTONADAS ESTAO INCLUIDAS NA CONTAGEM DE NEUTROFILOS"),
        Rule("hematology-red-reference-table", "VALORES HEMATOLOGICOS DE REFERENCIA - SERIE VERMELHA", true),
        Rule("hematology-adult-reference-table", "VALORES HEMATOLOGICOS DE REFERENCIA EM ADULTOS", true),
        Rule("hematology-adult-reference-scripts", "12 | 9 | CHCM | 9"),
        Rule("hematology-adult-reference-header", "HEMACIAS (X10 /L) HEMOGLOBINA"),
        Rule("hematology-adult-reference-unit", "(G/DL)"),
        Rule("hematology-adult-reference-men", "HOMENS | 5,0"),
        Rule("hematology-adult-reference-women", "MULHERES | 4,3"),
        Rule("urinalysis-automation", "SEMI-AUTOMACAO (U500 -"),
        Rule("urinalysis-microscopy", "COM MICROSCOPIA COMPLEMENTAR (FUS-2000 - DIRUI)"),
        Rule("vitros-manufacturer", "(JOHNSON)"),
        Rule("egfr-explanation", "NOTA: ESTIMADA POR CALCULO CONFORME O NATIONAL KIDNEY"),
        Rule("methodology-change", "ATENCAO: NOVA METODOLOGIA"),
        Rule("reference-change", "ATENCAO: NOVOS VALORES DE REFERENCIA"),
        Rule("control-change", "NOTA: ATENCAO PARA MUDANCA DE VALOR DE CONTROLE"),
        Rule("reference-date-change", "NOTA: ATENCAO PARA NOVO VALOR DE REFERENCIA"),
        Rule("toxoplasmosis-general", "NOTA: EM CASOS DE RESULTADOS REAGENTES PARA IGM E IGG DA TOXOPLASMOSE"),
        Rule("toxoplasmosis-sensitivity", "NOTA: PERCEBE-SE NA ROTINA LABORATORIAL QUE O METODO UTILIZADO"),
        Rule("toxoplasmosis-igg-screening", "NOTA 1: \"AS AMOSTRAS COM CONCENTRACOES"),
        Rule("toxoplasmosis-assay-comparison", "NOTA 2: \"OS RESULTADOS QUE SEGUEM FORAM OBTIDOS COM O ENSAIO ELECSYS TOXO IGG"),
        Rule("toxoplasmosis-igg-igm-guidance", "NOTA 3: EM CASOS DE RESULTADOS REAGENTES PARA IGM E IGG DA TOXOPLASMOSE"),
        Rule("toxoplasmosis-avidity-guidance", "NOTA: EM CASOS DE PRESENCA DE REATIVIDADE PAA IGG E IGM"),
        Rule("toxoplasmosis-avidity-general", "NOTA 2: AVIDEZ BAIXA SUGERE INFECCAO RECENTE"),
        Rule("toxoplasmosis-avidity-combination", "NOTA 3: ESTE EXAME DEVE SER AVALIADO CONJUNTAMENTE"),
        Rule("toxoplasmosis-avidity-time", "NOTA 4: AVIDEZ PARA TOXOPLASMOSE"),
        Rule("lipid-total-guidance", "NOTA 1: \"VALORES DE COLESTEROL TOTAL"),
        Rule("lipid-risk-guidance", "NOTA 2: VALORES DE REFERENCIAIS E DE ALVO TERAPEUTICO"),
        Rule("triglycerides-pediatric", "NOTA 1: CRIANCAS E ADOLESCENTES COM TRIGLICERIDEOS"),
        Rule("triglycerides-repeat", "NOTA 2: QUANDO OS NIVEIS DE TRIGLICERIDES"),
        Rule("ldl-pediatric", "NOTA: CRIANCAS E ADOLESCENTES COM LDL-C"),
        Rule("non-hdl-calculation", "NOTA: O COLESTEROL NAO-HDL E CALCULADO"),
        Rule("hba1c-diagnosis", "NOTA 1: O DIAGNOSTICO DE DIABETES MELLITUS"),
        Rule("hba1c-method", "NOTA 2: O METODO UTILIZADO NESTA DOSAGEM DE HEMOGLOBINA GLICADA"),
        Rule("hba1c-estimated-glucose", "NOTA 3: A GLICEMIA MEDIA ESTIMADA"),
        Rule("hba1c-overflow-guidance", "EM CASOS DE RESULTADO SUPERIOR A 14%"),
        Rule("hba1c-adag-source", "BASE NOS VALORES REFERENCIAIS DESTAS (THE A1C-DERIVED"),
        Rule("hba1c-guidance", "NOTA 4: CONFORME A AMERICAN DIABETES ASSOCIATION"),
        Rule("troponin-guidance", "NOTA: \"A LESAO DO MIOCARDIO"),
        Rule("troponin-cutoff", "NOTA: UTILIZANDO COMO BASE OS CRITERIOS DA OMS"),
        Rule("vdrl-guidance", "NOTA: A MAIOR IMPORTANCIA CLINICA NA REACAO DE VDRL"),
        Rule("vdrl-source", "NOTA: \"ADPTADO DE: BRASIL, MINISTERIO DA SAUDE"),
        Rule("hiv-confirmation", "NOTA: O DIAGNOSTICO SOROLOGICO DA INFECCAO PELO HIV"),
        Rule("hcv-screening", "NOTA 1: O METODO DE EIE"),
        Rule("hcv-confirmation", "NOTA 1: AMOSTRAS COM RESULTADOS REAGENTE DEVEM SER CONFIRMADAS"),
        Rule("hcv-immunosuppression", "NOTA 2: PACIENTES IMUNOSSUPRIMIDOS"),
        Rule("generic-confirmation", "NOTA: SUGERIMOS A REALIZACAO DE TESTE CONFIRMATORIO"),
        Rule("psa-correlation", "NOTA: CORRELACIONAR COM O VALOR DE PSA TOTAL"),
        Rule("dengue-negative", "NOTA: SE O RESULTADO FOR NAO REAGENTE E OS SINTOMAS PERSISTIREM"),
        Rule("dengue-interpretation", "NOTA: OS RESULTADOS OBTIDOS COM ESSE TESTE"),
        Rule("dengue-antigen-window", "NOTA: PODE OCORRER RESULTADO NEGATIVO QUANDO A QUANTIDADE DE ANTIGENOS DA DENGUE"),
        Rule("dengue-exposure", "NOTA: A POSSIBILIDADE DE EXPOSICAO OU INFECCAO PELO VIRUS DA DENGUE"),
        Rule("heterophile-interference", "NOTA: CONCENTRACOES ANORMALMENTE ELEVADAS DE ANTICORPOS HETEROFILOS"),
        Rule("sars-quantification", "NOTA: O VALOR QUANTITATIVO OU A CONCENTRACAO DE ANTIGENOS DE SARS-COV-2"),
        Rule("biotin-interference", "NOTA: O USO DE BIOTINA EXOGENA"),
        Rule("macroproteinuria-interference", "NOTA: VALORES FALSAMENTE BAIXOS PODEM OCORRER EM PACIENTES COM MACROPROTEINURIA"),
        Rule("generic-interpretation", "NOTA: OS RESULTADOS DEVEM SER INTERPRETADOS CONSIDERANDO-SE FATORES"),
        Rule("ck-ratio", "NOTA: VALORES FORA DA FAIXA DE"),
        Rule("support-laboratory", "NOTA 1: EXAME REALIZADO EM LABORATORIO DE APOIO"),
        Rule("support-laboratory-forwarded", "NOTA: EXAME ENCAMINHADO"),
        Rule("cmv-assay-comparison", "OBSERVACAO: \"OS RESULTADOS QUE SEGUEM FORAM OBTIDOS COM O ENSAIO ELECSYS CMV IGG"),
        Rule("cutoff-magnitude", "OBSERVACAO: A MAGNITUDE DO RESULTADO DETERMINADO ACIMA DO CUTOFF"),
    ];

    public static SpuriousNoteRule? Match(string text)
    {
        var key = ComparisonKey(text);
        return Rules.FirstOrDefault(rule => key.StartsWith(rule.Prefix, StringComparison.Ordinal));
    }

    public static SpuriousNoteRule? MatchBlockStart(string text, string? nextText)
    {
        var key = ComparisonKey(text).TrimEnd(':');
        var nextKey = ComparisonKey(nextText ?? string.Empty);
        if (key == "OBSERVACOES" &&
            nextKey.StartsWith("1. VALORES PERSISTENTES ABAIXO DE 60", StringComparison.Ordinal))
        {
            return Rule("egfr-observations", "OBSERVACOES");
        }

        if (key == "NOTAS" &&
            nextKey.StartsWith("- O DIAGNOSTICO SOROLOGICO DA INFECCAO PELO HIV", StringComparison.Ordinal))
        {
            return Rule("hiv-notes-block", "NOTAS");
        }

        if (key == "NOTAS" &&
            nextKey.StartsWith("- O USO DE BIOTINA EXOGENA", StringComparison.Ordinal))
        {
            return Rule("assay-interference-block", "NOTAS");
        }

        if (key == "NOTAS" &&
            nextKey.StartsWith("- OS RESULTADOS DEVEM SER INTERPRETADOS CONSIDERANDO-SE FATORES COMO IDADE", StringComparison.Ordinal))
        {
            return Rule("psa-notes-block", "NOTAS");
        }

        if (key == "NOTA" &&
            nextKey.StartsWith("A PRODUCAO DO HCG SE INICIA", StringComparison.Ordinal))
        {
            return Rule("hcg-guidance-block", "NOTA");
        }

        return null;
    }

    public static string ComparisonKey(string text)
    {
        var normalized = text
            .Replace("\uFB01", "fi", StringComparison.Ordinal)
            .Replace("\uFB02", "fl", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return Regex.Replace(builder.ToString(), @"\s+", " ")
            .Trim()
            .ToUpperInvariant();
    }

    private static SpuriousNoteRule Rule(
        string id,
        string prefix,
        bool continuesUntilFooter = false) =>
        new($"note.{id}.v1", prefix, continuesUntilFooter);
}
