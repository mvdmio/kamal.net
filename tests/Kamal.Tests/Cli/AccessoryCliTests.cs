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

   private const string DeployWithProxiedAccessory =
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
        web:
          image: nginx:latest
          host: 1.1.1.3
          proxy:
            host: web.example.com
      """;

   private const string RunningLookup = "docker container ls --filter 'name=^app-web$' --quiet";
   private const string ProxyRemove = "docker exec kamal-proxy kamal-proxy remove app-web";

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

   // The fake does not track container state, so the lookup must be shown to run before the stop,
   // and the route removal after it.
   private static void AssertRouteRemovedAfterLookupAndStop(List<string> commands)
   {
      var lookup = commands.FindIndex(command => command.Contains(RunningLookup));
      var stop = commands.FindIndex(command => command.Contains("docker container stop app-web"));
      var remove = commands.FindIndex(command => command.Contains(ProxyRemove));

      Assert.True(lookup >= 0, "running-container lookup did not run");
      Assert.True(stop >= 0, "container stop did not run");
      Assert.True(remove >= 0, "kamal-proxy remove did not run");
      Assert.True(lookup < stop, "running-container lookup ran after the container stop");
      Assert.True(stop < remove, "kamal-proxy remove ran before the container stop");
   }

   [Fact]
   public async Task StopRemovesProxyRouteWhenContainerWasRunning()
   {
      using var harness = new CliTestHarness(DeployWithProxiedAccessory);
      harness.RespondTo(RunningLookup, "abc123\n");

      var exitCode = await harness.Run("accessory", "stop", "web");

      Assert.Equal(0, exitCode);

      var commands = harness.CommandsOn("1.1.1.3");
      AssertRouteRemovedAfterLookupAndStop(commands);
   }

   [Fact]
   public async Task RemoveRemovesProxyRouteWhenContainerWasRunning()
   {
      using var harness = new CliTestHarness(DeployWithProxiedAccessory);
      harness.RespondTo(RunningLookup, "abc123\n");

      var exitCode = await harness.Run("accessory", "remove", "web", "-y");

      Assert.Equal(0, exitCode);

      var commands = harness.CommandsOn("1.1.1.3");
      AssertRouteRemovedAfterLookupAndStop(commands);
      Assert.Contains(commands, command => command.Contains("docker container prune --force --filter label=service=app-web"));
      Assert.Contains(commands, command => command.Contains("docker image rm --force nginx:latest"));
      Assert.Contains(commands, command => command.Contains("rm -rf app-web"));
   }

   [Fact]
   public async Task StopSkipsProxyRouteRemovalWhenContainerWasNotRunning()
   {
      using var harness = new CliTestHarness(DeployWithProxiedAccessory);

      var exitCode = await harness.Run("accessory", "stop", "web");

      Assert.Equal(0, exitCode);

      var commands = harness.CommandsOn("1.1.1.3");
      Assert.Contains(commands, command => command.Contains(RunningLookup));
      Assert.DoesNotContain(commands, command => command.Contains("kamal-proxy remove"));
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

   [Fact]
   public async Task ExecAfterEndOfOptionsKeepsGuestFlagsInRemoteArgv()
   {
      using var harness = new CliTestHarness(DeployWithAccessory);

      // Without --, recursive -c binds as config-file; after --, -c stays in remote argv.
      var exitCode = await harness.Run(
         "accessory", "exec", "mysql", "--reuse", "--", "sh", "-c", "SELECT 1");

      Assert.Equal(0, exitCode);
      Assert.Contains("Launching command from existing container...", harness.Output);

      var remote = Assert.Single(harness.CommandsOn("1.1.1.3"), command => command.Contains("docker exec"));
      Assert.Contains("docker exec app-mysql", remote);
      // Shell-escaped tokens preserve multi-word argv (SELECT 1) and the guest -c flag.
      Assert.Contains("\"sh\" \"-c\" \"SELECT 1\"", remote);
   }

   [Fact]
   public async Task ExecMultiWordArgvPreservesBoundariesInNewContainer()
   {
      using var harness = new CliTestHarness(DeployWithAccessory);

      var exitCode = await harness.Run(
         "accessory", "exec", "mysql", "--", "sh", "-c", "echo hello world");

      Assert.Equal(0, exitCode);

      var remote = Assert.Single(harness.CommandsOn("1.1.1.3"), command => command.Contains("docker run") && command.Contains("mysql:8.0"));
      Assert.Contains("\"sh\" \"-c\" \"echo hello world\"", remote);
   }
}
