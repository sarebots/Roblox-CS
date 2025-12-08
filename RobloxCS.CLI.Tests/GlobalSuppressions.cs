using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Usage", "CA1416:Validate platform compatibility", Justification = "Tests guard for OS before executing platform-specific code", Scope = "member", Target = "~M:RobloxCS.CLI.Tests.RuntimeSpecRunnerTests.Run_InvokesLuneExecutableWhenHarnessExists")]
[assembly: SuppressMessage("Usage", "CA1416:Validate platform compatibility", Justification = "Tests guard for OS before executing platform-specific code", Scope = "member", Target = "~M:RobloxCS.CLI.Tests.RuntimeSpecRunnerTests.Run_ReportsFailureWhenLuneFails")]
