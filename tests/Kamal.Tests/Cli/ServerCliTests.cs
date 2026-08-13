using Kamal.Cli;

namespace Kamal.Tests.Cli;

/// <summary>Port of the high-value parts of <c>test/cli/server_test.rb</c>.</summary>
[Collection("kamal-config")]
public sealed class ServerCliTests
{
   [Fact]
   public async Task ExecWritesOutputPerHost()
   {
      using var harness = new CliTestHarness();
      harness.RespondTo("date", "Today\n");

      var exitCode = await harness.Run("server", "exec", "date");

      Assert.Equal(0, exitCode);
      Assert.Contains("Running 'date' on 1.1.1.1, 1.1.1.2...", harness.Output);
      Assert.Contains("App Host: 1.1.1.1", harness.Output);
      Assert.Contains("App Host: 1.1.1.2", harness.Output);
      Assert.Contains("Today", harness.Output);
   }

   [Fact]
   public async Task ExecRawWritesStdoutVerbatim()
   {
      using var harness = new CliTestHarness();
      harness.RespondTo("date", "Today");

      var exitCode = await harness.Run("server", "exec", "date", "--raw");

      Assert.Equal(0, exitCode);
      Assert.Contains("TodayToday", harness.Output);
      Assert.DoesNotContain("App Host:", harness.Output);
      Assert.DoesNotContain("Running 'date'", harness.Output);
   }

   [Fact]
   public async Task ExecRawIsIncompatibleWithInteractive()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("server", "exec", "date", "--raw", "--interactive");

      Assert.Equal(1, exitCode);
      Assert.Contains("Raw is not compatible with interactive", harness.Output);
   }
}
