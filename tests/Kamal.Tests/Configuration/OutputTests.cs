using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/output_test.rb (logger instances are descriptors in the port).</summary>
[Collection("kamal-config")]
public class OutputTests
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
   public void DisabledByDefault()
   {
      var config = Config;
      Assert.False(config.Output.Enabled);
      Assert.Empty(config.Output.Loggers);
   }

   [Fact]
   public void EnabledWithOtelEndpoint()
   {
      _deploy["output"] = new Cfg { ["otel"] = new Cfg { ["endpoint"] = "http://otel-gateway:4318" } };
      var config = Config;

      Assert.True(config.Output.Enabled);
      Assert.Single(config.Output.Loggers);
      Assert.Equal("otel", config.Output.Loggers.First().Type);
   }

   [Fact]
   public void EnabledWithFilePath()
   {
      _deploy["output"] = new Cfg { ["file"] = new Cfg { ["path"] = "/var/log/kamal/" } };
      var config = Config;

      Assert.True(config.Output.Enabled);
      Assert.Single(config.Output.Loggers);
      Assert.Equal("file", config.Output.Loggers.First().Type);
   }

   [Fact]
   public void EnabledWithBothOtelAndFile()
   {
      _deploy["output"] = new Cfg
      {
         ["otel"] = new Cfg { ["endpoint"] = "http://otel-gateway:4318" },
         ["file"] = new Cfg { ["path"] = "/var/log/kamal/" }
      };
      var config = Config;

      Assert.True(config.Output.Enabled);
      Assert.Equal(2, config.Output.Loggers.Count);
   }

   [Fact]
   public void EmptyOutputSectionIsNotEnabled()
   {
      _deploy["output"] = new Cfg();
      var config = Config;

      Assert.False(config.Output.Enabled);
      Assert.Empty(config.Output.Loggers);
   }

   [Fact]
   public void OtelWithoutEndpointRaises()
   {
      _deploy["output"] = new Cfg { ["otel"] = new Cfg() };

      var error = Assert.Throws<ArgumentException>(() => Config);
      Assert.Equal("OTel endpoint is required", error.Message);
   }

   [Fact]
   public void FileWithoutPathRaises()
   {
      _deploy["output"] = new Cfg { ["file"] = new Cfg() };

      var error = Assert.Throws<ArgumentException>(() => Config);
      Assert.Equal("file path is required", error.Message);
   }
}
