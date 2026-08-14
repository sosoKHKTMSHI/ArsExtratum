using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using ArsExtractum.Core.Assembly;
using ArsExtractum.Core.DerivedMeasurements;
using ArsExtractum.Core.Documents;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.Pipeline;
using ArsExtractum.Core.OutputProjection;

namespace ArsExtractum.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ProcessingPipeline _pipeline;
    private InputPdfItem? _selectedDocument;
    private PatientItemViewModel? _selectedPatient;
    private StageOptionViewModel? _selectedStage;
    private string _outputText = "Adicione um ou mais PDFs, mantenha as etapas desejadas habilitadas e selecione Processar.";
    private string _statusText = "Pronto. Processamento local, offline e sem persistência automática.";
    private string _durationText = string.Empty;
    private double _progressPercent;
    private bool _isBusy;
    private TimeSpan _lastTotalDuration;
    private TimeSpan _assemblyDuration;
    private TimeSpan _semanticDuration;
    private TimeSpan _derivedDuration;
    private TimeSpan _outputProjectionDuration;
    private bool _showUnits;
    private bool _showCultures;

    public MainWindowViewModel(ProcessingPipeline pipeline)
    {
        _pipeline = pipeline;
        Stages = new ObservableCollection<StageOptionViewModel>(pipeline.Stages
            .Append(PatientEpisodeAssembler.Descriptor)
            .Append(LaboratorySemanticExtractor.Descriptor)
            .Append(DerivedMeasurementComputer.Descriptor)
            .Append(OutputProjector.Descriptor)
            .Select(static descriptor => new StageOptionViewModel(descriptor)));
        _selectedStage = Stages.LastOrDefault();
    }

    public ObservableCollection<InputPdfItem> Documents { get; } = [];

    public ObservableCollection<PatientItemViewModel> Patients { get; } = [];

    public ObservableCollection<StageOptionViewModel> Stages { get; }

    public IReadOnlyList<DocumentRunRecord> CompletedRuns { get; private set; } = [];

    public PatientBatch? PatientBatch { get; private set; }

    public SemanticPatientBatch? SemanticPatientBatch { get; private set; }

    public ClinicalOutputBatch? ClinicalOutputBatch { get; private set; }

    public bool ShowUnits
    {
        get => _showUnits;
        set
        {
            if (SetProperty(ref _showUnits, value) && SemanticPatientBatch?.DerivedMeasurementCoverage?.IsComplete == true)
            {
                ProjectClinicalOutput(true);
                RefreshOutput();
            }
        }
    }

    public bool ShowCultures
    {
        get => _showCultures;
        set
        {
            if (SetProperty(ref _showCultures, value) && SemanticPatientBatch?.DerivedMeasurementCoverage?.IsComplete == true)
            {
                ProjectClinicalOutput(true);
                RefreshOutput();
            }
        }
    }

    public bool HasSelectedPatientCultures => SelectedSemanticPatient()?.Episodes
        .SelectMany(static episode => episode.LaboratoryOccurrences)
        .Any(OutputProjector.IsCultureOccurrence) == true;

    public string CultureWarningText => HasSelectedPatientCultures
        ? CultureReviewTextFormatter.WarningText
        : string.Empty;

    public string CultureReviewText => SemanticPatientBatch is null
        ? "A extração semântica ainda não foi executada."
        : CultureReviewTextFormatter.Format(SemanticPatientBatch, SelectedPatient?.Patient.PatientKey);

    public InputPdfItem? SelectedDocument
    {
        get => _selectedDocument;
        set => SetProperty(ref _selectedDocument, value);
    }

    public PatientItemViewModel? SelectedPatient
    {
        get => _selectedPatient;
        set
        {
            if (SetProperty(ref _selectedPatient, value))
            {
                OnPropertyChanged(nameof(HasSelectedPatientCultures));
                OnPropertyChanged(nameof(CultureWarningText));
                OnPropertyChanged(nameof(CultureReviewText));
                RefreshOutput();
            }
        }
    }

    public StageOptionViewModel? SelectedStage
    {
        get => _selectedStage;
        set
        {
            if (SetProperty(ref _selectedStage, value))
            {
                RefreshOutput();
            }
        }
    }

    public string OutputText
    {
        get => _outputText;
        private set => SetProperty(ref _outputText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string DurationText
    {
        get => _durationText;
        private set => SetProperty(ref _durationText, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var existing = Documents
            .Select(static item => item.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths
                     .Where(static path => path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                     .Select(Path.GetFullPath)
                     .Where(File.Exists))
        {
            if (existing.Add(path))
            {
                Documents.Add(new InputPdfItem(path));
            }
        }

        SelectedDocument ??= Documents.FirstOrDefault();
        StatusText = Documents.Count == 0
            ? "Nenhum PDF válido foi adicionado."
            : $"{Documents.Count} PDF(s) aguardando processamento.";
    }

    public void RemoveSelected()
    {
        if (SelectedDocument is null)
        {
            return;
        }

        Documents.Remove(SelectedDocument);
        SelectedDocument = Documents.FirstOrDefault();
        ClearResults();
    }

    public void Clear()
    {
        Documents.Clear();
        SelectedDocument = null;
        ClearResults();
    }

    public async Task ProcessAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || Documents.Count == 0)
        {
            StatusText = Documents.Count == 0
                ? "Adicione ao menos um PDF antes de processar."
                : StatusText;
            return;
        }

        var requestedStageIds = Stages
            .Where(static stage => stage.IsEnabled)
            .Select(static stage => stage.Id)
            .ToArray();
        if (requestedStageIds.Length == 0)
        {
            StatusText = "Habilite ao menos uma etapa.";
            return;
        }

        var outputProjectionRequested = requestedStageIds.Contains(
            StageIds.OutputProjection,
            StringComparer.Ordinal);
        var derivedRequested = outputProjectionRequested || requestedStageIds.Contains(
            StageIds.DerivedMeasurementComputation,
            StringComparer.Ordinal);
        var semanticRequested = derivedRequested || requestedStageIds.Contains(
            StageIds.LaboratorySemanticExtraction,
            StringComparer.Ordinal);
        var assemblyRequested = semanticRequested || requestedStageIds.Contains(
            StageIds.PatientEpisodeAssembly,
            StringComparer.Ordinal);
        var documentStageIds = requestedStageIds
            .Where(static id => id is not StageIds.PatientEpisodeAssembly and
                                not StageIds.LaboratorySemanticExtraction and
                                not StageIds.DerivedMeasurementComputation and
                                not StageIds.OutputProjection)
            .ToHashSet(StringComparer.Ordinal);
        if (assemblyRequested)
        {
            documentStageIds.Add(StageIds.Sanitization);
        }

        IsBusy = true;
        ProgressPercent = 0d;
        DurationText = string.Empty;
        var runs = new List<DocumentRunRecord>(Documents.Count);
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (var index = 0; index < Documents.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = Documents[index];
                item.Status = "Lendo arquivo...";
                StatusText = $"Processando {item.DisplayName} ({index + 1}/{Documents.Count})...";
                try
                {
                    var bytes = await File.ReadAllBytesAsync(item.FilePath, cancellationToken);
                    item.Status = "Executando etapas...";
                    var execution = await Task.Run(
                        () => _pipeline.ExecuteAsync(
                            new SourcePdf(item.DisplayName, bytes),
                            documentStageIds,
                            cancellationToken),
                        cancellationToken);
                    runs.Add(new DocumentRunRecord(item.DisplayName, execution, null));
                    item.Status = BuildItemStatus(execution);
                }
                catch (Exception exception)
                {
                    // A falha de um arquivo não interrompe o restante do lote.
                    var safeMessage = exception.GetBaseException().Message;
                    runs.Add(new DocumentRunRecord(item.DisplayName, null, safeMessage));
                    item.Status = $"Falha: {safeMessage}";
                }

                ProgressPercent = (index + 1d) / Documents.Count * 100d;
            }

            CompletedRuns = runs;
            AssemblePatients(assemblyRequested, runs);
            ExtractLaboratorySemantics(semanticRequested);
            ComputeDerivedMeasurements(derivedRequested);
            ProjectClinicalOutput(outputProjectionRequested);
        }
        finally
        {
            started.Stop();
            IsBusy = false;
        }

        _lastTotalDuration = started.Elapsed;
        StatusText = $"Concluído: {runs.Count(static run => run.Execution is not null)} sucesso(s), " +
                     $"{runs.Count(static run => run.Execution is null)} falha(s)" +
                     (PatientBatch is null
                         ? "."
                         : $", {PatientBatch.Patients.Count} paciente(s), {PatientBatch.EpisodeCount} episódio(s)" +
                           (SemanticPatientBatch is null
                               ? "."
                               : $", {SemanticPatientBatch.Coverage.OccurrenceCount} ocorrência(s) laboratorial(is)" +
                                 (SemanticPatientBatch.DerivedMeasurementCoverage is null
                                     ? "."
                                     : $", {SemanticPatientBatch.DerivedMeasurementCoverage.ComputedCount} TFG(s) derivada(s).")));
        RefreshOutput();
    }

    private void AssemblePatients(bool requested, IReadOnlyList<DocumentRunRecord> runs)
    {
        Patients.Clear();
        PatientBatch = null;
        _assemblyDuration = TimeSpan.Zero;
        if (!requested)
        {
            SelectedPatient = null;
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var sanitizations = runs
            .Select(static (run, index) => new
            {
                Payload = run.Execution?.Stages.GetValueOrDefault(StageIds.Sanitization)?.Payload,
                InputIndex = index,
            })
            .Where(static item => item.Payload is SanitizedDocument)
            .Select(static item => new PatientAssemblyInput(
                (SanitizedDocument)item.Payload!,
                item.InputIndex))
            .ToArray();
        PatientBatch = PatientEpisodeAssembler.Assemble(sanitizations);
        var failedEntries = runs
            .Select((run, index) => (run, index))
            .Where(static item => item.run.Execution is null)
            .Select(item => new AssemblyLedgerEntry(
                $"failed-{item.index:D4}",
                item.run.FileName,
                item.index,
                "Failed",
                null,
                0,
                0,
                0,
                0,
                item.run.ErrorMessage ?? "Falha sem mensagem disponível."))
            .ToArray();
        if (failedEntries.Length > 0)
        {
            PatientBatch = PatientBatch with
            {
                Ledger = PatientBatch.Ledger.Concat(failedEntries)
                    .OrderBy(static entry => entry.InputIndex)
                    .ToArray(),
            };
        }
        watch.Stop();
        _assemblyDuration = watch.Elapsed;
        foreach (var patient in PatientBatch.Patients)
        {
            Patients.Add(new PatientItemViewModel(patient));
        }

        SelectedPatient = Patients.FirstOrDefault();
    }

    private void ExtractLaboratorySemantics(bool requested)
    {
        SemanticPatientBatch = null;
        _semanticDuration = TimeSpan.Zero;
        if (!requested || PatientBatch is null)
        {
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        SemanticPatientBatch = new LaboratorySemanticExtractor().Extract(
            new LaboratorySemanticExtractionInput(PatientBatch));
        watch.Stop();
        _semanticDuration = watch.Elapsed;
    }

    private void ComputeDerivedMeasurements(bool requested)
    {
        _derivedDuration = TimeSpan.Zero;
        if (!requested || SemanticPatientBatch is null)
        {
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        SemanticPatientBatch = DerivedMeasurementComputer.Enrich(
            new DerivedMeasurementComputationInput(SemanticPatientBatch));
        watch.Stop();
        _derivedDuration = watch.Elapsed;
    }

    private void ProjectClinicalOutput(bool requested)
    {
        ClinicalOutputBatch = null;
        _outputProjectionDuration = TimeSpan.Zero;
        if (!requested || SemanticPatientBatch is null)
        {
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        ClinicalOutputBatch = OutputProjector.Project(new OutputProjectionInput(
            SemanticPatientBatch,
            new OutputProjectionOptions(ShowUnits, ShowCultures)));
        watch.Stop();
        _outputProjectionDuration = watch.Elapsed;
    }

    private SemanticPatient? SelectedSemanticPatient() => SemanticPatientBatch?.Patients.FirstOrDefault(patient =>
        SelectedPatient is null || patient.PatientKey == SelectedPatient.Patient.PatientKey);

    private static string BuildItemStatus(DocumentExecution execution)
    {
        var seconds = execution.Stages.Values.Sum(static stage => stage.Duration.TotalSeconds);
        var warningCount = execution.Stages.Values.Sum(static stage =>
            stage.Notices.Count(static notice => notice.Severity != ProcessingNoticeSeverity.Information));
        return $"Concluído em {seconds.ToString("0.000", CultureInfo.CurrentCulture)} s" +
               (warningCount == 0 ? string.Empty : $" — {warningCount} aviso(s)");
    }

    private void RefreshOutput()
    {
        if (CompletedRuns.Count == 0 || SelectedStage is null)
        {
            return;
        }

        if (string.Equals(SelectedStage.Id, StageIds.LaboratorySemanticExtraction, StringComparison.Ordinal))
        {
            DurationText = $"Etapa: {_semanticDuration.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture)} s | " +
                           $"lote: {_lastTotalDuration.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture)} s";
            OutputText = SemanticPatientBatch is null
                ? "A extração semântica laboratorial não foi executada."
                : LaboratorySemanticTextFormatter.Format(
                    SemanticPatientBatch,
                    SelectedPatient?.Patient.PatientKey);
            return;
        }

        if (string.Equals(SelectedStage.Id, StageIds.DerivedMeasurementComputation, StringComparison.Ordinal))
        {
            DurationText = $"Etapa: {_derivedDuration.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture)} s | " +
                           $"lote: {_lastTotalDuration.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture)} s";
            OutputText = SemanticPatientBatch?.DerivedMeasurementCoverage is null
                ? "O cálculo derivado CKD-EPI 2021 não foi executado."
                : DerivedMeasurementTextFormatter.Format(
                    SemanticPatientBatch,
                    SelectedPatient?.Patient.PatientKey);
            return;
        }

        if (string.Equals(SelectedStage.Id, StageIds.OutputProjection, StringComparison.Ordinal))
        {
            DurationText = $"Etapa: {_outputProjectionDuration.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture)} s | " +
                           $"lote: {_lastTotalDuration.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture)} s";
            OutputText = ClinicalOutputBatch is null
                ? "A projeção clínica final não foi executada."
                : ClinicalOutputTextFormatter.Format(
                    ClinicalOutputBatch,
                    SelectedPatient?.Patient.PatientKey);
            return;
        }

        if (string.Equals(
                SelectedStage.Id,
                StageIds.PatientEpisodeAssembly,
                StringComparison.Ordinal))
        {
            DurationText = $"Etapa: {_assemblyDuration.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture)} s | " +
                           $"lote: {_lastTotalDuration.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture)} s";
            OutputText = PatientBatch is null
                ? "A montagem de pacientes e episódios não foi executada."
                : PatientAssemblyTextFormatter.Format(
                    PatientBatch,
                    SelectedPatient?.Patient.PatientKey);
            return;
        }

        var selectedInputIndexes = SelectedPatient?.Patient.SourceDocuments
            .Select(static document => document.InputIndex)
            .ToHashSet() ?? [];
        var visibleRuns = CompletedRuns
            .Select(static (run, index) => (Run: run, Index: index))
            .Where(item => SelectedPatient is null || selectedInputIndexes.Contains(item.Index))
            .Select(static item => item.Run)
            .ToArray();
        var stageDuration = visibleRuns
            .Where(static run => run.Execution is not null)
            .Select(run => run.Execution!.Stages.GetValueOrDefault(SelectedStage.Id))
            .Where(static result => result is not null)
            .Sum(static result => result!.Duration.TotalSeconds);
        DurationText = $"Etapa: {stageDuration.ToString("0.000", CultureInfo.CurrentCulture)} s | " +
                       $"lote: {_lastTotalDuration.TotalSeconds.ToString("0.000", CultureInfo.CurrentCulture)} s";

        var builder = new StringBuilder();
        foreach (var run in visibleRuns)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine().AppendLine();
            }

            builder.Append("==================== ARQUIVO: ")
                .Append(run.FileName)
                .AppendLine(" ====================");
            if (run.Execution is null)
            {
                builder.Append("FALHA: ").AppendLine(run.ErrorMessage);
                continue;
            }

            if (!run.Execution.Stages.TryGetValue(SelectedStage.Id, out var stage))
            {
                builder.AppendLine("A etapa selecionada não foi executada para este arquivo.");
                continue;
            }

            builder.AppendLine(stage.DisplayText.TrimEnd());
            if (stage.Notices.Count > 0)
            {
                builder.AppendLine().AppendLine("AVISOS DA ETAPA:");
                foreach (var notice in stage.Notices)
                {
                    builder.Append("- ").Append(notice.Code).Append(": ")
                        .AppendLine(notice.Message);
                }
            }
        }

        OutputText = builder.ToString();
    }

    private void ClearResults()
    {
        CompletedRuns = [];
        PatientBatch = null;
        SemanticPatientBatch = null;
        ClinicalOutputBatch = null;
        Patients.Clear();
        SelectedPatient = null;
        OutputText = "Adicione um ou mais PDFs, mantenha as etapas desejadas habilitadas e selecione Processar.";
        DurationText = string.Empty;
        _lastTotalDuration = TimeSpan.Zero;
        _assemblyDuration = TimeSpan.Zero;
        _semanticDuration = TimeSpan.Zero;
        _derivedDuration = TimeSpan.Zero;
        _outputProjectionDuration = TimeSpan.Zero;
        ProgressPercent = 0d;
        StatusText = Documents.Count == 0
            ? "Pronto. Processamento local, offline e sem persistência automática."
            : $"{Documents.Count} PDF(s) aguardando processamento.";
    }
}
