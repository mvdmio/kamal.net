using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/proxy/boot_test.rb.</summary>
[Collection("kamal-config")]
public class ProxyBootTests : IDisposable
{
   private readonly EnvVarScope _versionScope = new("VERSION", "missing");
   private readonly ProxyBoot _proxyBootConfig;

   public ProxyBootTests()
   {
      _proxyBootConfig = new KamalConfiguration(BaseDeploy()).ProxyBoot;
   }

   public void Dispose()
   {
      _versionScope.Dispose();
   }

   [Fact]
   public void ProxyDirectories()
   {
      Assert.Equal(".kamal/proxy/apps-config", _proxyBootConfig.AppsDirectory);
      Assert.Equal("/home/kamal-proxy/.apps-config", _proxyBootConfig.AppsContainerDirectory);
      Assert.Equal(".kamal/proxy/apps-config/app", _proxyBootConfig.AppDirectory);
      Assert.Equal("/home/kamal-proxy/.apps-config/app", _proxyBootConfig.AppContainerDirectory);
      Assert.Equal(".kamal/proxy/apps-config/app/error_pages", _proxyBootConfig.ErrorPagesDirectory);
      Assert.Equal("/home/kamal-proxy/.apps-config/app/error_pages", _proxyBootConfig.ErrorPagesContainerDirectory);
      Assert.Equal(".kamal/proxy/apps-config/app/tls", _proxyBootConfig.TlsDirectory);
      Assert.Equal("/home/kamal-proxy/.apps-config/app/tls", _proxyBootConfig.TlsContainerDirectory);
   }

   [Fact]
   public void DefaultBootOptions()
   {
      Assert.Equal(
         ["--publish 80:80 --publish 443:443", "--log-opt", "max-size=10m"],
         S(_proxyBootConfig.DefaultBootOptions));
   }
}
