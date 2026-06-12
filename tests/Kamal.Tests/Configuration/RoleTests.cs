using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/role_test.rb.</summary>
[Collection("kamal-config")]
public class RoleTests
{
   private readonly Cfg _deploy;
   private readonly Cfg _deployWithRoles;

   public RoleTests()
   {
      _deploy = BaseDeploy();
      _deploy.Remove("volumes");

      _deployWithRoles = BaseDeploy();
      _deployWithRoles.Remove("volumes");
      _deployWithRoles["servers"] = new Cfg
      {
         ["web"] = L("1.1.1.1", "1.1.1.2"),
         ["workers"] = new Cfg
         {
            ["hosts"] = L("1.1.1.3", "1.1.1.4"),
            ["cmd"] = "bin/jobs",
            ["env"] = new Cfg { ["REDIS_URL"] = "redis://a/b", ["WEB_CONCURRENCY"] = "4" }
         }
      };
   }

   private KamalConfiguration Config => new(_deploy);
   private KamalConfiguration ConfigWithRoles => new(_deployWithRoles);

   private Cfg WorkersConfig => (Cfg)((Cfg)_deployWithRoles["servers"]!)["workers"]!;

   [Fact]
   public void Hosts()
   {
      Assert.Equal(["1.1.1.1", "1.1.1.2"], Config.Role("web")!.Hosts);
      Assert.Equal(["1.1.1.3", "1.1.1.4"], ConfigWithRoles.Role("workers")!.Hosts);
   }

   [Fact]
   public void MissingEnvTagIsIgnored()
   {
      WorkersConfig["hosts"] = L(new Cfg { ["1.1.1.3"] = L("job") });

      var role = ConfigWithRoles.Role("workers")!;
      Assert.Equal("redis://a/b", role.Env("1.1.1.3").Clear["REDIS_URL"]);
   }

   [Fact]
   public void Cmd()
   {
      Assert.Null(Config.Role("web")!.Cmd);
      Assert.Equal("bin/jobs", ConfigWithRoles.Role("workers")!.Cmd);
   }

   [Fact]
   public void LabelArgs()
   {
      Assert.Equal(
         ["--label", "service=\"app\"", "--label", "role=\"workers\"", "--label", "destination"],
         S(ConfigWithRoles.Role("workers")!.LabelArgs));
   }

   [Fact]
   public void SpecialLabelArgsForWeb()
   {
      Assert.Equal(
         ["--label", "service=\"app\"", "--label", "role=\"web\"", "--label", "destination"],
         S(Config.Role("web")!.LabelArgs));
   }

   [Fact]
   public void CustomLabels()
   {
      _deploy["labels"] = new Cfg { ["my.custom.label"] = "50" };
      Assert.Equal("50", Config.Role("web")!.Labels["my.custom.label"]);
   }

   [Fact]
   public void CustomLabelsViaRoleSpecialization()
   {
      _deployWithRoles["labels"] = new Cfg { ["my.custom.label"] = "50" };
      WorkersConfig["labels"] = new Cfg { ["my.custom.label"] = "70" };
      Assert.Equal("70", ConfigWithRoles.Role("workers")!.Labels["my.custom.label"]);
   }

   [Fact]
   public void DefaultProxyLabelOnNonWebRole()
   {
      ((Cfg)_deployWithRoles["servers"]!)["beta"] = new Cfg { ["proxy"] = true, ["hosts"] = L("1.1.1.5") };

      Assert.Equal(
         ["--label", "service=\"app\"", "--label", "role=\"beta\"", "--label", "destination"],
         S(ConfigWithRoles.Role("beta")!.LabelArgs));
   }

   [Fact]
   public void EnvOverwrittenByRole()
   {
      var role = ConfigWithRoles.Role("workers")!;

      Assert.Equal("redis://a/b", role.Env("1.1.1.3").Clear["REDIS_URL"]);

      Assert.Equal(
         ["--env", "REDIS_URL=\"redis://a/b\"", "--env", "WEB_CONCURRENCY=\"4\"", "--env-file", ".kamal/apps/app/env/roles/workers.env"],
         S(role.EnvArgs("1.1.1.3")));

      Assert.Equal("\n", role.SecretsIo("1.1.1.3"));
   }

   [Fact]
   public void ContainerName()
   {
      using var version = new EnvVarScope("VERSION", "12345");

      Assert.Equal("app-workers-12345", ConfigWithRoles.Role("workers")!.ContainerName());
      Assert.Equal("app-web-12345", ConfigWithRoles.Role("web")!.ContainerName());
   }

   [Fact]
   public void EnvArgs()
   {
      var role = ConfigWithRoles.Role("workers")!;

      Assert.Equal(
         ["--env", "REDIS_URL=\"redis://a/b\"", "--env", "WEB_CONCURRENCY=\"4\"", "--env-file", ".kamal/apps/app/env/roles/workers.env"],
         S(role.EnvArgs("1.1.1.3")));

      Assert.Equal("\n", role.SecretsIo("1.1.1.3"));
   }

   [Fact]
   public void EnvSecretOverwrittenByRole()
   {
      using var secrets = new TestSecrets("REDIS_PASSWORD=secret456\nDB_PASSWORD=secret&\"123");

      _deployWithRoles["env"] = new Cfg
      {
         ["clear"] = new Cfg { ["REDIS_URL"] = "redis://a/b" },
         ["secret"] = L("REDIS_PASSWORD")
      };

      WorkersConfig["env"] = new Cfg
      {
         ["clear"] = new Cfg { ["REDIS_URL"] = "redis://a/b", ["WEB_CONCURRENCY"] = "4" },
         ["secret"] = L("DB_PASSWORD")
      };

      var role = new KamalConfiguration(_deployWithRoles, secrets: secrets.Secrets).Role("workers")!;

      Assert.Equal(
         ["--env", "REDIS_URL=\"redis://a/b\"", "--env", "WEB_CONCURRENCY=\"4\"", "--env-file", ".kamal/apps/app/env/roles/workers.env"],
         S(role.EnvArgs("1.1.1.3")));

      Assert.Equal("REDIS_PASSWORD=secret456\nDB_PASSWORD=secret&\"123\n", role.SecretsIo("1.1.1.3"));
   }

   [Fact]
   public void EnvSecretsOnlyInRole()
   {
      using var secrets = new TestSecrets("DB_PASSWORD=secret123");

      WorkersConfig["env"] = new Cfg
      {
         ["clear"] = new Cfg { ["REDIS_URL"] = "redis://a/b", ["WEB_CONCURRENCY"] = "4" },
         ["secret"] = L("DB_PASSWORD")
      };

      var role = new KamalConfiguration(_deployWithRoles, secrets: secrets.Secrets).Role("workers")!;

      Assert.Equal(
         ["--env", "REDIS_URL=\"redis://a/b\"", "--env", "WEB_CONCURRENCY=\"4\"", "--env-file", ".kamal/apps/app/env/roles/workers.env"],
         S(role.EnvArgs("1.1.1.3")));

      Assert.Equal("DB_PASSWORD=secret123\n", role.SecretsIo("1.1.1.3"));
   }

   [Fact]
   public void EnvSecretsOnlyAtTopLevel()
   {
      using var secrets = new TestSecrets("REDIS_PASSWORD=secret456");

      _deployWithRoles["env"] = new Cfg
      {
         ["clear"] = new Cfg { ["REDIS_URL"] = "redis://a/b" },
         ["secret"] = L("REDIS_PASSWORD")
      };

      var role = new KamalConfiguration(_deployWithRoles, secrets: secrets.Secrets).Role("workers")!;

      Assert.Equal(
         ["--env", "REDIS_URL=\"redis://a/b\"", "--env", "WEB_CONCURRENCY=\"4\"", "--env-file", ".kamal/apps/app/env/roles/workers.env"],
         S(role.EnvArgs("1.1.1.3")));

      Assert.Equal("REDIS_PASSWORD=secret456\n", role.SecretsIo("1.1.1.3"));
   }

   [Fact]
   public void EnvOverwrittenByRoleWithSecrets()
   {
      using var secrets = new TestSecrets("REDIS_PASSWORD=secret456");

      _deployWithRoles["env"] = new Cfg
      {
         ["clear"] = new Cfg { ["REDIS_URL"] = "redis://a/b" },
         ["secret"] = L("REDIS_PASSWORD")
      };

      WorkersConfig["env"] = new Cfg
      {
         ["clear"] = new Cfg { ["REDIS_URL"] = "redis://c/d" }
      };

      var role = new KamalConfiguration(_deployWithRoles, secrets: secrets.Secrets).Role("workers")!;

      Assert.Equal(
         ["--env", "REDIS_URL=\"redis://c/d\"", "--env-file", ".kamal/apps/app/env/roles/workers.env"],
         S(role.EnvArgs("1.1.1.3")));

      Assert.Equal("REDIS_PASSWORD=secret456\n", role.SecretsIo("1.1.1.3"));
   }

   [Fact]
   public void AssetPathAndVolumeArgs()
   {
      using var version = new EnvVarScope("VERSION", "12345");

      var config = ConfigWithRoles;
      Assert.Null(config.Role("web")!.AssetVolumeArgs);
      Assert.Null(config.Role("workers")!.AssetVolumeArgs);
      Assert.Null(config.Role("web")!.AssetPath);
      Assert.Null(config.Role("workers")!.AssetPath);
      Assert.False(config.Role("web")!.Assets);
      Assert.False(config.Role("workers")!.Assets);

      _deployWithRoles["asset_path"] = "foo";
      var configWithAssets = ConfigWithRoles;
      Assert.Equal("foo", configWithAssets.Role("web")!.AssetPath);
      Assert.Equal("foo", configWithAssets.Role("workers")!.AssetPath);
      Assert.Equal(
         ["--volume", "$PWD/.kamal/apps/app/assets/volumes/web-12345:foo"],
         S(configWithAssets.Role("web")!.AssetVolumeArgs!));
      Assert.Null(configWithAssets.Role("workers")!.AssetVolumeArgs);
      Assert.True(configWithAssets.Role("web")!.Assets);
      Assert.False(configWithAssets.Role("workers")!.Assets);

      _deployWithRoles.Remove("asset_path");
      ((Cfg)_deployWithRoles["servers"]!)["web"] = new Cfg { ["hosts"] = L("1.1.1.1", "1.1.1.2"), ["asset_path"] = "bar" };
      configWithAssets = ConfigWithRoles;
      Assert.Equal("bar", configWithAssets.Role("web")!.AssetPath);
      Assert.Null(configWithAssets.Role("workers")!.AssetPath);
      Assert.Equal(
         ["--volume", "$PWD/.kamal/apps/app/assets/volumes/web-12345:bar"],
         S(configWithAssets.Role("web")!.AssetVolumeArgs!));
      Assert.Null(configWithAssets.Role("workers")!.AssetVolumeArgs);
      Assert.True(configWithAssets.Role("web")!.Assets);
      Assert.False(configWithAssets.Role("workers")!.Assets);
   }

   [Fact]
   public void AssetPathWithMountOptions()
   {
      using var version = new EnvVarScope("VERSION", "12345");

      _deployWithRoles["asset_path"] = "/rails/public/assets:z";
      var configWithAssets = ConfigWithRoles;
      Assert.Equal("/rails/public/assets", configWithAssets.Role("web")!.AssetPath);
      Assert.Equal("z", configWithAssets.Role("web")!.AssetPathOptions);
      Assert.Equal(
         ["--volume", "$PWD/.kamal/apps/app/assets/volumes/web-12345:/rails/public/assets:z"],
         S(configWithAssets.Role("web")!.AssetVolumeArgs!));

      _deployWithRoles.Remove("asset_path");
      ((Cfg)_deployWithRoles["servers"]!)["web"] = new Cfg { ["hosts"] = L("1.1.1.1", "1.1.1.2"), ["asset_path"] = "/assets:ro,z" };
      configWithAssets = ConfigWithRoles;
      Assert.Equal("/assets", configWithAssets.Role("web")!.AssetPath);
      Assert.Equal("ro,z", configWithAssets.Role("web")!.AssetPathOptions);
      Assert.Equal(
         ["--volume", "$PWD/.kamal/apps/app/assets/volumes/web-12345:/assets:ro,z"],
         S(configWithAssets.Role("web")!.AssetVolumeArgs!));
   }

   [Fact]
   public void AssetExtractedPath()
   {
      using var version = new EnvVarScope("VERSION", "12345");

      Assert.Equal(".kamal/apps/app/assets/extracted/web-12345", ConfigWithRoles.Role("web")!.AssetExtractedDirectory());
      Assert.Equal(".kamal/apps/app/assets/extracted/workers-12345", ConfigWithRoles.Role("workers")!.AssetExtractedDirectory());
   }

   [Fact]
   public void AssetVolumePath()
   {
      using var version = new EnvVarScope("VERSION", "12345");

      Assert.Equal(".kamal/apps/app/assets/volumes/web-12345", ConfigWithRoles.Role("web")!.AssetVolumeDirectory());
      Assert.Equal(".kamal/apps/app/assets/volumes/workers-12345", ConfigWithRoles.Role("workers")!.AssetVolumeDirectory());
   }

   [Fact]
   public void StopArgsWithProxy()
   {
      Assert.Empty(ConfigWithRoles.Role("web")!.StopArgs);
   }

   [Fact]
   public void StopArgsWithNoProxy()
   {
      Assert.Equal(new object[] { "-t", 30 }, ConfigWithRoles.Role("workers")!.StopArgs);
   }

   [Fact]
   public void RoleSpecificProxyConfig()
   {
      _deployWithRoles["proxy"] = new Cfg { ["response_timeout"] = 15 };
      WorkersConfig["proxy"] = new Cfg { ["response_timeout"] = 18 };

      var config = ConfigWithRoles;
      Assert.Equal("15s", config.Role("web")!.Proxy!.DeployOptions["target-timeout"]);
      Assert.Equal("18s", config.Role("workers")!.Proxy!.DeployOptions["target-timeout"]);
   }

   [Fact]
   public void CannotSetRestartInOptions()
   {
      WorkersConfig["options"] = new Cfg { ["restart"] = "always" };

      Assert.Throws<KamalConfigurationError>(() => ConfigWithRoles);
   }
}
