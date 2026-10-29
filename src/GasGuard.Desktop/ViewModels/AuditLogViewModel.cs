using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GasGuard.Application.Abstractions;
using GasGuard.Application.Models;
using GasGuard.Domain;
using Microsoft.Extensions.Logging;

namespace GasGuard.Desktop.ViewModels;

public sealed partial class AuditLogViewModel : PageViewModelBase
{
    private const int PageSize = 50;
    private readonly IAuditLogService _audit;
    private readonly IClock _clock;

    public AuditLogViewModel(
        IAuditLogService audit,
        IClock clock,
        ILogger<AuditLogViewModel> logger) : base(logger)
    {
        _audit = audit;
        _clock = clock;
        var today = clock.ToLagosDate(clock.UtcNow);
        _fromDateText = today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _toDateText = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        OutcomeOptions =
        [
            new("All outcomes", null),
            new("Success", AuditOutcome.Success),
            new("Failure", AuditOutcome.Failure),
            new("Denied", AuditOutcome.Denied)
        ];
        _selectedOutcome = OutcomeOptions[0];
    }

    public override string Title => "Audit Log";

    public ObservableCollection<AuditEntryItem> Entries { get; } = [];
    public IReadOnlyList<ChoiceOption<AuditOutcome?>> OutcomeOptions { get; }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _actionFilter = string.Empty;
    [ObservableProperty] private string _fromDateText;
    [ObservableProperty] private string _toDateText;
    [ObservableProperty] private ChoiceOption<AuditOutcome?> _selectedOutcome;
    [ObservableProperty] private bool _hasEntries;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalPages = 1;
    [ObservableProperty] private string _pageText = "Page 1 of 1";
    [ObservableProperty] private bool _canGoPrevious;
    [ObservableProperty] private bool _canGoNext;

    protected override async Task LoadAsync()
    {
        if (!TryGetDates(out var fromUtc, out var toUtc, out var error))
        {
            SetError(error);
            return;
        }

        var result = await _audit.QueryAsync(new AuditQuery(
            SearchText,
            fromUtc,
            toUtc,
            ActionFilter,
            SelectedOutcome.Value,
            Page,
            PageSize));

        Entries.Clear();
        foreach (var entry in result.Items)
        {
            Entries.Add(entry);
        }

        HasEntries = Entries.Count > 0;
        TotalPages = result.TotalPages;
        CanGoPrevious = result.HasPrevious;
        CanGoNext = result.HasNext;
        PageText = $"Page {result.Page} of {result.TotalPages} · {result.TotalCount} entries";
        SetError(null);
        SetStatus(null);
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        Page = 1;
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        Page = Math.Max(1, Page - 1);
        await ActivateAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (!CanGoNext)
        {
            return;
        }

        Page = Math.Min(TotalPages, Page + 1);
        await ActivateAsync();
    }

    private bool TryGetDates(out DateTime? fromUtc, out DateTime? toUtc, out string? error)
    {
        fromUtc = null;
        toUtc = null;
        error = null;

        if (!string.IsNullOrWhiteSpace(FromDateText))
        {
            if (!DateOnly.TryParseExact(FromDateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate))
            {
                error = "From date must use yyyy-MM-dd.";
                return false;
            }

            fromUtc = _clock.LagosToUtc(fromDate.ToDateTime(TimeOnly.MinValue));
        }

        if (!string.IsNullOrWhiteSpace(ToDateText))
        {
            if (!DateOnly.TryParseExact(ToDateText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate))
            {
                error = "To date must use yyyy-MM-dd.";
                return false;
            }

            toUtc = _clock.LagosToUtc(toDate.AddDays(1).ToDateTime(TimeOnly.MinValue));
        }

        if (fromUtc.HasValue && toUtc.HasValue && toUtc <= fromUtc)
        {
            error = "The end date must be on or after the start date.";
            return false;
        }

        return true;
    }
}
