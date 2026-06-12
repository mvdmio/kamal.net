using Kamal.Configuration;

namespace Kamal.Commands;

public sealed partial class Builder
{
   /// <summary>Port of <c>Kamal::Commands::Builder::Local</c>.</summary>
   public class Local : Base
   {
      public Local(KamalConfiguration config) : base(config)
      {
      }

      public override object[]? Create()
      {
         if (BuilderConfig.DockerDriver)
            return null;

         return Docker("buildx", "create", "--name", BuilderName, $"--driver={Driver}", DriverOptions);
      }

      public override object[]? Remove()
      {
         return BuilderConfig.DockerDriver ? null : Docker("buildx", "rm", BuilderName);
      }

      protected override string BuilderName =>
         RegistryConfig.Local ? $"kamal-local-registry-{Driver}" : $"kamal-local-{Driver}";
   }
}
