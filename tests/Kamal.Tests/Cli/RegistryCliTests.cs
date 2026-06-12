namespace Kamal.Tests.Cli;

/// <summary>Port of the high-value parts of <c>test/cli/registry_test.rb</c>.</summary>
[Collection("kamal-config")]
public sealed class RegistryCliTests
{
   [Fact]
   public async Task LoginLogsInLocallyAndRemotely()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("registry", "login");

      Assert.Equal(0, exitCode);

      Assert.Contains(harness.CommandsOn("localhost"), command => command.Contains("docker login"));
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("docker login"));
      Assert.Contains(harness.CommandsOn("1.1.1.2"), command => command.Contains("docker login"));
   }

   [Fact]
   public async Task LoginSkipsLocalWhenAsked()
   {
      using var harness = new CliTestHarness();

      Assert.Equal(0, await harness.Run("registry", "login", "-L"));

      Assert.DoesNotContain(harness.CommandsOn("localhost"), command => command.Contains("docker login"));
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("docker login"));
   }

   [Fact]
   public async Task LogoutLogsOutLocallyAndRemotely()
   {
      using var harness = new CliTestHarness();

      Assert.Equal(0, await harness.Run("registry", "logout"));

      Assert.Contains(harness.CommandsOn("localhost"), command => command.Contains("docker logout"));
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("docker logout"));
      Assert.Contains(harness.CommandsOn("1.1.1.2"), command => command.Contains("docker logout"));
   }
}
