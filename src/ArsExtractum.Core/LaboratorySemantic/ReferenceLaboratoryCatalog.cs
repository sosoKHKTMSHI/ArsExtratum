using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ArsExtractum.Core.LaboratorySemantic;

public sealed record ReferenceLaboratoryCatalogDocument(
    string SchemaVersion,
    string CatalogId,
    string CatalogVersion,
    string ReferenceCorpusId,
    string AssemblySchemaVersion,
    string AssemblyRulesVersion,
    IReadOnlyList<ReferenceLaboratoryConcept> Concepts);

public sealed record ReferenceLaboratoryConcept(
    string ConceptId,
    string DisplayName,
    string CanonicalDocumentaryKey,
    IReadOnlyList<string> ObservedAliases,
    string StructuralFormId,
    string ExtractionStrategyId,
    IReadOnlyList<string> ObservedComponents,
    IReadOnlyList<string> ObservedFields,
    IReadOnlyList<string> ObservedUnits,
    IReadOnlyList<string> ObservedSpecimens,
    IReadOnlyList<string> SegmentationHints,
    IReadOnlyList<CatalogEvidenceLocator> EvidenceLocators,
    int ObservedOccurrenceCount);

public sealed record CatalogEvidenceLocator(
    string BlockId,
    string DocumentId,
    int PageNumber,
    string LineId,
    string EpisodeKey,
    string ObservedAlias);

public sealed class ReferenceLaboratoryCatalog
{
    private const string ResourceSuffix = "fsph-nh-laboratory-catalog.v1.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private readonly Dictionary<string, ReferenceLaboratoryConcept> _aliases;

    private ReferenceLaboratoryCatalog(ReferenceLaboratoryCatalogDocument document)
    {
        Document = document;
        _aliases = new Dictionary<string, ReferenceLaboratoryConcept>(StringComparer.Ordinal);
        foreach (var concept in document.Concepts)
        {
            foreach (var alias in concept.ObservedAliases)
            {
                var key = Normalize(LiteralLabel(alias));
                if (!_aliases.TryAdd(key, concept) && _aliases[key].ConceptId != concept.ConceptId)
                {
                    throw new InvalidOperationException($"Alias documental ambíguo no catálogo v1: '{alias}'.");
                }
            }
        }

        if (document.Concepts.Select(static item => item.ConceptId).Distinct(StringComparer.Ordinal).Count() !=
            document.Concepts.Count)
        {
            throw new InvalidOperationException("O catálogo v1 contém ConceptId duplicado.");
        }
    }

    public ReferenceLaboratoryCatalogDocument Document { get; }

    public IReadOnlyList<ReferenceLaboratoryConcept> Concepts => Document.Concepts;

    public bool TryMatch(string text, out ReferenceLaboratoryConcept concept) =>
        _aliases.TryGetValue(Normalize(LiteralLabel(text)), out concept!);

    public static ReferenceLaboratoryCatalog LoadBuiltIn()
    {
        var assembly = typeof(ReferenceLaboratoryCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Recurso do catálogo laboratorial v1 não foi encontrado.");
        var document = JsonSerializer.Deserialize<ReferenceLaboratoryCatalogDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException("O catálogo laboratorial v1 não pôde ser desserializado.");
        return new ReferenceLaboratoryCatalog(document);
    }

    public static string LiteralLabel(string text)
    {
        var value = string.Join(' ', text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            return value[..colon].Trim(' ', '.');
        }

        var pipe = value.IndexOf('|', StringComparison.Ordinal);
        if (pipe >= 0 && value[(pipe + 1)..].Any(char.IsDigit))
        {
            return value[..pipe].Trim(' ', '.');
        }

        return value.Trim(' ', '.');
    }

    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var previousSpace = true;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
                previousSpace = false;
            }
            else if (!previousSpace)
            {
                builder.Append(' ');
                previousSpace = true;
            }
        }

        return builder.ToString().Trim();
    }
}
