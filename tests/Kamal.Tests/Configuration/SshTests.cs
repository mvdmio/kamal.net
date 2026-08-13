using Kamal.Configuration;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Port of test/configuration/ssh_test.rb (logger-level assertions excluded, see Ssh deviation).</summary>
[Collection("kamal-config")]
public class SshTests
{
   private readonly Cfg _deploy = BaseDeploy();

   private KamalConfiguration Config => new(_deploy);

   [Fact]
   public void SshOptions()
   {
      Assert.Equal("root", Config.Ssh.Options["user"]);

      _deploy["ssh"] = new Cfg { ["user"] = "app" };
      Assert.Equal("app", Config.Ssh.Options["user"]);

      _deploy["ssh"] = new Cfg { ["log_level"] = "debug" };
      Assert.Equal("debug", Config.Ssh.LogLevel);

      _deploy["ssh"] = new Cfg { ["port"] = 2222 };
      Assert.Equal(2222, Config.Ssh.Options["port"]);

      _deploy["ssh"] = new Cfg { ["config"] = true };
      Assert.Equal(true, Config.Ssh.Options["config"]);

      _deploy["ssh"] = new Cfg { ["config"] = false };
      Assert.Equal(false, Config.Ssh.Options["config"]);

      _deploy["ssh"] = new Cfg { ["config"] = "~/config.mine" };
      Assert.Equal("~/config.mine", Config.Ssh.Options["config"]);

      _deploy["ssh"] = new Cfg { ["config"] = L("~/config.mine.1", "~/config.mine.2") };
      Assert.Equal(L("~/config.mine.1", "~/config.mine.2"), Config.Ssh.Options["config"]);

      Assert.False(Config.Ssh.Options.ContainsKey("forward_agent"));

      _deploy["ssh"] = new Cfg { ["forward_agent"] = false };
      Assert.Equal(false, Config.Ssh.Options["forward_agent"]);

      _deploy["ssh"] = new Cfg { ["forward_agent"] = true };
      Assert.Equal(true, Config.Ssh.Options["forward_agent"]);
   }

   [Fact]
   public void SshOptionsWithProxyHost()
   {
      _deploy["ssh"] = new Cfg { ["proxy"] = "1.2.3.4" };

      var proxy = Assert.IsType<SshJumpProxy>(Config.Ssh.Options["proxy"]);
      Assert.Equal("root@1.2.3.4", proxy.JumpProxies);
   }

   [Fact]
   public void SshOptionsWithProxyHostAndUser()
   {
      _deploy["ssh"] = new Cfg { ["proxy"] = "app@1.2.3.4" };

      var proxy = Assert.IsType<SshJumpProxy>(Config.Ssh.Options["proxy"]);
      Assert.Equal("app@1.2.3.4", proxy.JumpProxies);
   }

   [Fact]
   public void SshOptionsWithProxyCommand()
   {
      _deploy["ssh"] = new Cfg { ["proxy_command"] = "ssh -W %h:%p user@proxy" };

      var proxy = Assert.IsType<SshCommandProxy>(Config.Ssh.Options["proxy"]);
      Assert.Equal("ssh -W %h:%p user@proxy", proxy.Command);
   }

   [Fact]
   public void SshKeyDataWithPlainValueArray()
   {
      _deploy["ssh"] = new Cfg { ["key_data"] = L("-----BEGIN OPENSSH PRIVATE KEY-----") };

      Assert.Equal(["-----BEGIN OPENSSH PRIVATE KEY-----"], (List<string>)Config.Ssh.Options["key_data"]!);
   }

   [Fact]
   public void SshKeyDataWithArrayContainingOneSecretString()
   {
      using var secrets = new TestSecrets("SSH_PRIVATE_KEY=secret_ssh_key");
      _deploy["ssh"] = new Cfg { ["key_data"] = L("SSH_PRIVATE_KEY") };

      var config = new KamalConfiguration(_deploy, secrets: secrets.Secrets);
      Assert.Equal(["secret_ssh_key"], (List<string>)config.Ssh.Options["key_data"]!);
   }

   [Fact]
   public void SshKeyDataWithArrayContainingMultipleSecretStrings()
   {
      using var secrets = new TestSecrets("SSH_PRIVATE_KEY=secret_ssh_key\nSECOND_KEY=second_secret_ssh_key");
      _deploy["ssh"] = new Cfg { ["key_data"] = L("SSH_PRIVATE_KEY", "SECOND_KEY") };

      var config = new KamalConfiguration(_deploy, secrets: secrets.Secrets);
      Assert.Equal(["secret_ssh_key", "second_secret_ssh_key"], (List<string>)config.Ssh.Options["key_data"]!);
   }

   [Fact]
   public void StrictHostKeyCheckingDefaultsToFalse()
   {
      Assert.False(Config.Ssh.StrictHostKeyChecking);
   }

   [Fact]
   public void StrictHostKeyCheckingCanBeEnabled()
   {
      _deploy["ssh"] = new Cfg { ["strict_host_key_checking"] = true };
      Assert.True(Config.Ssh.StrictHostKeyChecking);
      Assert.Equal(true, Config.Ssh.Options["strict_host_key_checking"]);
   }

   [Fact]
   public void KnownHostsDefaultsToUserKnownHostsWhenStrict()
   {
      _deploy["ssh"] = new Cfg { ["strict_host_key_checking"] = true };
      var paths = Config.Ssh.ResolvedKnownHostsPaths();
      Assert.Contains(paths, p => p.EndsWith(Path.Combine(".ssh", "known_hosts"), StringComparison.Ordinal));
   }

   [Fact]
   public void KnownHostsCanBeConfigured()
   {
      _deploy["ssh"] = new Cfg
      {
         ["strict_host_key_checking"] = true,
         ["known_hosts"] = "/tmp/custom_known_hosts"
      };
      Assert.Equal(["/tmp/custom_known_hosts"], Config.Ssh.ResolvedKnownHostsPaths());
   }

   [Fact]
   public void PassphraseResolvesFromSecrets()
   {
      using var secrets = new TestSecrets("SSH_KEY_PASSPHRASE=s3cret");
      _deploy["ssh"] = new Cfg { ["passphrase"] = "SSH_KEY_PASSPHRASE" };

      var config = new KamalConfiguration(_deploy, secrets: secrets.Secrets);
      Assert.Equal("s3cret", config.Ssh.Passphrase);
   }
}
