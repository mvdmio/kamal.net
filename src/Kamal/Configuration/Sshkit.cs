using Kamal.Configuration.Validation;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Sshkit</c>.</summary>
public sealed class Sshkit
{
   private readonly IDictionary<string, object?> _sshkitConfig;

   public Sshkit(KamalConfiguration config)
   {
      _sshkitConfig = RubyHelpers.AsDict(config.RawConfig.Get("sshkit")) ?? new OrderedDictionary<string, object?>();

      new Validator(config.RawConfig.Get("sshkit") ?? _sshkitConfig, ValidationDocs.Doc("sshkit").Get("sshkit"), "sshkit").Validate();
   }

   public int MaxConcurrentStarts => Convert.ToInt32(_sshkitConfig.Fetch("max_concurrent_starts", 30));

   public int PoolIdleTimeout => Convert.ToInt32(_sshkitConfig.Fetch("pool_idle_timeout", 900));

   public int DnsRetries => Convert.ToInt32(_sshkitConfig.Fetch("dns_retries", 3));

   public IDictionary<string, object?> ToH() => _sshkitConfig;
}
