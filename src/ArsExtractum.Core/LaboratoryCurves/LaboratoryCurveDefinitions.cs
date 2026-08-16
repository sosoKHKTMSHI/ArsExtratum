namespace ArsExtractum.Core.LaboratoryCurves;

public static class LaboratoryCurveDefinitions
{
    public const string Hemoglobin = "hemoglobin";
    public const string Platelets = "platelets";
    public const string Leukocytes = "leukocytes";
    public const string LeukogramFractions = "leukogram-fractions";
    public const string CReactiveProtein = "c-reactive-protein";
    public const string Ast = "ast";
    public const string Alt = "alt";
    public const string Amylase = "amylase";
    public const string Lipase = "lipase";
    public const string BilirubinsIsolated = "bilirubins-isolated";
    public const string BilirubinsFractions = "bilirubins-fractions";
    public const string Creatinine = "creatinine";
    public const string Egfr = "egfr-ckd-epi-2021";
    public const string Urea = "urea";
    public const string Sodium = "sodium";
    public const string Potassium = "potassium";

    public static IReadOnlyList<LaboratoryCurveOption> Options { get; } =
    [
        new(Hemoglobin, "Hemoglobina", true, 10),
        new(Platelets, "Plaquetas", true, 20),
        new(Leukocytes, "Leucócitos totais", true, 30),
        new(LeukogramFractions, "Leucograma com frações", false, 40),
        new(CReactiveProtein, "PCR", true, 50),
        new(Ast, "TGO", true, 60),
        new(Alt, "TGP", true, 70),
        new(Amylase, "Amilase", true, 80),
        new(Lipase, "Lipase", true, 90),
        new(BilirubinsIsolated, "Bilirrubinas isoladas", true, 100),
        new(BilirubinsFractions, "Bilirrubina com frações", false, 110),
        new(Creatinine, "Creatinina", true, 120),
        new(Egfr, "TFG CKD-EPI", true, 130),
        new(Urea, "Ureia", true, 140),
        new(Sodium, "Sódio", true, 150),
        new(Potassium, "Potássio", true, 160),
    ];

    public static LaboratoryCurveOption ByKey(string key) =>
        Options.Single(option => option.Key == key);
}
