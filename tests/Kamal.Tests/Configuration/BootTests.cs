using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/boot_test.rb.</summary>
[Collection("kamal-config")]
public class BootTests
{
   private static KamalConfiguration ConfigWithBoot(Cfg? boot)
   {
      var deploy = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["servers"] = new Cfg { ["web"] = L("1.1.1.1", "1.1.1.2"), ["workers"] = L("1.1.1.3", "1.1.1.4") }
      };

      if (boot is not null)
         deploy["boot"] = boot;

      return new KamalConfiguration(deploy);
   }

   [Fact]
   public void NoBootConfig()
   {
      var config = ConfigWithBoot(null);

      Assert.Null(config.Boot.Limit);
      Assert.Null(config.Boot.Wait);
      Assert.Null(config.Boot.ParallelRoles);
   }

   [Fact]
   public void SpecificLimitGroupStrategy()
   {
      var config = ConfigWithBoot(new Cfg { ["limit"] = 3, ["wait"] = 2 });

      Assert.Equal(3, config.Boot.Limit);
      Assert.Equal(2, config.Boot.Wait);
   }

   [Fact]
   public void PercentageBasedGroupStrategy()
   {
      var config = ConfigWithBoot(new Cfg { ["limit"] = "50%", ["wait"] = 2 });

      Assert.Equal(2, config.Boot.Limit);
      Assert.Equal(2, config.Boot.Wait);
   }

   [Fact]
   public void PercentageBasedGroupStrategyLimitIsAtLeastOne()
   {
      var config = ConfigWithBoot(new Cfg { ["limit"] = "1%", ["wait"] = 2 });

      Assert.Equal(1, config.Boot.Limit);
      Assert.Equal(2, config.Boot.Wait);
   }

   [Fact]
   public void ParallelRoles()
   {
      var config = ConfigWithBoot(new Cfg { ["parallel_roles"] = true });

      Assert.Equal(true, config.Boot.ParallelRoles);
   }
}
