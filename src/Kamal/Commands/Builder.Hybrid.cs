using Kamal.Configuration;

namespace Kamal.Commands;

public sealed partial class Builder
{
   /// <summary>Port of <c>Kamal::Commands::Builder::Hybrid</c>.</summary>
   public class Hybrid : Remote
   {
      public Hybrid(KamalConfiguration config) : base(config)
      {
      }

      public override object[]? Create()
      {
         return Combine(
            CreateLocalBuildx(),
            CreateRemoteContext(),
            AppendRemoteBuildx());
      }

      protected override string BuilderName => $"kamal-hybrid-{Driver}-{RemoteBuilderNameSuffix}";

      private object[] CreateLocalBuildx()
      {
         return Docker("buildx", "create", PlatformOptions(BuilderConfig.LocalArches), "--name", BuilderName, $"--driver={Driver}", DriverOptions);
      }

      private object[] AppendRemoteBuildx()
      {
         return Docker("buildx", "create", PlatformOptions(BuilderConfig.RemoteArches), "--append", "--name", BuilderName, DriverOptions, RemoteContextName);
      }
   }
}
