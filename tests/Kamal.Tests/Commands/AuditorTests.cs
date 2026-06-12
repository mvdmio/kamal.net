using Kamal.Configuration;
using Kamal.Tests.Configuration;
using static Kamal.Tests.Commands.Cmd;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/auditor_test.rb.</summary>
[Collection("kamal-config")]
public class AuditorTests : IDisposable
{
   private const string Performer = "deployer@example.com";

   private readonly Cfg _config;
   private readonly GitScope _gitScope;

   public AuditorTests()
   {
      _gitScope = new GitScope(new FakeGitRunner
      {
         Outputs = { ["config user.email"] = Performer }
      });

      _config = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["servers"] = L("1.1.1.1")
      };
   }

   public void Dispose() => _gitScope.Dispose();

   [Fact]
   public void Record()
   {
      AssertRecorded(
         recordedAt => $"mkdir -p .kamal && echo \"[{recordedAt}] [{Performer}] app removed container\" >> .kamal/app-audit.log",
         () => NewCommand().Record("app removed container"));
   }

   [Fact]
   public void RecordWithDestination()
   {
      AssertRecorded(
         recordedAt => $"mkdir -p .kamal && echo \"[{recordedAt}] [{Performer}] [staging] app removed container\" >> .kamal/app-staging-audit.log",
         () => NewCommand(destination: "staging").Record("app removed container"));
   }

   [Fact]
   public void RecordWithCommandDetails()
   {
      AssertRecorded(
         recordedAt => $"mkdir -p .kamal && echo \"[{recordedAt}] [{Performer}] [web] app removed container\" >> .kamal/app-audit.log",
         () => NewCommand(details: [KV("role", "web")]).Record("app removed container"));
   }

   [Fact]
   public void RecordWithArgDetails()
   {
      AssertRecorded(
         recordedAt => $"mkdir -p .kamal && echo \"[{recordedAt}] [{Performer}] [value] app removed container\" >> .kamal/app-audit.log",
         () => NewCommand().Record("app removed container", KV("detail", "value")));
   }

   private Kamal.Commands.Auditor NewCommand(string? destination = null, KeyValuePair<string, object?>[]? details = null)
   {
      return new Kamal.Commands.Auditor(
         new KamalConfiguration(_config, destination: destination, version: "123"),
         details ?? []);
   }

   /// <summary>Ruby freezes time; here the expected string is checked against the timestamps just before and after the call.</summary>
   private static void AssertRecorded(Func<string, string> expected, Func<object[]> record)
   {
      var before = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
      var actual = Join(record());
      var after = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

      Assert.Contains(actual, new[] { expected(before), expected(after) });
   }
}
