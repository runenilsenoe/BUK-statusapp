using System.Text;
using BukStatus.Core.Samvirk;

namespace BukStatus.Core.Tests;
public class SamvirkParserTests
{
    private static SamvirkData Parse(string text) => SamvirkParser.Parse(Encoding.UTF8.GetBytes(text), "test.csv");
    [Fact] public void Months_are_dynamic_unordered_and_preserve_empty_and_zero()
    {
        var result = Parse("BCC Person ID;Display Name;Church Id;Discount 2027-01;Coins 2026-12;Remaining 100 coins 2027-01;Coins 2027-01;Birth Date;On Track Until Date;Current Balance NOK\n1;Testperson;49;ATeam;0;123,45;;01.02.2000;31.01.2027;456,75");
        var p = Assert.Single(result.People);
        Assert.Equal(new DateOnly(2000, 2, 1), p.BirthDate);
        Assert.Equal(456.75m, p.Amounts["Current Balance NOK"]);
        Assert.Equal(0m, p.Months[0].Coins);
        Assert.Equal("ATeam", p.Months[1].Discount);
        Assert.Null(p.Months[1].Coins);
        Assert.Equal(123.45m, p.Months[1].Remaining100);
        Assert.Equal(new DateOnly(2027, 1, 31), p.Dates["On Track Until Date"]);
    }
    [Fact] public void Xlsx_reads_typed_excel_dates_and_future_months()
    {
        using var bytes = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(bytes, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            void Entry(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
            Entry("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/></Types>");
            Entry("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Entry("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"r1\"/></sheets></workbook>");
            Entry("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"r1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"r2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Entry("xl/styles.xml", "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cellXfs count=\"2\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
            string[] headers = ["BCC Person ID", "Display Name", "Church Id", "Birth Date", "Coins 2028-03"];
            var cells = string.Join("", headers.Select((h,i) => $"<c r=\"{(char)('A'+i)}1\" t=\"inlineStr\"><is><t>{h}</t></is></c>"));
            Entry("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">{cells}</row><row r=\"2\"><c r=\"A2\"><v>1</v></c><c r=\"B2\" t=\"inlineStr\"><is><t>Testperson</t></is></c><c r=\"C2\"><v>49</v></c><c r=\"D2\" s=\"1\"><v>36557</v></c><c r=\"E2\"><v>0</v></c></row></sheetData></worksheet>");
        }
        var person = Assert.Single(SamvirkParser.Parse(bytes.ToArray(), "test.xlsx").People);
        Assert.Equal(new DateOnly(2000, 2, 1), person.BirthDate);
        Assert.Equal(new DateOnly(2028, 3, 1), person.Months[0].Month);
        Assert.Equal(0m, person.Months[0].Coins);
    }
    [Theory]
    [InlineData("Coins 2027-13", "0")]
    [InlineData("Coins 2027-01", "bad")]
    [InlineData("Setup Date", "31.02.2027")]
    public void Malformed_values_reject_entire_file(string header, string value) => Assert.Throws<InvalidDataException>(() => Parse($"BCC Person ID;Display Name;Church Id;{header}\n1;Testperson;49;{value}"));
    [Fact] public void Quoted_semicolons_work_and_duplicate_people_are_rejected()
    {
        Assert.Equal("Test; Person", Parse("BCC Person ID;Display Name;Church Id\n1;\"Test; Person\";49").People[0].Name);
        Assert.Throws<InvalidDataException>(() => Parse("BCC Person ID;Display Name;Church Id\n1;Test;49\n1;Test;49"));
    }
}
