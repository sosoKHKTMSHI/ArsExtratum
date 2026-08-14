using ArsExtractum.Core.LaboratorySemantic;

namespace ArsExtractum.Core.OutputProjection;

internal static class OutputProjectionRules
{
    private static readonly string[][] OrderedGroups =
    [
        ["hemograma-completo", "dosagem-de-hemoglobina", "hematocrito", "reticulocitos", "plaquetas"],
        ["tempo-de-protrombina-tp", "tempo-de-tromboplastina-parcial-ativada-ttpa"],
        ["proteina-c-reativa", "velocidade-de-hemossedimentacao-vhs", "dosagem-de-ferritina"],
        ["ureia", "creatinina", "acido-urico"],
        ["sodio", "potassio", "cloreto", "calcio-total", "magnesio", "fosforo"],
        ["glicemia", "glicemia-de-jejum", "glicemia-ao-acaso", "glicemia-apos-1-hora", "glicemia-apos-2-horas", "hemoglobina-glicada-hba1c", "lactato-acido-lactico", "cetonemia", "colesterol-total", "colesterol-hdl", "colesterol-ldl", "triglicerideos"],
        ["transaminase-glutamico-oxalacetica-tgo", "transaminase-glutamico-piruvica-tgp", "fosfatase-alcalina", "gamaglutamil-transferase-ggt", "bilirrubina-direta", "dosagem-de-bilirrubina-neonatal", "albumina", "desidrogenase-latica-ldh"],
        ["amilase", "lipase"],
        ["troponina-i-ultrassensivel", "troponina-t-ultrassensivel", "creatinoquinase-fracao-mb-ckmb"],
        ["gasometria-arterial", "gasometria-venosa"],
        ["exame-qualitativo-de-urina-equ", "creatinina-na-urina", "microalbuminuria-amostra-isolada", "proteinas-na-urina", "proteinuria-urina-24-horas"],
        ["urocultura", "cultural", "cultural-de-aspirado-traqueal", "cultura-de-swab-axilar-e-inguinal", "cultura-de-swab-retal", "hemocultura-amostra-um", "hemocultura-amostra-dois", "hemocultura-pediatrico", "hemocultura-de-cateter", "antibiograma", "antibiograma-de-urina", "antibiograma-de-aspirado-traqueal", "antibiograma-de-hemocultura-pediatrica", "bacterioscopico-gram", "pesquisa-de-baar-amostra-um", "pesquisa-de-fungos"],
        ["anti-hiv-1-e-2", "hbsag", "anti-hbs", "anti-hcv", "citomegalovirus-igg", "citomegalovirus-igm", "toxoplasmose-igg", "toxoplasmose-igm", "toxoplasmose-avidez-anticorpos-igg", "vdrl-qualitativo", "pesquisa-de-antigeno-sars-cov-2", "teste-rapido-dengue-ns1"],
        ["hormonio-tireoestimulante-tsh", "tiroxina-livre-t4-livre", "tiroxina-t4", "triiodotironina-t3", "beta-hcg", "antigeno-prostatico-especifico-total-psa", "antigeno-prostatico-especifico-livre-psa-livre", "tipagem-sanguinea-grupo-abo-e-fator-rh"],
    ];

    private static readonly Dictionary<string, (int Group, int Order)> ConceptOrder = OrderedGroups
        .SelectMany((group, groupIndex) => group.Select((id, order) =>
            new KeyValuePair<string, (int, int)>($"fsph-nh.{id}", (groupIndex, order))))
        .ToDictionary(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> ConceptLabels = new(StringComparer.Ordinal)
    {
        ["fsph-nh.plaquetas"] = "Plaq",
        ["fsph-nh.tempo-de-tromboplastina-parcial-ativada-ttpa"] = "TTPa",
        ["fsph-nh.creatinina"] = "Cr", ["fsph-nh.ureia"] = "Ureia",
        ["fsph-nh.sodio"] = "Na", ["fsph-nh.potassio"] = "K", ["fsph-nh.cloreto"] = "Cl",
        ["fsph-nh.calcio-total"] = "Ca", ["fsph-nh.magnesio"] = "Mg", ["fsph-nh.fosforo"] = "P",
        ["fsph-nh.glicemia"] = "Glic", ["fsph-nh.lactato-acido-lactico"] = "Lact",
        ["fsph-nh.proteina-c-reativa"] = "PCR", ["fsph-nh.velocidade-de-hemossedimentacao-vhs"] = "VHS",
        ["fsph-nh.transaminase-glutamico-oxalacetica-tgo"] = "TGO",
        ["fsph-nh.transaminase-glutamico-piruvica-tgp"] = "TGP",
        ["fsph-nh.fosfatase-alcalina"] = "FA", ["fsph-nh.gamaglutamil-transferase-ggt"] = "GGT",
        ["fsph-nh.albumina"] = "Alb", ["fsph-nh.creatinoquinase-fracao-mb-ckmb"] = "CK-MB",
    };

    private static readonly Dictionary<string, string> ComponentLabels = new(StringComparer.Ordinal)
    {
        ["HEMOGLOBINA"] = "Hb", ["HEMATOCRITO"] = "Ht", ["HEMACIAS"] = "Hemácias",
        ["V.C.M"] = "VCM", ["VCM"] = "VCM", ["H.C.M"] = "HCM", ["HCM"] = "HCM",
        ["C.H.C.M"] = "CHCM", ["CHCM"] = "CHCM", ["RDW"] = "RDW",
        ["LEUCOCITOS"] = "Leuco", ["SEGMENTADOS (NEUTROFILOS)"] = "Neut", ["NEUTROFILOS"] = "Neut",
        ["BASTONETES"] = "Bast", ["LINFOCITOS"] = "Linf", ["MONOCITOS"] = "Mono",
        ["EOSINOFILOS"] = "Eos", ["BASOFILOS"] = "Baso", ["PLAQUETAS"] = "Plaq",
        ["RETICULOCITOS"] = "Retic", ["GLICEMIA"] = "Glic", ["LACTATO"] = "Lact",
        ["PH"] = "pH", ["PCO2"] = "pCO2", ["PO2"] = "pO2", ["HCO3"] = "HCO3",
        ["B.E"] = "BE", ["BE"] = "BE", ["O2 SATURACAO"] = "SatO2", ["SATO2"] = "SatO2",
        ["BILIRRUBINA TOTAL"] = "BT", ["BILIRRUBINA DIRETA"] = "BD", ["BILIRRUBINA INDIRETA"] = "BI",
        ["TEMPO DE POSITIVIDADE"] = "Tempo de positividade",
    };

    public static (int Group, int Order) OrderOf(string conceptId) =>
        ConceptOrder.GetValueOrDefault(conceptId, (int.MaxValue, int.MaxValue));

    public static string ConceptLabel(LaboratoryOccurrence occurrence) =>
        ConceptLabels.GetValueOrDefault(occurrence.ConceptId, TitleCaseDocumentLabel(occurrence.DisplayName));

    public static string ComponentLabel(string label)
    {
        var normalized = ReferenceLaboratoryCatalog.Normalize(label);
        return ComponentLabels.GetValueOrDefault(normalized, TitleCaseDocumentLabel(label));
    }

    private static string TitleCaseDocumentLabel(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Any(char.IsLower))
        {
            return trimmed;
        }

        return System.Globalization.CultureInfo.GetCultureInfo("pt-BR").TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
    }
}
