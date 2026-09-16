using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using StayOnTarget.Models;

namespace StayOnTarget.Converters;

public class BillCategoryBucketNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // Expecting:
        // values[0] = PeriodBill (or int BillId)
        // values[1] = Bills collection
        // values[2] = SubCategories collection
        // values[3] = Buckets collection
        if (values.Length < 4) return string.Empty;

        int billId = 0;
        if (values[0] is PeriodBill periodBill)
        {
            billId = periodBill.BillId;
        }
        else if (values[0] is int id)
        {
            billId = id;
        }

        if (billId <= 0) return string.Empty;

        // 1. Locate the parent Bill using BillId
        Bill? parentBill = null;
        if (values[1] is IEnumerable bills)
        {
            parentBill = bills.OfType<Bill>().FirstOrDefault(b => b.Id == billId);
        }

        if (parentBill == null) return string.Empty;

        string subCategoryName = string.Empty;
        string bucketName = string.Empty;

        // 2. Resolve Subcategory Name
        if (parentBill.SubCategoryId.HasValue && values[2] is IEnumerable subCategories)
        {
            var subCat = subCategories.OfType<SubCategory>()
                .FirstOrDefault(s => s.Id == parentBill.SubCategoryId.Value);
            if (subCat != null)
            {
                subCategoryName = subCat.Name;
            }
        }

        // 3. Resolve Bucket Name
        if (parentBill.BucketId.HasValue && values[3] is IEnumerable buckets)
        {
            var bucket = buckets.OfType<BudgetBucket>()
                .FirstOrDefault(b => b.Id == parentBill.BucketId.Value);
            if (bucket != null)
            {
                bucketName = bucket.Name;
            }
        }

        // Format combined string
        if (!string.IsNullOrEmpty(subCategoryName) && !string.IsNullOrEmpty(bucketName))
        {
            return $"{subCategoryName} ({bucketName})";
        }

        return !string.IsNullOrEmpty(subCategoryName) ? subCategoryName : bucketName;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}