using Kamal.Configuration;
using Kamal.Tests.Configuration;
using static Kamal.Tests.Commands.Cmd;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/registry_test.rb.</summary>
[Collection("kamal-config")]
public class RegistryTests
{
   private readonly Cfg _config;
   private TestSecrets? _secrets;

   public RegistryTests()
   {
      _config = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret", ["server"] = "hub.docker.com" },
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["servers"] = L("1.1.1.1"),
         ["accessories"] = new Cfg
         {
            ["db"] = new Cfg
            {
               ["image"] = "mysql:8.0",
               ["hosts"] = L("1.1.1.1"),
               ["registry"] = new Cfg { ["username"] = "user", ["password"] = "pw", ["server"] = "other.hub.docker.com" }
            }
         }
      };
   }

   [Fact]
   public void RegistryLogin()
   {
      Assert.Equal(
         "docker login hub.docker.com -u \"dhh\" -p \"secret\"",
         Join(Registry().Login()));
   }

   [Fact]
   public void GivenRegistryLogin()
   {
      Assert.Equal(
         "docker login other.hub.docker.com -u \"user\" -p \"pw\"",
         Join(Registry().Login(registryConfig: AccessoryRegistryConfig())));
   }

   [Fact]
   public void RegistryLoginWithEnvPassword()
   {
      _secrets = new TestSecrets("KAMAL_REGISTRY_PASSWORD=more-secret\nKAMAL_MYSQL_REGISTRY_PASSWORD=secret-pw");
      ((Cfg)_config["registry"]!)["password"] = L("KAMAL_REGISTRY_PASSWORD");
      ((Cfg)((Cfg)((Cfg)_config["accessories"]!)["db"]!)["registry"]!)["password"] = L("KAMAL_MYSQL_REGISTRY_PASSWORD");

      Assert.Equal(
         "docker login hub.docker.com -u \"dhh\" -p \"more-secret\"",
         Join(Registry().Login()));

      Assert.Equal(
         "docker login other.hub.docker.com -u \"user\" -p \"secret-pw\"",
         Join(Registry().Login(registryConfig: AccessoryRegistryConfig())));
   }

   [Fact]
   public void RegistryLoginEscapePassword()
   {
      _secrets = new TestSecrets("KAMAL_REGISTRY_PASSWORD=more-secret'\"");
      ((Cfg)_config["registry"]!)["password"] = L("KAMAL_REGISTRY_PASSWORD");

      Assert.Equal(
         "docker login hub.docker.com -u \"dhh\" -p \"more-secret'\\\"\"",
         Join(Registry().Login()));
   }

   [Fact]
   public void RegistryLoginWithEnvUsername()
   {
      _secrets = new TestSecrets("KAMAL_REGISTRY_USERNAME=also-secret");
      ((Cfg)_config["registry"]!)["username"] = L("KAMAL_REGISTRY_USERNAME");

      Assert.Equal(
         "docker login hub.docker.com -u \"also-secret\" -p \"secret\"",
         Join(Registry().Login()));
   }

   [Fact]
   public void RegistryLogout()
   {
      Assert.Equal("docker logout hub.docker.com", Join(Registry().Logout()));
   }

   [Fact]
   public void GivenRegistryLogout()
   {
      Assert.Equal(
         "docker logout other.hub.docker.com",
         Join(Registry().Logout(registryConfig: AccessoryRegistryConfig())));
   }

   [Fact]
   public void RegistrySetup()
   {
      _config["registry"] = new Cfg { ["server"] = "localhost:5000" };
      Assert.Equal(
         "docker start kamal-docker-registry || docker run --detach -p 127.0.0.1:5000:5000 --name kamal-docker-registry registry:3",
         Join(Registry().Setup()));
   }

   [Fact]
   public void RegistryRemove()
   {
      Assert.Equal("docker stop kamal-docker-registry && docker rm kamal-docker-registry", Join(Registry().Remove()));
   }

   private Kamal.Commands.Registry Registry() => new(MainConfig());

   private KamalConfiguration MainConfig() => new(_config, secrets: _secrets?.Secrets);

   private Kamal.Configuration.Registry AccessoryRegistryConfig() => MainConfig().Accessory("db")!.Registry!;
}
