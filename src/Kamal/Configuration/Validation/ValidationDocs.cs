using System.Collections.Concurrent;
using System.Reflection;

namespace Kamal.Configuration.Validation;

/// <summary>
/// Access to the example configuration docs embedded from <c>Configuration/Docs/*.yml</c>
/// (Ruby ships these in <c>lib/kamal/configuration/docs</c>). They serve double duty:
/// the raw text backs the future <c>kamal docs</c> command and the parsed form is the
/// example structure the validators check user configuration against.
/// </summary>
public static class ValidationDocs
{
   private static readonly ConcurrentDictionary<string, IDictionary<string, object?>> ParsedDocs = new();

   /// <summary>The available doc names (accessory, alias, boot, builder, configuration, ...).</summary>
   public static IReadOnlyList<string> Names =>
      Assembly.GetExecutingAssembly()
         .GetManifestResourceNames()
         .Where(name => name.Contains(".Configuration.Docs.") && name.EndsWith(".yml"))
         .Select(name => name.Split(".Configuration.Docs.").Last()[..^".yml".Length])
         .OrderBy(name => name, StringComparer.Ordinal)
         .ToList();

   /// <summary>Returns the raw YAML documentation for a section (for the <c>kamal docs</c> command).</summary>
   public static string Read(string name)
   {
      var assembly = Assembly.GetExecutingAssembly();
      var resourceName = assembly
         .GetManifestResourceNames()
         .FirstOrDefault(resource => resource.EndsWith($".Configuration.Docs.{name}.yml", StringComparison.OrdinalIgnoreCase));

      if (resourceName is null)
         throw new KamalConfigurationError($"No documentation found for {name}");

      using var stream = assembly.GetManifestResourceStream(resourceName)!;
      using var reader = new StreamReader(stream);
      return reader.ReadToEnd();
   }

   /// <summary>The parsed example document for a section (Ruby's <c>validation_yml</c>), cached.</summary>
   public static IDictionary<string, object?> Doc(string name)
   {
      return ParsedDocs.GetOrAdd(name, key =>
         YamlLoader.Load(Read(key)) as IDictionary<string, object?>
            ?? throw new KamalConfigurationError($"Invalid documentation yml for {key}"));
   }
}
