using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/sshkit_test.rb.</summary>
[Collection("kamal-config")]
public class SshkitTests
{
   private readonly Cfg _deploy = BaseDeploy();

   [Fact]
   public void SshkitDefaults()
   {
      var config = new KamalConfiguration(_deploy);

      Assert.Equal(30, config.Sshkit.MaxConcurrentStarts);
      Assert.Equal(900, config.Sshkit.PoolIdleTimeout);
      Assert.Equal(3, config.Sshkit.DnsRetries);
   }

   [Fact]
   public void SshkitOverrides()
   {
      _deploy["sshkit"] = new Cfg
      {
         ["max_concurrent_starts"] = 50,
         ["pool_idle_timeout"] = 600,
         ["dns_retries"] = 5
      };
      var config = new KamalConfiguration(_deploy);

      Assert.Equal(50, config.Sshkit.MaxConcurrentStarts);
      Assert.Equal(600, config.Sshkit.PoolIdleTimeout);
      Assert.Equal(5, config.Sshkit.DnsRetries);
   }
}
