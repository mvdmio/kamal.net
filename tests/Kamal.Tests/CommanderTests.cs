using Kamal.Configuration;
using Kamal.Execution;
using Kamal.Output;

namespace Kamal.Tests;

/// <summary>Port of <c>test/commander_test.rb</c>.</summary>
[Collection("kamal-config")]
public sealed class CommanderTests : IDisposable
{
   private const string DeployWithRoles =
      """
      service: app
      image: dhh/app
      servers:
        web:
          - 1.1.1.1
          - 1.1.1.2
        workers:
          hosts:
            - 1.1.1.3
            - 1.1.1.4
      env:
        REDIS_URL: redis://x/y
      registry:
        server: registry.digitalocean.com
        username: user
        password: pw
      builder:
        arch: amd64
      deploy_timeout: 1
      """;

   private const string DeployWithTwoRolesOneHost =
      """
      service: app
      image: dhh/app
      servers:
        workers:
          hosts:
            - 1.1.1.1
        web:
          hosts:
            - 1.1.1.1
      registry:
        server: registry.digitalocean.com
        username: user
        password: pw
      builder:
        arch: amd64
      """;

   private const string DeployPrimaryWebRoleOverride =
      """
      service: app
      image: dhh/app
      servers:
        web_chicago:
          proxy: {}
          hosts:
            - 1.1.1.1
            - 1.1.1.2
        web_tokyo:
          proxy: {}
          hosts:
            - 1.1.1.3
            - 1.1.1.4
      registry:
        server: registry.digitalocean.com
        username: user
        password: pw
      builder:
        arch: amd64
      primary_role: web_tokyo
      """;

   private const string DeployWithMultipleProxyRoles =
      """
      service: app
      image: dhh/app
      servers:
        web:
          hosts:
            - 1.1.1.1
            - 1.1.1.2
          proxy: true
        web_tokyo:
          hosts:
            - 1.1.1.3
            - 1.1.1.4
          proxy: true
        workers:
          cmd: bin/jobs
          hosts:
            - 1.1.1.1
            - 1.1.1.2
        workers_tokyo:
          cmd: bin/jobs
          hosts:
            - 1.1.1.3
            - 1.1.1.4
      builder:
        arch: amd64
      registry:
        server: registry.digitalocean.com
        username: user
        password: pw
      """;

   private const string DeployWithSingleAccessory =
      """
      service: app
      image: dhh/app
      servers:
        web:
          - "1.1.1.1"
          - "1.1.1.2"
        workers:
          - "1.1.1.3"
          - "1.1.1.4"
      registry:
        username: user
        password: pw
      builder:
        arch: amd64
      accessories:
        mysql:
          image: mysql:5.7
          host: 1.1.1.5
          port: 3306
          env:
            clear:
              MYSQL_ROOT_HOST: '%'
            secret:
              - MYSQL_ROOT_PASSWORD
          directories:
            - data:/var/lib/mysql
      """;

   private const string DeployWithAccessoriesOnIndependentServer =
      """
      service: app
      image: dhh/app
      servers:
        web:
          - "1.1.1.1"
          - "1.1.1.2"
        workers:
          - "1.1.1.3"
          - "1.1.1.4"
      registry:
        username: user
        password: pw
      builder:
        arch: amd64
      accessories:
        mysql:
          image: mysql:5.7
          host: 1.1.1.5
          port: 3306
          directories:
            - data:/var/lib/mysql
        redis:
          image: redis:latest
          roles:
            - web
          port: 6379
          directories:
            - data:/data
      readiness_delay: 0
      """;

   private const string DeployWithRolesWorkersPrimary =
      """
      service: app
      image: dhh/app
      servers:
        workers:
          - 1.1.1.1
          - 1.1.1.2
        web:
          - 1.1.1.3
          - 1.1.1.4
      registry:
        server: registry.digitalocean.com
        username: user
        password: pw
      builder:
        arch: amd64
      primary_role: workers
      """;

   private readonly List<string> _tempDirs = new();

   public void Dispose()
   {
      KamalOutput.Reset();

      foreach (var dir in _tempDirs)
      {
         try
         {
            Directory.Delete(dir, recursive: true);
         }
         catch (IOException)
         {
         }
      }
   }

   private Commander ConfigureWith(string yaml)
   {
      var dir = Path.Combine(Path.GetTempPath(), "kamal-commander-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      _tempDirs.Add(dir);

      var configFile = Path.Combine(dir, "deploy.yml");
      File.WriteAllText(configFile, yaml);

      var commander = new Commander();
      commander.Configure(configFile: configFile);

      return commander;
   }

   [Fact]
   public void LazyConfiguration()
   {
      var kamal = ConfigureWith(DeployWithRoles);

      Assert.True(kamal.Configured);
      Assert.IsType<KamalConfiguration>(kamal.Config);
   }

   [Fact]
   public void OverwritingHosts()
   {
      var kamal = ConfigureWith(DeployWithRoles);
      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3", "1.1.1.4"], kamal.Hosts);

      kamal.SetSpecificHosts(["1.1.1.1", "1.1.1.2"]);
      Assert.Equal(["1.1.1.1", "1.1.1.2"], kamal.Hosts);

      kamal.SetSpecificHosts(["1.1.1.1*"]);
      Assert.Equal(["1.1.1.1"], kamal.Hosts);

      kamal.SetSpecificHosts(["1.1.1.*", "*.1.2.*"]);
      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3", "1.1.1.4"], kamal.Hosts);

      kamal.SetSpecificHosts(["*"]);
      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3", "1.1.1.4"], kamal.Hosts);

      kamal.SetSpecificHosts(["1.1.1.[12]"]);
      Assert.Equal(["1.1.1.1", "1.1.1.2"], kamal.Hosts);

      var exception = Assert.Throws<ArgumentException>(() => kamal.SetSpecificHosts(["*miss"]));
      Assert.Contains("hosts match for *miss", exception.Message);
   }

   [Fact]
   public void FilteringHostsByFilteringRoles()
   {
      var kamal = ConfigureWith(DeployWithRoles);
      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3", "1.1.1.4"], kamal.Hosts);

      kamal.SetSpecificRoles(["web"]);
      Assert.Equal(["1.1.1.1", "1.1.1.2"], kamal.Hosts);

      var exception = Assert.Throws<ArgumentException>(() => kamal.SetSpecificRoles(["*miss"]));
      Assert.Contains("roles match for *miss", exception.Message);
   }

   [Fact]
   public void FilteringRoles()
   {
      var kamal = ConfigureWith(DeployWithRoles);
      Assert.Equal(["web", "workers"], kamal.Roles.Select(role => role.Name));

      kamal.SetSpecificRoles(["workers"]);
      Assert.Equal(["workers"], kamal.Roles.Select(role => role.Name));

      kamal.SetSpecificRoles(["w*"]);
      Assert.Equal(["web", "workers"], kamal.Roles.Select(role => role.Name));

      kamal.SetSpecificRoles(["we*", "*orkers"]);
      Assert.Equal(["web", "workers"], kamal.Roles.Select(role => role.Name));

      kamal.SetSpecificRoles(["*"]);
      Assert.Equal(["web", "workers"], kamal.Roles.Select(role => role.Name));

      kamal.SetSpecificRoles(["w{eb,orkers}"]);
      Assert.Equal(["web", "workers"], kamal.Roles.Select(role => role.Name));

      var exception = Assert.Throws<ArgumentException>(() => kamal.SetSpecificRoles(["*miss"]));
      Assert.Contains("roles match for *miss", exception.Message);
   }

   [Fact]
   public void FilteringRolesByFilteringHosts()
   {
      var kamal = ConfigureWith(DeployWithRoles);
      Assert.Equal(["web", "workers"], kamal.Roles.Select(role => role.Name));

      kamal.SetSpecificHosts(["1.1.1.3"]);
      Assert.Equal(["workers"], kamal.Roles.Select(role => role.Name));
   }

   [Fact]
   public void OverwritingHostsWithPrimary()
   {
      var kamal = ConfigureWith(DeployWithRoles);
      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3", "1.1.1.4"], kamal.Hosts);

      kamal.SpecificPrimary();
      Assert.Equal(["1.1.1.1"], kamal.Hosts);
   }

   [Fact]
   public void PrimaryHostWithSpecificHostsViaRole()
   {
      var kamal = ConfigureWith(DeployWithRoles);

      kamal.SetSpecificRoles("workers");
      Assert.Equal("1.1.1.3", kamal.PrimaryHost);
   }

   [Fact]
   public void PrimaryRole()
   {
      var kamal = ConfigureWith(DeployWithRoles);
      Assert.Equal("web", kamal.PrimaryRole?.Name);

      kamal.SetSpecificRoles("workers");
      Assert.Equal("workers", kamal.PrimaryRole?.Name);
   }

   [Fact]
   public void RolesOn()
   {
      var kamal = ConfigureWith(DeployWithRoles);

      Assert.Equal(["web"], kamal.RolesOn("1.1.1.1").Select(role => role.Name));
      Assert.Equal(["workers"], kamal.RolesOn("1.1.1.3").Select(role => role.Name));
   }

   [Fact]
   public void RolesOnWebComesFirst()
   {
      var kamal = ConfigureWith(DeployWithTwoRolesOneHost);

      Assert.Equal(["web", "workers"], kamal.RolesOn("1.1.1.1").Select(role => role.Name));
   }

   [Fact]
   public void MatchesThePrimaryRoleFromAListOfSpecificRoles()
   {
      var kamal = ConfigureWith(DeployPrimaryWebRoleOverride);

      kamal.SetSpecificRoles(["web_*"]);
      Assert.Equal(["web_tokyo", "web_chicago"], kamal.Roles.Select(role => role.Name));
      Assert.Equal("web_tokyo", kamal.PrimaryRole?.Name);
      Assert.Equal("1.1.1.3", kamal.PrimaryHost);
      Assert.Equal(["1.1.1.3", "1.1.1.4", "1.1.1.1", "1.1.1.2"], kamal.Hosts);
   }

   [Fact]
   public void ProxyHostsObserveFilteredRoles()
   {
      var kamal = ConfigureWith(DeployWithMultipleProxyRoles);

      kamal.SetSpecificRoles(["web_tokyo"]);
      Assert.Equal(["1.1.1.3", "1.1.1.4"], kamal.ProxyHosts);
   }

   [Fact]
   public void ProxyHostsObserveFilteredHosts()
   {
      var kamal = ConfigureWith(DeployWithMultipleProxyRoles);

      kamal.SetSpecificHosts(["1.1.1.2"]);
      Assert.Equal(["1.1.1.2"], kamal.ProxyHosts);
   }

   [Fact]
   public void AccessoryHostsWithoutFiltering()
   {
      var kamal = ConfigureWith(DeployWithSingleAccessory);
      Assert.Equal(["1.1.1.5"], kamal.AccessoryHosts);

      kamal = ConfigureWith(DeployWithAccessoriesOnIndependentServer);
      Assert.Equal(["1.1.1.5", "1.1.1.1", "1.1.1.2"], kamal.AccessoryHosts);
   }

   [Fact]
   public void AccessoryHostsWithRoleFiltering()
   {
      var kamal = ConfigureWith(DeployWithSingleAccessory);
      kamal.SetSpecificRoles(["web"]);
      Assert.Equal([], kamal.AccessoryHosts);

      kamal = ConfigureWith(DeployWithAccessoriesOnIndependentServer);
      kamal.SetSpecificRoles(["web"]);
      Assert.Equal(["1.1.1.1", "1.1.1.2"], kamal.AccessoryHosts);

      kamal.SetSpecificRoles(["workers"]);
      Assert.Equal([], kamal.AccessoryHosts);
   }

   [Fact]
   public void PrimaryRoleHostsAreFirst()
   {
      var kamal = ConfigureWith(DeployWithRolesWorkersPrimary);

      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3", "1.1.1.4"], kamal.Hosts);
      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3", "1.1.1.4"], kamal.AppHosts);
   }

   [Fact]
   public void WithSpecificHostsRestoresPrimaryHostAfterBlock()
   {
      var kamal = ConfigureWith(DeployWithRoles);
      var originalPrimary = kamal.PrimaryHost;
      Assert.Equal("1.1.1.1", originalPrimary);

      kamal.WithSpecificHosts("1.1.1.3", () => Assert.Equal("1.1.1.3", kamal.PrimaryHost));

      Assert.Equal(originalPrimary, kamal.PrimaryHost);
   }

   [Fact]
   public void WithSpecificHostsRestoresPrimaryHostAfterIteratingMultipleHosts()
   {
      var kamal = ConfigureWith(DeployWithRoles);
      var originalPrimary = kamal.PrimaryHost;
      var hostsVisited = new List<string?>();

      foreach (var host in kamal.Hosts)
         kamal.WithSpecificHosts(host, () => hostsVisited.Add(kamal.PrimaryHost));

      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3", "1.1.1.4"], hostsVisited);
      Assert.Equal(originalPrimary, kamal.PrimaryHost);
   }

   [Fact]
   public void WithSpecificHostsRestoresPrimaryHostEvenAfterException()
   {
      var kamal = ConfigureWith(DeployWithRoles);
      var originalPrimary = kamal.PrimaryHost;

      Assert.Throws<InvalidOperationException>(() =>
         kamal.WithSpecificHosts("1.1.1.3", () => throw new InvalidOperationException("test error")));

      Assert.Equal(originalPrimary, kamal.PrimaryHost);
   }

   [Fact]
   public void ResetRestoresInitialState()
   {
      var kamal = ConfigureWith(DeployWithRoles);
      _ = kamal.Config;
      kamal.SetSpecificRoles("workers");
      kamal.Verbosity = Verbosity.Debug;
      kamal.Connected = true;
      kamal.Logging = true;

      kamal.Reset();

      Assert.False(kamal.Configured);
      Assert.Null(kamal.SpecificRoles);
      Assert.Null(kamal.SpecificHosts);
      Assert.Equal(Verbosity.Info, kamal.Verbosity);
      Assert.False(kamal.Connected);
      Assert.False(kamal.Logging);
   }

   [Fact]
   public void WithVerbosityRestoresTheGlobalVerbosity()
   {
      var kamal = ConfigureWith(DeployWithRoles);

      kamal.WithVerbosity(Verbosity.Debug, () =>
      {
         Assert.Equal(Verbosity.Debug, kamal.Verbosity);
         Assert.Equal(Verbosity.Debug, KamalOutput.Verbosity);
      });

      Assert.Equal(Verbosity.Info, kamal.Verbosity);
   }

   [Fact]
   public void ResolveAliasReadsTheRawConfigWithoutCreatingTheConfig()
   {
      var kamal = ConfigureWith(DeployWithRoles + "\naliases:\n  console: app exec -i \"bin/console\"\n");

      Assert.Equal("app exec -i \"bin/console\"", kamal.ResolveAlias("console"));
      Assert.Null(kamal.ResolveAlias("missing"));
   }

   [Fact]
   public void CommandBuilderAccessorsAreCachedWhereRubyCachesThem()
   {
      var kamal = ConfigureWith(DeployWithRoles);

      Assert.Same(kamal.Builder, kamal.Builder);
      Assert.Same(kamal.Docker, kamal.Docker);
      Assert.Same(kamal.Hook, kamal.Hook);
      Assert.Same(kamal.Lock, kamal.Lock);
      Assert.Same(kamal.Prune, kamal.Prune);
      Assert.Same(kamal.Registry, kamal.Registry);
      Assert.Same(kamal.Server, kamal.Server);

      Assert.NotSame(kamal.App(), kamal.App());
      Assert.NotSame(kamal.Proxy("1.1.1.1"), kamal.Proxy("1.1.1.1"));
   }

   [Fact]
   public void AccessoryNames()
   {
      var kamal = ConfigureWith(DeployWithAccessoriesOnIndependentServer);

      Assert.Equal(["mysql", "redis"], kamal.AccessoryNames);
   }

   [Fact]
   public async Task ModifyBroadcastsToConfiguredFileLoggers()
   {
      var logDir = Path.Combine(Path.GetTempPath(), "kamal-modify-logs-" + Guid.NewGuid().ToString("N"));
      _tempDirs.Add(logDir);

      var yaml = DeployWithRoles + $"\noutput:\n  file:\n    path: {logDir.Replace('\\', '/')}\n";
      var kamal = ConfigureWith(yaml);

      await kamal.Modify("deploy", null, () =>
      {
         kamal.Log("Booting app on all hosts");
         return Task.CompletedTask;
      });

      Assert.True(kamal.Logging);

      var file = Assert.Single(Directory.GetFiles(logDir, "*.log"));
      var contents = await File.ReadAllTextAsync(file);
      Assert.Contains("Booting app on all hosts", contents);
      Assert.Contains("# Completed in", contents);
      Assert.Contains("deploy", Path.GetFileName(file));
   }

   [Fact]
   public async Task ModifyRecordsFailures()
   {
      var logDir = Path.Combine(Path.GetTempPath(), "kamal-modify-logs-" + Guid.NewGuid().ToString("N"));
      _tempDirs.Add(logDir);

      var yaml = DeployWithRoles + $"\noutput:\n  file:\n    path: {logDir.Replace('\\', '/')}\n";
      var kamal = ConfigureWith(yaml);

      await Assert.ThrowsAsync<ExecuteError>(() =>
         kamal.Modify("deploy", null, () => throw new ExecuteError("1.1.1.1", "boot failed")));

      var file = Assert.Single(Directory.GetFiles(logDir, "*.log"));
      Assert.Contains("# FAILED: ExecuteError: boot failed", await File.ReadAllTextAsync(file));
   }
}
