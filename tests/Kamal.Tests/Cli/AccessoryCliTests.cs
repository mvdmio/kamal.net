namespace Kamal.Tests.Cli;

/// <summary>Port of the high-value parts of <c>test/cli/accessory_test.rb</c>.</summary>
[Collection("kamal-config")]
public sealed class AccessoryCliTests
{
   private const string DeployWithAccessory =
      """
      service: app
      image: dhh/app
      servers:
        - 1.1.1.1
      registry:
        username: user
        password: pw
      builder:
        arch: amd64
      accessories:
        mysql:
          image: mysql:8.0
          host: 1.1.1.3
          port: 3306
      """;

   [Fact]
   public async Task BootStartsTheAccessory()
   {
      using var harness = new CliTestHarness(DeployWithAccessory);

      var exitCode = await harness.Run("accessory", "boot", "mysql");

      Assert.Equal(0, exitCode);

      var commands = harness.CommandsOn("1.1.1.3");
      Assert.Contains(commands, command => command.Contains("docker login"));
      Assert.Contains(commands, command => command.Contains("docker network create kamal"));
      Assert.Contains(commands, command => command.Contains("docker run --name app-mysql") && command.Contains("mysql:8.0") && command.Contains("--publish 3306"));
   }

   [Fact]
   public async Task BootSkipsHostsWithExistingContainer()
   {
      using var harness = new CliTestHarness(DeployWithAccessory);
      harness.RespondTo("docker ps -a -q --filter label=service=app-mysql", "12345\n");

      var exitCode = await harness.Run("accessory", "boot", "mysql");

      Assert.Equal(0, exitCode);
      Assert.Contains("Skipping booting `mysql` on 1.1.1.3, a container already exists", harness.Output);
      Assert.DoesNotContain(harness.CommandsOn("1.1.1.3"), command => command.Contains("docker run --name app-mysql"));
   }

   [Fact]
   public async Task BootAllBootsEveryAccessory()
   {
      using var harness = new CliTestHarness(DeployWithAccessory);

      Assert.Equal(0, await harness.Run("accessory", "boot", "all"));

      Assert.Contains(harness.CommandsOn("1.1.1.3"), command => command.Contains("docker run --name app-mysql"));
   }

   [Fact]
   public async Task RemoveWithConfirmationRemovesEverything()
   {
      using var harness = new CliTestHarness(DeployWithAccessory);

      var exitCode = await harness.Run("accessory", "remove", "mysql", "-y");

      Assert.Equal(0, exitCode);

      var commands = harness.CommandsOn("1.1.1.3");
      Assert.Contains(commands, command => command.Contains("docker container stop app-mysql"));
      Assert.Contains(commands, command => command.Contains("docker container prune --force --filter label=service=app-mysql"));
      Assert.Contains(commands, command => command.Contains("docker image rm --force mysql:8.0"));
      Assert.Contains(commands, command => command.Contains("rm -rf app-mysql"));
   }

   [Fact]
   public async Task UnknownAccessoryReportsError()
   {
      using var harness = new CliTestHarness(DeployWithAccessory);

      var exitCode = await harness.Run("accessory", "details", "nope");

      Assert.Equal(0, exitCode);
      Assert.Contains("No accessory by the name of 'nope' (options: mysql)", harness.ErrorOutput);
   }

   [Fact]
   public async Task LogsShowsAccessoryLogs()
   {
      using var harness = new CliTestHarness(DeployWithAccessory);
      harness.RespondTo("docker logs app-mysql", "mysql ready\n");

      Assert.Equal(0, await harness.Run("accessory", "logs", "mysql"));

      Assert.Contains("mysql ready", harness.Output);
   }
}
