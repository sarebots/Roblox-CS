using System;
using System.Linq;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public class LoggerOverrideDocsTests
{
    [Fact]
    public void GetOverrideDocs_AllowsNullInput()
    {
        var docs = Logger.GetOverrideDocs(null!);
        Assert.Empty(docs);
    }

    [Fact]
    public void GetOverrideDocs_DeduplicatesDocs()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3044", "ROBLOXCS3042", "ROBLOXCS3044" });
        var list = docs.ToList();
        Assert.Single(list);
        Assert.Contains("docs/struct-ref-out-guidance.md", list, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReturnsAllDistinctDocs()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3042", "ROBLOXCS3032" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_IgnoresUnknownCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { "CS8345", "ROBLOXCS9999", "ROBLOXCS3032" });
        var list = docs.ToList();
        Assert.Single(list);
        Assert.Equal("docs/ts-iter-guardrails.md", list[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_IgnoresEmptyCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { " ", "\t", " ROBLOXCS3042  " });
        var list = docs.ToList();
        Assert.Single(list);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_DedupesCaseInsensitiveCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { "robloxcs3032", "ROBLOXCS3032" });
        var list = docs.ToList();
        Assert.Single(list);
        Assert.Equal("docs/ts-iter-guardrails.md", list[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_OrdersDocsAlphabetically()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3032", "ROBLOXCS3042" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_OrdersDocsRegardlessOfInput()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3032", "ROBLOXCS3042", "ROBLOXCS3032" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReturnsEmptyForUnknownCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { "CS9999", "ROBLOXCS0000" });
        Assert.Empty(docs);
    }

    [Fact]
    public void GetOverrideDocs_WithNoCodes_ReturnsEmpty()
    {
        var docs = Logger.GetOverrideDocs(Array.Empty<string>());
        Assert.Empty(docs);
    }

    [Fact]
    public void GetOverrideDocs_DedupesAndOrdersMixedCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { " robloxcs3042", "ROBLOXCS3042", " robloxcs3032 ", "ROBLOXCS9999", "  " });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_IgnoresNullCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { null!, "ROBLOXCS3032" });
        var list = docs.ToList();
        Assert.Single(list);
        Assert.Equal("docs/ts-iter-guardrails.md", list[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsBracketedCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { "[ROBLOXCS3042] something" });
        var list = docs.ToList();
        Assert.Single(list);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsBracketedCodesCaseInsensitive()
    {
        var docs = Logger.GetOverrideDocs(new[] { "[robloxcs3032] detail" });
        var list = docs.ToList();
        Assert.Single(list);
        Assert.Equal("docs/ts-iter-guardrails.md", list[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsInlineCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { "warn: ROBLOXCS3032 triggered" });
        var list = docs.ToList();
        Assert.Single(list);
        Assert.Equal("docs/ts-iter-guardrails.md", list[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_IgnoresNonWordBoundaryCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3032x" });
        Assert.Empty(docs);
    }

    [Fact]
    public void GetOverrideDocs_AllowsPunctuationAfterCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3032," });
        var list = docs.ToList();
        Assert.Single(list);
        Assert.Equal("docs/ts-iter-guardrails.md", list[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_IgnoresCodesEmbeddedInWords()
    {
        var docs = Logger.GetOverrideDocs(new[] { "fooROBLOXCS3032bar" });
        Assert.Empty(docs);
    }

    [Fact]
    public void GetOverrideDocs_IgnoresCodesWithTrailingDigits()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS30321" });
        Assert.Empty(docs);
    }

    [Fact]
    public void GetOverrideDocs_ReadsCodesSeparatedByPunctuation()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3032,ROBLOXCS3042" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsCodesAcrossNewlines()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3032\nROBLOXCS3042" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsCodesSeparatedBySemicolons()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3032; ROBLOXCS3042" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsCodesSeparatedByTabs()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3032\tROBLOXCS3042" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_DedupesAcrossMixedSeparators()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3032, ROBLOXCS3042; ROBLOXCS3032\tROBLOXCS3042" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsCodesWithWhitespaceAndPunctuation()
    {
        var docs = Logger.GetOverrideDocs(new[] { " ROBLOXCS3032 , \t ROBLOXCS3042 " });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsCodesSeparatedByCarriageReturn()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3032\rROBLOXCS3042" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsMultipleBracketedCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { "[ROBLOXCS3042] + [ROBLOXCS3032]" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsMultipleInlineCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { "ROBLOXCS3042 and ROBLOXCS3032 triggered" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsMixedBracketAndInlineCodes()
    {
        var docs = Logger.GetOverrideDocs(new[] { "[ROBLOXCS3042] and ROBLOXCS3032" });
        var list = docs.ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal("docs/struct-ref-out-guidance.md", list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("docs/ts-iter-guardrails.md", list[1], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_ReadsInlineCodesCaseInsensitive()
    {
        var docs = Logger.GetOverrideDocs(new[] { "warn: robloxcs3032 triggered" });
        var list = docs.ToList();
        Assert.Single(list);
        Assert.Equal("docs/ts-iter-guardrails.md", list[0], StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOverrideDocs_WhitespaceOnly_ReturnsEmpty()
    {
        var docs = Logger.GetOverrideDocs(new[] { "  ", "\t", "\n" });
        Assert.Empty(docs);
    }
}
