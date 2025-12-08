using System;
using System.IO;
using RobloxCS.Shared;
using Xunit;

namespace RobloxCS.Tests;

public class LoggerTests
{
    [Theory]
    [InlineData("ROBLOXCS3032")]
    [InlineData("ROBLOXCS3033")]
    [InlineData("ROBLOXCS3034")]
    [InlineData("ROBLOXCS3035")]
    [InlineData("ROBLOXCS3036")]
    public void IteratorDiagnosticHint_IncludesGuardrailDoc(string diagnosticCode)
    {
        var originalExit = Logger.Exit;
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Logger.Exit = false;
        Logger.ResetDiagnosticState();

        try
        {
            CleanExitException? capturedException = null;
            try
            {
                throw Logger.Error($"[{diagnosticCode}] iterator helpers disabled.");
            }
            catch (CleanExitException ex)
            {
                capturedException = ex;
            }

            Assert.NotNull(capturedException);
            var output = writer.ToString();
            Assert.Contains("docs/ts-iter-guardrails.md", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Logger.ResetDiagnosticState();
            Logger.Exit = originalExit;
            Console.SetOut(originalOut);
        }
    }

    [Theory]
    [InlineData("ROBLOXCS3042")]
    [InlineData("ROBLOXCS3044")]
    public void RefLikeDiagnosticSummary_IncludesGuidanceDoc(string diagnosticCode)
    {
        var originalExit = Logger.Exit;
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Logger.Exit = false;
        Logger.ResetDiagnosticState();

        try
        {
            CleanExitException? capturedException = null;
            try
            {
                throw Logger.Error($"[{diagnosticCode}] ref-like struct restriction.");
            }
            catch (CleanExitException ex)
            {
                capturedException = ex;
            }

            Assert.NotNull(capturedException);
            var output = writer.ToString();
            Assert.Contains("docs/struct-ref-out-guidance.md", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Logger.ResetDiagnosticState();
            Logger.Exit = originalExit;
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void NonOverrideDiagnostic_DoesNotEmitOverrideDocs()
    {
        var originalExit = Logger.Exit;
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Logger.Exit = false;
        Logger.ResetDiagnosticState();

        try
        {
            CleanExitException? capturedException = null;
            try
            {
                throw Logger.Error("[ROBLOXCS3010] sample misuse.");
            }
            catch (CleanExitException ex)
            {
                capturedException = ex;
            }

            Assert.NotNull(capturedException);
            var output = writer.ToString();
            Assert.DoesNotContain("ts-iter-guardrails", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("struct-ref-out-guidance", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Logger.ResetDiagnosticState();
            Logger.Exit = originalExit;
            Console.SetOut(originalOut);
        }
    }

    [Theory]
    [InlineData("ROBLOXCS3042", "docs/struct-ref-out-guidance.md")]
    [InlineData("ROBLOXCS3032", "docs/ts-iter-guardrails.md")]
    public void OverrideDiagnosticHint_UsesGenericWording(string diagnosticCode, string expectedDoc)
    {
        var originalExit = Logger.Exit;
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Logger.Exit = false;
        Logger.ResetDiagnosticState();

        try
        {
            CleanExitException? capturedException = null;
            try
            {
                throw Logger.Error($"[{diagnosticCode}] sample message.");
            }
            catch (CleanExitException ex)
            {
                capturedException = ex;
            }

            Assert.NotNull(capturedException);
            var output = writer.ToString();
            Assert.Contains($"Hint: see {expectedDoc} for guidance on {diagnosticCode}.", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Logger.ResetDiagnosticState();
            Logger.Exit = originalExit;
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void DiagnosticSummary_PrintsAllOverrideDocsOnce()
    {
        var originalExit = Logger.Exit;
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Logger.Exit = false;
        Logger.ResetDiagnosticState();

        try
        {
            // Trigger two diagnostics that map to different override docs.
            try
            {
                throw Logger.Error("[ROBLOXCS3042] ref struct restriction.");
            }
            catch (CleanExitException)
            {
                // expected
            }

            try
            {
                throw Logger.Error("[ROBLOXCS3032] iterator helpers disabled.");
            }
            catch (CleanExitException)
            {
                // expected
            }

            var output = writer.ToString();
            Assert.Contains("docs/struct-ref-out-guidance.md", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/ts-iter-guardrails.md", output, StringComparison.OrdinalIgnoreCase);
            var structIndex = output.IndexOf("docs/struct-ref-out-guidance.md", StringComparison.OrdinalIgnoreCase);
            var iterIndex = output.IndexOf("docs/ts-iter-guardrails.md", StringComparison.OrdinalIgnoreCase);
            Assert.True(structIndex >= 0 && iterIndex >= 0 && structIndex < iterIndex, "Override docs should be ordered alphabetically in the summary.");
        }
        finally
        {
            Logger.ResetDiagnosticState();
            Logger.Exit = originalExit;
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void MaybeReportDiagnosticHint_HandlesMultipleCodesInSingleMessage()
    {
        var originalExit = Logger.Exit;
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Logger.Exit = false;
        Logger.ResetDiagnosticState();

        try
        {
            CleanExitException? capturedException = null;
            try
            {
                throw Logger.Error("ROBLOXCS3032 and [robloxcs3042] reported together");
            }
            catch (CleanExitException ex)
            {
                capturedException = ex;
            }

            Assert.NotNull(capturedException);
            var output = writer.ToString();
            Assert.Contains("ROBLOXCS3032: 1 occurrence", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3042: 1 occurrence", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/ts-iter-guardrails.md", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("docs/struct-ref-out-guidance.md", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Logger.ResetDiagnosticState();
            Logger.Exit = originalExit;
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void MaybeReportDiagnosticHint_DoesNotDoubleCountSameCodeInMessage()
    {
        var originalExit = Logger.Exit;
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Logger.Exit = false;
        Logger.ResetDiagnosticState();

        try
        {
            CleanExitException? capturedException = null;
            try
            {
                throw Logger.Error("ROBLOXCS3032 and ROBLOXCS3032 repeated");
            }
            catch (CleanExitException ex)
            {
                capturedException = ex;
            }

            Assert.NotNull(capturedException);
            var output = writer.ToString();
            Assert.Contains("ROBLOXCS3032: 1 occurrence", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Logger.ResetDiagnosticState();
            Logger.Exit = originalExit;
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void MaybeReportDiagnosticHint_HandlesEmptyMessage()
    {
        var originalExit = Logger.Exit;
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Logger.Exit = false;
        Logger.ResetDiagnosticState();

        try
        {
            CleanExitException? capturedException = null;
            try
            {
                throw Logger.Error(string.Empty);
            }
            catch (CleanExitException ex)
            {
                capturedException = ex;
            }

            Assert.NotNull(capturedException);
            var output = writer.ToString();
            Assert.DoesNotContain("ROBLOXCS", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Logger.ResetDiagnosticState();
            Logger.Exit = originalExit;
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void MaybeReportDiagnosticHint_IgnoresNullMessage()
    {
        var originalExit = Logger.Exit;
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Logger.Exit = false;
        Logger.ResetDiagnosticState();

        try
        {
            CleanExitException? capturedException = null;
            try
            {
                throw Logger.Error(null!);
            }
            catch (CleanExitException ex)
            {
                capturedException = ex;
            }

            Assert.NotNull(capturedException);
            var output = writer.ToString();
            Assert.DoesNotContain("ROBLOXCS", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Logger.ResetDiagnosticState();
            Logger.Exit = originalExit;
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void MaybeReportDiagnosticHint_HandlesCodesAcrossLines()
    {
        var originalExit = Logger.Exit;
        var originalOut = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        Logger.Exit = false;
        Logger.ResetDiagnosticState();

        try
        {
            CleanExitException? capturedException = null;
            try
            {
                throw Logger.Error("ROBLOXCS3032\n[ROBLOXCS3042]");
            }
            catch (CleanExitException ex)
            {
                capturedException = ex;
            }

            Assert.NotNull(capturedException);
            var output = writer.ToString();
            Assert.Contains("ROBLOXCS3032: 1 occurrence", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ROBLOXCS3042: 1 occurrence", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Logger.ResetDiagnosticState();
            Logger.Exit = originalExit;
            Console.SetOut(originalOut);
        }
    }

}
