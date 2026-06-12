using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/proxy_test.rb.</summary>
[Collection("kamal-config")]
public class ProxyTests
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

   [Fact]
   public void SslWithHost()
   {
      _deploy["proxy"] = new Cfg { ["ssl"] = true, ["host"] = "example.com" };
      Assert.True(Config.Proxy.Ssl);
   }

   [Fact]
   public void SslWithMultipleHostsPassedViaHost()
   {
      _deploy["proxy"] = new Cfg { ["ssl"] = true, ["host"] = "example.com,anotherexample.com" };
      Assert.True(Config.Proxy.Ssl);
   }

   [Fact]
   public void SslWithMultipleHostsPassedViaHosts()
   {
      _deploy["proxy"] = new Cfg { ["ssl"] = true, ["hosts"] = L("example.com", "anotherexample.com") };
      Assert.True(Config.Proxy.Ssl);
   }

   [Fact]
   public void SslWithNoHost()
   {
      _deploy["proxy"] = new Cfg { ["ssl"] = true };
      Assert.Throws<KamalConfigurationError>(() => Config.Proxy.Ssl);
   }

   [Fact]
   public void SslWithBothHostAndHosts()
   {
      _deploy["proxy"] = new Cfg { ["ssl"] = true, ["host"] = "example.com", ["hosts"] = L("anotherexample.com") };
      Assert.Throws<KamalConfigurationError>(() => Config.Proxy.Ssl);
   }

   [Fact]
   public void SslFalse()
   {
      _deploy["proxy"] = new Cfg { ["ssl"] = false };
      Assert.False(Config.Proxy.Ssl);
   }

   [Fact]
   public void FalseNotAllowed()
   {
      _deploy["proxy"] = false;
      var error = Assert.Throws<KamalConfigurationError>(() => Config.Proxy);
      Assert.Equal("proxy: should be a hash", error.Message);
   }

   [Fact]
   public void SslWithCertificateAndPrivateKeyFromSecrets()
   {
      using var secrets = new TestSecrets("CERT_PEM=certificate\nKEY_PEM=private_key");
      _deploy["proxy"] = new Cfg
      {
         ["ssl"] = new Cfg { ["certificate_pem"] = "CERT_PEM", ["private_key_pem"] = "KEY_PEM" },
         ["host"] = "example.com"
      };

      var proxy = new KamalConfiguration(_deploy, secrets: secrets.Secrets).Proxy;
      Assert.Equal(".kamal/proxy/apps-config/app/tls/cert.pem", proxy.HostTlsCert);
      Assert.Equal(".kamal/proxy/apps-config/app/tls/key.pem", proxy.HostTlsKey);
      Assert.Equal("/home/kamal-proxy/.apps-config/app/tls/cert.pem", proxy.ContainerTlsCert);
      Assert.Equal("/home/kamal-proxy/.apps-config/app/tls/key.pem", proxy.ContainerTlsKey);
   }

   [Fact]
   public void DeployOptionsWithCustomSslCertificates()
   {
      using var secrets = new TestSecrets("CERT_PEM=certificate\nKEY_PEM=private_key");
      _deploy["proxy"] = new Cfg
      {
         ["ssl"] = new Cfg { ["certificate_pem"] = "CERT_PEM", ["private_key_pem"] = "KEY_PEM" },
         ["host"] = "example.com"
      };

      var proxy = new KamalConfiguration(_deploy, secrets: secrets.Secrets).Proxy;
      var options = proxy.DeployOptions;
      Assert.Equal(true, options["tls"]);
      Assert.Equal("/home/kamal-proxy/.apps-config/app/tls/cert.pem", options["tls-certificate-path"]);
      Assert.Equal("/home/kamal-proxy/.apps-config/app/tls/key.pem", options["tls-private-key-path"]);
   }

   [Fact]
   public void SslWithCertificateAndNoPrivateKey()
   {
      using var secrets = new TestSecrets("CERT_PEM=certificate");
      _deploy["proxy"] = new Cfg
      {
         ["ssl"] = new Cfg { ["certificate_pem"] = "CERT_PEM" },
         ["host"] = "example.com"
      };

      Assert.Throws<KamalConfigurationError>(() => new KamalConfiguration(_deploy, secrets: secrets.Secrets).Proxy.Ssl);
   }

   [Fact]
   public void SslWithPrivateKeyAndNoCertificate()
   {
      using var secrets = new TestSecrets("KEY_PEM=private_key");
      _deploy["proxy"] = new Cfg
      {
         ["ssl"] = new Cfg { ["private_key_pem"] = "KEY_PEM" },
         ["host"] = "example.com"
      };

      Assert.Throws<KamalConfigurationError>(() => new KamalConfiguration(_deploy, secrets: secrets.Secrets).Proxy.Ssl);
   }
}
