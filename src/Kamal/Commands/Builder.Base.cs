using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal.Commands;

public sealed partial class Builder
{
   /// <summary>Port of <c>Kamal::Commands::Builder::Base</c>.</summary>
   public abstract class Base : CommandsBase
   {
      public const string EndpointDockerHostInspect = "'{{.Endpoints.docker.Host}}'";

      /// <summary>Test hook for the Dockerfile existence check (Ruby tests stub <c>Pathname#exist?</c>).</summary>
      public static Func<string, bool>? DockerfileExists { get; set; }

      protected Base(KamalConfiguration config) : base(config)
      {
      }

      /// <summary>Raised when the configured Dockerfile is missing (Ruby's <c>BuilderError</c>).</summary>
      public sealed class BuilderError : Exception
      {
         public BuilderError(string message) : base(message)
         {
         }
      }

      public abstract object[]? Create();

      public abstract object[]? Remove();

      public object[] Clean() => Docker("image", "rm", "--force", Config.AbsoluteImage);

      public virtual object[] Push(string exportAction = "registry", bool tagAsDirty = false, bool noCache = false)
      {
         return Docker("buildx", "build",
            $"--output=type={exportAction}",
            PlatformOptions(BuilderConfig.Arches),
            BuilderConfig.DockerDriver ? null : new object[] { "--builder", BuilderName },
            BuildTagOptions(tagAsDirty: tagAsDirty),
            BuildOptions(),
            noCache ? new object[] { "--no-cache" } : null,
            BuildContext,
            "2>&1");
      }

      public object[] Pull() => Docker("pull", Config.AbsoluteImage);

      public virtual object[] Info()
      {
         return Combine(
            Docker("context", "ls"),
            Docker("buildx", "ls"));
      }

      public virtual object[]? InspectBuilder()
      {
         return BuilderConfig.DockerDriver ? null : Docker("buildx", "inspect", BuilderName);
      }

      public object[] BuildOptions()
      {
         return Flatten(BuildCache(), BuildLabels(), BuildArgs(), BuildSecrets(), BuildDockerfile(), BuildTarget(), BuildSsh(), BuilderProvenance(), BuilderSbom());
      }

      public string BuildContext => Config.Builder.Context;

      public object[] ValidateImage()
      {
         return Pipe(
            Docker("inspect", "-f", "'{{ .Config.Labels.service }}'", Config.AbsoluteImage),
            Any(
               new object[] { "grep", "-x", Config.Service! },
               $"(echo \"Image {Config.AbsoluteImage} is missing the 'service' label\" && exit 1)"));
      }

      public object[] FirstMirror() => Docker("info", "--format '{{index .RegistryConfig.Mirrors 0}}'");

      public virtual bool LoginToRegistryLocally => true;

      public virtual IDictionary<string, string> PushEnv => new OrderedDictionary<string, string>();

      protected Configuration.Builder BuilderConfig => Config.Builder;

      protected Configuration.Registry RegistryConfig => Config.Registry;

      protected abstract string BuilderName { get; }

      protected string Driver => BuilderConfig.Driver;

      protected string? Remote_ => BuilderConfig.Remote;

      protected List<string> BuildTagNames(bool tagAsDirty = false)
      {
         var tagNames = new List<string> { Config.AbsoluteImage, Config.LatestImage };

         return tagAsDirty ? tagNames.Select(tag => $"{tag}-dirty").ToList() : tagNames;
      }

      protected object[] BuildTagOptions(bool tagAsDirty = false)
      {
         return BuildTagNames(tagAsDirty: tagAsDirty).SelectMany(name => new object[] { "-t", name }).ToArray();
      }

      protected object[]? DriverOptions => RegistryConfig.Local ? ["--driver-opt", "network=host"] : null;

      protected static List<object> PlatformOptions(List<string> arches)
      {
         if (arches.Count == 0)
            return [];

         return KamalUtils.Argumentize("--platform", string.Join(",", arches.Select(arch => $"linux/{arch}")));
      }

      private object[]? BuildCache()
      {
         if (BuilderConfig.CacheTo is { } cacheTo && BuilderConfig.CacheFrom is { } cacheFrom)
            return ["--cache-to", cacheTo, "--cache-from", cacheFrom];

         return null;
      }

      private List<object> BuildLabels()
      {
         return KamalUtils.Argumentize("--label", new OrderedDictionary<string, object?> { ["service"] = Config.Service });
      }

      private List<object> BuildArgs() => KamalUtils.Argumentize("--build-arg", BuilderConfig.Args, sensitive: true);

      private List<object> BuildSecrets()
      {
         // Ruby: argumentize "--secret", secrets.keys.collect { |secret| [ "id", secret ] }
         var args = new List<object>();

         foreach (var secret in BuilderConfig.Secrets.Keys)
         {
            args.Add("--secret");
            args.Add($"id={KamalUtils.EscapeShellValue(secret)}");
         }

         return args;
      }

      private List<object> BuildDockerfile()
      {
         var dockerfile = BuilderConfig.Dockerfile;
         var exists = DockerfileExists?.Invoke(dockerfile) ?? File.Exists(Path.GetFullPath(dockerfile));

         if (!exists)
            throw new BuilderError($"Missing {dockerfile}");

         return KamalUtils.Argumentize("--file", dockerfile);
      }

      private List<object>? BuildTarget()
      {
         return RubyHelpers.IsPresent(BuilderConfig.Target) ? KamalUtils.Argumentize("--target", BuilderConfig.Target) : null;
      }

      private List<object>? BuildSsh()
      {
         return RubyHelpers.IsPresent(BuilderConfig.Ssh) ? KamalUtils.Argumentize("--ssh", BuilderConfig.Ssh) : null;
      }

      private List<object>? BuilderProvenance()
      {
         return BuilderConfig.Provenance is null ? null : KamalUtils.Argumentize("--provenance", BuilderConfig.Provenance);
      }

      private List<object>? BuilderSbom()
      {
         return BuilderConfig.Sbom is null ? null : KamalUtils.Argumentize("--sbom", BuilderConfig.Sbom);
      }
   }
}
