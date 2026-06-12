using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal;

public sealed partial class Commander
{
   /// <summary>
   /// Port of <c>Kamal::Commander::Specifics</c>: resolves the effective hosts/roles after
   /// applying the <c>--hosts</c>/<c>--roles</c> filters, with primary role and host sorted first.
   /// </summary>
   public sealed class Specifics
   {
      private readonly KamalConfiguration _config;
      private readonly List<string>? _specificHosts;
      private readonly List<Role>? _specificRoles;
      private List<string>? _appHosts;

      public Specifics(KamalConfiguration config, List<string>? specificHosts, List<Role>? specificRoles)
      {
         _config = config;
         _specificHosts = specificHosts;
         _specificRoles = specificRoles;

         Roles = SpecifiedRoles();
         Hosts = SpecifiedHosts();

         PrimaryHost = specificHosts?.FirstOrDefault() ?? PrimarySpecificRole()?.PrimaryHost ?? config.PrimaryHost;
         PrimaryRole = PrimaryOrFirstRole(PrimaryHost is null ? [] : RolesOn(PrimaryHost));

         KamalUtils.StableSort(Roles, role => role == PrimaryRole ? 0 : 1);
         SortPrimaryRoleHostsFirst(Hosts);
      }

      public string? PrimaryHost { get; }

      public Role? PrimaryRole { get; }

      public List<string> Hosts { get; }

      public List<Role> Roles { get; }

      public List<Role> RolesOn(string host)
      {
         return Roles.Where(role => role.Hosts.Contains(host)).ToList();
      }

      public List<string> AppHosts => _appHosts ??= SortPrimaryRoleHostsFirst(Intersect(_config.AppHosts, SpecifiedHosts()));

      public List<string> ProxyHosts => Intersect(_config.ProxyHosts, SpecifiedHosts());

      public List<string> AccessoryHosts => Intersect(_config.Accessories.SelectMany(accessory => accessory.Hosts), SpecifiedHosts());

      private Role? PrimarySpecificRole()
      {
         return _specificRoles is { Count: > 0 } ? PrimaryOrFirstRole(_specificRoles) : null;
      }

      private Role? PrimaryOrFirstRole(List<Role> roles)
      {
         return roles.FirstOrDefault(role => role == _config.PrimaryRole) ?? roles.FirstOrDefault();
      }

      private List<Role> SpecifiedRoles()
      {
         var hostFilter = _specificHosts ?? _config.AllHosts;

         return (_specificRoles ?? _config.Roles)
            .Where(role => role.Hosts.Any(hostFilter.Contains))
            .ToList();
      }

      private List<string> SpecifiedHosts()
      {
         var specifiedHosts = (_specificHosts ?? _config.AllHosts).ToList();
         var specificRoleHosts = _specificRoles?.SelectMany(role => role.Hosts).ToList();

         if (specificRoleHosts is { Count: > 0 })
            return specifiedHosts.Where(specificRoleHosts.Contains).ToList();

         return specifiedHosts;
      }

      private List<string> SortPrimaryRoleHostsFirst(List<string> hosts)
      {
         KamalUtils.StableSort(hosts, host => RolesOn(host).Any(role => role == PrimaryRole) ? 0 : 1);

         return hosts;
      }

      /// <summary>Ruby's <c>Array#&amp;</c>: elements of the left list (in order, deduplicated) present in the right.</summary>
      private static List<string> Intersect(IEnumerable<string> left, ICollection<string> right)
      {
         return left.Where(right.Contains).Distinct().ToList();
      }
   }
}
