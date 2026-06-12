using Kamal.Configuration;
using Kamal.Utils;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration_test.rb.</summary>
[Collection("kamal-config")]
public class ConfigurationTests : IDisposable
{
   private readonly EnvVarScope _versionScope = new("VERSION", "missing");

   private readonly Cfg _deploy;
   private readonly Cfg _deployWithRoles;

   public ConfigurationTests()
   {
      _deploy = BaseDeploy();

      _deployWithRoles = BaseDeploy();
      _deployWithRoles["servers"] = new Cfg
      {
         ["web"] = L("1.1.1.1", "1.1.1.2"),
         ["workers"] = new Cfg { ["hosts"] = L("1.1.1.1", "1.1.1.3") }
      };
   }

   public void Dispose()
   {
      _versionScope.Dispose();
   }

   private KamalConfiguration Config => new(_deploy);
   private KamalConfiguration ConfigWithRoles => new(_deployWithRoles);

   [Theory]
   [InlineData("service")]
   [InlineData("image")]
   [InlineData("registry")]
   public void RequiredConfigKeys(string key)
   {
      _deploy.Remove(key);
      Assert.Throws<KamalConfigurationError>(() => new KamalConfiguration(_deploy));
   }

   [Theory]
   [InlineData("username")]
   [InlineData("password")]
   public void RegistryCredentialsRequired(string key)
   {
      ((Cfg)_deploy["registry"]!).Remove(key);
      Assert.Throws<KamalConfigurationError>(() => new KamalConfiguration(_deploy));
   }

   [Fact]
   public void ImageUsesServiceNameIfRegistryIsLocal()
   {
      _deploy["registry"] = new Cfg { ["server"] = "localhost:5000" };
      _deploy.Remove("image");

      Assert.Equal("app", Config.Image);
   }

   [Fact]
   public void ImageUsesImageIfRegistryIsLocal()
   {
      _deploy["registry"] = new Cfg { ["server"] = "localhost:5000" };

      Assert.Equal("dhh/app", Config.Image);
   }

   [Fact]
   public void ServiceNameValid()
   {
      _deploy["service"] = "hey-app1_primary";
      _ = Config;

      _deploy["service"] = "MyApp";
      _ = Config;
   }

   [Fact]
   public void ServiceNameInvalid()
   {
      _deploy["service"] = "app.com";
      Assert.Throws<KamalConfigurationError>(() => Config);
   }

   [Fact]
   public void ServersRequired()
   {
      _deploy.Remove("servers");
      Assert.Throws<KamalConfigurationError>(() => Config);
   }

   [Fact]
   public void ServersNotRequiredWithAccessories()
   {
      _deploy.Remove("servers");
      _deploy["accessories"] = new Cfg
      {
         ["foo"] = new Cfg { ["image"] = "foo/bar", ["host"] = "1.1.1.1" }
      };

      _ = Config;
   }

   [Fact]
   public void Roles()
   {
      Assert.Equal(["web"], Config.Roles.Select(role => role.Name));
      Assert.Equal(["web", "workers"], ConfigWithRoles.Roles.Select(role => role.Name));
   }

   [Fact]
   public void RoleLookup()
   {
      Assert.Equal("web", Config.Role("web")!.Name);
      Assert.Equal("workers", ConfigWithRoles.Role("workers")!.Name);
      Assert.Null(Config.Role("missing"));
   }

   [Fact]
   public void AllHosts()
   {
      Assert.Equal(["1.1.1.1", "1.1.1.2"], Config.AllHosts);
      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3"], ConfigWithRoles.AllHosts);
   }

   [Fact]
   public void PrimaryHost()
   {
      Assert.Equal("1.1.1.1", Config.PrimaryHost);
      Assert.Equal("1.1.1.1", ConfigWithRoles.PrimaryHost);
   }

   [Fact]
   public void ProxyHosts()
   {
      Assert.Equal(["1.1.1.1", "1.1.1.2"], ConfigWithRoles.ProxyHosts);

      ((Cfg)((Cfg)_deployWithRoles["servers"]!)["workers"]!)["proxy"] = true;
      var config = new KamalConfiguration(_deployWithRoles);

      Assert.Equal(["1.1.1.1", "1.1.1.2", "1.1.1.3"], config.ProxyHosts);
   }

   [Fact]
   public void VersionNoGitRepo()
   {
      using var noVersion = new EnvVarScope("VERSION", null);
      using var git = new GitScope(new FakeGitRunner { UsedResult = false });

      var error = Assert.Throws<InvalidOperationException>(() => Config.Version);
      Assert.Contains("no git repository found", error.Message);
   }

   [Fact]
   public void VersionFromGitCommitted()
   {
      using var noVersion = new EnvVarScope("VERSION", null);
      var fake = new FakeGitRunner();
      fake.Outputs["rev-parse HEAD"] = "git-version";
      fake.Outputs["status --porcelain"] = "";
      using var git = new GitScope(fake);

      Assert.Equal("git-version", Config.Version);
   }

   [Fact]
   public void VersionFromGitUncommitted()
   {
      // With no builder context configured, a git clone build is used, so no uncommitted suffix.
      using var noVersion = new EnvVarScope("VERSION", null);
      var fake = new FakeGitRunner();
      fake.Outputs["rev-parse HEAD"] = "git-version";
      fake.Outputs["status --porcelain"] = "M   file\n";
      using var git = new GitScope(fake);

      Assert.Equal("git-version", Config.Version);
   }

   [Fact]
   public void VersionFromUncommittedContext()
   {
      using var noVersion = new EnvVarScope("VERSION", null);
      ((Cfg)_deploy["builder"]!)["context"] = ".";
      var config = Config;

      var fake = new FakeGitRunner();
      fake.Outputs["rev-parse HEAD"] = "git-version";
      fake.Outputs["status --porcelain"] = "M   file\n";
      using var git = new GitScope(fake);

      Assert.Matches("^git-version_uncommitted_[0-9a-f]{16}$", config.Version);
   }

   [Fact]
   public void VersionFromEnv()
   {
      using var version = new EnvVarScope("VERSION", "env-version");
      Assert.Equal("env-version", Config.Version);
   }

   [Fact]
   public void VersionFromArg()
   {
      var config = Config;
      config.Version = "arg-version";
      Assert.Equal("arg-version", config.Version);
   }

   [Fact]
   public void Repository()
   {
      Assert.Equal("dhh/app", Config.Repository);

      ((Cfg)_deploy["registry"]!)["server"] = "ghcr.io";
      Assert.Equal("ghcr.io/dhh/app", Config.Repository);
   }

   [Fact]
   public void AbsoluteImage()
   {
      Assert.Equal("dhh/app:missing", Config.AbsoluteImage);

      ((Cfg)_deploy["registry"]!)["server"] = "ghcr.io";
      Assert.Equal("ghcr.io/dhh/app:missing", Config.AbsoluteImage);
   }

   [Fact]
   public void ServiceWithVersion()
   {
      Assert.Equal("app-missing", Config.ServiceWithVersion);
   }

   [Fact]
   public void HostsRequiredForAllRoles()
   {
      // Empty server list for implied web role
      _deploy["servers"] = L();
      Assert.Throws<KamalConfigurationError>(() => Config);

      // Empty server list
      _deploy["servers"] = new Cfg { ["web"] = L() };
      Assert.Throws<KamalConfigurationError>(() => Config);

      // Missing hosts key
      _deploy["servers"] = new Cfg { ["web"] = new Cfg() };
      Assert.Throws<KamalConfigurationError>(() => Config);

      // Empty hosts list
      _deploy["servers"] = new Cfg { ["web"] = new Cfg { ["hosts"] = L() } };
      Assert.Throws<KamalConfigurationError>(() => Config);

      // Nil hosts
      _deploy["servers"] = new Cfg { ["web"] = new Cfg { ["hosts"] = null } };
      Assert.Throws<KamalConfigurationError>(() => Config);

      // One role with hosts, one without
      _deploy["servers"] = new Cfg { ["web"] = L("web"), ["workers"] = new Cfg { ["hosts"] = L() } };
      Assert.Throws<KamalConfigurationError>(() => Config);
   }

   [Fact]
   public void AllowEmptyRoles()
   {
      _deploy["servers"] = new Cfg { ["web"] = L("web"), ["workers"] = new Cfg { ["hosts"] = L() } };
      _deploy["allow_empty_roles"] = true;
      _ = Config;

      _deploy["servers"] = new Cfg { ["web"] = L(), ["workers"] = new Cfg { ["hosts"] = L() } };
      Assert.Throws<KamalConfigurationError>(() => Config);
   }

   [Fact]
   public void VolumeArgs()
   {
      Assert.Equal(["--volume", "/local/path:/container/path"], S(Config.VolumeArgs));
   }

   [Fact]
   public void LoggingArgsDefault()
   {
      Assert.Equal(["--log-opt", "max-size=\"10m\""], S(Config.LoggingArgs));
   }

   [Fact]
   public void LoggingArgsWithConfiguredOptions()
   {
      _deploy["logging"] = new Cfg
      {
         ["options"] = new Cfg { ["max-size"] = "100m", ["max-file"] = 5 }
      };

      Assert.Equal(["--log-opt", "max-size=\"100m\"", "--log-opt", "max-file=\"5\""], S(Config.LoggingArgs));
   }

   [Fact]
   public void LoggingArgsWithConfiguredDriverAndOptions()
   {
      _deploy["logging"] = new Cfg
      {
         ["driver"] = "local",
         ["options"] = new Cfg { ["max-size"] = "100m", ["max-file"] = 5 }
      };

      Assert.Equal(
         ["--log-driver", "\"local\"", "--log-opt", "max-size=\"100m\"", "--log-opt", "max-file=\"5\""],
         S(Config.LoggingArgs));
   }

   // DEVIATION: Ruby evaluates deploy.yml through ERB before YAML parsing ("erb evaluation of
   // yml config" test). The C# port loads YAML directly, so the fixtures below use literals.
   private const string DeployForDestYaml = """
      service: app
      image: dhh/app
      registry:
        server: registry.digitalocean.com
        username: my-user
        password: my-password
      builder:
        arch: amd64
      """;

   [Fact]
   public void CreateFromLoadsConfigFile()
   {
      using var fixtures = new FixtureDir();
      var configFile = fixtures.Write("deploy.yml", DeployForDestYaml + "\nservers:\n  - 1.1.1.1\n");

      var config = KamalConfiguration.CreateFrom(configFile);
      Assert.Equal("my-user", config.Registry.Username);
   }

   [Fact]
   public void DestinationIsLoadedIntoEnv()
   {
      using var fixtures = new FixtureDir();
      var configFile = fixtures.Write("deploy_for_dest.yml", DeployForDestYaml);
      fixtures.Write("deploy_for_dest.world.yml", "servers:\n  - 1.1.1.1\n  - 1.1.1.2\nenv:\n  REDIS_URL: redis://x/y\n");

      _ = KamalConfiguration.CreateFrom(configFile, destination: "world");
      Assert.Equal("world", Environment.GetEnvironmentVariable("KAMAL_DESTINATION"));
   }

   [Fact]
   public void DestinationYmlConfigMerge()
   {
      using var fixtures = new FixtureDir();
      var configFile = fixtures.Write("deploy_for_dest.yml", DeployForDestYaml);
      fixtures.Write("deploy_for_dest.world.yml", "servers:\n  - 1.1.1.1\n  - 1.1.1.2\nenv:\n  REDIS_URL: redis://x/y\n");
      fixtures.Write("deploy_for_dest.mars.yml", "servers:\n  - 1.1.1.3\n  - 1.1.1.4\nenv:\n  REDIS_URL: redis://a/b\n");

      var config = KamalConfiguration.CreateFrom(configFile, destination: "world");
      Assert.Equal("1.1.1.1", config.AllHosts.First());

      config = KamalConfiguration.CreateFrom(configFile, destination: "mars");
      Assert.Equal("1.1.1.3", config.AllHosts.First());
   }

   [Fact]
   public void DestinationYmlConfigFileMissing()
   {
      using var fixtures = new FixtureDir();
      var configFile = fixtures.Write("deploy_for_dest.yml", DeployForDestYaml);

      Assert.Throws<InvalidOperationException>(() => KamalConfiguration.CreateFrom(configFile, destination: "missing"));
   }

   [Fact]
   public void DestinationRequired()
   {
      using var fixtures = new FixtureDir();
      var configFile = fixtures.Write("deploy_for_required_dest.yml", DeployForDestYaml + "\nrequire_destination: true\n");
      fixtures.Write(
         "deploy_for_required_dest.world.yml",
         "servers:\n  - 1.1.1.1\n  - 1.1.1.2\nenv:\n  REDIS_URL: redis://x/y\naliases:\n  world_deploy: deploy -d world\n");

      Assert.Throws<ArgumentException>(() => KamalConfiguration.CreateFrom(configFile));

      _ = KamalConfiguration.CreateFrom(configFile, destination: "world");
   }

   [Fact]
   public void ToH()
   {
      var hash = Config.ToH();

      Assert.Equal(
         ["roles", "hosts", "primary_host", "version", "repository", "absolute_image", "service_with_version", "volume_args", "ssh_options", "sshkit", "builder", "logging"],
         hash.Keys);

      Assert.Equal(["web"], (List<string>)hash["roles"]!);
      Assert.Equal(["1.1.1.1", "1.1.1.2"], (List<string>)hash["hosts"]!);
      Assert.Equal("1.1.1.1", hash["primary_host"]);
      Assert.Equal("missing", hash["version"]);
      Assert.Equal("dhh/app", hash["repository"]);
      Assert.Equal("dhh/app:missing", hash["absolute_image"]);
      Assert.Equal("app-missing", hash["service_with_version"]);
      Assert.Equal(["--volume", "/local/path:/container/path"], S((List<object>)hash["volume_args"]!));
      Assert.Equal(["--log-opt", "max-size=\"10m\""], S((List<object>)hash["logging"]!));

      var sshOptions = (OrderedDictionary<string, object?>)hash["ssh_options"]!;
      Assert.Equal("root", sshOptions["user"]);
      Assert.Equal(22, sshOptions["port"]);
      Assert.Equal(true, sshOptions["keepalive"]);
      Assert.Equal(30, sshOptions["keepalive_interval"]);
      Assert.Equal("fatal", sshOptions["log_level"]);

      Assert.Empty((IDictionary<string, object?>)hash["sshkit"]!);
      Assert.Equal("amd64", ((IDictionary<string, object?>)hash["builder"]!)["arch"]);
   }

   [Fact]
   public void MinVersionIsLower()
   {
      _deploy["minimum_version"] = "0.0.1";
      Assert.Equal("0.0.1", Config.MinimumVersion);
   }

   [Fact]
   public void MinVersionIsEqual()
   {
      _deploy["minimum_version"] = KamalConfiguration.KamalVersion;
      Assert.Equal(KamalConfiguration.KamalVersion, Config.MinimumVersion);
   }

   [Fact]
   public void MinVersionIsHigher()
   {
      _deploy["minimum_version"] = "10000.0.0";
      Assert.Throws<KamalConfigurationError>(() => Config);
   }

   [Fact]
   public void RunDirectory()
   {
      Assert.Equal(".kamal", Config.RunDirectory);
   }

   [Fact]
   public void AssetPath()
   {
      Assert.Null(Config.AssetPath);

      _deploy["asset_path"] = "foo";
      Assert.Equal("foo", Config.AssetPath);
   }

   [Fact]
   public void PrimaryRole()
   {
      Assert.Equal("web", Config.PrimaryRole!.Name);

      ((Cfg)_deployWithRoles["servers"]!)["alternate_web"] = new Cfg { ["hosts"] = L("1.1.1.4", "1.1.1.5") };
      _deployWithRoles["primary_role"] = "alternate_web";
      var config = new KamalConfiguration(_deployWithRoles);

      Assert.Equal("alternate_web", config.PrimaryRole!.Name);
      Assert.Equal("1.1.1.4", config.PrimaryHost);
      Assert.True(config.Role("alternate_web")!.Primary);
      Assert.True(config.Role("alternate_web")!.RunningProxy);
   }

   [Fact]
   public void PrimaryRoleMissing()
   {
      _deploy["primary_role"] = "bar";

      var error = Assert.Throws<KamalConfigurationError>(() => Config);
      Assert.Contains("bar isn't defined", error.Message);
   }

   [Fact]
   public void RetainContainers()
   {
      Assert.Equal(5, Config.RetainContainers);

      _deployWithRoles["retain_containers"] = 2;
      Assert.Equal(2, ConfigWithRoles.RetainContainers);

      _deployWithRoles["retain_containers"] = 0;
      Assert.Throws<KamalConfigurationError>(() => ConfigWithRoles);
   }

   [Fact]
   public void Extensions()
   {
      // Port of fixtures/deploy_with_extensions.yml: exercises x- extensions, anchors and merge keys.
      using var fixtures = new FixtureDir();
      var configFile = fixtures.Write("deploy_with_extensions.yml", """
         x-web: &web
           proxy: {}

         service: app
         image: dhh/app
         servers:
           web_chicago:
             <<: *web
             hosts:
               - 1.1.1.1
               - 1.1.1.2
           web_tokyo:
             <<: *web
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
         primary_role: web_tokyo
         """);

      var config = KamalConfiguration.CreateFrom(configFile);
      Assert.True(config.Role("web_tokyo")!.RunningProxy);
      Assert.True(config.Role("web_chicago")!.RunningProxy);
   }

   [Fact]
   public void TraefikHooksRaiseError()
   {
      using var fixtures = new FixtureDir();
      fixtures.Write("post-traefik-reboot", "");
      fixtures.Write("pre-traefik-reboot", "");

      // The Ruby test chdirs into a temp dir with .kamal/hooks; the port points hooks_path there.
      _deploy["hooks_path"] = Path.GetDirectoryName(fixtures.PathOf("pre-traefik-reboot"));

      var exception = Assert.Throws<KamalConfigurationError>(() => Config);
      Assert.Equal("Found pre-traefik-reboot, post-traefik-reboot, these should be renamed to (pre|post)-proxy-reboot", exception.Message);
   }

   [Fact]
   public void ProxySslRolesWithNoHost()
   {
      ((Cfg)((Cfg)_deployWithRoles["servers"]!)["workers"]!)["proxy"] = new Cfg { ["ssl"] = true };

      var exception = Assert.Throws<KamalConfigurationError>(() => ConfigWithRoles);
      Assert.Equal("servers/workers/proxy: Must set a host to enable automatic SSL", exception.Message);
   }

   [Fact]
   public void ProxySslRolesWithMultipleServers()
   {
      ((Cfg)((Cfg)_deployWithRoles["servers"]!)["workers"]!)["proxy"] = new Cfg { ["ssl"] = true, ["host"] = "foo.example.com" };

      var exception = Assert.Throws<KamalConfigurationError>(() => ConfigWithRoles);
      Assert.Equal("SSL is only supported on a single server unless you provide custom certificates, found 2 servers for role workers", exception.Message);
   }

   [Fact]
   public void TwoProxySslRolesWithSameHost()
   {
      var servers = (Cfg)_deployWithRoles["servers"]!;
      servers["web"] = new Cfg { ["hosts"] = L("1.1.1.1"), ["proxy"] = new Cfg { ["ssl"] = true, ["host"] = "foo.example.com" } };
      servers["workers"] = new Cfg { ["hosts"] = L("1.1.1.1"), ["proxy"] = new Cfg { ["ssl"] = true, ["host"] = "foo.example.com" } };

      var exception = Assert.Throws<KamalConfigurationError>(() => ConfigWithRoles);
      Assert.Equal("Different roles can't share the same host for SSL: foo.example.com", exception.Message);
   }

   [Fact]
   public void TwoProxySslRolesWithSameHostInAHostsArray()
   {
      var servers = (Cfg)_deployWithRoles["servers"]!;
      servers["web"] = new Cfg { ["hosts"] = L("1.1.1.1"), ["proxy"] = new Cfg { ["ssl"] = true, ["hosts"] = L("foo.example.com", "bar.example.com") } };
      servers["workers"] = new Cfg { ["hosts"] = L("1.1.1.1"), ["proxy"] = new Cfg { ["ssl"] = true, ["hosts"] = L("www.example.com", "foo.example.com") } };

      var exception = Assert.Throws<KamalConfigurationError>(() => ConfigWithRoles);
      Assert.Equal("Different roles can't share the same host for SSL: foo.example.com", exception.Message);
   }

   [Fact]
   public void HooksOutputDefaultIsNull()
   {
      Assert.Null(Config.HooksOutputFor("pre-deploy"));
   }

   [Fact]
   public void HooksOutputGlobalSetting()
   {
      _deploy["hooks_output"] = "verbose";
      var config = Config;

      Assert.Equal("verbose", config.HooksOutputFor("pre-deploy"));
      Assert.Equal("verbose", config.HooksOutputFor("post-deploy"));
   }

   [Fact]
   public void HooksOutputPerHookSettings()
   {
      _deploy["hooks_output"] = new Cfg { ["pre-deploy"] = "verbose", ["post-deploy"] = "quiet" };
      var config = Config;

      Assert.Equal("verbose", config.HooksOutputFor("pre-deploy"));
      Assert.Equal("quiet", config.HooksOutputFor("post-deploy"));
   }

   [Fact]
   public void HooksOutputPerHookReturnsNullForUnconfiguredHooks()
   {
      _deploy["hooks_output"] = new Cfg { ["pre-deploy"] = "verbose" };
      var config = Config;

      Assert.Equal("verbose", config.HooksOutputFor("pre-deploy"));
      Assert.Null(config.HooksOutputFor("post-deploy"));
   }

   [Fact]
   public void HooksOutputInvalidRaisesError()
   {
      _deploy["hooks_output"] = "invalid";

      var error = Assert.Throws<KamalConfigurationError>(() => Config);
      Assert.Contains("Invalid hooks_output 'invalid'", error.Message);
   }

   [Fact]
   public void HooksOutputInvalidPerHookRaisesError()
   {
      _deploy["hooks_output"] = new Cfg { ["pre-deploy"] = "invalid" };

      var error = Assert.Throws<KamalConfigurationError>(() => Config);
      Assert.Contains("Invalid hooks_output 'invalid' for hook 'pre-deploy'", error.Message);
   }
}
