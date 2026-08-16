using System.Collections.ObjectModel;
using System.IO;
using ArsExtractum.Core.LaboratorySemantic;
using ArsExtractum.Core.OutputProjection;
using ArsExtractum.Runtime;

namespace ArsExtractum.UserApp.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IProductionSessionProcessor _processor;
    private UserPdfItem? _selectedDocument;
    private UserPatientItem? _selectedPatient;
    private ProductionSessionResult? _session;
    private CancellationTokenSource? _processingCancellation;
    private string _outputText = "";
    private string _statusText = "Adicione PDFs para iniciar.";
    private string _noticeText = "";
    private string _baseNoticeText = "";
    private double _progressPercent;
    private bool _isBusy;
    private bool _showUnits;

    public MainWindowViewModel() : this(new ProductionSessionProcessor())
    {
    }

    public MainWindowViewModel(IProductionSessionProcessor processor) =>
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));

    public ObservableCollection<UserPdfItem> Documents { get; } = [];
    public ObservableCollection<UserPatientItem> Patients { get; } = [];

    public UserPdfItem? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (SetProperty(ref _selectedDocument, value))
            {
                NotifyActions();
            }
        }
    }

    public UserPatientItem? SelectedPatient
    {
        get => _selectedPatient;
        set
        {
            if (SetProperty(ref _selectedPatient, value))
            {
                RefreshCanonicalOutput();
                NotifyPatientState();
                UpdateNoticeText();
            }
        }
    }

    public string OutputText
    {
        get => _outputText;
        set
        {
            if (SetProperty(ref _outputText, value))
            {
                OnPropertyChanged(nameof(CanCopyOutput));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string NoticeText
    {
        get => _noticeText;
        private set
        {
            if (SetProperty(ref _noticeText, value))
            {
                OnPropertyChanged(nameof(HasNotices));
            }
        }
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                NotifyActions();
            }
        }
    }

    public bool IsIdle => !IsBusy;
    public bool HasNotices => !string.IsNullOrWhiteSpace(NoticeText);
    public bool CanProcess => IsIdle && Documents.Count > 0;
    public bool CanRemoveDocument => IsIdle && SelectedDocument is not null;
    public bool CanClearSession => IsIdle && (Documents.Count > 0 || Patients.Count > 0);
    public bool CanCopyOutput => !string.IsNullOrWhiteSpace(OutputText);
    public bool CanOpenCurves => SelectedPatient is not null &&
        _session?.SemanticPatientBatch.DerivedMeasurementCoverage?.IsComplete == true;
    public bool HasSelectedPatientCultures => SelectedSemanticPatient()?.Episodes
        .SelectMany(static episode => episode.LaboratoryOccurrences)
        .Any(OutputProjector.IsCultureOccurrence) == true;
    public bool CanReviewCultures => HasSelectedPatientCultures;
    public string CultureReviewText => _session is null
        ? "Nenhum resultado foi processado."
        : CultureReviewTextFormatter.Format(_session.SemanticPatientBatch, SelectedPatient?.Patient.PatientKey);
    public SemanticPatientBatch? SemanticPatientBatch => _session?.SemanticPatientBatch;
    public string? SelectedPatientKey => SelectedPatient?.Patient.PatientKey;
    public string? SelectedPatientName => SelectedPatient?.DisplayName;

    public bool ShowUnits
    {
        get => _showUnits;
        set
        {
            if (SetProperty(ref _showUnits, value))
            {
                ReprojectClinicalOutput();
            }
        }
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (IsBusy)
        {
            return;
        }

        var existing = Documents.Select(static document => document.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalidCount = 0;
        var duplicateCount = 0;
        var addedCount = 0;
        foreach (var candidate in paths)
        {
            if (string.IsNullOrWhiteSpace(candidate) ||
                !candidate.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(candidate))
            {
                invalidCount++;
                continue;
            }

            var fullPath = Path.GetFullPath(candidate);
            if (!existing.Add(fullPath))
            {
                duplicateCount++;
                continue;
            }

            Documents.Add(new UserPdfItem(fullPath));
            addedCount++;
        }

        if (addedCount > 0)
        {
            ClearResults();
            SelectedDocument ??= Documents.FirstOrDefault();
        }

        _baseNoticeText = BuildInputNotice(invalidCount, duplicateCount);
        UpdateNoticeText();
        StatusText = Documents.Count == 0
            ? "Nenhum PDF válido foi adicionado."
            : $"{Documents.Count} PDF(s) pronto(s) para processar.";
        NotifyActions();
    }

    public void RemoveSelected()
    {
        if (!CanRemoveDocument || SelectedDocument is null)
        {
            return;
        }

        var index = Documents.IndexOf(SelectedDocument);
        Documents.Remove(SelectedDocument);
        SelectedDocument = Documents.Count == 0
            ? null
            : Documents[Math.Min(index, Documents.Count - 1)];
        ClearResults();
        StatusText = Documents.Count == 0
            ? "Adicione PDFs para iniciar."
            : $"{Documents.Count} PDF(s) pronto(s) para processar.";
        NotifyActions();
    }

    public void ClearSession()
    {
        if (IsBusy)
        {
            return;
        }

        Documents.Clear();
        SelectedDocument = null;
        ClearResults();
        _baseNoticeText = "";
        UpdateNoticeText();
        StatusText = "Adicione PDFs para iniciar.";
        ProgressPercent = 0d;
        NotifyActions();
    }

    public async Task ProcessAsync()
    {
        if (!CanProcess)
        {
            return;
        }

        ClearResults();
        _baseNoticeText = "";
        UpdateNoticeText();
        IsBusy = true;
        ProgressPercent = 0d;
        foreach (var document in Documents)
        {
            document.Status = "Aguardando";
        }

        var cancellation = new CancellationTokenSource();
        _processingCancellation = cancellation;
        var progress = new Progress<ProductionProgress>(update =>
        {
            ProgressPercent = update.Percent;
            StatusText = update.Message;
            var current = Documents.FirstOrDefault(document =>
                string.Equals(document.FilePath, update.CurrentFilePath, StringComparison.OrdinalIgnoreCase));
            if (current is not null)
            {
                current.Status = update.IsCurrentDocumentComplete ? "Concluído" : "Processando...";
            }
        });

        try
        {
            _session = await _processor.ProcessAsync(
                Documents.Select(static document => document.FilePath).ToArray(),
                new OutputProjectionOptions(ShowUnits, false),
                progress,
                cancellation.Token);
            foreach (var result in _session.Documents)
            {
                var item = Documents.First(document =>
                    string.Equals(document.FilePath, result.FilePath, StringComparison.OrdinalIgnoreCase));
                item.Status = result.Succeeded ? "Concluído" : "Falha";
            }

            foreach (var patient in _session.PatientBatch.Patients)
            {
                Patients.Add(new UserPatientItem(patient));
            }

            SelectedPatient = Patients.FirstOrDefault();
            var failed = _session.Documents.Where(static document => !document.Succeeded).ToArray();
            _baseNoticeText = failed.Length == 0
                ? ""
                : $"{failed.Length} PDF(s) não puderam ser processados. Revise os arquivos marcados como falha.";
            UpdateNoticeText();
            StatusText = $"Concluído: {Patients.Count} paciente(s), {_session.PatientBatch.EpisodeCount} episódio(s).";
            ProgressPercent = 100d;
        }
        catch (OperationCanceledException)
        {
            ClearResults();
            foreach (var document in Documents)
            {
                document.Status = "Pronto para processar";
            }
            StatusText = "Processamento cancelado. A sessão está pronta para uma nova execução.";
            _baseNoticeText = "O processamento foi cancelado; nenhum resultado parcial foi apresentado.";
            UpdateNoticeText();
            ProgressPercent = 0d;
        }
        catch (Exception exception)
        {
            ClearResults();
            StatusText = "Não foi possível concluir o processamento.";
            _baseNoticeText = exception.GetBaseException().Message;
            UpdateNoticeText();
            ProgressPercent = 0d;
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(_processingCancellation, cancellation))
            {
                _processingCancellation = null;
            }
            IsBusy = false;
            NotifyPatientState();
        }
    }

    public void CancelProcessing() => _processingCancellation?.Cancel();

    public void Dispose()
    {
        _processingCancellation?.Cancel();
        _processingCancellation?.Dispose();
        _processingCancellation = null;
        GC.SuppressFinalize(this);
    }

    private void ReprojectClinicalOutput()
    {
        if (_session is null)
        {
            return;
        }

        var clinical = ProductionRuntime.ProjectClinicalOutput(
            _session.SemanticPatientBatch,
            new OutputProjectionOptions(ShowUnits, false));
        _session = _session with { ClinicalOutputBatch = clinical };
        RefreshCanonicalOutput();
    }

    private void RefreshCanonicalOutput()
    {
        OutputText = _session is null || SelectedPatient is null
            ? ""
            : ClinicalOutputTextFormatter.Format(
                _session.ClinicalOutputBatch,
                SelectedPatient.Patient.PatientKey);
    }

    private SemanticPatient? SelectedSemanticPatient() => _session?.SemanticPatientBatch.Patients
        .FirstOrDefault(patient => patient.PatientKey == SelectedPatient?.Patient.PatientKey);

    private void ClearResults()
    {
        _session = null;
        foreach (var document in Documents)
        {
            document.Status = "Pronto para processar";
        }
        Patients.Clear();
        SelectedPatient = null;
        OutputText = "";
        NotifyPatientState();
    }

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CanProcess));
        OnPropertyChanged(nameof(CanRemoveDocument));
        OnPropertyChanged(nameof(CanClearSession));
    }

    private void NotifyPatientState()
    {
        OnPropertyChanged(nameof(CanOpenCurves));
        OnPropertyChanged(nameof(HasSelectedPatientCultures));
        OnPropertyChanged(nameof(CanReviewCultures));
        OnPropertyChanged(nameof(CultureReviewText));
        OnPropertyChanged(nameof(SemanticPatientBatch));
        OnPropertyChanged(nameof(SelectedPatientKey));
        OnPropertyChanged(nameof(SelectedPatientName));
        OnPropertyChanged(nameof(CanCopyOutput));
    }

    private void UpdateNoticeText()
    {
        var notices = new List<string>();
        if (!string.IsNullOrWhiteSpace(_baseNoticeText))
        {
            notices.Add(_baseNoticeText);
        }
        if (HasSelectedPatientCultures)
        {
            notices.Add(CultureReviewTextFormatter.WarningText);
        }
        NoticeText = string.Join(Environment.NewLine, notices);
    }

    private static string BuildInputNotice(int invalidCount, int duplicateCount)
    {
        var messages = new List<string>();
        if (invalidCount > 0)
        {
            messages.Add($"{invalidCount} arquivo(s) ignorado(s): selecione somente PDFs existentes.");
        }
        if (duplicateCount > 0)
        {
            messages.Add($"{duplicateCount} PDF(s) já constavam na sessão.");
        }
        return string.Join(Environment.NewLine, messages);
    }
}
