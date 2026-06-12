using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>
/// Covers proxy run configuration and conflict detection
/// (Ruby exercises these via fixtures/deploy_with_proxy_run_config*.yml).
/// </summary>
[Collection("kamal-config")]
public class ProxyRunTests
{
   private static Cfg Deploy(string accessoryHost, bool accessoryRun)
   {
      var accessory = new Cfg
      {
         ["image"] = "mysql:5.7",
         ["host"] = accessoryHost,
         ["port"] = 3306
      };

      if (accessoryRun)
         accessory["proxy"] = new Cfg { ["run"] = new Cfg { ["debug"] = false, ["metrics_port"] = 9190 } };

      return new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["servers"] = new Cfg { ["web"] = L("1.1.1.1", "1.1.1.2"), ["workers"] = L("1.1.1.3", "1.1.1.4") },
         ["registry"] = new Cfg { ["username"] = "user", ["password"] = "pw" },
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["proxy"] = new Cfg
         {
            ["run"] = new Cfg
            {
               ["registry"] = "registry:4443",
               ["debug"] = true,
               ["metrics_port"] = 9090,
               ["options"] = new Cfg { ["cpus"] = 1.5 }
            }
         },
         ["accessories"] = new Cfg { ["mysql"] = accessory }
      };
   }

   [Fact]
   public void RunConfigOnSeparateHostsIsAllowed()
   {
      var config = new KamalConfiguration(Deploy(accessoryHost: "1.1.1.3", accessoryRun: true));

      var webRun = config.ProxyRunFor("1.1.1.1")!;
      Assert.Equal("registry:4443/basecamp/kamal-proxy:v0.9.2", webRun.Image);
      Assert.Equal(9090, webRun.MetricsPort);
      Assert.Equal(true, webRun.Debug);
      Assert.Equal("kamal-proxy run --debug --metrics-port \"9090\"", webRun.RunCommand);

      var accessoryRun = config.ProxyRunFor("1.1.1.3")!;
      Assert.Equal(9190, accessoryRun.MetricsPort);
   }

   [Fact]
   public void ConflictingRunConfigsOnOneHostRaise()
   {
      var error = Assert.Throws<KamalConfigurationError>(
         () => new KamalConfiguration(Deploy(accessoryHost: "1.1.1.2", accessoryRun: true)));

      Assert.Equal("Conflicting proxy run configurations for host 1.1.1.2", error.Message);
   }
}
