using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Demka.Portal.Core.Imports;

/// <summary>
/// Reads the first worksheet of an .xlsx file, or a .csv file, row by row without loading it all into memory.
/// No third-party packages: .xlsx is a zip of XML parts, read here with XmlReader.
/// Values come back as text: numbers and dates in their raw stored form (dates as Excel serial numbers).
/// </summary>
public static class TabularReader
{
    public static IEnumerable<string?[]> ReadRows(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" => ReadXlsx(stream),
            ".csv" or ".txt" => ReadCsv(stream),
            _ => throw new InvalidDataException("Upload an .xlsx or .csv file.")
        };
    }

    // ---------------- XLSX ----------------
    private static IEnumerable<string?[]> ReadXlsx(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var sheetPath = FirstSheetPath(zip);
        var shared = ReadSharedStrings(zip);
        var entry = zip.GetEntry(sheetPath) ?? throw new InvalidDataException("The workbook has no readable worksheet.");

        using var s = entry.Open();
        using var xr = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = false, DtdProcessing = DtdProcessing.Prohibit });

        // A small state machine: never call ReadElementContent*, which moves the reader and makes loops skip nodes.
        var row = new List<string?>();
        var sb = new StringBuilder();
        var inRow = false; var inCell = false; var capture = false; var seenValue = false; var inPhonetic = false;
        var col = 0; var nextCol = 0; string? type = null;

        while (xr.Read())
        {
            switch (xr.NodeType)
            {
                case XmlNodeType.Element:
                    switch (xr.LocalName)
                    {
                        case "row":
                            if (xr.IsEmptyElement) { yield return []; break; }
                            row.Clear(); nextCol = 0; inRow = true;
                            break;
                        case "c" when inRow:
                            var r = xr.GetAttribute("r");
                            col = r is null ? nextCol : ColumnIndex(r);
                            nextCol = col + 1;
                            type = xr.GetAttribute("t");
                            sb.Clear(); seenValue = false;
                            if (xr.IsEmptyElement) Put(row, col, null); else inCell = true;
                            break;
                        case "rPh":
                            if (!xr.IsEmptyElement) inPhonetic = true;
                            break;
                        case "v" or "t" when inCell && !inPhonetic:
                            seenValue = true;
                            capture = !xr.IsEmptyElement;
                            break;
                    }
                    break;
                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    if (capture) sb.Append(xr.Value);
                    break;
                case XmlNodeType.EndElement:
                    switch (xr.LocalName)
                    {
                        case "v" or "t": capture = false; break;
                        case "rPh": inPhonetic = false; break;
                        case "c" when inCell:
                            inCell = false;
                            string? value = seenValue ? sb.ToString() : null;
                            if (value is not null && type == "s" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                                value = idx >= 0 && idx < shared.Count ? shared[idx] : "";
                            else if (value is not null && type == "b") value = value == "1" ? "TRUE" : "FALSE";
                            Put(row, col, value);
                            break;
                        case "row" when inRow:
                            inRow = false;
                            yield return row.ToArray();
                            break;
                    }
                    break;
            }
        }
    }

    private static void Put(List<string?> row, int col, string? value)
    {
        while (row.Count < col) row.Add(null);
        if (row.Count == col) row.Add(value); else row[col] = value;
    }

    private static string FirstSheetPath(ZipArchive zip)
    {
        var wb = zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("This is not a valid .xlsx file.");
        string? relId = null;
        using (var s = wb.Open())
        using (var xr = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
            while (xr.Read())
                if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "sheet")
                {
                    relId = xr.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                    break;
                }

        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (relId is not null && rels is not null)
        {
            using var s = rels.Open();
            using var xr = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            while (xr.Read())
                if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "Relationship" && xr.GetAttribute("Id") == relId)
                {
                    var target = xr.GetAttribute("Target") ?? "";
                    return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                }
        }
        return "xl/worksheets/sheet1.xml";
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var e = zip.GetEntry("xl/sharedStrings.xml");
        if (e is null) return list;
        using var s = e.Open();
        using var xr = XmlReader.Create(s, new XmlReaderSettings { IgnoreWhitespace = false, DtdProcessing = DtdProcessing.Prohibit });
        var sb = new StringBuilder();
        bool inSi = false, capture = false, inPhonetic = false;
        while (xr.Read())
        {
            switch (xr.NodeType)
            {
                case XmlNodeType.Element:
                    if (xr.LocalName == "si") { sb.Clear(); inSi = !xr.IsEmptyElement; if (!inSi) list.Add(""); }
                    else if (xr.LocalName == "rPh" && !xr.IsEmptyElement) inPhonetic = true;
                    else if (xr.LocalName == "t" && inSi && !inPhonetic) capture = !xr.IsEmptyElement;
                    break;
                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace:
                    if (capture) sb.Append(xr.Value);
                    break;
                case XmlNodeType.EndElement:
                    if (xr.LocalName == "t") capture = false;
                    else if (xr.LocalName == "rPh") inPhonetic = false;
                    else if (xr.LocalName == "si" && inSi) { list.Add(sb.ToString()); inSi = false; }
                    break;
            }
        }
        return list;
    }

    /// <summary>"A1" → 0, "AB12" → 27.</summary>
    public static int ColumnIndex(string cellRef)
    {
        var n = 0;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z') n = n * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') n = n * 26 + (ch - 'a' + 1);
            else break;
        }
        return n - 1;
    }

    // ---------------- CSV ----------------
    private static IEnumerable<string?[]> ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var first = reader.ReadLine();
        if (first is null) yield break;
        var sep = first.Count(c => c == ';') > first.Count(c => c == ',') ? ';' : ',';
        foreach (var r in ParseCsv(first, reader, sep)) yield return r;
    }

    private static IEnumerable<string?[]> ParseCsv(string firstLine, TextReader reader, char sep)
    {
        var line = firstLine;
        while (line is not null)
        {
            var fields = new List<string?>();
            var sb = new StringBuilder();
            var inQuotes = false;
            var i = 0;
            while (true)
            {
                if (i >= line.Length)
                {
                    if (inQuotes)
                    {
                        var next = reader.ReadLine();
                        if (next is null) break;
                        sb.Append('\n'); line = next; i = 0; continue;
                    }
                    break;
                }
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                    if (c == '"') { inQuotes = false; i++; continue; }
                    sb.Append(c); i++;
                }
                else
                {
                    if (c == '"') { inQuotes = true; i++; continue; }
                    if (c == sep) { fields.Add(sb.ToString()); sb.Clear(); i++; continue; }
                    sb.Append(c); i++;
                }
            }
            fields.Add(sb.ToString());
            yield return fields.ToArray();
            line = reader.ReadLine();
        }
    }

    // ---------------- value helpers ----------------
    public static bool TryParseNumber(string? s, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return true; // empty amount = 0
        var t = s.Trim().Replace(",", "");
        var neg = t.StartsWith('(') && t.EndsWith(')');
        if (neg) t = t[1..^1];
        if (decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            if (neg) value = -value;
            return true;
        }
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { value = (decimal)d; return true; }
        return false;
    }

    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm", "dd-MM-yyyy", "dd.MM.yyyy", "M/d/yyyy"];

    /// <summary>Excel serial numbers (from .xlsx) or common text dates (from .csv). Day-first is assumed for slashes.</summary>
    public static bool TryParseDate(string? s, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }
        if (DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }
        return false;
    }
}
