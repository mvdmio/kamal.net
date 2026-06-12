using Kamal.Configuration.Validation;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Boot</c>.</summary>
public sealed class Boot
{
   public Boot(KamalConfiguration config)
   {
      BootConfig = RubyHelpers.AsDict(config.RawConfig.Get("boot")) ?? new OrderedDictionary<string, object?>();
      HostCount = config.AllHosts.Count;

      new Validator(config.RawConfig.Get("boot") ?? BootConfig, ValidationDocs.Doc("boot").Get("boot"), "boot").Validate();
   }

   public IDictionary<string, object?> BootConfig { get; }
   public int HostCount { get; }

   /// <summary>The boot limit: an absolute number, or computed from a percentage string like "25%".</summary>
   public object? Limit
   {
      get
      {
         var limit = BootConfig.Get("limit");

         if (RubyHelpers.RubyToS(limit).EndsWith('%'))
         {
            var percentage = RubyToI(RubyHelpers.RubyToS(limit));
            return Math.Max(HostCount * percentage / 100, 1);
         }

         return limit;
      }
   }

   public object? Wait => BootConfig.Get("wait");

   public object? ParallelRoles => BootConfig.Get("parallel_roles");

   /// <summary>Ruby's <c>String#to_i</c>: parses the leading integer, 0 when there is none.</summary>
   private static int RubyToI(string value)
   {
      var digits = value.TakeWhile(char.IsAsciiDigit).ToArray();
      return digits.Length > 0 ? int.Parse(new string(digits)) : 0;
   }
}
