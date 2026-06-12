using Kamal.Configuration.Validation;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Servers</c>.</summary>
public sealed class Servers
{
   public Servers(KamalConfiguration config)
   {
      var serversConfig = config.RawConfig.Get("servers");

      new ServersValidator(serversConfig, ValidationDocs.Doc("servers").Get("servers"), "servers").Validate();

      Roles = RoleNames(serversConfig).Select(roleName => new Role(roleName, config)).ToList();
   }

   public List<Role> Roles { get; }

   private static List<string> RoleNames(object? serversConfig)
   {
      return serversConfig switch
      {
         null => [],
         List<object?> => ["web"],
         IDictionary<string, object?> dict => dict.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList(),
         _ => []
      };
   }
}
