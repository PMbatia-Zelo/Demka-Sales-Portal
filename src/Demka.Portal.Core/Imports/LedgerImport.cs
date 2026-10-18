using System.Text.RegularExpressions;

namespace Demka.Portal.Core.Imports;

/// <summary>One invoice or credit memo line from a Business Central Customer Ledger Entries export.</summary>
public sealed record LedgerLine(
    int RowNumber,
    DateOnly PostingDate,
    string CustomerNo,
    string CustomerName,
    string NameKey,
    string PostingGroup,
    decimal TaxableInclVat);

public sealed record LedgerRejection(int RowNumber, string Reason);

/// <summary>Where a customer + branch name goes. OutletId null with Ignore = true means "not Modern Trade, skip".</summary>
public sealed record MappingTarget(int? OutletId, bool Ignore, string Source);

public sealed record OutletMonthAmount(int OutletId, DateOnly Month, decimal Amount, int Lines);

public sealed record UnmappedName(string CustomerNo, string NameKey, string CustomerName, string PostingGroup, DateOnly Month, decimal Amount, int Lines);

public sealed record NewMapping(string CustomerNo, string NameKey, int OutletId, string Source);

public sealed class LedgerParseResult
{
    public int RowsRead { get; set; }
    public int LinesUsed { get; set; }
    public int OtherDocumentTypes { get; set; }
    public List<LedgerLine> Lines { get; } = [];
    public List<LedgerRejection> Rejected { get; } = [];
    public DateOnly? FirstDate { get; set; }
    public DateOnly? LastDate { get; set; }
}

public sealed class LedgerAllocation
{
    public List<OutletMonthAmount> Outlets { get; } = [];
    public List<UnmappedName> Unmapped { get; } = [];
    public List<NewMapping> NewMappings { get; } = [];
    public decimal MappedAmount { get; set; }
    public decimal IgnoredAmount { get; set; }
    public decimal UnmappedAmount { get; set; }
}

/// <summary>
/// The Modern Trade report method, applied to a ledger export:
/// <list type="bullet">
/// <item>Only invoices and credit memos count.</item>
/// <item>Taxable value incl. VAT = (Amount − Sales (LCY)) × (1 + VAT) ÷ VAT, i.e. the line's VAT grossed up.
///   Fresh milk is VAT-exempt, so it drops out on its own.</item>
/// <item>The outlet is named by "Invoice Customer Name" (the branch on head-office accounts); lines without one use the account name.</item>
/// <item>Names map to outlets by: saved mapping → exact outlet name (also without a trailing " CHEESE") → the account's only outlet.</item>
/// </list>
/// </summary>
public static class LedgerImport
{
    public static readonly string[] RequiredColumns =
        ["Posting Date", "Document Type", "Customer No.", "Customer Name Custom", "Invoice Customer Name", "Amount", "Sales (LCY)"];

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static string NormalizeName(string? s) => Spaces.Replace((s ?? "").Trim().ToUpperInvariant(), " ");

    /// <param name="rows">All rows of the file, header first.</param>
    /// <param name="latestAllowed">Lines dated after this (e.g. today) are rejected as not yet valid.</param>
    public static LedgerParseResult Parse(IEnumerable<string?[]> rows, decimal vatRate, DateOnly latestAllowed)
    {
        var result = new LedgerParseResult();
        using var e = rows.GetEnumerator();

        // Find the header row (allow a few title rows above it).
        Dictionary<string, int>? cols = null;
        var rowNo = 0;
        while (cols is null && e.MoveNext())
        {
            rowNo++;
            var names = e.Current.Select(v => (v ?? "").Trim()).ToList();
            if (names.Contains("Posting Date") && names.Contains("Customer No."))
                cols = names.Select((n, i) => (n, i)).Where(x => x.n != "").GroupBy(x => x.n).ToDictionary(g => g.Key, g => g.First().i);
            if (rowNo > 20) break;
        }
        if (cols is null)
            throw new InvalidDataException("This doesn't look like a Customer Ledger Entries export: no 'Posting Date' and 'Customer No.' header found.");
        var missing = RequiredColumns.Where(c => !cols.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException("The file is missing these columns: " + string.Join(", ", missing) + ".");

        int iDate = cols["Posting Date"], iType = cols["Document Type"], iCust = cols["Customer No."], iName = cols["Customer Name Custom"],
            iInv = cols["Invoice Customer Name"], iAmt = cols["Amount"], iSales = cols["Sales (LCY)"];
        var iGroup = cols.GetValueOrDefault("Customer Posting Group", -1);
        var grossUp = (1 + vatRate) / vatRate;
        string? Get(string?[] r, int i) => i >= 0 && i < r.Length ? r[i] : null;

        while (e.MoveNext())
        {
            rowNo++;
            var r = e.Current;
            if (r.All(string.IsNullOrWhiteSpace)) continue;
            result.RowsRead++;

            var type = (Get(r, iType) ?? "").Trim();
            if (type is not ("Invoice" or "Credit Memo")) { result.OtherDocumentTypes++; continue; }

            var cust = (Get(r, iCust) ?? "").Trim();
            if (cust == "") { result.Rejected.Add(new(rowNo, "No customer number")); continue; }
            if (!TabularReader.TryParseDate(Get(r, iDate), out var date)) { result.Rejected.Add(new(rowNo, $"Posting date not readable: '{Get(r, iDate)}'")); continue; }
            if (date > latestAllowed) { result.Rejected.Add(new(rowNo, $"Posting date {date:yyyy-MM-dd} is in the future")); continue; }
            if (!TabularReader.TryParseNumber(Get(r, iAmt), out var amount) || !TabularReader.TryParseNumber(Get(r, iSales), out var sales))
            { result.Rejected.Add(new(rowNo, "Amount or Sales (LCY) not a number")); continue; }

            var accountName = NormalizeName(Get(r, iName));
            var branch = NormalizeName(Get(r, iInv));
            var key = branch != "" ? branch : accountName;
            var taxable = (amount - sales) * grossUp;

            result.Lines.Add(new LedgerLine(rowNo, date, cust, accountName, key, (Get(r, iGroup) ?? "").Trim(), taxable));
            result.LinesUsed++;
            if (result.FirstDate is null || date < result.FirstDate) result.FirstDate = date;
            if (result.LastDate is null || date > result.LastDate) result.LastDate = date;
        }
        return result;
    }

    /// <summary>Head-office accounts book cheese as "QUICKMART FEDHA CHEESE": the branch is the name without the suffix.</summary>
    private static readonly string[] BranchSuffixes = [" CHEESE"];

    private static bool TryWithoutSuffix(string key, IReadOnlyDictionary<string, int> names, out int id)
    {
        foreach (var suffix in BranchSuffixes)
            if (key.EndsWith(suffix, StringComparison.Ordinal) && names.TryGetValue(key[..^suffix.Length], out id)) return true;
        id = 0;
        return false;
    }

    /// <summary>Assigns parsed lines in [fromMonth, ...] to outlets.</summary>
    /// <param name="saved">Saved mappings keyed by (customer no., name key).</param>
    /// <param name="outletIdByName">Outlet id by normalised outlet name.</param>
    public static LedgerAllocation Allocate(
        IEnumerable<LedgerLine> lines,
        DateOnly fromMonth,
        IReadOnlyDictionary<(string CustomerNo, string NameKey), MappingTarget> saved,
        IReadOnlyDictionary<string, int> outletIdByName)
    {
        var inRange = lines.Where(l => l.PostingDate >= fromMonth).ToList();
        var result = new LedgerAllocation();

        // Pass 1: saved mapping, then exact outlet name.
        var resolved = new Dictionary<(string, string), MappingTarget?>();
        foreach (var key in inRange.Select(l => (l.CustomerNo, l.NameKey)).Distinct())
        {
            if (saved.TryGetValue(key, out var t)) resolved[key] = t;
            else if (outletIdByName.TryGetValue(key.NameKey, out var id) || TryWithoutSuffix(key.NameKey, outletIdByName, out id))
            {
                resolved[key] = new MappingTarget(id, false, "Name");
                result.NewMappings.Add(new NewMapping(key.CustomerNo, key.NameKey, id, "Name"));
            }
            else resolved[key] = null;
        }

        // Pass 2: an account whose known names all point to one outlet takes its other lines too
        // (credit notes posted without a branch, old spellings).
        var accountOutlets = saved.Where(kv => kv.Value.OutletId is not null)
            .Select(kv => (kv.Key.CustomerNo, kv.Value.OutletId!.Value))
            .Concat(resolved.Where(kv => kv.Value?.OutletId is not null).Select(kv => (kv.Key.Item1, kv.Value!.OutletId!.Value)))
            .GroupBy(x => x.Item1)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Item2).Distinct().ToList());
        foreach (var key in resolved.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList())
        {
            if (accountOutlets.TryGetValue(key.Item1, out var ids) && ids.Count == 1)
            {
                resolved[key] = new MappingTarget(ids[0], false, "Account");
                result.NewMappings.Add(new NewMapping(key.Item1, key.Item2, ids[0], "Account"));
            }
        }

        foreach (var g in inRange.GroupBy(l => (l.CustomerNo, l.NameKey, Month: new DateOnly(l.PostingDate.Year, l.PostingDate.Month, 1))))
        {
            var amount = g.Sum(l => l.TaxableInclVat);
            var target = resolved[(g.Key.CustomerNo, g.Key.NameKey)];
            if (target is { Ignore: true }) { result.IgnoredAmount += amount; continue; }
            if (target?.OutletId is { } outletId)
            {
                result.Outlets.Add(new OutletMonthAmount(outletId, g.Key.Month, amount, g.Count()));
                result.MappedAmount += amount;
            }
            else
            {
                var first = g.First();
                result.Unmapped.Add(new UnmappedName(g.Key.CustomerNo, g.Key.NameKey, first.CustomerName, first.PostingGroup, g.Key.Month, amount, g.Count()));
                result.UnmappedAmount += amount;
            }
        }

        // Several (customer, name) pairs can land on the same outlet and month: combine them.
        var combined = result.Outlets.GroupBy(o => (o.OutletId, o.Month))
            .Select(g => new OutletMonthAmount(g.Key.OutletId, g.Key.Month, g.Sum(x => x.Amount), g.Sum(x => x.Lines))).ToList();
        result.Outlets.Clear();
        result.Outlets.AddRange(combined);
        return result;
    }
}
