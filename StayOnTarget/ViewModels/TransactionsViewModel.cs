using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StayOnTarget.Models;
using StayOnTarget.Services;

namespace StayOnTarget.ViewModels;

public partial class TransactionsViewModel : ObservableObject
{
    private readonly ISpendingAnalyticsService _analyticsService;

    [ObservableProperty]
    private AnalyticsMetrics? _hoveredPayeeMetrics;

    [ObservableProperty]
    private bool _isLoadingMetrics;

    public TransactionsViewModel(ISpendingAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    [RelayCommand]
    private async Task OnTransactionRowHoveredAsync(TransactionViewModel? transaction)
    {
        if (transaction == null || string.IsNullOrWhiteSpace(transaction.NormalizedDescription))
        {
            HoveredPayeeMetrics = null;
            return;
        }

        // Avoid re-fetching if we're already inspecting the same payee
        if (HoveredPayeeMetrics != null && 
            Enumerable.FirstOrDefault<MonthlySpendPoint>(HoveredPayeeMetrics.MonthlyHistory)?.YearMonth == transaction.NormalizedDescription)
        {
            return;
        }

        IsLoadingMetrics = true;
        
        // Fetch 24-month analytics for the hovered payee
        HoveredPayeeMetrics = await _analyticsService.GetPayeeAnalyticsAsync(transaction.NormalizedDescription);
        
        IsLoadingMetrics = false;
    }
}