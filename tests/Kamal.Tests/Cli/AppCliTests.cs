using Kamal.Execution;

namespace Kamal.Tests.Cli;

/// <summary>Port of the high-value parts of <c>test/cli/app_test.rb</c>.</summary>
[Collection("kamal-config")]
public sealed class AppCliTests
{
   private const string SingleHostDeploy =
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
      """;

   [Fact]
   public async Task BootHappyPathBootsAndDeploysToProxy()
   {
      using var harness = new CliTestHarness(SingleHostDeploy);

      harness.Responders.Add((host, command) =>
      {
         if (command.Contains("name=^app-web-999$") && harness.CommandsOn(host).Any(c => c.Contains("docker run --detach")))
            return new RunResult(0, "abc12345678\n", "");

         return null;
      });

      var exitCode = await harness.Run("app", "boot", "--version", "999");

      Assert.Equal(0, exitCode);
      Assert.Contains("Start container with version 999 (or reboot if already running)...", harness.Output);

      var commands = harness.CommandsOn("1.1.1.1");
      var run = commands.FindIndex(command => command.Contains("docker run --detach") && command.Contains("--name app-web-999"));
      var deploy = commands.FindIndex(command => command.Contains("kamal-proxy deploy app-web") && command.Contains("--target=\"abc12345678:80\""));
      var tag = commands.FindIndex(command => command.Contains("docker tag dhh/app:999 dhh/app:latest"));

      Assert.True(run >= 0, $"expected an app docker run, got: {string.Join("\n", commands)}");
      Assert.True(deploy > run, "expected the proxy deploy after the container run");
      Assert.True(tag > deploy, "expected the latest tag once booted");

      // The secrets env file is uploaded before the boot.
      Assert.Contains(commands, command => command.Contains("mkdir -p .kamal/apps/app/env/roles"));
   }

   [Fact]
   public async Task BootRenamesClashingContainer()
   {
      using var harness = new CliTestHarness(SingleHostDeploy);
      harness.RespondTo("name=^app-web-999$", "1234abcd\n");

      Assert.Equal(0, await harness.Run("app", "boot", "--version", "999"));

      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("docker rename app-web-999 app-web-999_replaced_"));
      Assert.Contains("Renaming container 999 to 999_replaced_", harness.Output);
   }

   [Fact]
   public async Task BootFailsWhenEndpointIsMissing()
   {
      using var harness = new CliTestHarness(SingleHostDeploy);

      var exitCode = await harness.Run("app", "boot", "--version", "999");

      Assert.Equal(1, exitCode);
      Assert.Contains("Failed to get endpoint for web on 1.1.1.1, did the container boot?", harness.Output);

      // The failed container is stopped and the lock released.
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("docker stop"));
      Assert.Contains("Releasing the deploy lock...", harness.Output);
   }

   [Fact]
   public async Task ExecReuseRunsInExistingContainer()
   {
      using var harness = new CliTestHarness(SingleHostDeploy);
      harness.RespondTo("--latest", "999\n");
      harness.RespondTo("docker exec app-web-999", "ruby 3.1.0\n");

      var exitCode = await harness.Run("app", "exec", "--reuse", "ruby -v");

      Assert.Equal(0, exitCode);
      Assert.Contains("Get current version of running container...", harness.Output);
      Assert.Contains("Launching command with version 999 from existing container...", harness.Output);
      Assert.Contains("App Host: 1.1.1.1", harness.Output);
      Assert.Contains("ruby 3.1.0", harness.Output);
   }

   [Fact]
   public async Task ExecWithoutCommandFails()
   {
      using var harness = new CliTestHarness(SingleHostDeploy);

      var exitCode = await harness.Run("app", "exec");

      Assert.Equal(1, exitCode);
      Assert.Contains("No command provided. You must specify a command to execute.", harness.Output);
   }

   [Fact]
   public async Task ExecDetachIsIncompatibleWithReuse()
   {
      using var harness = new CliTestHarness(SingleHostDeploy);

      var exitCode = await harness.Run("app", "exec", "--detach", "--reuse", "ls");

      Assert.Equal(1, exitCode);
      Assert.Contains("Detach is not compatible with reuse", harness.Output);
   }

   [Fact]
   public async Task StaleContainersStopsOldVersions()
   {
      using var harness = new CliTestHarness(SingleHostDeploy);
      harness.Responders.Add((_, command) =>
         command.Contains("while read line") && !command.Contains("--latest") ? new RunResult(0, "123\n456\n", "") : null);
      harness.RespondTo("--latest", "456\n");

      var exitCode = await harness.Run("app", "stale_containers", "--stop");

      Assert.Equal(0, exitCode);
      Assert.Contains("Stopping stale container for role web with version 123", harness.Output);
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("name=^app-web-123$") && command.Contains("docker stop"));
   }

   [Fact]
   public async Task StaleContainersDetectsWithoutStopping()
   {
      using var harness = new CliTestHarness(SingleHostDeploy);
      harness.Responders.Add((_, command) =>
         command.Contains("while read line") && !command.Contains("--latest") ? new RunResult(0, "123\n", "") : null);
      harness.RespondTo("--latest", "999\n");

      var exitCode = await harness.Run("app", "stale_containers");

      Assert.Equal(0, exitCode);
      Assert.Contains("Detected stale container for role web with version 123 (use `kamal app stale_containers --stop` to stop)", harness.Output);
      Assert.DoesNotContain(harness.AllCommands, command => command.Contains("docker stop"));
   }

   [Fact]
   public async Task VersionShowsRunningVersionByHost()
   {
      using var harness = new CliTestHarness(SingleHostDeploy);
      harness.RespondTo("--latest", "150\n");

      Assert.Equal(0, await harness.Run("app", "version"));

      Assert.Contains("App Host: 1.1.1.1", harness.Output);
      Assert.Contains("150", harness.Output);
   }

   [Fact]
   public async Task LogsShowsNothingFoundOnFailure()
   {
      using var harness = new CliTestHarness(SingleHostDeploy);
      harness.RespondTo("docker logs", "", exitCode: 1, stderr: "no container\n");

      Assert.Equal(0, await harness.Run("app", "logs"));

      Assert.Contains("Nothing found", harness.Output);
   }
}
