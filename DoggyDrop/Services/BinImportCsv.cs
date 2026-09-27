using System.Text;

namespace DoggyDrop.Services;

public sealed class BinImportException(string message) : Exception(message);
public sealed record ImportCsv(string[] Headers, IReadOnlyList<string[]> Rows, char Delimiter);

// Strict UTF-8 and a bounded CSV state machine; quotes may contain delimiters and newlines.
public static class BinImportCsv
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxRows = 10000;
    public const int MaxColumns = 64;
    public const int MaxField = 4096;
    public const int MaxCells = 200000;

    public static async Task<ImportCsv> ReadAsync(Stream stream, string? delimiter, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaxBytes) throw new BinImportException("CSV je prevelik. Največ 5 MiB.");
            buffer.Write(chunk, 0, count);
        }
        string text;
        try { text = new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { throw new BinImportException("Kodiranje ni veljaven UTF-8. Izvozi datoteko kot CSV UTF-8 (š, č, ž)."); }
        if (text.Contains('\0')) throw new BinImportException("CSV vsebuje nedovoljen ničelni znak.");
        if (string.IsNullOrWhiteSpace(text)) throw new BinImportException("CSV je prazen; prva vrstica mora vsebovati glavo.");
        if (delimiter is not (null or "" or ";" or ",")) throw new BinImportException("Izberi podpičje ali vejico.");
        if (!string.IsNullOrEmpty(delimiter)) return Parse(text, delimiter[0]);
        ImportCsv? semicolon = null, comma = null;
        try { semicolon = Parse(text, ';'); } catch (BinImportException) { }
        try { comma = Parse(text, ','); } catch (BinImportException) { }
        if (semicolon != null && comma == null) return semicolon;
        if (comma != null && semicolon == null) return comma;
        throw new BinImportException("Ločila ni mogoče zanesljivo določiti ali CSV ni pravilen. Izberi ločilo in ponovno naloži datoteko.");
    }

    private static ImportCsv Parse(string text, char delimiter)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder();
        var quoted = false; var closed = false; var cells = 0;
        void AddField()
        {
            if (row.Count == MaxColumns || ++cells > MaxCells) throw new BinImportException("Preveč stolpcev ali celic (največ 64 stolpcev in 200.000 celic).");
            row.Add(field.ToString()); field.Clear(); closed = false;
        }
        void AddRow()
        {
            AddField();
            if (rows.Count == MaxRows + 1) throw new BinImportException("CSV presega 10.000 podatkovnih vrstic.");
            rows.Add(row.ToArray()); row.Clear();
        }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else field.Append(c);
            }
            else if (c == delimiter) AddField();
            else if (c is '\r' or '\n') { AddRow(); if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; }
            else if (c == '"' && field.Length == 0 && !closed) quoted = true;
            else if (closed || c == '"') throw new BinImportException($"Nepravilni narekovaji v CSV vrstici {rows.Count + 1}.");
            else field.Append(c);
            if (field.Length > MaxField) throw new BinImportException($"Polje v vrstici {rows.Count + 1} presega 4096 znakov.");
        }
        if (quoted) throw new BinImportException("CSV ima nezaključen narekovaj.");
        if (field.Length > 0 || row.Count > 0 || closed) AddRow();
        if (rows.Count < 2) throw new BinImportException("CSV potrebuje glavo in vsaj eno podatkovno vrstico.");
        var headers = rows[0].Select(h => h.Trim()).ToArray();
        if (headers.Length < 2 || headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new BinImportException("Glava mora imeti vsaj dva neprazna, različna naziva stolpcev.");
        if (headers.All(h => BinImportMapping.Coordinate(h) != null))
            throw new BinImportException("Prva vrstica mora biti glava z nazivi stolpcev, ne koordinate.");
        return new(headers, rows.Skip(1).ToArray(), delimiter);
    }
}
