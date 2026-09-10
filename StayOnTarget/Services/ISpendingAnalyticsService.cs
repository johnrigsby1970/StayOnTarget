using StayOnTarget.Models;

namespace StayOnTarget.Services;

public interface ISpendingAnalyticsService
{
    Task<AnalyticsMetrics> GetPayeeAnalyticsAsync(string normalizedPayeeName, int monthsHistory = 24);
    Task<AnalyticsMetrics> GetSubCategoryAnalyticsAsync(int subCategoryId, int monthsHistory = 24);
    Task<AnalyticsMetrics> GetBucketAnalyticsAsync(int bucketId, int monthsHistory = 24);
    Task<AnalyticsMetrics> GetBillAnalyticsAsync(int billId, int monthsHistory = 24);
}