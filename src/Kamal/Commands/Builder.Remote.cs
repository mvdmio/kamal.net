using System.Text.RegularExpressions;
using Kamal.Configuration;

namespace Kamal.Commands;

public sealed partial class Builder
{
   /// <summary>Port of <c>Kamal::Commands::Builder::Remote</c>.</summary>
   public class Remote : Base
   {
      public Remote(KamalConfiguration config) : base(config)
      {
      }

      public override object[]? Create()
      {
         return Chain(
            CreateRemoteContext(),
            CreateBuildx());
      }

      public override object[]? Remove()
      {
         return Chain(
            RemoveRemoteContext(),
            RemoveBuildx());
      }

      public override object[] Info()
      {
         return Chain(
            Docker("context", "ls"),
            Docker("buildx", "ls"));
      }

      public override object[]? InspectBuilder()
      {
         return CombineBy("||",
            Combine(InspectBuildx(), InspectRemoteContext()),
            new object[] { "(echo no compatible builder && exit 1)" });
      }

      public override bool LoginToRegistryLocally => false;

      public override IDictionary<string, string> PushEnv =>
         new OrderedDictionary<string, string> { ["BUILDKIT_NO_CLIENT_TOKEN"] = "1" };

      protected override string BuilderName => $"kamal-remote-{RemoteBuilderNameSuffix}";

      protected string RemoteContextName => $"{BuilderName}-context";

      protected string RemoteBuilderNameSuffix =>
         $"{Regex.Replace(Remote_!, "[^a-z0-9_-]", "-")}{(RegistryConfig.Local ? "-local-registry" : "")}";

      protected object[] CreateRemoteContext()
      {
         return Docker("context", "create", RemoteContextName, "--description", $"'{BuilderName} host'", "--docker", $"'host={Remote_}'");
      }

      private object[] InspectBuildx()
      {
         return Pipe(
            Docker("buildx", "inspect", BuilderName),
            Grep("-q", $"Endpoint:.*{RemoteContextName}"));
      }

      private object[] InspectRemoteContext()
      {
         return Pipe(
            Docker("context", "inspect", RemoteContextName, "--format", EndpointDockerHostInspect),
            Grep("-xq", Remote_));
      }

      private object[] RemoveRemoteContext() => Docker("context", "rm", RemoteContextName);

      private object[] CreateBuildx() => Docker("buildx", "create", "--name", BuilderName, DriverOptions, RemoteContextName);

      private object[] RemoveBuildx() => Docker("buildx", "rm", BuilderName);
   }
}
