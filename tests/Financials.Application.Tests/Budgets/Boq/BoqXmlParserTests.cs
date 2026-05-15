using System.Globalization;
using Financials.Application.Budgets.Boq;

namespace Financials.Application.Tests.Budgets.Boq;

/// <summary>
/// Direct unit tests for <see cref="BoqXmlParser"/>. The parser is exercised
/// indirectly via <c>ImportBoqCommandHandler</c> and the F1 integration slice,
/// but its branch behaviour (empty / malformed / wrong namespace / per-line
/// validation) deserves a dedicated suite.
/// </summary>
public class BoqXmlParserTests
{
    private const string ValidProjectId = "11111111-1111-1111-1111-111111111111";
    private const string ValidCostCodeId = "22222222-2222-2222-2222-222222222222";

    private static string ValidDocument(
        string? projectId = ValidProjectId,
        string? reason = "Initial budget",
        string? currency = "GBP",
        string linesXml = """
            <Line lineNumber="1">
              <CimsCostCodeId>22222222-2222-2222-2222-222222222222</CimsCostCodeId>
              <Description>Concrete C30/37</Description>
              <Quantity>120.500</Quantity>
              <UnitOfMeasure>m3</UnitOfMeasure>
              <UnitRate>185.00</UnitRate>
              <WorkPackage>WP-A</WorkPackage>
              <Nrm2Group>3.1</Nrm2Group>
            </Line>
            """,
        string version = "1.0")
    {
        return string.Create(CultureInfo.InvariantCulture, $"""
            <?xml version="1.0" encoding="utf-8"?>
            <BoqDocument xmlns="urn:genera-systems:boq:1.0" version="{version}">
              <Header>
                <FinancialsProjectId>{projectId}</FinancialsProjectId>
                <RevisionReason>{reason}</RevisionReason>
                <Currency>{currency}</Currency>
              </Header>
              <Lines>
                {linesXml}
              </Lines>
            </BoqDocument>
            """);
    }

    [Fact]
    public void Parse_valid_minimal_document_returns_one_line_and_no_errors()
    {
        var result = BoqXmlParser.Parse(ValidDocument());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Document.Should().NotBeNull();
        result.Document!.FinancialsProjectId.Should().Be(Guid.Parse(ValidProjectId));
        result.Document.RevisionReason.Should().Be("Initial budget");
        result.Document.Currency.Should().Be("GBP");
        result.Document.Lines.Should().ContainSingle();

        var line = result.Document.Lines[0];
        line.LineNumber.Should().Be(1);
        line.CimsCostCodeId.Should().Be(Guid.Parse(ValidCostCodeId));
        line.Description.Should().Be("Concrete C30/37");
        line.Quantity.Should().Be(120.500m);
        line.UnitOfMeasure.Should().Be("m3");
        line.UnitRate.Should().Be(185.00m);
        line.WorkPackage.Should().Be("WP-A");
        line.Nrm2Group.Should().Be("3.1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Parse_empty_or_whitespace_input_returns_empty_error(string input)
    {
        var result = BoqXmlParser.Parse(input);

        result.IsValid.Should().BeFalse();
        result.Document.Should().BeNull();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("empty");
    }

    [Fact]
    public void Parse_malformed_xml_returns_well_formed_error()
    {
        var result = BoqXmlParser.Parse("<BoqDocument><not-closed>");

        result.IsValid.Should().BeFalse();
        result.Document.Should().BeNull();
        result.Errors.Should().ContainSingle()
            .Which.Should().StartWith("BoQ XML is not well-formed:");
    }

    [Fact]
    public void Parse_wrong_namespace_fails()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <BoqDocument xmlns="urn:wrong:1.0" version="1.0">
              <Header><FinancialsProjectId>11111111-1111-1111-1111-111111111111</FinancialsProjectId>
                <RevisionReason>r</RevisionReason></Header>
              <Lines/>
            </BoqDocument>
            """;

        var result = BoqXmlParser.Parse(xml);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("urn:genera-systems:boq:1.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_wrong_schema_version_is_reported()
    {
        var result = BoqXmlParser.Parse(ValidDocument(version: "2.0"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Unsupported version", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_missing_header_fails_fast()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <BoqDocument xmlns="urn:genera-systems:boq:1.0" version="1.0">
              <Lines/>
            </BoqDocument>
            """;

        var result = BoqXmlParser.Parse(xml);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Header element is required.");
    }

    [Fact]
    public void Parse_non_guid_project_id_is_flagged()
    {
        var result = BoqXmlParser.Parse(ValidDocument(projectId: "not-a-guid"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("FinancialsProjectId must be a Guid", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_missing_revision_reason_is_flagged()
    {
        var result = BoqXmlParser.Parse(ValidDocument(reason: ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("RevisionReason is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_missing_lines_element_fails()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <BoqDocument xmlns="urn:genera-systems:boq:1.0" version="1.0">
              <Header>
                <FinancialsProjectId>11111111-1111-1111-1111-111111111111</FinancialsProjectId>
                <RevisionReason>r</RevisionReason>
              </Header>
            </BoqDocument>
            """;

        var result = BoqXmlParser.Parse(xml);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Lines element is required.");
    }

    [Fact]
    public void Parse_empty_lines_element_fails()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <BoqDocument xmlns="urn:genera-systems:boq:1.0" version="1.0">
              <Header>
                <FinancialsProjectId>11111111-1111-1111-1111-111111111111</FinancialsProjectId>
                <RevisionReason>r</RevisionReason>
              </Header>
              <Lines/>
            </BoqDocument>
            """;

        var result = BoqXmlParser.Parse(xml);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("At least one Line element is required.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("")]
    public void Parse_invalid_line_number_is_flagged(string raw)
    {
        var lineXml = $"""
            <Line lineNumber="{raw}">
              <CimsCostCodeId>22222222-2222-2222-2222-222222222222</CimsCostCodeId>
              <Description>x</Description>
              <Quantity>1</Quantity>
              <UnitOfMeasure>nr</UnitOfMeasure>
              <UnitRate>1</UnitRate>
            </Line>
            """;

        var result = BoqXmlParser.Parse(ValidDocument(linesXml: lineXml));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("lineNumber attribute", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_negative_quantity_is_flagged()
    {
        var lineXml = """
            <Line lineNumber="1">
              <CimsCostCodeId>22222222-2222-2222-2222-222222222222</CimsCostCodeId>
              <Description>x</Description>
              <Quantity>-1</Quantity>
              <UnitOfMeasure>nr</UnitOfMeasure>
              <UnitRate>1</UnitRate>
            </Line>
            """;

        var result = BoqXmlParser.Parse(ValidDocument(linesXml: lineXml));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Quantity must be a non-negative decimal", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_negative_unit_rate_is_flagged()
    {
        var lineXml = """
            <Line lineNumber="1">
              <CimsCostCodeId>22222222-2222-2222-2222-222222222222</CimsCostCodeId>
              <Description>x</Description>
              <Quantity>1</Quantity>
              <UnitOfMeasure>nr</UnitOfMeasure>
              <UnitRate>-0.01</UnitRate>
            </Line>
            """;

        var result = BoqXmlParser.Parse(ValidDocument(linesXml: lineXml));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("UnitRate must be a non-negative decimal", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_decimal_uses_dot_separator_under_invariant_culture()
    {
        // Lock the contract: '.' is the decimal point regardless of OS culture.
        var lineXml = """
            <Line lineNumber="1">
              <CimsCostCodeId>22222222-2222-2222-2222-222222222222</CimsCostCodeId>
              <Description>x</Description>
              <Quantity>120.5</Quantity>
              <UnitOfMeasure>nr</UnitOfMeasure>
              <UnitRate>10.25</UnitRate>
            </Line>
            """;

        var result = BoqXmlParser.Parse(ValidDocument(linesXml: lineXml));

        result.IsValid.Should().BeTrue();
        result.Document!.Lines[0].Quantity.Should().Be(120.5m);
        result.Document.Lines[0].UnitRate.Should().Be(10.25m);
    }

    [Fact]
    public void Parse_duplicate_line_numbers_are_reported()
    {
        var lineXml = """
            <Line lineNumber="1">
              <CimsCostCodeId>22222222-2222-2222-2222-222222222222</CimsCostCodeId>
              <Description>a</Description>
              <Quantity>1</Quantity>
              <UnitOfMeasure>nr</UnitOfMeasure>
              <UnitRate>1</UnitRate>
            </Line>
            <Line lineNumber="1">
              <CimsCostCodeId>22222222-2222-2222-2222-222222222222</CimsCostCodeId>
              <Description>b</Description>
              <Quantity>1</Quantity>
              <UnitOfMeasure>nr</UnitOfMeasure>
              <UnitRate>1</UnitRate>
            </Line>
            """;

        var result = BoqXmlParser.Parse(ValidDocument(linesXml: lineXml));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Duplicate lineNumber 1.");
    }

    [Fact]
    public void Parse_omitted_currency_becomes_null()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <BoqDocument xmlns="urn:genera-systems:boq:1.0" version="1.0">
              <Header>
                <FinancialsProjectId>11111111-1111-1111-1111-111111111111</FinancialsProjectId>
                <RevisionReason>r</RevisionReason>
              </Header>
              <Lines>
                <Line lineNumber="1">
                  <CimsCostCodeId>22222222-2222-2222-2222-222222222222</CimsCostCodeId>
                  <Description>x</Description>
                  <Quantity>1</Quantity>
                  <UnitOfMeasure>nr</UnitOfMeasure>
                  <UnitRate>1</UnitRate>
                </Line>
              </Lines>
            </BoqDocument>
            """;

        var result = BoqXmlParser.Parse(xml);

        result.IsValid.Should().BeTrue();
        result.Document!.Currency.Should().BeNull();
        result.Document.Lines[0].WorkPackage.Should().BeNull();
        result.Document.Lines[0].Nrm2Group.Should().BeNull();
    }
}
