using System.Globalization;
using System.Xml.Linq;

namespace Financials.Application.Budgets.Boq;

public sealed record BoqParseResult(BoqDocument? Document, IReadOnlyList<string> Errors)
{
    public bool IsValid => Document is not null && Errors.Count == 0;
}

public static class BoqXmlParser
{
    public const string Namespace = "urn:genera-systems:boq:1.0";
    public const string SchemaVersion = "1.0";

    private static readonly XNamespace Ns = Namespace;
    private static readonly IReadOnlyList<string> NoErrors = Array.Empty<string>();

    // Strict decimal: optional leading sign, optional decimal point, NOTHING ELSE.
    // Specifically: no thousands separator (NumberStyles.Number's default behavior
    // would accept "1,000" as 1000 under invariant culture — silent data corruption);
    // no whitespace inside the number; no exponent; no parentheses.
    // We Trim() the input ourselves to absorb XML formatting whitespace.
    private const NumberStyles StrictDecimalStyle =
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    // M-6: storage is decimal(19,4); the parser rejects values with more than
    // 4 fractional digits rather than silently truncating at insert. A value
    // like 12.34567 would round to 12.3457 in the DB without any warning if we
    // accepted it — a quiet £-precision loss across thousands of lines.
    private const int MaxFractionalDigits = 4;

    private static bool TryParseStrictDecimal(string? raw, out decimal value)
    {
        value = 0m;
        if (raw is null)
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (!decimal.TryParse(trimmed, StrictDecimalStyle, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return FractionalDigits(trimmed) <= MaxFractionalDigits;
    }

    /// <summary>
    /// Count digits after the decimal point on the raw input text. We work
    /// from the string rather than the parsed decimal because <c>decimal</c>
    /// normalises trailing zeros — "1.0000" and "1" parse to the same value
    /// but the input precision differs.
    /// </summary>
    private static int FractionalDigits(string trimmed)
    {
        var dot = trimmed.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? 0 : trimmed.Length - dot - 1;
    }

    public static BoqParseResult Parse(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return new BoqParseResult(null, new List<string> { "BoQ XML is empty." });
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xmlContent);
        }
        catch (System.Xml.XmlException ex)
        {
            return new BoqParseResult(null, new List<string> { $"BoQ XML is not well-formed: {ex.Message}" });
        }

        var errors = new List<string>();
        var root = doc.Root;
        if (root is null || root.Name != Ns + "BoqDocument")
        {
            errors.Add($"Root element must be {{{Namespace}}}BoqDocument.");
            return new BoqParseResult(null, errors);
        }

        var version = root.Attribute("version")?.Value;
        if (version != SchemaVersion)
        {
            errors.Add($"Unsupported version '{version}'. Expected '{SchemaVersion}'.");
        }

        var header = root.Element(Ns + "Header");
        if (header is null)
        {
            errors.Add("Header element is required.");
            return new BoqParseResult(null, errors);
        }

        var projectIdRaw = header.Element(Ns + "FinancialsProjectId")?.Value?.Trim();
        if (!Guid.TryParse(projectIdRaw, out var financialsProjectId))
        {
            errors.Add("Header/FinancialsProjectId must be a Guid.");
        }

        var reason = header.Element(Ns + "RevisionReason")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            errors.Add("Header/RevisionReason is required.");
        }

        var currency = header.Element(Ns + "Currency")?.Value?.Trim();

        var lines = new List<BoqDocumentLine>();
        var linesElement = root.Element(Ns + "Lines");
        if (linesElement is null)
        {
            errors.Add("Lines element is required.");
            return new BoqParseResult(null, errors);
        }

        var lineElements = linesElement.Elements(Ns + "Line").ToList();
        if (lineElements.Count == 0)
        {
            errors.Add("At least one Line element is required.");
            return new BoqParseResult(null, errors);
        }

        for (var index = 0; index < lineElements.Count; index++)
        {
            var line = lineElements[index];
            var prefix = $"Line[{index + 1}]";
            var lineNumberRaw = line.Attribute("lineNumber")?.Value;
            if (!int.TryParse(lineNumberRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNumber)
                || lineNumber <= 0)
            {
                errors.Add($"{prefix}: lineNumber attribute must be a positive integer.");
                continue;
            }

            var costCodeRaw = line.Element(Ns + "CimsCostCodeId")?.Value?.Trim();
            if (!Guid.TryParse(costCodeRaw, out var costCode))
            {
                errors.Add($"{prefix}: CimsCostCodeId must be a Guid.");
                continue;
            }

            var description = line.Element(Ns + "Description")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                errors.Add($"{prefix}: Description is required.");
                continue;
            }

            if (!TryParseStrictDecimal(line.Element(Ns + "Quantity")?.Value, out var quantity)
                || quantity < 0m)
            {
                errors.Add($"{prefix}: Quantity must be a non-negative decimal using '.' as the decimal point and no thousands separator.");
                continue;
            }

            var uom = line.Element(Ns + "UnitOfMeasure")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(uom))
            {
                errors.Add($"{prefix}: UnitOfMeasure is required.");
                continue;
            }

            if (!TryParseStrictDecimal(line.Element(Ns + "UnitRate")?.Value, out var unitRate)
                || unitRate < 0m)
            {
                errors.Add($"{prefix}: UnitRate must be a non-negative decimal using '.' as the decimal point and no thousands separator.");
                continue;
            }

            var workPackage = line.Element(Ns + "WorkPackage")?.Value?.Trim();
            var nrm2 = line.Element(Ns + "Nrm2Group")?.Value?.Trim();

            lines.Add(new BoqDocumentLine(
                lineNumber,
                costCode,
                description!,
                quantity,
                uom!,
                unitRate,
                string.IsNullOrEmpty(workPackage) ? null : workPackage,
                string.IsNullOrEmpty(nrm2) ? null : nrm2));
        }

        var duplicateLineNumbers = lines
            .GroupBy(l => l.LineNumber)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var duplicate in duplicateLineNumbers)
        {
            errors.Add($"Duplicate lineNumber {duplicate}.");
        }

        if (errors.Count > 0)
        {
            return new BoqParseResult(null, errors);
        }

        return new BoqParseResult(
            new BoqDocument(financialsProjectId, reason!, string.IsNullOrEmpty(currency) ? null : currency, lines),
            NoErrors);
    }
}
