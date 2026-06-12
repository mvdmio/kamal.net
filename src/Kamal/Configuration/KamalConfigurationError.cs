namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::ConfigurationError</c>.</summary>
public class KamalConfigurationError : Exception
{
   public KamalConfigurationError(string message)
      : base(message)
   {
   }
}
