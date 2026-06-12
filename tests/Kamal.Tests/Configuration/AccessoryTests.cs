using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/accessory_test.rb (ERB file-expansion tests excluded, see deviations).</summary>
[Collection("kamal-config")]
public class AccessoryTests
{
   private readonly Cfg _deploy;

   public AccessoryTests()
   {
      _deploy = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
         ["servers"] = new Cfg
         {
            ["web"] = L(new Cfg { ["1.1.1.1"] = "writer" }, new Cfg { ["1.1.1.2"] = "reader" }),
            ["workers"] = L(new Cfg { ["1.1.1.3"] = "writer" }, "1.1.1.4")
         },
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["env"] = new Cfg { ["REDIS_URL"] = "redis://x/y" },
         ["accessories"] = new Cfg
         {
            ["mysql"] = new Cfg
            {
               ["image"] = "public.registry/mysql:8.0",
               ["host"] = "1.1.1.5",
               ["port"] = "3306",
               ["env"] = new Cfg
               {
                  ["clear"] = new Cfg { ["MYSQL_ROOT_HOST"] = "%" },
                  ["secret"] = L("MYSQL_ROOT_PASSWORD")
               },
               ["files"] = L(
                  "config/mysql/my.cnf:/etc/mysql/my.cnf",
                  "db/structure.sql:/docker-entrypoint-initdb.d/structure.sql"),
               ["directories"] = L("data:/var/lib/mysql")
            },
            ["redis"] = new Cfg
            {
               ["image"] = "redis:latest",
               ["hosts"] = L("1.1.1.6", "1.1.1.7"),
               ["port"] = "6379:6379",
               ["labels"] = new Cfg { ["cache"] = "true" },
               ["env"] = new Cfg { ["SOMETHING"] = "else" },
               ["volumes"] = L("/var/lib/redis:/data"),
               ["options"] = new Cfg { ["cpus"] = "4", ["memory"] = "2GB" }
            },
            ["monitoring"] = new Cfg
            {
               ["service"] = "custom-monitoring",
               ["image"] = "monitoring:latest",
               ["registry"] = new Cfg { ["server"] = "other.registry", ["username"] = "user", ["password"] = "pw" },
               ["role"] = "web",
               ["port"] = "4321:4321",
               ["labels"] = new Cfg { ["cache"] = "true" },
               ["env"] = new Cfg { ["STATSD_PORT"] = "8126" },
               ["options"] = new Cfg { ["cpus"] = "4", ["memory"] = "2GB" },
               ["proxy"] = new Cfg { ["host"] = "monitoring.example.com" }
            },
            ["proxy"] = new Cfg
            {
               ["image"] = "proxy:latest",
               ["tags"] = L("writer", "reader")
            },
            ["logger"] = new Cfg
            {
               ["image"] = "logger:latest",
               ["tag"] = "writer"
            }
         }
      };
   }

   private KamalConfiguration Config => new(_deploy);

   private Cfg MysqlConfig => (Cfg)((Cfg)_deploy["accessories"]!)["mysql"]!;
   private Cfg RedisConfig => (Cfg)((Cfg)_deploy["accessories"]!)["redis"]!;

   [Fact]
   public void ServiceName()
   {
      var config = Config;
      Assert.Equal("app-mysql", config.Accessory("mysql")!.ServiceName);
      Assert.Equal("app-redis", config.Accessory("redis")!.ServiceName);
      Assert.Equal("custom-monitoring", config.Accessory("monitoring")!.ServiceName);
   }

   [Fact]
   public void Image()
   {
      var config = Config;
      Assert.Equal("public.registry/mysql:8.0", config.Accessory("mysql")!.Image);
      Assert.Equal("redis:latest", config.Accessory("redis")!.Image);
      Assert.Equal("other.registry/monitoring:latest", config.Accessory("monitoring")!.Image);
   }

   [Fact]
   public void Registry()
   {
      var config = Config;
      Assert.Null(config.Accessory("mysql")!.Registry);
      Assert.Null(config.Accessory("redis")!.Registry);

      var monitoringRegistry = config.Accessory("monitoring")!.Registry!;
      Assert.Equal("other.registry", monitoringRegistry.Server);
      Assert.Equal("user", monitoringRegistry.Username);
      Assert.Equal("pw", monitoringRegistry.Password);
   }

   [Fact]
   public void Port()
   {
      var config = Config;
      Assert.Equal("3306:3306", config.Accessory("mysql")!.Port);
      Assert.Equal("6379:6379", config.Accessory("redis")!.Port);
   }

   [Fact]
   public void Host()
   {
      var config = Config;
      Assert.Equal(["1.1.1.5"], config.Accessory("mysql")!.Hosts);
      Assert.Equal(["1.1.1.6", "1.1.1.7"], config.Accessory("redis")!.Hosts);
      Assert.Equal(["1.1.1.1", "1.1.1.2"], config.Accessory("monitoring")!.Hosts);
      Assert.Equal(["1.1.1.1", "1.1.1.3", "1.1.1.2"], config.Accessory("proxy")!.Hosts);
      Assert.Equal(["1.1.1.1", "1.1.1.3"], config.Accessory("logger")!.Hosts);
   }

   [Fact]
   public void MissingHost()
   {
      MysqlConfig["host"] = null;

      Assert.Throws<KamalConfigurationError>(() => Config);
   }

   [Fact]
   public void SettingHostHostsRolesAndTags()
   {
      MysqlConfig["hosts"] = L("mysql-db1");
      MysqlConfig["roles"] = L("db");

      var exception = Assert.Throws<KamalConfigurationError>(() => Config);
      Assert.Equal("accessories/mysql: specify one of `host`, `hosts`, `role`, `roles`, `tag` or `tags`", exception.Message);
   }

   [Fact]
   public void AllHosts()
   {
      Assert.Equal(
         ["1.1.1.1", "1.1.1.2", "1.1.1.3", "1.1.1.4", "1.1.1.5", "1.1.1.6", "1.1.1.7"],
         Config.AllHosts.OrderBy(host => host, StringComparer.Ordinal));
   }

   [Fact]
   public void LabelArgs()
   {
      var config = Config;
      Assert.Equal(["--label", "service=\"app-mysql\""], S(config.Accessory("mysql")!.LabelArgs));
      Assert.Equal(
         ["--label", "service=\"app-redis\"", "--label", "cache=\"true\""],
         S(config.Accessory("redis")!.LabelArgs));
   }

   [Fact]
   public void EnvArgs()
   {
      using var secrets = new TestSecrets("MYSQL_ROOT_PASSWORD=secret123");
      var config = new KamalConfiguration(_deploy, secrets: secrets.Secrets);

      Assert.Equal(
         ["--env", "MYSQL_ROOT_HOST=\"%\"", "--env-file", ".kamal/apps/app/env/accessories/mysql.env"],
         S(config.Accessory("mysql")!.EnvArgs));
      Assert.Equal("MYSQL_ROOT_PASSWORD=secret123\n", config.Accessory("mysql")!.SecretsIo);
      Assert.Equal(
         ["--env", "SOMETHING=\"else\"", "--env-file", ".kamal/apps/app/env/accessories/redis.env"],
         S(config.Accessory("redis")!.EnvArgs));
      Assert.Equal("\n", config.Accessory("redis")!.SecretsIo);
   }

   [Fact]
   public void VolumeArgs()
   {
      var config = Config;
      Assert.Equal(
         [
            "--volume", "$PWD/app-mysql/etc/mysql/my.cnf:/etc/mysql/my.cnf",
            "--volume", "$PWD/app-mysql/docker-entrypoint-initdb.d/structure.sql:/docker-entrypoint-initdb.d/structure.sql",
            "--volume", "$PWD/app-mysql/data:/var/lib/mysql"
         ],
         S(config.Accessory("mysql")!.VolumeArgs));
      Assert.Equal(["--volume", "/var/lib/redis:/data"], S(config.Accessory("redis")!.VolumeArgs));
   }

   [Fact]
   public void VolumeArgsWithDockerNamedVolume()
   {
      RedisConfig["volumes"] = L("redis_data:/data");

      Assert.Equal(["--volume", "redis_data:/data"], S(Config.Accessory("redis")!.VolumeArgs));
   }

   [Fact]
   public void DirectoryWithARelativePath()
   {
      MysqlConfig["directories"] = L("data:/var/lib/mysql");

      var directories = Config.Accessory("mysql")!.Directories;
      var (key, value) = directories.Single();
      Assert.Equal("$PWD/app-mysql/data", key);
      Assert.Equal(new AccessoryPath("app-mysql/data", "/var/lib/mysql", null, null, null), value);
   }

   [Fact]
   public void DirectoryWithAnAbsolutePath()
   {
      MysqlConfig["directories"] = L("/var/data/mysql:/var/lib/mysql");

      var directories = Config.Accessory("mysql")!.Directories;
      var (key, value) = directories.Single();
      Assert.Equal("/var/data/mysql", key);
      Assert.Equal(new AccessoryPath("/var/data/mysql", "/var/lib/mysql", null, null, null), value);
   }

   [Fact]
   public void DirectoryWithMountOptions()
   {
      MysqlConfig["files"] = L();
      MysqlConfig["directories"] = L("data:/var/lib/mysql:z");

      var accessory = Config.Accessory("mysql")!;
      var (key, value) = accessory.Directories.Single();
      Assert.Equal("$PWD/app-mysql/data", key);
      Assert.Equal(new AccessoryPath("app-mysql/data", "/var/lib/mysql", "z", null, null), value);
      Assert.Equal(["--volume", "$PWD/app-mysql/data:/var/lib/mysql:z"], S(accessory.VolumeArgs));
   }

   [Fact]
   public void FileWithMountOptions()
   {
      MysqlConfig["files"] = L("config/mysql/my.cnf:/etc/mysql/my.cnf:ro,z");
      MysqlConfig["directories"] = L();

      var accessory = Config.Accessory("mysql")!;
      Assert.Equal("ro,z", accessory.Files.Values.First().Options);
      Assert.Equal(["--volume", "$PWD/app-mysql/etc/mysql/my.cnf:/etc/mysql/my.cnf:ro,z"], S(accessory.VolumeArgs));
   }

   [Fact]
   public void FileWithStringFormatHasDefaultMode()
   {
      MysqlConfig["files"] = L("config/mysql/my.cnf:/etc/mysql/my.cnf");
      MysqlConfig["directories"] = L();

      var files = Config.Accessory("mysql")!.Files;
      Assert.Equal("755", files.Values.First().Mode);
      Assert.Null(files.Values.First().Owner);
   }

   [Fact]
   public void FileWithHashFormatAndCustomMode()
   {
      MysqlConfig["files"] = L(new Cfg { ["local"] = "config/mysql/my.cnf", ["remote"] = "/etc/mysql/my.cnf", ["mode"] = "0600" });
      MysqlConfig["directories"] = L();

      var files = Config.Accessory("mysql")!.Files;
      Assert.Equal("0600", files.Values.First().Mode);
      Assert.Null(files.Values.First().Owner);
   }

   [Fact]
   public void FileWithHashFormatAndCustomOwner()
   {
      MysqlConfig["files"] = L(new Cfg { ["local"] = "config/mysql/my.cnf", ["remote"] = "/etc/mysql/my.cnf", ["owner"] = "mysql:mysql" });
      MysqlConfig["directories"] = L();

      var files = Config.Accessory("mysql")!.Files;
      Assert.Equal("755", files.Values.First().Mode);
      Assert.Equal("mysql:mysql", files.Values.First().Owner);
   }

   [Fact]
   public void FileWithHashFormatAndAllOptions()
   {
      MysqlConfig["files"] = L(new Cfg
      {
         ["local"] = "config/mysql/my.cnf",
         ["remote"] = "/etc/mysql/my.cnf",
         ["mode"] = "0640",
         ["owner"] = "1000:1000",
         ["options"] = "Z"
      });
      MysqlConfig["directories"] = L();

      var accessory = Config.Accessory("mysql")!;
      var fileConfig = accessory.Files.Values.First();
      Assert.Equal("0640", fileConfig.Mode);
      Assert.Equal("1000:1000", fileConfig.Owner);
      Assert.Equal("Z", fileConfig.Options);
      Assert.Equal(["--volume", "$PWD/app-mysql/etc/mysql/my.cnf:/etc/mysql/my.cnf:Z"], S(accessory.VolumeArgs));
   }

   [Fact]
   public void DirectoryWithHashFormatAndCustomMode()
   {
      MysqlConfig["files"] = L();
      MysqlConfig["directories"] = L(new Cfg { ["local"] = "data", ["remote"] = "/var/lib/mysql", ["mode"] = "0750" });

      var directories = Config.Accessory("mysql")!.Directories;
      Assert.Equal("0750", directories.Values.First().Mode);
      Assert.Null(directories.Values.First().Owner);
   }

   [Fact]
   public void DirectoryWithHashFormatAndCustomOwner()
   {
      MysqlConfig["files"] = L();
      MysqlConfig["directories"] = L(new Cfg { ["local"] = "data", ["remote"] = "/var/lib/mysql", ["owner"] = "mysql:mysql" });

      var directories = Config.Accessory("mysql")!.Directories;
      Assert.Null(directories.Values.First().Mode);
      Assert.Equal("mysql:mysql", directories.Values.First().Owner);
   }

   [Fact]
   public void DirectoryWithHashFormatAndAllOptions()
   {
      MysqlConfig["files"] = L();
      MysqlConfig["directories"] = L(new Cfg
      {
         ["local"] = "data",
         ["remote"] = "/var/lib/mysql",
         ["mode"] = "0750",
         ["owner"] = "1000:1000",
         ["options"] = "z"
      });

      var accessory = Config.Accessory("mysql")!;
      var dirConfig = accessory.Directories.Values.First();
      Assert.Equal("0750", dirConfig.Mode);
      Assert.Equal("1000:1000", dirConfig.Owner);
      Assert.Equal("z", dirConfig.Options);
      Assert.Equal(["--volume", "$PWD/app-mysql/data:/var/lib/mysql:z"], S(accessory.VolumeArgs));
   }

   [Fact]
   public void Options()
   {
      Assert.Equal(["--cpus", "\"4\"", "--memory", "\"2GB\""], S(Config.Accessory("redis")!.OptionArgs));
   }

   [Fact]
   public void NetworkArgsDefault()
   {
      Assert.Equal(["--network", "kamal"], S(Config.Accessory("mysql")!.NetworkArgs));
   }

   [Fact]
   public void NetworkArgsWithConfiguredOptions()
   {
      MysqlConfig["network"] = "database";

      Assert.Equal(["--network", "database"], S(Config.Accessory("mysql")!.NetworkArgs));
   }

   [Fact]
   public void Proxy()
   {
      var config = Config;
      Assert.True(config.Accessory("monitoring")!.RunningProxy);
      Assert.Equal(["monitoring.example.com"], config.Accessory("monitoring")!.Proxy!.Hosts);
   }

   [Fact]
   public void CannotSetRestartInOptions()
   {
      MysqlConfig["options"] = new Cfg { ["restart"] = "always" };

      Assert.Throws<KamalConfigurationError>(() => Config);
   }
}
