using Kamal.Configuration;

namespace Kamal.Commands;

/// <summary>
/// Port of <c>Kamal::Commands::Builder</c>: picks the concrete builder (local/remote/hybrid/
/// pack/cloud) from the configuration and delegates the command building to it.
/// The Clone mixin lives in Builder.Clone.cs; the subtypes are nested classes in Builder.*.cs.
/// </summary>
public sealed partial class Builder : CommandsBase
{
   private Base? _target;
   private Remote? _remote;
   private Local? _local;
   private Hybrid? _hybrid;
   private Pack? _pack;
   private Cloud? _cloud;

   public Builder(KamalConfiguration config) : base(config)
   {
   }

   /// <summary>Port of <c>name</c>: the underscored builder type ("local", "remote", "hybrid", "pack", "cloud").</summary>
   public string Name
   {
      get
      {
         return Target switch
         {
            Hybrid => "hybrid",
            Remote => "remote",
            Pack => "pack",
            Cloud => "cloud",
            _ => "local"
         };
      }
   }

   public Base Target
   {
      get
      {
         return _target ??= Config.Builder.IsRemote
            ? Config.Builder.IsLocal ? HybridBuilder : RemoteBuilder
            : Config.Builder.Pack
               ? PackBuilder
               : Config.Builder.IsCloud
                  ? CloudBuilder
                  : LocalBuilder;
      }
   }

   public Remote RemoteBuilder => _remote ??= new Remote(Config);

   public Local LocalBuilder => _local ??= new Local(Config);

   public Hybrid HybridBuilder => _hybrid ??= new Hybrid(Config);

   public Pack PackBuilder => _pack ??= new Pack(Config);

   public Cloud CloudBuilder => _cloud ??= new Cloud(Config);

   // Delegations to the target builder.
   public object[]? Create() => Target.Create();
   public object[]? Remove() => Target.Remove();
   public object[] Push(string exportAction = "registry", bool tagAsDirty = false, bool noCache = false) => Target.Push(exportAction, tagAsDirty: tagAsDirty, noCache: noCache);
   public object[] Clean() => Target.Clean();
   public object[] Pull() => Target.Pull();
   public object[] Info() => Target.Info();
   public object[]? InspectBuilder() => Target.InspectBuilder();
   public object[] ValidateImage() => Target.ValidateImage();
   public object[] FirstMirror() => Target.FirstMirror();
   public bool LoginToRegistryLocally => Target.LoginToRegistryLocally;
   public IDictionary<string, string> PushEnv => Target.PushEnv;
}
