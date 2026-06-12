using System.Text.RegularExpressions;
using Kamal.Configuration;
using static Kamal.Tests.Commands.Cmd;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/lock_test.rb.</summary>
[Collection("kamal-config")]
public class LockTests
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
   public void Status()
   {
      Assert.Equal(
         "stat .kamal/lock-app-production > /dev/null && cat .kamal/lock-app-production/details | base64 -d",
         Join(NewCommand().Status()));
   }

   [Fact]
   public void Acquire()
   {
      Assert.Matches(
         new Regex("mkdir \\.kamal/lock-app-production && echo \".*\" > \\.kamal/lock-app-production/details", RegexOptions.Singleline),
         Join(NewCommand().Acquire("Hello", "123")));
   }

   [Fact]
   public void Release()
   {
      Assert.Equal(
         "rm .kamal/lock-app-production/details && rm -r .kamal/lock-app-production",
         Join(NewCommand().Release()));
   }

   private Kamal.Commands.Lock NewCommand()
   {
      return new Kamal.Commands.Lock(new KamalConfiguration(_config, version: "123", destination: "production"));
   }
}
