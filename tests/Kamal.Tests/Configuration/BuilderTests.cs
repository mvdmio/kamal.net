using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/builder_test.rb.</summary>
[Collection("kamal-config")]
public class BuilderTests
{
   private readonly Cfg _deploy = new()
   {
      ["service"] = "app",
      ["image"] = "dhh/app",
      ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
      ["builder"] = new Cfg { ["arch"] = "amd64" },
      ["servers"] = L("1.1.1.1")
   };

   private KamalConfiguration Config => new(_deploy);

   private Cfg BuilderConfig => (Cfg)_deploy["builder"]!;

   [Fact]
   public void Local()
   {
      Assert.True(Config.Builder.IsLocal);
   }

   [Fact]
   public void Remote()
   {
      Assert.False(Config.Builder.IsRemote);
   }

   [Fact]
   public void PackDefault()
   {
      Assert.False(Config.Builder.Pack);
   }

   [Fact]
   public void PackWithPackBuilder()
   {
      _deploy["builder"] = new Cfg { ["arch"] = "arm64", ["pack"] = new Cfg { ["builder"] = "heroku/builder:24" } };

      Assert.True(Config.Builder.Pack);
   }

   [Fact]
   public void PackDetails()
   {
      _deploy["builder"] = new Cfg
      {
         ["arch"] = "amd64",
         ["pack"] = new Cfg { ["builder"] = "heroku/builder:24", ["buildpacks"] = L("heroku/ruby", "heroku/procfile") }
      };

      Assert.Equal("heroku/builder:24", Config.Builder.PackBuilder);
      Assert.Equal(["heroku/ruby", "heroku/procfile"], Config.Builder.PackBuildpacks!.Select(b => b!.ToString()));
   }

   [Fact]
   public void RemoteIsNullByDefault()
   {
      Assert.Null(Config.Builder.Remote);
   }

   [Fact]
   public void SettingBothLocalAndRemoteConfigs()
   {
      _deploy["builder"] = new Cfg
      {
         ["arch"] = L("amd64", "arm64"),
         ["remote"] = "ssh://root@192.168.0.1"
      };

      var previous = Kamal.Utils.KamalUtils.DockerArchOverride;
      Kamal.Utils.KamalUtils.DockerArchOverride = "amd64";
      try
      {
         var builder = Config.Builder;
         Assert.True(builder.IsLocal);
         Assert.True(builder.IsRemote);

         Assert.Equal(["amd64", "arm64"], builder.Arches);
         Assert.Equal("ssh://root@192.168.0.1", builder.Remote);
      }
      finally
      {
         Kamal.Utils.KamalUtils.DockerArchOverride = previous;
      }
   }

   [Fact]
   public void Cached()
   {
      Assert.False(Config.Builder.Cached);
   }

   [Fact]
   public void InvalidCacheTypeSpecified()
   {
      BuilderConfig["cache"] = new Cfg { ["type"] = "invalid" };

      Assert.Throws<KamalConfigurationError>(() => Config.Builder);
   }

   [Fact]
   public void CacheFrom()
   {
      Assert.Null(Config.Builder.CacheFrom);
   }

   [Fact]
   public void CacheTo()
   {
      Assert.Null(Config.Builder.CacheTo);
   }

   [Fact]
   public void SettingGhaCache()
   {
      _deploy["builder"] = new Cfg
      {
         ["arch"] = "amd64",
         ["cache"] = new Cfg { ["type"] = "gha", ["options"] = "mode=max,scope=test" }
      };

      Assert.Equal("type=gha,scope=test", Config.Builder.CacheFrom);
      Assert.Equal("type=gha,mode=max,scope=test", Config.Builder.CacheTo);
   }

   [Fact]
   public void SettingRegistryCache()
   {
      _deploy["builder"] = new Cfg
      {
         ["arch"] = "amd64",
         ["cache"] = new Cfg { ["type"] = "registry", ["options"] = "mode=max,image-manifest=true,oci-mediatypes=true" }
      };

      Assert.Equal("type=registry,ref=dhh/app-build-cache", Config.Builder.CacheFrom);
      Assert.Equal("type=registry,ref=dhh/app-build-cache,mode=max,image-manifest=true,oci-mediatypes=true", Config.Builder.CacheTo);
   }

   [Fact]
   public void SettingRegistryCacheWhenUsingACustomRegistry()
   {
      ((Cfg)_deploy["registry"]!)["server"] = "registry.example.com";
      _deploy["builder"] = new Cfg
      {
         ["arch"] = "amd64",
         ["cache"] = new Cfg { ["type"] = "registry", ["options"] = "mode=max,image-manifest=true,oci-mediatypes=true" }
      };

      Assert.Equal("type=registry,ref=registry.example.com/dhh/app-build-cache", Config.Builder.CacheFrom);
      Assert.Equal("type=registry,ref=registry.example.com/dhh/app-build-cache,mode=max,image-manifest=true,oci-mediatypes=true", Config.Builder.CacheTo);
   }

   [Fact]
   public void SettingRegistryCacheWithImage()
   {
      _deploy["builder"] = new Cfg
      {
         ["arch"] = "amd64",
         ["cache"] = new Cfg { ["type"] = "registry", ["image"] = "kamal", ["options"] = "mode=max" }
      };

      Assert.Equal("type=registry,ref=kamal", Config.Builder.CacheFrom);
      Assert.Equal("type=registry,ref=kamal,mode=max", Config.Builder.CacheTo);
   }

   [Fact]
   public void Args()
   {
      Assert.Empty(Config.Builder.Args);
   }

   [Fact]
   public void SettingArgs()
   {
      BuilderConfig["args"] = new Cfg { ["key"] = "value" };

      var args = Config.Builder.Args;
      Assert.Equal("value", args["key"]);
      Assert.Single(args);
   }

   [Fact]
   public void Secrets()
   {
      Assert.Empty(Config.Builder.Secrets);
   }

   [Fact]
   public void SettingSecrets()
   {
      using var secrets = new TestSecrets("GITHUB_TOKEN=secret123");
      BuilderConfig["secrets"] = L("GITHUB_TOKEN");

      var config = new KamalConfiguration(_deploy, secrets: secrets.Secrets);
      Assert.Equal("secret123", config.Builder.Secrets["GITHUB_TOKEN"]);
      Assert.Single(config.Builder.Secrets);
   }

   [Fact]
   public void Dockerfile()
   {
      Assert.Equal("Dockerfile", Config.Builder.Dockerfile);
   }

   [Fact]
   public void SettingDockerfile()
   {
      BuilderConfig["dockerfile"] = "Dockerfile.dev";

      Assert.Equal("Dockerfile.dev", Config.Builder.Dockerfile);
   }

   [Fact]
   public void Context()
   {
      Assert.Equal(".", Config.Builder.Context);
   }

   [Fact]
   public void SettingContext()
   {
      BuilderConfig["context"] = "..";

      Assert.Equal("..", Config.Builder.Context);
   }

   [Fact]
   public void SshDefault()
   {
      Assert.Null(Config.Builder.Ssh);
   }

   [Fact]
   public void SettingSshParams()
   {
      BuilderConfig["ssh"] = "default=$SSH_AUTH_SOCK";

      Assert.Equal("default=$SSH_AUTH_SOCK", Config.Builder.Ssh);
   }

   [Fact]
   public void Provenance()
   {
      Assert.Null(Config.Builder.Provenance);
   }

   [Fact]
   public void SettingProvenance()
   {
      BuilderConfig["provenance"] = "mode=max";

      Assert.Equal("mode=max", Config.Builder.Provenance);
   }

   [Fact]
   public void Sbom()
   {
      Assert.Null(Config.Builder.Sbom);
   }

   [Fact]
   public void SettingSbom()
   {
      BuilderConfig["sbom"] = true;

      Assert.Equal(true, Config.Builder.Sbom);
   }

   [Fact]
   public void LocalDisabledButNoRemoteSet()
   {
      BuilderConfig["local"] = false;

      Assert.Throws<KamalConfigurationError>(() => Config.Builder);
   }

   [Fact]
   public void LocalDisabledAllArchesAreRemote()
   {
      BuilderConfig["local"] = false;
      BuilderConfig["remote"] = "ssh://root@192.168.0.1";
      BuilderConfig["arch"] = L("amd64", "arm64");

      var builder = Config.Builder;
      Assert.Empty(builder.LocalArches);
      Assert.Equal(["amd64", "arm64"], builder.RemoteArches);
   }
}
