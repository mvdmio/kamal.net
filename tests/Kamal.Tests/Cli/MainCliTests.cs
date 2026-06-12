namespace Kamal.Tests.Cli;

/// <summary>Port of the high-value parts of <c>test/cli/main_test.rb</c>.</summary>
[Collection("kamal-config")]
public sealed class MainCliTests
{
   [Fact]
   public async Task VersionPrintsKamalVersion()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("version");

      Assert.Equal(0, exitCode);
      Assert.Contains("2.11.0 (kamal.net)", harness.Output);
   }

   [Fact]
   public async Task DocsPrintsConfigurationDoc()
   {
      using var harness = new CliTestHarness();

      Assert.Equal(0, await harness.Run("docs"));
      Assert.Contains("Kamal Configuration", harness.Output);
   }

   [Fact]
   public async Task DocsSectionPrintsSectionDoc()
   {
      using var harness = new CliTestHarness();

      Assert.Equal(0, await harness.Run("docs", "proxy"));
      Assert.Contains("kamal-proxy", harness.Output);
   }

   [Fact]
   public async Task DocsUnknownSectionPrintsNotFound()
   {
      using var harness = new CliTestHarness();

      Assert.Equal(0, await harness.Run("docs", "foo"));
      Assert.Contains("No documentation found for foo", harness.Output);
   }

   [Fact]
   public async Task InitCreatesConfigSecretsAndHooks()
   {
      using var harness = new CliTestHarness(deployYml: null);

      Assert.Equal(0, await harness.Run("init"));

      Assert.Contains("Created configuration file in config/deploy.yml", harness.Output);
      Assert.Contains("Created .kamal/secrets file", harness.Output);
      Assert.Contains("Created sample hooks in .kamal/hooks", harness.Output);

      Assert.True(File.Exists(Path.Combine(harness.Dir, "config", "deploy.yml")));
      Assert.True(File.Exists(Path.Combine(harness.Dir, ".kamal", "secrets")));
      Assert.True(File.Exists(Path.Combine(harness.Dir, ".kamal", "hooks", "pre-deploy.sample")));
      Assert.True(File.Exists(Path.Combine(harness.Dir, ".kamal", "hooks", "docker-setup.sample")));

      Assert.Contains("service: my-app", File.ReadAllText(Path.Combine(harness.Dir, "config", "deploy.yml")));
   }

   [Fact]
   public async Task InitDoesNotOverwriteExistingConfig()
   {
      using var harness = new CliTestHarness(deployYml: null);

      Assert.Equal(0, await harness.Run("init"));
      Assert.Equal(0, await harness.Run("init"));

      Assert.Contains("Config file already exists in config/deploy.yml (remove first to create a new one)", harness.Output);
   }

   [Fact]
   public async Task ConfigPrintsRedactedConfigYaml()
   {
      using var harness = new CliTestHarness();

      Assert.Equal(0, await harness.Run("config"));

      Assert.Contains("service_with_version: app-999", harness.Output);
      Assert.Contains("primary_host: 1.1.1.1", harness.Output);
      Assert.Contains("dhh/app:999", harness.Output);
   }

   [Fact]
   public async Task AuditShowsAuditLogByHost()
   {
      using var harness = new CliTestHarness();
      harness.RespondTo("tail -n 50 .kamal/app-audit.log", "deploy events\n");

      Assert.Equal(0, await harness.Run("audit"));

      Assert.Contains("App Host: 1.1.1.1", harness.Output);
      Assert.Contains("App Host: 1.1.1.2", harness.Output);
      Assert.Contains("deploy events", harness.Output);
   }

   [Fact]
   public async Task DeployWithSkipPushRunsTheFullSequenceUnderTheLock()
   {
      using var harness = new CliTestHarness();

      // The endpoint lookup must return a container id once the app container has been run.
      harness.Responders.Add((host, command) =>
      {
         if (command.Contains("name=^app-web-999$") && harness.CommandsOn(host).Any(c => c.Contains("docker run --detach")))
            return new Kamal.Execution.RunResult(0, "abc12345678\n", "");

         return null;
      });

      var exitCode = await harness.Run("deploy", "--skip-push");

      Assert.Equal(0, exitCode);

      Assert.Contains("Pull app image...", harness.Output);
      Assert.Contains("Acquiring the deploy lock...", harness.Output);
      Assert.Contains("Ensure kamal-proxy is running...", harness.Output);
      Assert.Contains("Detect stale containers...", harness.Output);
      Assert.Contains("Prune old containers and images...", harness.Output);
      Assert.Contains("Releasing the deploy lock...", harness.Output);
      Assert.Contains("Finished all in", harness.Output);

      var primary = harness.CommandsOn("1.1.1.1");

      int IndexOf(string fragment) => primary.FindIndex(command => command.Contains(fragment));

      var pull = IndexOf("docker pull dhh/app:999");
      var acquireLock = IndexOf("mkdir .kamal/lock-app");
      var proxyRun = IndexOf("docker container start kamal-proxy");
      var appRun = IndexOf("docker run --detach");
      var proxyDeploy = IndexOf("kamal-proxy deploy app-web");
      var tagLatest = IndexOf("docker tag dhh/app:999 dhh/app:latest");
      var prune = IndexOf("docker image prune");
      var releaseLock = IndexOf("rm -r .kamal/lock-app");

      Assert.True(pull >= 0, "expected image pull");
      Assert.True(acquireLock > pull, "lock should be acquired after the pull");
      Assert.True(proxyRun > acquireLock, "proxy should boot under the lock");
      Assert.True(appRun > proxyRun, "app should boot after the proxy");
      Assert.True(proxyDeploy > appRun, "proxy deploy should happen after the app boot");
      Assert.True(tagLatest > proxyDeploy, "latest tag should be applied after boot");
      Assert.True(prune > tagLatest, "prune should run after boot");
      Assert.True(releaseLock > prune, "lock should be released last");

      // Both hosts boot the app.
      Assert.Contains(harness.CommandsOn("1.1.1.2"), command => command.Contains("docker run --detach"));
   }

   [Fact]
   public async Task DeployFailsWithLockErrorWhenLockIsTaken()
   {
      using var harness = new CliTestHarness();
      harness.RespondTo("mkdir .kamal/lock-app", "", exitCode: 1, stderr: "mkdir: cannot create directory '.kamal/lock-app': File exists\n");

      var exitCode = await harness.Run("deploy", "--skip-push");

      Assert.Equal(1, exitCode);
      Assert.Contains("Deploy lock already in place!", harness.Output);
      Assert.Contains("Deploy lock found. Run 'kamal lock help' for more information", harness.Output);
   }

   [Fact]
   public async Task RollbackToUnavailableVersionDoesNotBoot()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("rollback", "123");

      Assert.Equal(0, exitCode);
      Assert.Contains("The app version '123' is not available as a container (use 'kamal app containers' for available versions)", harness.Output);
      Assert.DoesNotContain(harness.AllCommands, command => command.Contains("docker run --detach"));
   }

   [Fact]
   public async Task RollbackToAvailableVersionBootsThatVersion()
   {
      using var harness = new CliTestHarness();
      harness.RespondTo("name=^app-web-123$", "abc12345678\n");

      var exitCode = await harness.Run("rollback", "123");

      Assert.Equal(0, exitCode);
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("docker run --detach") && command.Contains("app-web-123"));
   }

   [Fact]
   public async Task RemoveIsAbortedWithoutConfirmation()
   {
      using var harness = new CliTestHarness();
      Kamal.Cli.CliBase.AskHandler = () => "N";

      var exitCode = await harness.Run("remove");

      Assert.Equal(0, exitCode);
      Assert.Contains("Aborted", harness.Output);
      Assert.Empty(harness.AllCommands);
   }
}
