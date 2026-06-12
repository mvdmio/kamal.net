using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Kamal.Configuration;

/// <summary>
/// Loads YAML into the generic object graph Ruby's <c>YAML.unsafe_load</c> would produce:
/// mappings become <see cref="OrderedDictionary{TKey,TValue}"/> (string keys, insertion order),
/// sequences become <see cref="List{T}"/> and plain scalars are resolved to
/// null / bool / int / long / double / string. Anchors, aliases and merge keys
/// (<c>&lt;&lt;</c>) are supported. Ruby symbols (<c>:foo</c>) are loaded as plain strings.
/// </summary>
public static partial class YamlLoader
{
   [GeneratedRegex(@"^[-+]?(0x[0-9a-fA-F_]+|0o[0-7_]+|0b[01_]+|[0-9][0-9_]*)$")]
   private static partial Regex IntegerRegex();

   [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9][0-9_]*(\.[0-9_]*)?)([eE][-+]?[0-9]+)?$")]
   private static partial Regex FloatRegex();

   public static object? LoadFile(string path)
   {
      return Load(File.ReadAllText(path));
   }

   public static object? Load(string yaml)
   {
      var parser = new Parser(new StringReader(yaml));
      parser.Consume<StreamStart>();

      if (parser.TryConsume<StreamEnd>(out _))
         return null;

      parser.Consume<DocumentStart>();

      object? result = null;
      if (!parser.Accept<DocumentEnd>(out _))
         result = ParseNode(parser, new Dictionary<string, object?>());

      parser.Consume<DocumentEnd>();
      return result;
   }

   private static object? ParseNode(IParser parser, Dictionary<string, object?> anchors)
   {
      if (parser.TryConsume<AnchorAlias>(out var alias))
      {
         if (anchors.TryGetValue(alias.Value.Value, out var anchored))
            return anchored;

         throw new YamlException($"Unknown YAML alias: {alias.Value.Value}");
      }

      if (parser.TryConsume<Scalar>(out var scalar))
      {
         var value = ParseScalar(scalar);
         if (!scalar.Anchor.IsEmpty)
            anchors[scalar.Anchor.Value] = value;

         return value;
      }

      if (parser.TryConsume<SequenceStart>(out var sequenceStart))
      {
         var list = new List<object?>();
         if (!sequenceStart.Anchor.IsEmpty)
            anchors[sequenceStart.Anchor.Value] = list;

         while (!parser.TryConsume<SequenceEnd>(out _))
            list.Add(ParseNode(parser, anchors));

         return list;
      }

      if (parser.TryConsume<MappingStart>(out var mappingStart))
      {
         var dict = new OrderedDictionary<string, object?>();
         if (!mappingStart.Anchor.IsEmpty)
            anchors[mappingStart.Anchor.Value] = dict;

         var mergeSources = new List<IDictionary<string, object?>>();

         while (!parser.TryConsume<MappingEnd>(out _))
         {
            var key = Utils.RubyHelpers.RubyToS(ParseNode(parser, anchors));
            var value = ParseNode(parser, anchors);

            if (key == "<<")
            {
               // YAML merge key: a mapping or a sequence of mappings.
               if (value is IDictionary<string, object?> single)
               {
                  mergeSources.Add(single);
               }
               else if (value is List<object?> multiple)
               {
                  foreach (var entry in multiple)
                  {
                     if (entry is IDictionary<string, object?> entryDict)
                        mergeSources.Add(entryDict);
                  }
               }
            }
            else
            {
               // Last occurrence wins for duplicate keys (Psych behavior); position is kept.
               dict[key] = value;
            }
         }

         // Per the YAML merge spec: the mapping's own keys win, earlier merge sources win over later.
         var merged = new OrderedDictionary<string, object?>();
         foreach (var source in mergeSources)
         {
            foreach (var (key, value) in source)
            {
               if (!merged.ContainsKey(key) && !dict.ContainsKey(key))
                  merged[key] = value;
            }
         }

         if (merged.Count > 0)
         {
            foreach (var (key, value) in dict)
               merged[key] = value;

            if (!mappingStart.Anchor.IsEmpty)
               anchors[mappingStart.Anchor.Value] = merged;

            return merged;
         }

         return dict;
      }

      throw new YamlException($"Unexpected YAML event: {parser.Current?.GetType().Name}");
   }

   private static object? ParseScalar(Scalar scalar)
   {
      // Quoted or block scalars (and explicitly tagged strings) stay strings.
      if (scalar.Style != ScalarStyle.Plain || !scalar.Tag.IsEmpty)
         return scalar.Value;

      var value = scalar.Value;

      switch (value)
      {
         case "" or "~" or "null" or "Null" or "NULL":
            return null;
         case "true" or "True" or "TRUE":
            return true;
         case "false" or "False" or "FALSE":
            return false;
      }

      // Ruby's Psych loads ":foo" as a Symbol; the port treats symbols as strings.
      if (value.Length > 1 && value[0] == ':' && value.Skip(1).All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
         return value[1..];

      if (IntegerRegex().IsMatch(value))
      {
         var cleaned = value.Replace("_", "");
         var negative = cleaned.StartsWith('-');
         var unsigned = cleaned.TrimStart('+', '-');

         long parsed;
         if (unsigned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            parsed = long.Parse(unsigned[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
         else if (unsigned.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            parsed = Convert.ToInt64(unsigned[2..], 8);
         else if (unsigned.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            parsed = Convert.ToInt64(unsigned[2..], 2);
         else
            parsed = long.Parse(unsigned, CultureInfo.InvariantCulture);

         if (negative)
            parsed = -parsed;

         return parsed is >= int.MinValue and <= int.MaxValue ? (int)parsed : parsed;
      }

      if (FloatRegex().IsMatch(value) && value.Contains('.'))
         return double.Parse(value.Replace("_", ""), CultureInfo.InvariantCulture);

      return value;
   }
}
