using System.Text;
using Demka.Portal.Core.Imports;

namespace Demka.Portal.Tests;

public class LedgerImportTests
{
    private const string Header = "Posting Date,Document Type,Customer No.,Customer Name Custom,Invoice Customer Name,Amount,Sales (LCY),Customer Posting Group";

    private static LedgerParseResult Parse(params string[] lines)
    {
        var csv = string.Join("\n", new[] { Header }.Concat(lines));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return LedgerImport.Parse(TabularReader.ReadRows(ms, "ledger.csv").ToList(), 0.16m, new DateOnly(2026, 10, 18));
    }

    [Fact]
    public void Taxable_value_is_the_vat_grossed_up()
    {
        // AFIA, January 2026 in the real ledger: Amount 113,703.00, Sales (LCY) 98,019.84 → taxable 113,703 incl. VAT.
        var r = Parse("2026-01-15,Invoice,C00294,AFIA,AFIA,113703.00,98019.84,SUPERMARKET");
        Assert.Equal(113_703m, Math.Round(r.Lines.Single().TaxableInclVat, 0));
    }

    [Fact]
    public void Fresh_milk_is_vat_exempt_so_it_drops_out()
    {
        var r = Parse("2026-10-02,Invoice,C1,SHOP,SHOP,5000,5000,SUPERMARKET");
        Assert.Equal(0m, r.Lines.Single().TaxableInclVat);
    }

    [Fact]
    public void Only_invoices_and_credit_memos_count_and_future_dates_are_rejected()
    {
        var r = Parse(
            "2026-10-02,Payment,C1,SHOP,,-1160,-1000,SUPERMARKET",
            "2026-10-03,Credit Memo,C1,SHOP,,-116,-100,SUPERMARKET",
            "2026-10-29,Invoice,C1,SHOP,,1160,1000,SUPERMARKET");
        Assert.Equal(1, r.OtherDocumentTypes);
        Assert.Single(r.Lines);
        Assert.Single(r.Rejected);
        Assert.Contains("future", r.Rejected[0].Reason);
    }

    [Fact]
    public void Branch_name_wins_over_account_name()
    {
        var r = Parse("2026-10-02,Invoice,CUST033,NAIVAS LIMITED,NAIVAS  KAMAKIS ,1160,1000,SUPERMARKET");
        Assert.Equal("NAIVAS KAMAKIS", r.Lines.Single().NameKey);
    }

    [Fact]
    public void Names_map_by_saved_link_then_outlet_name_then_the_accounts_only_outlet()
    {
        var r = Parse(
            "2026-10-02,Invoice,C9,BESTMART KALUNDU,BESTMART KALUNDU,1160,1000,SUPERMARKET",   // exact outlet name
            "2026-10-03,Credit Memo,C9,BESTMART LIMITED,,-116,-100,SUPERMARKET",               // same account, no branch
            "2026-10-04,Invoice,CUST033,NAIVAS LIMITED,NAIVAS KAMAKIS,2320,2000,SUPERMARKET",  // saved link
            "2026-10-05,Invoice,C7,UNKNOWN SHOP,,1160,1000,SUPERMARKET");                      // nothing matches
        var saved = new Dictionary<(string, string), MappingTarget> { [("CUST033", "NAIVAS KAMAKIS")] = new(2, false, "Seed") };
        var names = new Dictionary<string, int> { ["BESTMART KALUNDU"] = 1, ["NAIVAS NEWGATE MALL - KAMAKIS"] = 2 };

        var a = LedgerImport.Allocate(r.Lines, new DateOnly(2026, 10, 1), saved, names);

        Assert.Equal(1044m, Math.Round(a.Outlets.Single(o => o.OutletId == 1).Amount, 2)); // invoice 1,160 less credit 116
        Assert.Equal(2320m, Math.Round(a.Outlets.Single(o => o.OutletId == 2).Amount, 2));
        Assert.Single(a.Unmapped);
        Assert.Contains(a.NewMappings, m => m.Source == "Account" && m.NameKey == "BESTMART LIMITED");
    }

    [Fact]
    public void Cheese_lines_on_head_office_accounts_go_to_the_branch()
    {
        var r = Parse("2026-10-02,Invoice,CUST128,QUICKMART LIMITED,QUICKMART FEDHA CHEESE,1160,1000,SUPERMARKET");
        var a = LedgerImport.Allocate(r.Lines, new DateOnly(2026, 10, 1),
            new Dictionary<(string, string), MappingTarget>(), new Dictionary<string, int> { ["QUICKMART FEDHA"] = 5 });
        Assert.Equal(5, a.Outlets.Single().OutletId);
    }
}
