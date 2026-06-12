using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/env/tags_test.rb.</summary>
[Collection("kamal-config")]
public class EnvTagsTests
{
   private readonly Cfg _deploy;
   private readonly Cfg _deployWithRoles;

   public EnvTagsTests()
   {
      _deploy = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
         ["servers"] = L(
            new Cfg { ["1.1.1.1"] = "odd" },
            new Cfg { ["1.1.1.2"] = "even" },
            new Cfg { ["1.1.1.3"] = L("odd", "three") }),
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["env"] = new Cfg
         {
            ["clear"] = new Cfg { ["REDIS_URL"] = "redis://x/y", ["THREE"] = "false" },
            ["tags"] = new Cfg
            {
               ["odd"] = new Cfg { ["TYPE"] = "odd" },
               ["even"] = new Cfg { ["TYPE"] = "even" },
               ["three"] = new Cfg { ["THREE"] = "true" }
            }
         }
      };

      _deployWithRoles = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["servers"] = new Cfg
         {
            ["web"] = L(new Cfg { ["1.1.1.1"] = "odd" }, "1.1.1.2"),
            ["workers"] = new Cfg
            {
               ["hosts"] = L(new Cfg { ["1.1.1.3"] = L("odd", "oddjob") }, "1.1.1.4"),
               ["cmd"] = "bin/jobs",
               ["env"] = new Cfg { ["REDIS_URL"] = "redis://a/b", ["WEB_CONCURRENCY"] = 4 }
            }
         },
         ["env"] = new Cfg
         {
            ["tags"] = new Cfg
            {
               ["odd"] = new Cfg { ["TYPE"] = "odd" },
               ["oddjob"] = new Cfg { ["TYPE"] = "oddjob" }
            }
         }
      };
   }

   private KamalConfiguration Config => new(_deploy);
   private KamalConfiguration ConfigWithRoles => new(_deployWithRoles);

   [Fact]
   public void Tags()
   {
      var config = Config;
      Assert.Equal(3, config.EnvTags.Count);
      Assert.Equal(["odd", "even", "three"], config.EnvTags.Select(tag => tag.Name));
      Assert.Equal("odd", config.EnvTag("odd")!.Env.Clear["TYPE"]);
      Assert.Equal("even", config.EnvTag("even")!.Env.Clear["TYPE"]);
      Assert.Equal("true", config.EnvTag("three")!.Env.Clear["THREE"]);
   }

   [Fact]
   public void TagsWithRoles()
   {
      var config = ConfigWithRoles;
      Assert.Equal(2, config.EnvTags.Count);
      Assert.Equal(["odd", "oddjob"], config.EnvTags.Select(tag => tag.Name));
      Assert.Equal("odd", config.EnvTag("odd")!.Env.Clear["TYPE"]);
      Assert.Equal("oddjob", config.EnvTag("oddjob")!.Env.Clear["TYPE"]);
   }

   [Fact]
   public void TagOverridesEnv()
   {
      var config = Config;
      Assert.Equal("false", config.Role("web")!.Env("1.1.1.1").Clear["THREE"]);
      Assert.Equal("true", config.Role("web")!.Env("1.1.1.3").Clear["THREE"]);
   }

   [Fact]
   public void LaterTagWins()
   {
      var deploy = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
         ["servers"] = L(new Cfg { ["1.1.1.1"] = L("first", "second") }),
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["env"] = new Cfg
         {
            ["tags"] = new Cfg
            {
               ["first"] = new Cfg { ["TYPE"] = "first" },
               ["second"] = new Cfg { ["TYPE"] = "second" }
            }
         }
      };

      var config = new KamalConfiguration(deploy);
      Assert.Equal("second", config.Role("web")!.Env("1.1.1.1").Clear["TYPE"]);
   }
}
