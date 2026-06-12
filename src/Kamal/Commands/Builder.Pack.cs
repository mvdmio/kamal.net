using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal.Commands;

public sealed partial class Builder
{
   /// <summary>Port of <c>Kamal::Commands::Builder::Pack</c>.</summary>
   public class Pack : Base
   {
      public Pack(KamalConfiguration config) : base(config)
      {
      }

      public override object[] Push(string exportAction = "registry", bool tagAsDirty = false, bool noCache = false)
      {
         return Combine(
            Build(tagAsDirty: tagAsDirty, noCache: noCache),
            Export(exportAction));
      }

      // Ruby's Pack builder has no create step (def remove;end and no def create).
      public override object[]? Create() => null;

      public override object[]? Remove() => null;

      public override object[] Info() => PackCmd("builder", "inspect", BuilderConfig.PackBuilder);

      public override object[]? InspectBuilder() => Info();

      // Push and Info are overridden, so the buildx builder name is never used for pack builds.
      protected override string BuilderName => throw new NotSupportedException("Pack builds do not use a buildx builder");

      private object[] Build(bool tagAsDirty = false, bool noCache = false)
      {
         return PackCmd("build",
            Config.Repository,
            "--platform", Platform,
            "--creation-time", "now",
            "--builder", BuilderConfig.PackBuilder,
            Buildpacks,
            BuildTagOptions(tagAsDirty: tagAsDirty),
            noCache ? new object[] { "--clear-cache" } : null,
            "--env", $"BP_IMAGE_LABELS=service={Config.Service}",
            KamalUtils.Argumentize("--env", BuilderConfig.Args),
            KamalUtils.Argumentize("--env", BuilderConfig.Secrets, sensitive: true),
            "--path", BuildContext);
      }

      private object[]? Export(string exportAction)
      {
         if (exportAction != "registry")
            return null;

         return Combine(
            Docker("push", Config.AbsoluteImage),
            Docker("push", Config.LatestImage));
      }

      private string Platform => $"linux/{BuilderConfig.LocalArches.First()}";

      private List<object[]> Buildpacks
      {
         get
         {
            var buildpacks = (BuilderConfig.PackBuildpacks ?? []).Select(RubyHelpers.RubyToS).ToList();
            buildpacks.Add("paketo-buildpacks/image-labels");

            return buildpacks.Select(buildpack => new object[] { "--buildpack", buildpack }).ToList();
         }
      }
   }
}
