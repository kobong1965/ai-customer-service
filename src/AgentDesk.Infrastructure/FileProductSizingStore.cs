using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentDesk.Core;

namespace AgentDesk.Infrastructure;

public sealed class FileProductSizingStore : IProductSizingProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _sync = new();
    private readonly string _filePath;

    public FileProductSizingStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentDesk",
            "product-sizing.json");
    }

    public IReadOnlyList<ProductSizingProfile> LoadAll()
    {
        lock (_sync)
        {
            return LoadUnsafe();
        }
    }

    public ProductSizingProfile AddReviewed(
        string productUrl,
        string productKey,
        string category,
        string fit,
        string variant,
        string accountScope,
        string measurementGuide,
        IReadOnlyList<SizeRecommendationRow> rows)
    {
        var candidate = Create(
            $"size:local:{Guid.NewGuid():N}",
            productUrl,
            productKey,
            category,
            fit,
            variant,
            accountScope,
            measurementGuide,
            rows);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            EnsureNotDuplicate(items, candidate);
            items.Insert(0, candidate);
            SaveUnsafe(items);
        }

        return candidate;
    }

    public ProductSizingProfile UpdateReviewed(
        string id,
        string productUrl,
        string productKey,
        string category,
        string fit,
        string variant,
        string accountScope,
        string measurementGuide,
        IReadOnlyList<SizeRecommendationRow> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var candidate = Create(
            id,
            productUrl,
            productKey,
            category,
            fit,
            variant,
            accountScope,
            measurementGuide,
            rows);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = items.FindIndex(item => item.Id.Equals(id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException("商品尺码规则不存在。");
            }

            EnsureNotDuplicate(items, candidate, id);
            candidate = candidate with { IsEnabled = items[index].IsEnabled };
            items[index] = candidate;
            SaveUnsafe(items);
            return candidate;
        }
    }

    public ProductSizingProfile ToggleEnabled(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var index = items.FindIndex(item => item.Id.Equals(id, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidOperationException("商品尺码规则不存在。");
            }

            var updated = items[index] with
            {
                IsEnabled = !items[index].IsEnabled,
                UpdatedAt = DateTimeOffset.Now
            };
            items[index] = updated;
            SaveUnsafe(items);
            return updated;
        }
    }

    public void Delete(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            if (items.RemoveAll(item => item.Id.Equals(id, StringComparison.Ordinal)) == 0)
            {
                throw new InvalidOperationException("商品尺码规则不存在。");
            }

            SaveUnsafe(items);
        }
    }

    public string ExportJson()
    {
        lock (_sync)
        {
            return JsonSerializer.Serialize(LoadUnsafe(), JsonOptions);
        }
    }

    public int ImportReviewed(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ProductSizingProfile[] incoming;
        try
        {
            incoming = JsonSerializer.Deserialize<ProductSizingProfile[]>(json, JsonOptions)
                ?? throw new InvalidOperationException("导入文件不包含商品尺码规则。");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("商品尺码文件不是有效 JSON。", exception);
        }

        lock (_sync)
        {
            var items = LoadUnsafe().ToList();
            var added = 0;
            foreach (var item in incoming.Where(item => item.IsReviewed))
            {
                ProductSizingProfile candidate;
                try
                {
                    candidate = Create(
                        $"size:local:{Guid.NewGuid():N}",
                        item.ProductUrl,
                        item.ProductKey,
                        item.Category,
                        item.Fit,
                        item.Variant,
                        item.AccountScope,
                        item.MeasurementGuide,
                        item.Rows);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    continue;
                }

                if (items.Any(existing => IsDuplicate(existing, candidate)))
                {
                    continue;
                }

                items.Insert(0, candidate);
                added++;
            }

            if (added > 0)
            {
                SaveUnsafe(items);
            }

            return added;
        }
    }

    public Task<IReadOnlyList<ProductSizingProfile>> FindAsync(
        string productKey,
        string query,
        string accountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedKey = NormalizeKey(productKey);
        var normalizedQuery = NormalizeKey(query);
        IReadOnlyList<ProductSizingProfile> result;
        lock (_sync)
        {
            var eligible = LoadUnsafe()
                .Where(item => item.IsReviewed && item.IsEnabled)
                .Where(item => MatchesAccount(item.AccountScope, accountId));
            var matches = eligible.Where(item =>
            {
                var itemKey = NormalizeKey(item.ProductKey);
                var itemUrl = NormalizeKey(item.ProductUrl);
                if (normalizedKey.Length > 0)
                {
                    return normalizedKey.Equals(itemKey, StringComparison.OrdinalIgnoreCase)
                        || normalizedKey.Equals(itemUrl, StringComparison.OrdinalIgnoreCase)
                        || (itemKey.Length >= 4 && normalizedKey.Contains(itemKey, StringComparison.OrdinalIgnoreCase));
                }

                return (itemKey.Length >= 4 && normalizedQuery.Contains(itemKey, StringComparison.OrdinalIgnoreCase))
                    || (itemUrl.Length >= 8 && normalizedQuery.Contains(itemUrl, StringComparison.OrdinalIgnoreCase));
            });
            var matchedItems = matches.ToArray();
            var variantSignal = normalizedKey + normalizedQuery;
            var explicitVariantMatches = matchedItems
                .Where(item => variantSignal.Contains(
                    NormalizeKey(item.Variant),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var selected = explicitVariantMatches.Length > 0 ? explicitVariantMatches : matchedItems;
            result = selected
                .OrderByDescending(item => normalizedQuery.Contains(
                    NormalizeKey(item.Variant),
                    StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(item => item.UpdatedAt)
                .Take(8)
                .ToArray();
        }

        return Task.FromResult(result);
    }

    public string ComputeFingerprint()
    {
        lock (_sync)
        {
            var material = JsonSerializer.Serialize(
                LoadUnsafe()
                    .Where(item => item.IsReviewed && item.IsEnabled)
                    .OrderBy(item => item.Id, StringComparer.Ordinal)
                    .ToArray(),
                JsonOptions);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
        }
    }

    public static string NormalizeUrl(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("商品链接必须是有效的 HTTP 或 HTTPS 地址。");
        }

        var builder = new UriBuilder(uri) { Fragment = string.Empty };
        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static ProductSizingProfile Create(
        string id,
        string productUrl,
        string productKey,
        string category,
        string fit,
        string variant,
        string accountScope,
        string measurementGuide,
        IReadOnlyList<SizeRecommendationRow> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(fit);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant);
        ArgumentNullException.ThrowIfNull(rows);
        var normalizedRows = rows.Select(NormalizeRow).ToArray();
        if (normalizedRows.Length is < 1 or > 40)
        {
            throw new InvalidOperationException("每套尺码规则需包含 1–40 行。");
        }

        ValidateLength(productKey, 2, 200, "商品标识/SKU");
        ValidateLength(category, 1, 40, "品类");
        ValidateLength(fit, 1, 40, "版型");
        ValidateLength(variant, 1, 40, "版本");
        ValidateLength(string.IsNullOrWhiteSpace(accountScope) ? "全部账号" : accountScope, 1, 80, "适用账号");
        if (!string.IsNullOrWhiteSpace(measurementGuide) && measurementGuide.Trim().Length > 500)
        {
            throw new InvalidOperationException("测量提示不能超过 500 个字符。");
        }

        if (normalizedRows.GroupBy(row => row.Size, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new InvalidOperationException("同一套规则内的尺码名称不能重复。");
        }

        return new ProductSizingProfile(
            id,
            NormalizeUrl(productUrl),
            productKey.Trim(),
            category.Trim(),
            fit.Trim(),
            variant.Trim(),
            string.IsNullOrWhiteSpace(accountScope) ? "全部账号" : accountScope.Trim(),
            measurementGuide?.Trim() ?? string.Empty,
            normalizedRows,
            true,
            true,
            DateTimeOffset.Now);
    }

    private static SizeRecommendationRow NormalizeRow(SizeRecommendationRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentException.ThrowIfNullOrWhiteSpace(row.Size);
        ValidateLength(row.Size, 1, 20, "尺码名称");
        ValidateRange(row.MinHeightCm, row.MaxHeightCm, 50, 250, "身高");
        ValidateRange(row.MinWeightKg, row.MaxWeightKg, 10, 300, "体重");
        ValidateRange(row.MinWaistCm, row.MaxWaistCm, 20, 250, "腰围");
        ValidateRange(row.MinBustCm, row.MaxBustCm, 20, 250, "胸围");
        if (!string.IsNullOrWhiteSpace(row.Notes) && row.Notes.Trim().Length > 200)
        {
            throw new InvalidOperationException("尺码备注不能超过 200 个字符。");
        }

        if (row.MinHeightCm is null && row.MaxHeightCm is null
            && row.MinWeightKg is null && row.MaxWeightKg is null
            && row.MinWaistCm is null && row.MaxWaistCm is null
            && row.MinBustCm is null && row.MaxBustCm is null)
        {
            throw new InvalidOperationException($"尺码 {row.Size.Trim()} 至少需要填写一个测量范围。");
        }

        return row with { Size = row.Size.Trim(), Notes = row.Notes?.Trim() ?? string.Empty };
    }

    private IReadOnlyList<ProductSizingProfile> LoadUnsafe()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var items = JsonSerializer.Deserialize<ProductSizingProfile?[]>(File.ReadAllText(_filePath), JsonOptions) ?? [];
            return items
                .Where(item => item is not null
                    && item.Rows is not null
                    && !string.IsNullOrWhiteSpace(item.ProductUrl)
                    && !string.IsNullOrWhiteSpace(item.ProductKey)
                    && !string.IsNullOrWhiteSpace(item.Category)
                    && !string.IsNullOrWhiteSpace(item.Fit)
                    && !string.IsNullOrWhiteSpace(item.Variant))
                .Cast<ProductSizingProfile>()
                .ToArray();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return [];
        }
    }

    private void SaveUnsafe(IReadOnlyList<ProductSizingProfile> items)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("商品尺码规则路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(items, JsonOptions));
        File.Move(temporaryPath, _filePath, true);
    }

    private static void EnsureNotDuplicate(
        IEnumerable<ProductSizingProfile> items,
        ProductSizingProfile candidate,
        string? exceptId = null)
    {
        if (items.Any(item => !item.Id.Equals(exceptId, StringComparison.Ordinal)
            && IsDuplicate(item, candidate)))
        {
            throw new InvalidOperationException("相同商品、版本和账号范围的尺码规则已经存在。");
        }
    }

    private static bool IsDuplicate(ProductSizingProfile left, ProductSizingProfile right) =>
        NormalizeKey(left.ProductUrl).Equals(NormalizeKey(right.ProductUrl), StringComparison.OrdinalIgnoreCase)
        && NormalizeKey(left.ProductKey).Equals(NormalizeKey(right.ProductKey), StringComparison.OrdinalIgnoreCase)
        && NormalizeKey(left.Variant).Equals(NormalizeKey(right.Variant), StringComparison.OrdinalIgnoreCase)
        && NormalizeKey(left.AccountScope).Equals(NormalizeKey(right.AccountScope), StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAccount(string scope, string accountId) =>
        scope.Equals("全部账号", StringComparison.OrdinalIgnoreCase)
        || scope.Equals(accountId, StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(accountId)
            && accountId.Contains(scope, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeKey(string? value) => string.Concat(
        (value ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character)))
        .Trim()
        .TrimEnd('/');

    private static void ValidateLength(string value, int minimum, int maximum, string field)
    {
        var length = value.Trim().Length;
        if (length < minimum || length > maximum)
        {
            throw new InvalidOperationException($"{field}需为 {minimum}–{maximum} 个字符。");
        }
    }

    private static void ValidateRange(
        double? minimum,
        double? maximum,
        double allowedMinimum,
        double allowedMaximum,
        string field)
    {
        if (minimum is not null && (minimum < allowedMinimum || minimum > allowedMaximum)
            || maximum is not null && (maximum < allowedMinimum || maximum > allowedMaximum))
        {
            throw new InvalidOperationException($"{field}范围超出合理区间。");
        }

        if (minimum is not null && maximum is not null && minimum > maximum)
        {
            throw new InvalidOperationException($"{field}下限不能大于上限。");
        }
    }
}
