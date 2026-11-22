namespace Rayler7.Infrastructure.Spreadsheets;

public sealed record SheetAnalysis(
    string Name,
    int RowCount,
    int ColumnCount,
    int? HeaderRow,
    IReadOnlyList<string> Headers,
    int FilledCells,
    int EmptyCells,
    IReadOnlyList<string> Formulas,
    IReadOnlyList<string> MergedRanges,
    bool HasAutoFilter,
    int ConditionalFormatCount,
    IReadOnlyList<IReadOnlyList<string>> Preview);

public sealed record SpreadsheetAnalysis(
    string FileName,
    string Format,
    string Sha256,
    IReadOnlyList<SheetAnalysis> Sheets,
    IReadOnlyList<string> Warnings);
