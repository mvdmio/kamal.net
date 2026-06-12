using System.Text.RegularExpressions;
using Kamal.Configuration;

namespace Kamal.Commands;

public sealed partial class Builder
{
   /// <summary>
   /// Port of <c>Kamal::Commands::Builder::Cloud</c>.
   /// Expects <c>driver</c> to be of format "cloud docker-org-name/builder-name".
   /// </summary>
   public class Cloud : Base
   {
      public Cloud(KamalConfiguration config) : base(config)
      {
      }

      public override object[]? Create() => Docker("buildx", "create", "--driver", Driver);

      public override object[]? Remove() => Docker("buildx", "rm", BuilderName);

      protected override string BuilderName => Regex.Replace(Driver, "[ /]", "-");

      private object[] InspectBuildx()
      {
         return Pipe(
            Docker("buildx", "inspect", BuilderName),
            Grep("-q", "Endpoint:.*cloud://.*"));
      }
   }
}
