using Kamal.Configuration;
using Kamal.Secrets;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/env_test.rb.</summary>
[Collection("kamal-config")]
public class EnvTests
{
   [Fact]
   public void Simple()
   {
      AssertConfig(
         config: new Cfg { ["foo"] = "bar", ["baz"] = "haz" },
         clear: new Cfg { ["foo"] = "bar", ["baz"] = "haz" });
   }

   [Fact]
   public void Clear()
   {
      AssertConfig(
         config: new Cfg { ["clear"] = new Cfg { ["foo"] = "bar", ["baz"] = "haz" } },
         clear: new Cfg { ["foo"] = "bar", ["baz"] = "haz" });
   }

   [Fact]
   public void Secret()
   {
      using var secrets = new TestSecrets("PASSWORD=hello");

      AssertConfig(
         config: new Cfg { ["secret"] = L("PASSWORD") },
         secrets: new Cfg { ["PASSWORD"] = "hello" },
         kamalSecrets: secrets.Secrets);
   }

   [Fact]
   public void MissingSecret()
   {
      var env = new Env(
         new Cfg { ["secret"] = L("PASSWORD") },
         new KamalSecrets(secretsPath: ".kamal/secrets"));

      // DEVIATION: Ruby's Kamal::Secrets raises Kamal::ConfigurationError; the C# KamalSecrets
      // throws KeyNotFoundException for a missing secret.
      Assert.Throws<KeyNotFoundException>(() => env.SecretsIo);
   }

   [Fact]
   public void SecretAndClear()
   {
      using var secrets = new TestSecrets("PASSWORD=hello");

      AssertConfig(
         config: new Cfg
         {
            ["secret"] = L("PASSWORD"),
            ["clear"] = new Cfg { ["foo"] = "bar", ["baz"] = "haz" }
         },
         clear: new Cfg { ["foo"] = "bar", ["baz"] = "haz" },
         secrets: new Cfg { ["PASSWORD"] = "hello" },
         kamalSecrets: secrets.Secrets);
   }

   [Fact]
   public void AliasedSecrets()
   {
      using var secrets = new TestSecrets("ALIASED_PASSWORD=hello");

      AssertConfig(
         config: new Cfg
         {
            ["secret"] = L("PASSWORD:ALIASED_PASSWORD"),
            ["clear"] = new Cfg()
         },
         secrets: new Cfg { ["PASSWORD"] = "hello" },
         kamalSecrets: secrets.Secrets);
   }

   private static void AssertConfig(Cfg config, Cfg? clear = null, Cfg? secrets = null, KamalSecrets? kamalSecrets = null)
   {
      var env = new Env(config, kamalSecrets ?? new KamalSecrets(secretsPath: ".kamal/secrets"));

      var expectedClearArgs = (clear ?? new Cfg())
         .SelectMany(pair => new[] { "--env", $"{pair.Key}=\"{pair.Value}\"" })
         .ToList();
      Assert.Equal(expectedClearArgs, S(env.ClearArgs));

      var expectedSecrets = string.Join("\n", (secrets ?? new Cfg()).Select(pair => $"{pair.Key}={pair.Value}")) + "\n";
      Assert.Equal(expectedSecrets, env.SecretsIo);
   }
}
