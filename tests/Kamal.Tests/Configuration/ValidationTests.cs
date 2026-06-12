using Kamal.Configuration;
using Kamal.Utils;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/validation_test.rb.</summary>
[Collection("kamal-config")]
public class ValidationTests
{
   private static void AssertError(string message, string key, object? invalidValue)
   {
      var validConfig = new Cfg
      {
         ["service"] = "app",
         ["image"] = "app",
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["registry"] = new Cfg { ["username"] = "user", ["password"] = "secret" },
         ["servers"] = L("1.1.1.1")
      };

      validConfig[key] = invalidValue;

      var error = Assert.Throws<KamalConfigurationError>(() => new KamalConfiguration(validConfig));
      Assert.Equal(message, error.Message);
   }

   private static void AssertErrors(string message, params (string Key, object? Value)[] overrides)
   {
      var validConfig = new Cfg
      {
         ["service"] = "app",
         ["image"] = "app",
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["registry"] = new Cfg { ["username"] = "user", ["password"] = "secret" },
         ["servers"] = L("1.1.1.1")
      };

      foreach (var (key, value) in overrides)
         validConfig[key] = value;

      var error = Assert.Throws<KamalConfigurationError>(() => new KamalConfiguration(validConfig));
      Assert.Equal(message, error.Message);
   }

   [Fact]
   public void UnknownRootKey()
   {
      AssertError("unknown key: unknown", "unknown", "value");
      AssertErrors("unknown keys: unknown, unknown2", ("unknown", "value"), ("unknown2", "value"));
   }

   [Fact]
   public void WrongRootTypes()
   {
      foreach (var key in new[] { "service", "image", "asset_path", "hooks_path", "secrets_path", "primary_role", "minimum_version", "run_directory" })
         AssertError($"{key}: should be a string", key, L());

      foreach (var key in new[] { "require_destination", "allow_empty_roles" })
         AssertError($"{key}: should be a boolean", key, "foo");

      foreach (var key in new[] { "deploy_timeout", "drain_timeout", "retain_containers", "readiness_delay" })
         AssertError($"{key}: should be an integer", key, "foo");

      AssertError("volumes: should be an array", "volumes", "foo");

      AssertError("servers: should be an array or a hash", "servers", "foo");

      foreach (var key in new[] { "labels", "registry", "accessories", "env", "ssh", "sshkit", "builder", "proxy", "boot", "logging" })
         AssertError($"{key}: should be a hash", key, L());
   }

   [Fact]
   public void Servers()
   {
      AssertError("servers: should be an array or a hash", "servers", "foo");
      AssertError("servers/0: should be a string or a hash", "servers", L(L()));
      AssertError("servers/0: multiple hosts found", "servers", L(new Cfg { ["a"] = "b", ["c"] = "d" }));
      AssertError("servers/0/foo: should be a string or an array", "servers", L(new Cfg { ["foo"] = new Cfg() }));
      AssertError("servers/0/foo/0: should be a string", "servers", L(new Cfg { ["foo"] = L(L()) }));
   }

   [Fact]
   public void Roles()
   {
      AssertError("servers/web: should be an array or a hash", "servers", new Cfg { ["web"] = "foo" });
      AssertError("servers/web/hosts: should be an array", "servers", new Cfg { ["web"] = new Cfg { ["hosts"] = "" } });
      AssertError("servers/web/hosts/0: should be a string or a hash", "servers", new Cfg { ["web"] = new Cfg { ["hosts"] = L(L()) } });
      AssertError("servers/web/options: should be a hash", "servers", new Cfg { ["web"] = new Cfg { ["options"] = "" } });
      AssertError("servers/web/logging/options: should be a hash", "servers", new Cfg { ["web"] = new Cfg { ["logging"] = new Cfg { ["options"] = "" } } });
      AssertError("servers/web/logging/driver: should be a string", "servers", new Cfg { ["web"] = new Cfg { ["logging"] = new Cfg { ["driver"] = L() } } });
      AssertError(
         "servers/web/labels/service: invalid label. destination, role, and service are reserved labels",
         "servers", new Cfg { ["web"] = new Cfg { ["labels"] = new Cfg { ["service"] = "foo" } } });
      AssertError("servers/web/labels: should be a hash", "servers", new Cfg { ["web"] = new Cfg { ["labels"] = L() } });
      AssertError("servers/web/env: should be a hash", "servers", new Cfg { ["web"] = new Cfg { ["env"] = L() } });
      AssertError(
         "servers/web/env: tags are only allowed in the root env",
         "servers", new Cfg { ["web"] = new Cfg { ["hosts"] = L("1.1.1.1"), ["env"] = new Cfg { ["tags"] = new Cfg() } } });
   }

   [Fact]
   public void RegistryErrors()
   {
      AssertError("registry/username: is required", "registry", new Cfg());
      AssertError("registry/password: is required", "registry", new Cfg { ["username"] = "foo" });
      AssertError(
         "registry/password: should be a string or an array with one string (for secret lookup)",
         "registry", new Cfg { ["username"] = "foo", ["password"] = L("SECRET1", "SECRET2") });
      AssertError(
         "registry/server: should be a string",
         "registry", new Cfg { ["username"] = "foo", ["password"] = "bar", ["server"] = L() });
   }

   [Fact]
   public void Accessories()
   {
      AssertError("accessories/accessory1: should be a hash", "accessories", new Cfg { ["accessory1"] = L() });
      AssertError("accessories/accessory1: unknown key: unknown", "accessories", new Cfg { ["accessory1"] = new Cfg { ["unknown"] = "baz" } });
      AssertError("accessories/accessory1/options: should be a hash", "accessories", new Cfg { ["accessory1"] = new Cfg { ["options"] = L() } });
      AssertError(
         "accessories/accessory1/labels/destination: invalid label. destination, role, and service are reserved labels",
         "accessories", new Cfg { ["accessory1"] = new Cfg { ["host"] = "host", ["labels"] = new Cfg { ["destination"] = "foo" } } });
      AssertError("accessories/accessory1/host: should be a string", "accessories", new Cfg { ["accessory1"] = new Cfg { ["host"] = L() } });
      AssertError("accessories/accessory1/env: should be a hash", "accessories", new Cfg { ["accessory1"] = new Cfg { ["env"] = L() } });
      AssertError(
         "accessories/accessory1/env: tags are only allowed in the root env",
         "accessories", new Cfg { ["accessory1"] = new Cfg { ["host"] = "host", ["env"] = new Cfg { ["tags"] = new Cfg() } } });
   }

   [Fact]
   public void EnvErrors()
   {
      AssertError("env: should be a hash", "env", L());
      AssertError("env/FOO: should be a string", "env", new Cfg { ["FOO"] = L() });
      AssertError("env/clear/FOO: should be a string", "env", new Cfg { ["clear"] = new Cfg { ["FOO"] = L() } });
      AssertError("env/secret: should be an array", "env", new Cfg { ["secret"] = new Cfg { ["FOO"] = L() } });
      AssertError("env/secret/0: should be a string", "env", new Cfg { ["secret"] = L(L()) });
      AssertError("env/tags: should be a hash", "env", new Cfg { ["tags"] = L() });
      AssertError("env/tags/tag1: should be a hash", "env", new Cfg { ["tags"] = new Cfg { ["tag1"] = "foo" } });
      AssertError("env/tags/tag1/FOO: should be a string", "env", new Cfg { ["tags"] = new Cfg { ["tag1"] = new Cfg { ["FOO"] = L() } } });
      AssertError(
         "env/tags/tag1/clear/FOO: should be a string",
         "env", new Cfg { ["tags"] = new Cfg { ["tag1"] = new Cfg { ["clear"] = new Cfg { ["FOO"] = L() } } } });
      AssertError("env/tags/tag1/secret: should be an array", "env", new Cfg { ["tags"] = new Cfg { ["tag1"] = new Cfg { ["secret"] = new Cfg() } } });
      AssertError("env/tags/tag1/secret/0: should be a string", "env", new Cfg { ["tags"] = new Cfg { ["tag1"] = new Cfg { ["secret"] = L(L()) } } });
      AssertError(
         "env/tags/tag1: tags are only allowed in the root env",
         "env", new Cfg { ["tags"] = new Cfg { ["tag1"] = new Cfg { ["tags"] = new Cfg() } } });
   }

   [Fact]
   public void SshErrors()
   {
      AssertError("ssh: unknown key: foo", "ssh", new Cfg { ["foo"] = "bar" });
      AssertError("ssh/user: should be a string", "ssh", new Cfg { ["user"] = L() });
      AssertError("ssh/config: should be a boolean or a string or an array", "ssh", new Cfg { ["config"] = 1 });
   }

   [Fact]
   public void SshkitErrors()
   {
      AssertError("sshkit: unknown key: foo", "sshkit", new Cfg { ["foo"] = "bar" });
      AssertError("sshkit/max_concurrent_starts: should be an integer", "sshkit", new Cfg { ["max_concurrent_starts"] = "foo" });
      AssertError("sshkit/dns_retries: should be an integer", "sshkit", new Cfg { ["dns_retries"] = "foo" });
   }

   [Fact]
   public void BuilderErrors()
   {
      AssertError("builder: unknown key: foo", "builder", new Cfg { ["foo"] = "bar", ["arch"] = "amd64" });
      AssertError("builder/remote: should be a string", "builder", new Cfg { ["remote"] = new Cfg { ["foo"] = "bar" }, ["arch"] = "amd64" });
      AssertError("builder/arch: should be an array or a string", "builder", new Cfg { ["arch"] = new Cfg() });
      AssertError("builder/args: should be a hash", "builder", new Cfg { ["args"] = L("foo"), ["arch"] = "amd64" });
      AssertError("builder/cache/options: should be a string", "builder", new Cfg { ["cache"] = new Cfg { ["options"] = L() }, ["arch"] = "amd64" });
      AssertError(
         "builder: buildpacks only support building for one arch",
         "builder", new Cfg { ["arch"] = L("amd64", "arm64"), ["pack"] = new Cfg { ["builder"] = "heroku/builder:24" } });
   }

   [Fact]
   public void LocalRegistryWithRemoteBuilderRequiresSshUrl()
   {
      var previous = KamalUtils.DockerArchOverride;
      KamalUtils.DockerArchOverride = "amd64";

      try
      {
         AssertErrors(
            "Local registry with remote builder requires an SSH URL (e.g., ssh://user@host)",
            ("registry", new Cfg { ["server"] = "localhost:5000" }),
            ("builder", new Cfg { ["arch"] = "arm64", ["remote"] = "docker-container://remote-builder" }));

         // Should not raise with an SSH URL.
         _ = new KamalConfiguration(new Cfg
         {
            ["service"] = "app",
            ["image"] = "app",
            ["registry"] = new Cfg { ["server"] = "localhost:5000" },
            ["builder"] = new Cfg { ["arch"] = "arm64", ["remote"] = "ssh://user@host" },
            ["servers"] = L("1.1.1.1")
         });
      }
      finally
      {
         KamalUtils.DockerArchOverride = previous;
      }
   }
}
