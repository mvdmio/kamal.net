using Kamal.Configuration.Validation;
using Kamal.Secrets;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>An SSH proxy configuration (stand-in for <c>Net::SSH::Proxy::Jump</c> / <c>Net::SSH::Proxy::Command</c>).</summary>
public abstract record SshProxy;

/// <summary>A jump-host proxy in the form <c>user@host</c>.</summary>
public sealed record SshJumpProxy(string JumpProxies) : SshProxy;

/// <summary>A raw proxy command.</summary>
public sealed record SshCommandProxy(string Command) : SshProxy;

/// <summary>Port of <c>Kamal::Configuration::Ssh</c>.</summary>
public sealed class Ssh
{
   private readonly IDictionary<string, object?> _sshConfig;
   private readonly KamalSecrets _secrets;

   public Ssh(KamalConfiguration config)
   {
      _sshConfig = RubyHelpers.AsDict(config.RawConfig.Get("ssh")) ?? new OrderedDictionary<string, object?>();
      _secrets = config.Secrets;

      new Validator(config.RawConfig.Get("ssh") ?? _sshConfig, ValidationDocs.Doc("ssh").Get("ssh"), "ssh").Validate();
   }

   public string User => RubyHelpers.RubyToS(_sshConfig.Fetch("user", "root"));

   public object Port => _sshConfig.Fetch("port", 22)!;

   public SshProxy? Proxy
   {
      get
      {
         if (_sshConfig.Get("proxy") is string proxy && RubyHelpers.IsPresent(proxy))
            return new SshJumpProxy(proxy.Contains('@') ? proxy : $"root@{proxy}");

         if (_sshConfig.Get("proxy_command") is string proxyCommand && RubyHelpers.IsPresent(proxyCommand))
            return new SshCommandProxy(proxyCommand);

         return null;
      }
   }

   public object? KeysOnly => _sshConfig.Get("keys_only");

   public object? Keys => _sshConfig.Get("keys");

   public List<string>? KeyData
   {
      get
      {
         var keyData = RubyHelpers.AsList(_sshConfig.Get("key_data"));
         if (keyData is null)
            return null;

         return keyData.Select(entry =>
         {
            var key = RubyHelpers.RubyToS(entry);
            if (_secrets.ContainsKey(key))
               return _secrets[key];

            Console.Error.WriteLine("Inline key_data usage is deprecated and will be removed in Kamal 3. Please store your key_data in a secret.");
            return key;
         }).ToList();
      }
   }

   public object? Config => _sshConfig.Get("config");

   public string LogLevel => RubyHelpers.RubyToS(_sshConfig.Fetch("log_level", "fatal"));

   /// <summary>
   /// Port of <c>options</c>; the Ruby version also includes a stderr logger, which the
   /// C# port leaves to the SSH layer (deviation).
   /// </summary>
   public OrderedDictionary<string, object?> Options
   {
      get
      {
         var options = new OrderedDictionary<string, object?>
         {
            ["user"] = User,
            ["port"] = Port,
            ["proxy"] = Proxy,
            ["keepalive"] = true,
            ["keepalive_interval"] = 30,
            ["keys_only"] = KeysOnly,
            ["keys"] = Keys,
            ["key_data"] = KeyData,
            ["config"] = Config
         };

         return Compact(options);
      }
   }

   public OrderedDictionary<string, object?> ToH()
   {
      var result = Options;
      result["log_level"] = LogLevel;
      return result;
   }

   private static OrderedDictionary<string, object?> Compact(OrderedDictionary<string, object?> dict)
   {
      var result = new OrderedDictionary<string, object?>();
      foreach (var (key, value) in dict)
      {
         if (value is not null)
            result[key] = value;
      }

      return result;
   }
}
