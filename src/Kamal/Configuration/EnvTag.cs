using Kamal.Secrets;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Env::Tag</c>: a named set of extra env variables for tagged hosts.</summary>
public sealed class EnvTag
{
   public EnvTag(string name, object? config, KamalSecrets secrets)
   {
      Name = name;
      Config = config;
      Secrets = secrets;
   }

   public string Name { get; }
   public object? Config { get; }
   public KamalSecrets Secrets { get; }

   public Env Env => new(Config, Secrets);
}
