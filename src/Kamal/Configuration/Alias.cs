using Kamal.Configuration.Validation;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Alias</c>.</summary>
public sealed class Alias
{
   public Alias(string name, KamalConfiguration config)
   {
      Name = name;
      Command = RubyHelpers.AsDict(config.RawConfig.Get("aliases")).Get(name);

      new AliasValidator(
         Command,
         RubyHelpers.AsDict(ValidationDocs.Doc("alias").Get("aliases")).Get("uname"),
         $"aliases/{name}").Validate();
   }

   public string Name { get; }

   /// <summary>The aliased Kamal command line.</summary>
   public object? Command { get; }
}
