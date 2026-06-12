using Kamal.Configuration.Validation;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Logging</c>.</summary>
public sealed class Logging
{
   public Logging(object? loggingConfig, string context = "logging")
   {
      LoggingConfig = RubyHelpers.AsDict(loggingConfig) ?? new OrderedDictionary<string, object?>();

      new Validator(loggingConfig ?? LoggingConfig, ValidationDocs.Doc("logging").Get("logging"), context).Validate();
   }

   public IDictionary<string, object?> LoggingConfig { get; }

   public string? Driver => LoggingConfig.Get("driver") as string;

   public IDictionary<string, object?> Options =>
      RubyHelpers.AsDict(LoggingConfig.Fetch("options", null)) ?? new OrderedDictionary<string, object?>();

   public Logging Merge(Logging other)
   {
      return new Logging(RubyHelpers.DeepMerge(LoggingConfig, other.LoggingConfig));
   }

   public List<object> Args
   {
      get
      {
         if (RubyHelpers.IsPresent(Driver) || RubyHelpers.IsPresent(Options))
         {
            var driverOption = new OrderedDictionary<string, object?>();
            if (Driver is not null)
               driverOption["log-driver"] = Driver;

            return KamalUtils.Optionize(driverOption)
               .Concat(KamalUtils.Argumentize("--log-opt", Options))
               .ToList();
         }

         return KamalUtils.Argumentize("--log-opt", new OrderedDictionary<string, object?> { ["max-size"] = "10m" });
      }
   }
}
