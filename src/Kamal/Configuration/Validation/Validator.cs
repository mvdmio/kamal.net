using System.Collections;
using Kamal.Utils;

namespace Kamal.Configuration.Validation;

/// <summary>The Ruby classes the validator reasons about when checking config value types.</summary>
public enum ConfigType
{
   Hash,
   Array,
   String,
   Integer,
   Float,
   Boolean,
   Nil
}

/// <summary>
/// Port of <c>Kamal::Configuration::Validator</c>: validates user configuration against the
/// example structure from the embedded docs, raising <see cref="KamalConfigurationError"/>
/// with the same messages as Ruby.
/// </summary>
public class Validator
{
   protected object? Config { get; }
   protected object? Example { get; }
   protected string Context { get; private set; }

   public Validator(object? config, object? example, string context)
   {
      Config = config;
      Example = example;
      Context = context;
   }

   public virtual void Validate()
   {
      ValidateAgainstExample(Config, Example);
   }

   protected void ValidateAgainstExample(object? config, object? example)
   {
      ValidateType(config, TypeOf(example));

      if (example is IDictionary<string, object?> exampleDict && config is IDictionary<string, object?> configDict)
      {
         CheckUnknownKeys(configDict, exampleDict);

         foreach (var (key, value) in configDict)
         {
            if (IsExtension(key))
               continue;

            WithContext(key, () =>
            {
               var exampleValue = exampleDict.Get(key);

               if (Equals(exampleValue, "..."))
               {
                  if (!(key == "proxy" && value is bool))
                  {
                     if (key == "servers")
                        ValidateType(value, ConfigType.Array, ConfigType.Hash);
                     else
                        ValidateType(value, ConfigType.Hash);
                  }
               }
               else if (key == "ssl")
               {
                  ValidateType(value, ConfigType.Boolean, ConfigType.Hash);
               }
               else if (key == "hooks_output")
               {
                  ValidateHooksOutput(value);
               }
               else if (key == "hosts")
               {
                  ValidateServers(value);
               }
               else if (exampleValue is List<object?> exampleList)
               {
                  if (key == "arch")
                     ValidateArrayOfOrType(value, TypeOf(exampleList.FirstOrDefault()));
                  else if (key == "config")
                     ValidateSshConfig(value);
                  else if (key is "files" or "directories")
                     ValidatePaths(value);
                  else
                     ValidateArrayOf(value, TypeOf(exampleList.FirstOrDefault()));
               }
               else if (exampleValue is IDictionary<string, object?> exampleValueDict)
               {
                  switch (key)
                  {
                     case "options" or "args":
                        ValidateType(value, ConfigType.Hash);
                        break;
                     case "labels":
                        ValidateHashOf(value, TypeOf(exampleValueDict.Values.FirstOrDefault()));
                        break;
                     default:
                        ValidateAgainstExample(value, exampleValueDict);
                        break;
                  }
               }
               else
               {
                  ValidateType(value, TypeOf(exampleValue));
               }
            });
         }
      }
   }

   protected static ConfigType TypeOf(object? value)
   {
      return value switch
      {
         null => ConfigType.Nil,
         bool => ConfigType.Boolean,
         int or long => ConfigType.Integer,
         double or float or decimal => ConfigType.Float,
         string => ConfigType.String,
         IDictionary<string, object?> => ConfigType.Hash,
         IDictionary => ConfigType.Hash,
         IEnumerable => ConfigType.Array,
         _ => ConfigType.String
      };
   }

   private static bool ValidTypeOf(object? value, ConfigType type)
   {
      if (TypeOf(value) == type)
         return true;

      // Ruby treats symbols, numerics and booleans as string-ish.
      if (type == ConfigType.String && IsStringish(value))
         return true;

      return false;
   }

   private static bool IsStringish(object? value)
   {
      return value is string or int or long or double or float or decimal or bool;
   }

   protected static string TypeDescription(ConfigType type)
   {
      return type switch
      {
         ConfigType.Integer => "an integer",
         ConfigType.Array => "an array",
         ConfigType.Boolean => "a boolean",
         ConfigType.Hash => "a hash",
         ConfigType.String => "a string",
         ConfigType.Float => "a float",
         ConfigType.Nil => "a nilclass",
         _ => $"a {type.ToString().ToLowerInvariant()}"
      };
   }

   protected void ValidateArrayOfOrType(object? value, ConfigType type)
   {
      try
      {
         if (value is List<object?>)
            ValidateArrayOf(value, type);
         else
            ValidateType(value, type);
      }
      catch (KamalConfigurationError)
      {
         TypeError(ConfigType.Array, type);
      }
   }

   protected void ValidateArrayOf(object? array, ConfigType type)
   {
      ValidateType(array, ConfigType.Array);

      var list = (List<object?>)array!;
      for (var index = 0; index < list.Count; index++)
      {
         var value = list[index];
         WithContext(index.ToString(), () => ValidateType(value, type));
      }
   }

   protected void ValidateHashOf(object? hash, ConfigType type)
   {
      ValidateType(hash, ConfigType.Hash);

      foreach (var (key, value) in (IDictionary<string, object?>)hash!)
         WithContext(key, () => ValidateType(value, type));
   }

   protected void ValidateServers(object? servers)
   {
      ValidateType(servers, ConfigType.Array);

      var list = (List<object?>)servers!;
      for (var index = 0; index < list.Count; index++)
      {
         var server = list[index];
         WithContext(index.ToString(), () =>
         {
            ValidateType(server, ConfigType.String, ConfigType.Hash);

            if (server is IDictionary<string, object?> serverDict)
            {
               if (serverDict.Count != 1)
                  Error("multiple hosts found");

               var (host, tags) = serverDict.First();

               WithContext(host, () =>
               {
                  ValidateType(tags, ConfigType.String, ConfigType.Array);
                  if (tags is List<object?>)
                     ValidateArrayOf(tags, ConfigType.String);
               });
            }
         });
      }
   }

   protected void ValidateSshConfig(object? config)
   {
      if (config is List<object?>)
         ValidateArrayOf(config, ConfigType.String);
      else if (config is bool or string)
      {
         // Booleans and strings are allowed.
      }
      else
         TypeError(ConfigType.Boolean, ConfigType.Boolean, ConfigType.String, ConfigType.Array);
   }

   protected void ValidatePaths(object? paths)
   {
      ValidateType(paths, ConfigType.Array);

      var list = (List<object?>)paths!;
      for (var index = 0; index < list.Count; index++)
      {
         var path = list[index];
         WithContext(index.ToString(), () =>
         {
            ValidateType(path, ConfigType.String, ConfigType.Hash);

            if (path is IDictionary<string, object?> pathDict)
            {
               foreach (var key in new[] { "local", "remote", "mode", "owner", "options" })
               {
                  WithContext(key, () =>
                  {
                     if (pathDict.ContainsKey(key))
                        ValidateType(pathDict[key], ConfigType.String);
                  });
               }
            }
         });
      }
   }

   protected void ValidateHooksOutput(object? value)
   {
      // hooks_output can be either a string (global) or a hash (per-hook).
      if (value is IDictionary<string, object?> dict)
      {
         foreach (var (hook, level) in dict)
            WithContext(hook, () => ValidateType(level, ConfigType.String));
      }
      else
      {
         ValidateType(value, ConfigType.String);
      }
   }

   protected void ValidateType(object? value, params ConfigType[] types)
   {
      if (!types.Any(type => ValidTypeOf(value, type)))
         TypeError(types);
   }

   protected void Error(string message)
   {
      throw new KamalConfigurationError($"{ErrorContext}{message}");
   }

   protected void TypeError(params ConfigType[] expectedTypes)
   {
      var descriptions = expectedTypes.Select(TypeDescription).Distinct();
      Error($"should be {string.Join(" or ", descriptions)}");
   }

   protected void UnknownKeysError(IReadOnlyCollection<string> unknownKeys)
   {
      Error($"unknown {(unknownKeys.Count == 1 ? "key" : "keys")}: {string.Join(", ", unknownKeys)}");
   }

   private string ErrorContext => RubyHelpers.IsPresent(Context) ? $"{Context}: " : "";

   protected void WithContext(string context, Action action)
   {
      var oldContext = Context;
      try
      {
         Context = string.Join("/", new[] { Context, context }.Where(part => RubyHelpers.IsPresent(part)));
         action();
      }
      finally
      {
         Context = oldContext;
      }
   }

   protected virtual bool AllowExtensions => false;

   protected static bool IsExtension(string key) => key.StartsWith("x-", StringComparison.Ordinal);

   protected void CheckUnknownKeys(IDictionary<string, object?> config, IDictionary<string, object?> example)
   {
      var unknownKeys = config.Keys.Where(key => !example.ContainsKey(key)).ToList();

      if (AllowExtensions)
         unknownKeys = unknownKeys.Where(key => !IsExtension(key)).ToList();

      if (unknownKeys.Count > 0)
         UnknownKeysError(unknownKeys);
   }

   protected void ValidateLabels(object? labels)
   {
      if (RubyHelpers.IsBlank(labels))
         return;

      WithContext("labels", () =>
      {
         if (labels is IDictionary<string, object?> labelsDict)
         {
            foreach (var (key, _) in labelsDict)
            {
               WithContext(key, () =>
               {
                  if (key is "destination" or "role" or "service")
                     Error("invalid label. destination, role, and service are reserved labels");
               });
            }
         }
      });
   }

   protected void ValidateDockerOptions(object? options)
   {
      if (options is IDictionary<string, object?> optionsDict && optionsDict.Get("restart") is not (null or false))
         Error("Cannot set restart policy in docker options, unless-stopped is required");
   }
}
