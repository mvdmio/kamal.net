using Kamal.Configuration;
using Kamal.Tests.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/hook_test.rb.</summary>
[Collection("kamal-config")]
public class HookTests : IDisposable
{
   private readonly Cfg _config;
   private readonly GitScope _gitScope;
   private const string Performer = "deployer@example.com";

   public HookTests()
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
         ["servers"] = L("1.1.1.1"),
         ["builder"] = new Cfg { ["arch"] = "amd64" }
      };
   }

   public void Dispose() => _gitScope.Dispose();

   [Fact]
   public void Run()
   {
      Assert.Equal([".kamal/hooks/foo"], NewCommand().Run("foo"));
   }

   [Fact]
   public void Env()
   {
      var env = NewCommand().Env();

      AssertRecordedAt(env);
      Assert.Equal(
         new Dictionary<string, string>
         {
            ["KAMAL_RECORDED_AT"] = env["KAMAL_RECORDED_AT"],
            ["KAMAL_PERFORMER"] = Performer,
            ["KAMAL_VERSION"] = "123",
            ["KAMAL_SERVICE_VERSION"] = "app@123",
            ["KAMAL_SERVICE"] = "app"
         },
         env);
   }

   [Fact]
   public void RunWithCustomHooksPath()
   {
      Assert.Equal(["custom/hooks/path/foo"], NewCommand(new Cfg { ["hooks_path"] = "custom/hooks/path" }).Run("foo"));
   }

   [Fact]
   public void EnvWithSecrets()
   {
      using var secrets = new TestSecrets("DB_PASSWORD=secret");
      var env = NewCommand(secrets: secrets).Env(secrets: true);

      AssertRecordedAt(env);
      Assert.Equal(
         new Dictionary<string, string>
         {
            ["KAMAL_RECORDED_AT"] = env["KAMAL_RECORDED_AT"],
            ["KAMAL_PERFORMER"] = Performer,
            ["KAMAL_VERSION"] = "123",
            ["KAMAL_SERVICE_VERSION"] = "app@123",
            ["KAMAL_SERVICE"] = "app",
            ["DB_PASSWORD"] = "secret"
         },
         env);
   }

   private Kamal.Commands.Hook NewCommand(Cfg? extraConfig = null, TestSecrets? secrets = null)
   {
      var config = extraConfig is null ? _config : Cmd.DeepMerge(_config, extraConfig);

      return new Kamal.Commands.Hook(new KamalConfiguration(config, version: "123", secrets: secrets?.Secrets));
   }

   private static void AssertRecordedAt(IDictionary<string, string> env)
   {
      var recordedAt = DateTime.ParseExact(env["KAMAL_RECORDED_AT"], "yyyy-MM-dd'T'HH:mm:ss'Z'", null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
      Assert.True(Math.Abs((DateTime.UtcNow - recordedAt).TotalSeconds) < 10);
   }
}
