using Kamal.Configuration;

namespace Kamal.Utils;

/// <summary>
/// Port of <c>Kamal::Tags</c>: the audit/log tags attached to every Kamal operation
/// (recorded_at, performer, destination, version, service_version, service).
/// </summary>
public sealed class KamalTags
{
   private readonly OrderedDictionary<string, object?> _tags;

   public KamalTags(IEnumerable<KeyValuePair<string, object?>> tags)
   {
      _tags = new OrderedDictionary<string, object?>();

      // Ruby compacts the tags, dropping nil values.
      foreach (var (key, value) in tags)
      {
         if (value is not null)
            _tags[key] = value;
      }
   }

   /// <summary>The tags by name, in insertion order.</summary>
   public IReadOnlyDictionary<string, object?> Tags => _tags;

   /// <summary>Port of <c>Kamal::Tags.from_config</c>.</summary>
   public static KamalTags FromConfig(KamalConfiguration config, params KeyValuePair<string, object?>[] extra)
   {
      return new KamalTags(DefaultTags(config).Concat(extra));
   }

   /// <summary>Port of <c>Kamal::Tags.default_tags</c>.</summary>
   public static OrderedDictionary<string, object?> DefaultTags(KamalConfiguration config)
   {
      return new OrderedDictionary<string, object?>
      {
         ["recorded_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
         ["performer"] = RubyHelpers.Presence(Git.Email) ?? Environment.UserName,
         ["destination"] = config.Destination,
         ["version"] = config.Version,
         ["service_version"] = ServiceVersion(config),
         ["service"] = config.Service
      };
   }

   /// <summary>Port of <c>Kamal::Tags.service_version</c>: "service@abbreviated_version".</summary>
   public static string ServiceVersion(KamalConfiguration config)
   {
      return string.Join("@", new[] { config.Service, config.AbbreviatedVersion }.Where(part => part is not null));
   }

   /// <summary>Port of <c>Kamal::Tags#env</c>: the tags as KAMAL_* environment variables.</summary>
   public OrderedDictionary<string, string> Env
   {
      get
      {
         var env = new OrderedDictionary<string, string>();
         foreach (var (key, value) in _tags)
            env[$"KAMAL_{key.ToUpperInvariant()}"] = RubyHelpers.RubyToS(value);

         return env;
      }
   }

   /// <summary>Port of <c>Kamal::Tags#to_s</c>: "[value] [value] ...".</summary>
   public override string ToString()
   {
      return string.Join(" ", _tags.Values.Select(value => $"[{RubyHelpers.RubyToS(value)}]"));
   }

   /// <summary>Port of <c>Kamal::Tags#except</c>.</summary>
   public KamalTags Except(params string[] keys)
   {
      return new KamalTags(_tags.Where(pair => !keys.Contains(pair.Key)));
   }
}
