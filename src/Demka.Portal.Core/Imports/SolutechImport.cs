using System.Text.RegularExpressions;

namespace Demka.Portal.Core.Imports;

/// <summary>One check-in from the Solutech (SAT) "Check-in / check-out" attendance report.</summary>
public sealed record VisitLine(
    int RowNumber,
    long VisitId,
    string RepName,
    string RepKey,
    int ShopId,
    string ErpCode,
    string ShopName,
    string ShopKey,
    string Category,
    string Route,
    string Region,
    DateTime CheckIn,
    DateTime? CheckOut)
{
    /// <summary>Minutes between check-in and check-out; null when the rep never checked out.</summary>
    public int? MinutesOnSite => CheckOut is { } o && o >= CheckIn ? (int)Math.Round((o - CheckIn).TotalMinutes) : null;
}

public sealed class SolutechParseResult
{
    public int RowsRead { get; set; }
    public int DuplicateRows { get; set; }
    public List<VisitLine> Lines { get; } = [];
    public List<LedgerRejection> Rejected { get; } = [];
    public DateOnly? FirstDate { get; set; }
    public DateOnly? LastDate { get; set; }
}

/// <summary>Where a shop matched, and how.</summary>
public sealed record ShopMatch(int OutletId, string Source);

public sealed record OutletName(int Id, string Name);

public sealed record OutletSuggestion(int OutletId, string Name, double Score);

/// <summary>
/// Reads the Solutech attendance export and places each shop on an outlet:
/// saved link (by Solutech customer ID) → the ledger's link for the same ERP code and name → exact outlet name →
/// the same words in a different order ("CHIENI DOWNTOWN NYERI" = "CHIENI NYERI DOWNTOWN"). Anything else goes to the
/// admin with suggestions; spelling guesses are never applied automatically.
/// </summary>
public static class SolutechImport
{
    public static readonly string[] RequiredColumns = ["ID", "REP NAME", "CUSTOMER ID", "SHOP NAME", "CHECKIN TIME"];

    private static readonly Regex NonWord = new(@"[^A-Z0-9]+", RegexOptions.Compiled);
    private static readonly Regex NotLetters = new(@"[^A-Z' ]+", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Rep names in Solutech sometimes carry a phone number or a stray dash: "ELIZABETH MAKOKHA0728943481".</summary>
    public static string NormalizePerson(string? s) =>
        Spaces.Replace(NotLetters.Replace((s ?? "").ToUpperInvariant(), " "), " ").Trim();

    /// <summary>Words of a name, punctuation removed: "SKYMART KAHAWA.WEST" → SKYMART, KAHAWA, WEST.</summary>
    public static string[] Words(string? s) =>
        NonWord.Replace((s ?? "").ToUpperInvariant(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The words sorted, so word order doesn't matter.</summary>
    public static string SortedWordsKey(string? s) => string.Join(' ', Words(s).Order(StringComparer.Ordinal));

    public static SolutechParseResult Parse(IEnumerable<string?[]> rows, DateOnly latestAllowed)
    {
        var result = new SolutechParseResult();
        using var e = rows.GetEnumerator();
        Dictionary<string, int>? cols = null;
        var rowNo = 0;
        while (cols is null && e.MoveNext())
        {
            rowNo++;
            var names = e.Current.Select(v => (v ?? "").Trim().TrimStart('﻿').ToUpperInvariant()).ToList();
            if (names.Contains("REP NAME") && names.Contains("CHECKIN TIME"))
                cols = names.Select((n, i) => (n, i)).Where(x => x.n != "").GroupBy(x => x.n).ToDictionary(g => g.Key, g => g.First().i);
            if (rowNo > 20) break;
        }
        if (cols is null)
            throw new InvalidDataException("This doesn't look like a Solutech check-in report: no 'REP NAME' and 'CHECKIN TIME' header found.");
        var missing = RequiredColumns.Where(c => !cols.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidDataException("The file is missing these columns: " + string.Join(", ", missing) + ".");

        int iId = cols["ID"], iRep = cols["REP NAME"], iShopId = cols["CUSTOMER ID"], iShop = cols["SHOP NAME"], iIn = cols["CHECKIN TIME"];
        int iErp = cols.GetValueOrDefault("ERP CODE", -1), iCat = cols.GetValueOrDefault("CUSTOMER CATEGORY", -1),
            iRoute = cols.GetValueOrDefault("ROUTE NAME", -1), iRegion = cols.GetValueOrDefault("REGION NAME", -1),
            iOut = cols.GetValueOrDefault("CHECKOUT TIME", -1);
        string Get(string?[] r, int i) => i >= 0 && i < r.Length ? (r[i] ?? "").Trim() : "";

        var seen = new HashSet<long>();
        while (e.MoveNext())
        {
            rowNo++;
            var r = e.Current;
            if (r.All(string.IsNullOrWhiteSpace)) continue;
            result.RowsRead++;

            if (!long.TryParse(Get(r, iId), out var id)) { result.Rejected.Add(new(rowNo, $"ID not a number: '{Get(r, iId)}'")); continue; }
            if (!seen.Add(id)) { result.DuplicateRows++; continue; }
            if (!int.TryParse(Get(r, iShopId), out var shopId)) { result.Rejected.Add(new(rowNo, $"Customer ID not a number: '{Get(r, iShopId)}'")); continue; }
            var rep = Get(r, iRep);
            if (NormalizePerson(rep) == "") { result.Rejected.Add(new(rowNo, "No rep name")); continue; }
            if (!TabularReader.TryParseDateTime(Get(r, iIn), out var checkIn)) { result.Rejected.Add(new(rowNo, $"Check-in time not readable: '{Get(r, iIn)}'")); continue; }
            var day = DateOnly.FromDateTime(checkIn);
            if (day > latestAllowed) { result.Rejected.Add(new(rowNo, $"Check-in {day:yyyy-MM-dd} is in the future")); continue; }
            DateTime? checkOut = TabularReader.TryParseDateTime(Get(r, iOut), out var o) ? o : null;

            var shop = Get(r, iShop);
            result.Lines.Add(new VisitLine(rowNo, id, Spaces.Replace(rep, " "), NormalizePerson(rep), shopId, Get(r, iErp),
                Spaces.Replace(shop, " "), LedgerImport.NormalizeName(shop), Get(r, iCat), Spaces.Replace(Get(r, iRoute), " "), Get(r, iRegion), checkIn, checkOut));
            if (result.FirstDate is null || day < result.FirstDate) result.FirstDate = day;
            if (result.LastDate is null || day > result.LastDate) result.LastDate = day;
        }
        return result;
    }

    /// <summary>Index of outlet names for matching: exact (normalised) and word-order-free (only when unambiguous).</summary>
    public sealed class OutletIndex
    {
        public IReadOnlyList<OutletName> Outlets { get; }
        private readonly Dictionary<string, int> _exact;
        private readonly Dictionary<string, int> _sorted;
        private readonly List<(OutletName Outlet, string[] Words)> _words;

        public OutletIndex(IEnumerable<OutletName> outlets)
        {
            Outlets = outlets.ToList();
            _exact = Outlets.GroupBy(o => LedgerImport.NormalizeName(o.Name)).ToDictionary(g => g.Key, g => g.First().Id);
            _sorted = Outlets.GroupBy(o => SortedWordsKey(o.Name)).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Id);
            _words = Outlets.Select(o => (o, Words(o.Name))).ToList();
        }

        /// <param name="ledgerLinks">Links already made for the ledger: (BC customer no., name key) → outlet.</param>
        public ShopMatch? Match(string shopKey, string erpCode, IReadOnlyDictionary<(string CustomerNo, string NameKey), int>? ledgerLinks = null)
        {
            if (ledgerLinks is not null && erpCode != "" && ledgerLinks.TryGetValue((erpCode, shopKey), out var l)) return new(l, "Ledger");
            if (_exact.TryGetValue(shopKey, out var id)) return new(id, "Name");
            if (shopKey.EndsWith(" CHEESE", StringComparison.Ordinal) && _exact.TryGetValue(shopKey[..^7], out id)) return new(id, "Name");
            if (_sorted.TryGetValue(SortedWordsKey(shopKey), out id)) return new(id, "WordOrder");
            return null;
        }

        /// <summary>Closest outlet names, for the admin to confirm. Words that are nearly the same (MATHAIS/MATHAI,
        /// JUNTION/JUNCTION, FRANKMART/FRANKMATT) count as matches.</summary>
        public List<OutletSuggestion> Suggest(string shopName, int top = 3)
        {
            var a = Words(shopName);
            if (a.Length == 0) return [];
            var list = new List<OutletSuggestion>();
            foreach (var (o, b) in _words)
            {
                var matched = 0.0;
                var used = new bool[b.Length];
                foreach (var w in a)
                {
                    var best = -1; var bestSim = 0.0;
                    for (var j = 0; j < b.Length; j++)
                    {
                        if (used[j]) continue;
                        var sim = Similarity(w, b[j]);
                        if (sim > bestSim) { bestSim = sim; best = j; }
                    }
                    if (best >= 0 && bestSim >= 0.75) { used[best] = true; matched += bestSim; }
                }
                if (matched == 0) continue;
                var score = matched / (a.Length + b.Length - matched);
                if (a[0] == b[0] || Similarity(a[0], b[0]) >= 0.75) score += 0.15; // same chain name first
                list.Add(new OutletSuggestion(o.Id, o.Name, Math.Round(Math.Min(1, score), 3)));
            }
            return list.Where(s => s.Score >= 0.3).OrderByDescending(s => s.Score).ThenBy(s => s.Name).Take(top).ToList();
        }
    }

    /// <summary>1 − edit distance ÷ longer length. Single digits only match themselves.</summary>
    public static double Similarity(string x, string y)
    {
        if (x == y) return 1;
        if (x.Length < 3 || y.Length < 3) return 0;
        var d = new int[y.Length + 1];
        for (var j = 0; j <= y.Length; j++) d[j] = j;
        for (var i = 1; i <= x.Length; i++)
        {
            var prev = d[0]; d[0] = i;
            for (var j = 1; j <= y.Length; j++)
            {
                var tmp = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (x[i - 1] == y[j - 1] ? 0 : 1));
                prev = tmp;
            }
        }
        return 1.0 - (double)d[y.Length] / Math.Max(x.Length, y.Length);
    }
}
