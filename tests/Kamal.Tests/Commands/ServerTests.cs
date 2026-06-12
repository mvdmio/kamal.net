using Kamal.Configuration;
using static Kamal.Tests.Commands.Cmd;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/server_test.rb.</summary>
[Collection("kamal-config")]
public class ServerTests
{
   private readonly Cfg _config = new()
   {
      ["service"] = "app",
      ["image"] = "dhh/app",
      ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
      ["servers"] = L("1.1.1.1"),
      ["builder"] = new Cfg { ["arch"] = "amd64" }
   };

   [Fact]
   public void EnsureRunDirectory()
   {
      Assert.Equal("mkdir -p .kamal", Join(NewCommand().EnsureRunDirectory()));
   }

   private Kamal.Commands.Server NewCommand()
   {
      return new Kamal.Commands.Server(new KamalConfiguration(_config));
   }
}
