using System.Collections.ObjectModel;
using System.Globalization;
using ArsExtractum.Core.LaboratoryCurves;
using ArsExtractum.Core.LaboratorySemantic;

namespace ArsExtractum.UserApp.ViewModels;

public sealed class LaboratoryCurvesViewModel : ObservableObject
{
    private readonly SemanticPatientBatch _batch;
    private readonly string _patientKey;
    private CurveFilterChoice _selectedFilter;
    private DateTime? _startDate;
    private DateTime? _endDate;
    private string _lastDaysText = "7";
    private bool _includeDelta;
    private string _outputText = "Selecione uma ou mais curvas e gere o texto.";
    private string _validationMessage = "";

    public LaboratoryCurvesViewModel(
        SemanticPatientBatch batch,
        string patientKey,
        string patientDisplayName,
        DateOnly? currentDate = null)
    {
        _batch = batch;
        _patientKey = patientKey;
        PatientDisplayName = patientDisplayName;
        CurrentDate = currentDate ?? DateOnly.FromDateTime(DateTime.Today);
        Filters =
        [
            new CurveFilterChoice(LaboratoryCurveFilterMode.All, "Todos"),
            new CurveFilterChoice(LaboratoryCurveFilterMode.CustomRange, "Intervalo personalizado"),
            new CurveFilterChoice(LaboratoryCurveFilterMode.LastDays, "Últimos X dias"),
        ];
        _selectedFilter = Filters[0];
        Options = new ObservableCollection<LaboratoryCurveOptionViewModel>(
            LaboratoryCurveProjector.AvailableOptions(batch, patientKey)
                .Select(static option => new LaboratoryCurveOptionViewModel(option)));
    }

    public string PatientDisplayName { get; }
    public DateOnly CurrentDate { get; }
    public ObservableCollection<LaboratoryCurveOptionViewModel> Options { get; }
    public IReadOnlyList<CurveFilterChoice> Filters { get; }

    public CurveFilterChoice SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetProperty(ref _selectedFilter, value))
            {
                OnPropertyChanged(nameof(UsesCustomRange));
                OnPropertyChanged(nameof(UsesLastDays));
                OnPropertyChanged(nameof(IsAllFilter));
                OnPropertyChanged(nameof(IsCustomRangeFilter));
                OnPropertyChanged(nameof(IsLastDaysFilter));
            }
        }
    }

    public bool IsAllFilter
    {
        get => SelectedFilter.Mode == LaboratoryCurveFilterMode.All;
        set
        {
            if (value)
            {
                SelectedFilter = Filters.Single(static filter => filter.Mode == LaboratoryCurveFilterMode.All);
            }
        }
    }

    public bool IsCustomRangeFilter
    {
        get => SelectedFilter.Mode == LaboratoryCurveFilterMode.CustomRange;
        set
        {
            if (value)
            {
                SelectedFilter = Filters.Single(static filter => filter.Mode == LaboratoryCurveFilterMode.CustomRange);
            }
        }
    }

    public bool IsLastDaysFilter
    {
        get => SelectedFilter.Mode == LaboratoryCurveFilterMode.LastDays;
        set
        {
            if (value)
            {
                SelectedFilter = Filters.Single(static filter => filter.Mode == LaboratoryCurveFilterMode.LastDays);
            }
        }
    }

    public bool UsesCustomRange => SelectedFilter.Mode == LaboratoryCurveFilterMode.CustomRange;
    public bool UsesLastDays => SelectedFilter.Mode == LaboratoryCurveFilterMode.LastDays;

    public DateTime? StartDate
    {
        get => _startDate;
        set => SetProperty(ref _startDate, value);
    }

    public DateTime? EndDate
    {
        get => _endDate;
        set => SetProperty(ref _endDate, value);
    }

    public string LastDaysText
    {
        get => _lastDaysText;
        set => SetProperty(ref _lastDaysText, value);
    }

    public bool IncludeDelta
    {
        get => _includeDelta;
        set => SetProperty(ref _includeDelta, value);
    }

    public string OutputText
    {
        get => _outputText;
        private set => SetProperty(ref _outputText, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool Generate()
    {
        var selected = Options.Where(static option => option.IsSelected)
            .Select(static option => option.Key)
            .ToArray();
        if (selected.Length == 0)
        {
            ValidationMessage = "Selecione ao menos uma curva.";
            return false;
        }

        if (!TryBuildFilter(out var filter, out var message))
        {
            ValidationMessage = message;
            return false;
        }

        try
        {
            var projection = LaboratoryCurveProjector.Project(new LaboratoryCurveProjectionInput(
                _batch, _patientKey, selected, filter!, IncludeDelta, CurrentDate));
            OutputText = LaboratoryCurveTextFormatter.Format(projection, IncludeDelta);
            ValidationMessage = projection.Series.Count == 0
                ? "Nenhum resultado elegível foi encontrado no período selecionado."
                : "";
            return projection.Series.Count > 0;
        }
        catch (ArgumentException exception)
        {
            ValidationMessage = exception.Message;
            return false;
        }
    }

    private bool TryBuildFilter(out LaboratoryCurveFilter? filter, out string message)
    {
        filter = null;
        message = "";
        if (SelectedFilter.Mode == LaboratoryCurveFilterMode.All)
        {
            filter = new LaboratoryCurveFilter(LaboratoryCurveFilterMode.All);
            return true;
        }

        if (SelectedFilter.Mode == LaboratoryCurveFilterMode.CustomRange)
        {
            if (StartDate is null || EndDate is null || StartDate.Value.Date > EndDate.Value.Date)
            {
                message = "Informe um intervalo de datas válido.";
                return false;
            }

            filter = new LaboratoryCurveFilter(
                LaboratoryCurveFilterMode.CustomRange,
                DateOnly.FromDateTime(StartDate.Value),
                DateOnly.FromDateTime(EndDate.Value));
            return true;
        }

        if (!int.TryParse(LastDaysText, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days <= 0)
        {
            message = "Informe uma quantidade positiva de dias.";
            return false;
        }

        filter = new LaboratoryCurveFilter(LaboratoryCurveFilterMode.LastDays, LastDays: days);
        return true;
    }
}

public sealed class LaboratoryCurveOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public LaboratoryCurveOptionViewModel(LaboratoryCurveOption option)
    {
        Key = option.Key;
        DisplayName = option.DisplayName;
    }

    public string Key { get; }
    public string DisplayName { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed record CurveFilterChoice(LaboratoryCurveFilterMode Mode, string DisplayName);
