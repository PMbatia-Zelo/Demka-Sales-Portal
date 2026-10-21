using System.Text;
using Demka.Portal.Core.Imports;

namespace Demka.Portal.Tests;

public class SolutechImportTests
{
    private const string Header = "﻿ID,\"REP NAME\",\"CUSTOMER ID\",\"ERP CODE\",\"SHOP NAME\",\"CUSTOMER CATEGORY\",\"ROUTE NAME\",\"REGION NAME\",\"CHECKIN TIME\",\"CHECKOUT TIME\",\"TIME SPENT\",\"TRANSIT TIME\",DATE,\"DISTANCE VARIANCE\"";

    private static SolutechParseResult Parse(params string[] lines)
    {
        var csv = string.Join("\n", new[] { Header }.Concat(lines));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return SolutechImport.Parse(TabularReader.ReadRows(ms, "checkin.csv").ToList(), new DateOnly(2026, 10, 18));
    }

    [Fact]
    public void Reads_a_check_in_with_time_on_site()
    {
        // First row of the September report.
        var r = Parse("12978,\"PERIS WATETU WANJOHI\",147,C00232,\"NAIVAS SOKONI\",SUPERMARKET,\"PERIS WANJOHI TUE ROUTE\",WESTERN,\"2026-09-01 07:18:08\",\"2026-09-01 08:40:07\",\"01 Hrs 21 Mins\",\"14 hours\",\"01-Sep-2026 07:18:08\",\"19 m\"");
        var v = r.Lines.Single();
        Assert.Equal(12978, v.VisitId);
        Assert.Equal(147, v.ShopId);
        Assert.Equal("C00232", v.ErpCode);
        Assert.Equal("NAIVAS SOKONI", v.ShopKey);
        Assert.Equal(new DateTime(2026, 9, 1, 7, 18, 8), v.CheckIn);
        Assert.Equal(82, v.MinutesOnSite);
        Assert.Equal(new DateOnly(2026, 9, 1), r.FirstDate);
    }

    [Fact]
    public void Missing_check_out_duplicates_and_future_dates()
    {
        var r = Parse(
            "1,REP ONE,10,C1,SHOP,SUPERMARKET,R,WESTERN,\"2026-10-01 09:00:00\",,,,,",
            "1,REP ONE,10,C1,SHOP,SUPERMARKET,R,WESTERN,\"2026-10-01 09:00:00\",,,,,",
            "2,REP ONE,10,C1,SHOP,SUPERMARKET,R,WESTERN,\"2026-10-25 09:00:00\",,,,,");
        Assert.Null(r.Lines.Single().MinutesOnSite);
        Assert.Equal(1, r.DuplicateRows);
        Assert.Contains("future", r.Rejected.Single().Reason);
    }

    [Theory]
    [InlineData("ELIZABETH MAKOKHA0728943481", "ELIZABETH MAKOKHA")]
    [InlineData("JOSPHINE CHEPKEMBOI MUTWOL-", "JOSPHINE CHEPKEMBOI MUTWOL")]
    [InlineData("Ann  Wangari Maina", "ANN WANGARI MAINA")]
    public void Rep_names_lose_phone_numbers_and_stray_punctuation(string raw, string expected) =>
        Assert.Equal(expected, SolutechImport.NormalizePerson(raw));

    private static readonly SolutechImport.OutletIndex Index = new(
    [
        new OutletName(1, "CHIENI NYERI DOWNTOWN"), new OutletName(2, "NAIVAS SOKONI"), new OutletName(3, "MATHAI EMBU 2"),
        new OutletName(4, "MATHAI EMBU"), new OutletName(5, "SKYMART KAHAWA WEST"), new OutletName(6, "MAGUNAS KARATINA"),
        new OutletName(7, "MAGUNAS KARATINA SOKONI NEW"), new OutletName(8, "QUICKMART NGONG RD ADAMS")
    ]);

    [Fact]
    public void Shops_match_by_ledger_link_then_name_then_word_order()
    {
        var ledger = new Dictionary<(string, string), int> { [("C00031", "QUICKMART ADAMS")] = 8 };
        Assert.Equal(new ShopMatch(8, "Ledger"), Index.Match("QUICKMART ADAMS", "C00031", ledger));
        Assert.Equal(new ShopMatch(2, "Name"), Index.Match("NAIVAS SOKONI", "C00232", ledger));
        Assert.Equal(new ShopMatch(1, "WordOrder"), Index.Match("CHIENI DOWNTOWN NYERI", "766", ledger));
        Assert.Equal(new ShopMatch(5, "WordOrder"), Index.Match("SKYMART KAHAWA.WEST", "781", ledger));
        // A near spelling is never linked automatically.
        Assert.Null(Index.Match("MAGUNAS MAIN KARATINA", "720", ledger));
        Assert.Null(Index.Match("QUICKMART ADAMS", "OTHER", ledger));
    }

    [Fact]
    public void Suggestions_rank_the_likely_outlet_first()
    {
        Assert.Equal(3, Index.Suggest("MATHAIS 2 EMBU")[0].OutletId);
        Assert.Equal(6, Index.Suggest("MAGUNAS MAIN KARATINA")[0].OutletId);
        Assert.Equal(7, Index.Suggest("MAGUNAS KARATINA SOKONI")[0].OutletId);
        Assert.Empty(Index.Suggest("BARSABRA"));
    }
}
