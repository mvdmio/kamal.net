using System.Text.RegularExpressions;
using Kamal.Configuration;
using Kamal.Tests.Configuration;
using static Kamal.Tests.Commands.Cmd;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/builder_test.rb (local_arch pinned to amd64 via DockerArchScope).</summary>
[Collection("kamal-config")]
public class BuilderTests : IDisposable
{
   private const string LocalArch = "amd64";
   private const string RemoteArch = "arm64";

   private readonly Cfg _config;
   private readonly DockerArchScope _archScope;
   private readonly DockerfileScope _dockerfileScope;
   private TestSecrets? _secrets;

   public BuilderTests()
   {
      _archScope = new DockerArchScope(LocalArch);
      _dockerfileScope = new DockerfileScope(exists: true);

      _config = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
         ["servers"] = L("1.1.1.1"),
         ["builder"] = new Cfg { ["arch"] = "amd64" }
      };
   }

   public void Dispose()
   {
      _dockerfileScope.Dispose();
      _archScope.Dispose();
      _secrets?.Dispose();
   }

   [Fact]
   public void TargetLinuxAmd64LocallyByDefault()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["cache"] = new Cfg { ["type"] = "gha" } } });
      Assert.Equal("local", builder.Name);
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --cache-to type=gha --cache-from type=gha --label service=\"app\" --file Dockerfile . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void TargetSpecifiedArchLocallyByDefault()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["arch"] = L("amd64") } });
      Assert.Equal("local", builder.Name);
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --file Dockerfile . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void BuildWithCaching()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["cache"] = new Cfg { ["type"] = "gha" } } });
      Assert.Equal("local", builder.Name);
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --cache-to type=gha --cache-from type=gha --label service=\"app\" --file Dockerfile . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void HybridBuildIfRemoteIsSetAndBuildingMultiarch()
   {
      var builder = NewBuilderCommand(new Cfg
      {
         ["builder"] = new Cfg
         {
            ["arch"] = L("amd64", "arm64"),
            ["remote"] = "ssh://app@127.0.0.1",
            ["cache"] = new Cfg { ["type"] = "gha" }
         }
      });
      Assert.Equal("hybrid", builder.Name);
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64,linux/arm64 --builder kamal-hybrid-docker-container-ssh---app-127-0-0-1 -t dhh/app:123 -t dhh/app:latest --cache-to type=gha --cache-from type=gha --label service=\"app\" --file Dockerfile . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void RemoteBuildIfRemoteIsSetAndLocalDisabled()
   {
      var builder = NewBuilderCommand(new Cfg
      {
         ["builder"] = new Cfg
         {
            ["arch"] = L("amd64", "arm64"),
            ["remote"] = "ssh://app@127.0.0.1",
            ["cache"] = new Cfg { ["type"] = "gha" },
            ["local"] = false
         }
      });
      Assert.Equal("remote", builder.Name);
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64,linux/arm64 --builder kamal-remote-ssh---app-127-0-0-1 -t dhh/app:123 -t dhh/app:latest --cache-to type=gha --cache-from type=gha --label service=\"app\" --file Dockerfile . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void TargetRemoteWhenRemoteSetAndArchIsNonLocal()
   {
      var builder = NewBuilderCommand(new Cfg
      {
         ["builder"] = new Cfg
         {
            ["arch"] = L(RemoteArch),
            ["remote"] = "ssh://app@host",
            ["cache"] = new Cfg { ["type"] = "gha" }
         }
      });
      Assert.Equal("remote", builder.Name);
      Assert.Equal(
         $"docker buildx build --output=type=registry --platform linux/{RemoteArch} --builder kamal-remote-ssh---app-host -t dhh/app:123 -t dhh/app:latest --cache-to type=gha --cache-from type=gha --label service=\"app\" --file Dockerfile . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void TargetLocalWhenRemoteSetAndArchIsLocal()
   {
      var builder = NewBuilderCommand(new Cfg
      {
         ["builder"] = new Cfg
         {
            ["arch"] = L(LocalArch),
            ["remote"] = "ssh://app@host",
            ["cache"] = new Cfg { ["type"] = "gha" }
         }
      });
      Assert.Equal("local", builder.Name);
      Assert.Equal(
         $"docker buildx build --output=type=registry --platform linux/{LocalArch} --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --cache-to type=gha --cache-from type=gha --label service=\"app\" --file Dockerfile . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void TargetPackWhenPackIsSet()
   {
      var builder = NewBuilderCommand(new Cfg
      {
         ["image"] = "dhh/app",
         ["builder"] = new Cfg
         {
            ["arch"] = "amd64",
            ["pack"] = new Cfg { ["builder"] = "heroku/builder:24", ["buildpacks"] = L("heroku/ruby", "heroku/procfile") }
         }
      });
      Assert.Equal("pack", builder.Name);
      Assert.Equal(
         "pack build dhh/app --platform linux/amd64 --creation-time now --builder heroku/builder:24 --buildpack heroku/ruby --buildpack heroku/procfile --buildpack paketo-buildpacks/image-labels -t dhh/app:123 -t dhh/app:latest --env BP_IMAGE_LABELS=service=app --path . && docker push dhh/app:123 && docker push dhh/app:latest",
         Join(builder.Push()));
   }

   [Fact]
   public void PackBuildArgsPassedAsEnv()
   {
      var builder = NewBuilderCommand(new Cfg
      {
         ["image"] = "dhh/app",
         ["builder"] = new Cfg
         {
            ["args"] = new Cfg { ["a"] = 1, ["b"] = 2 },
            ["arch"] = "amd64",
            ["pack"] = new Cfg { ["builder"] = "heroku/builder:24", ["buildpacks"] = L("heroku/ruby", "heroku/procfile") }
         }
      });

      Assert.Equal(
         "pack build dhh/app --platform linux/amd64 --creation-time now --builder heroku/builder:24 --buildpack heroku/ruby --buildpack heroku/procfile --buildpack paketo-buildpacks/image-labels -t dhh/app:123 -t dhh/app:latest --env BP_IMAGE_LABELS=service=app --env a=\"1\" --env b=\"2\" --path . && docker push dhh/app:123 && docker push dhh/app:latest",
         Join(builder.Push()));
   }

   [Fact]
   public void PackBuildWithNoCache()
   {
      var builder = NewBuilderCommand(new Cfg
      {
         ["image"] = "dhh/app",
         ["builder"] = new Cfg
         {
            ["args"] = new Cfg { ["a"] = 1, ["b"] = 2 },
            ["arch"] = "amd64",
            ["pack"] = new Cfg { ["builder"] = "heroku/builder:24", ["buildpacks"] = L("heroku/ruby", "heroku/procfile") }
         }
      });

      Assert.Equal(
         "pack build dhh/app --platform linux/amd64 --creation-time now --builder heroku/builder:24 --buildpack heroku/ruby --buildpack heroku/procfile --buildpack paketo-buildpacks/image-labels -t dhh/app:123 -t dhh/app:latest --clear-cache --env BP_IMAGE_LABELS=service=app --env a=\"1\" --env b=\"2\" --path . && docker push dhh/app:123 && docker push dhh/app:latest",
         Join(builder.Push("registry", noCache: true)));
   }

   [Fact]
   public void PackBuildSecretsAsEnv()
   {
      _secrets = new TestSecrets("token_a=foo\ntoken_b=bar");
      var builder = NewBuilderCommand(new Cfg
      {
         ["image"] = "dhh/app",
         ["builder"] = new Cfg
         {
            ["secrets"] = L("token_a", "token_b"),
            ["arch"] = "amd64",
            ["pack"] = new Cfg { ["builder"] = "heroku/builder:24", ["buildpacks"] = L("heroku/ruby", "heroku/procfile") }
         }
      });

      Assert.Equal(
         "pack build dhh/app --platform linux/amd64 --creation-time now --builder heroku/builder:24 --buildpack heroku/ruby --buildpack heroku/procfile --buildpack paketo-buildpacks/image-labels -t dhh/app:123 -t dhh/app:latest --env BP_IMAGE_LABELS=service=app --env token_a=\"foo\" --env token_b=\"bar\" --path . && docker push dhh/app:123 && docker push dhh/app:latest",
         Join(builder.Push()));
   }

   [Fact]
   public void CloudBuilder()
   {
      var builder = NewBuilderCommand(new Cfg
      {
         ["builder"] = new Cfg { ["arch"] = L(LocalArch), ["driver"] = "cloud docker-org-name/builder-name" }
      });
      Assert.Equal("cloud", builder.Name);
      Assert.Equal(
         $"docker buildx build --output=type=registry --platform linux/{LocalArch} --builder cloud-docker-org-name-builder-name -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --file Dockerfile . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void BuildArgs()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["args"] = new Cfg { ["a"] = 1, ["b"] = 2 } } });
      Assert.Equal(
         "--label service=\"app\" --build-arg a=\"1\" --build-arg b=\"2\" --file Dockerfile",
         Join(builder.Target.BuildOptions()));
   }

   [Fact]
   public void BuildSecrets()
   {
      _secrets = new TestSecrets("token_a=foo\ntoken_b=bar");
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["secrets"] = L("token_a", "token_b") } });
      Assert.Equal(
         "--label service=\"app\" --secret id=\"token_a\" --secret id=\"token_b\" --file Dockerfile",
         Join(builder.Target.BuildOptions()));
   }

   [Fact]
   public void BuildDockerfile()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["dockerfile"] = "Dockerfile.xyz" } });
      Assert.Equal(
         "--label service=\"app\" --file Dockerfile.xyz",
         Join(builder.Target.BuildOptions()));
   }

   [Fact]
   public void MissingDockerfile()
   {
      using var missing = new DockerfileScope(exists: false);
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["dockerfile"] = "Dockerfile.xyz" } });

      Assert.Throws<Kamal.Commands.Builder.Base.BuilderError>(() => builder.Target.BuildOptions());
   }

   [Fact]
   public void BuildTarget()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["target"] = "prod" } });
      Assert.Equal(
         "--label service=\"app\" --file Dockerfile --target prod",
         Join(builder.Target.BuildOptions()));
   }

   [Fact]
   public void BuildContext()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["context"] = ".." } });
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --file Dockerfile .. 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void PushWithBuildArgs()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["args"] = new Cfg { ["a"] = 1, ["b"] = 2 } } });
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --build-arg a=\"1\" --build-arg b=\"2\" --file Dockerfile . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void PushWithBuildSecrets()
   {
      _secrets = new TestSecrets("a=foo\nb=bar");
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["secrets"] = L("a", "b") } });
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --secret id=\"a\" --secret id=\"b\" --file Dockerfile . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void BuildWithSshAgentSocket()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["ssh"] = "default=$SSH_AUTH_SOCK" } });

      Assert.Equal(
         "--label service=\"app\" --file Dockerfile --ssh default=$SSH_AUTH_SOCK",
         Join(builder.Target.BuildOptions()));
   }

   [Fact]
   public void ValidateImage()
   {
      Assert.Equal(
         "docker inspect -f '{{ .Config.Labels.service }}' dhh/app:123 | grep -x app || (echo \"Image dhh/app:123 is missing the 'service' label\" && exit 1)",
         Join(NewBuilderCommand().ValidateImage()));
   }

   [Fact]
   public void ContextBuild()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["context"] = "./foo" } });
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --file Dockerfile ./foo 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void PushWithProvenance()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["provenance"] = "mode=max" } });
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --file Dockerfile --provenance mode=max . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void PushWithProvenanceFalse()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["provenance"] = false } });
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --file Dockerfile --provenance false . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void PushWithSbom()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["sbom"] = true } });
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --file Dockerfile --sbom true . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void PushWithSbomFalse()
   {
      var builder = NewBuilderCommand(new Cfg { ["builder"] = new Cfg { ["sbom"] = false } });
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --file Dockerfile --sbom false . 2>&1",
         Join(builder.Push()));
   }

   [Fact]
   public void MirrorCount()
   {
      Assert.Equal(
         "docker info --format '{{index .RegistryConfig.Mirrors 0}}'",
         Join(NewBuilderCommand().FirstMirror()));
   }

   [Fact]
   public void PushWithNoCache()
   {
      var builder = NewBuilderCommand();
      Assert.Equal(
         "docker buildx build --output=type=registry --platform linux/amd64 --builder kamal-local-docker-container -t dhh/app:123 -t dhh/app:latest --label service=\"app\" --file Dockerfile --no-cache . 2>&1",
         Join(builder.Push("registry", noCache: true)));
   }

   [Fact]
   public void ClonePathWithSpaces()
   {
      using var git = new GitScope(new FakeGitRunner
      {
         Outputs = { ["rev-parse --show-toplevel"] = "/absolute/path with spaces" }
      });

      var command = NewBuilderCommand();
      var cloneCommand = Join(command.Clone());
      var cloneResetCommands = command.CloneResetSteps().Select(Join).ToList();

      Assert.Matches(new Regex(@"path\\ with\\ space"), cloneCommand);
      Assert.DoesNotMatch(new Regex("path with spaces"), cloneCommand);

      foreach (var resetCommand in cloneResetCommands)
      {
         Assert.Matches(new Regex(@"path\\ with\\ space"), resetCommand);
         Assert.DoesNotMatch(new Regex("path with spaces"), resetCommand);
      }
   }

   [Fact]
   public void LocalBuilderWithLocalRegistryIncludesNetworkHostDriverOption()
   {
      var builder = NewBuilderCommand(new Cfg { ["registry"] = new Cfg { ["server"] = "localhost:5000" } });
      Assert.Equal("local", builder.Name);
      Assert.Equal(
         "docker buildx create --name kamal-local-registry-docker-container --driver=docker-container --driver-opt network=host",
         Join(builder.Create()));
   }

   [Fact]
   public void RemoteBuilderWithLocalRegistry()
   {
      var builder = NewBuilderCommand(new Cfg
      {
         ["registry"] = new Cfg { ["server"] = "localhost:5000" },
         ["builder"] = new Cfg { ["arch"] = RemoteArch, ["remote"] = "ssh://app@1.1.1.5" }
      });
      Assert.Equal("remote", builder.Name);
      Assert.Equal(
         "docker context create kamal-remote-ssh---app-1-1-1-5-local-registry-context --description 'kamal-remote-ssh---app-1-1-1-5-local-registry host' --docker 'host=ssh://app@1.1.1.5' ; docker buildx create --name kamal-remote-ssh---app-1-1-1-5-local-registry --driver-opt network=host kamal-remote-ssh---app-1-1-1-5-local-registry-context",
         Join(builder.Create()));
   }

   [Fact]
   public void HybridBuilderWithLocalRegistry()
   {
      var builder = NewBuilderCommand(new Cfg
      {
         ["registry"] = new Cfg { ["server"] = "localhost:5000" },
         ["builder"] = new Cfg { ["arch"] = L("amd64", "arm64"), ["remote"] = "ssh://app@1.1.1.5" }
      });
      Assert.Equal("hybrid", builder.Name);
      Assert.Equal(
         "docker buildx create --platform linux/amd64 --name kamal-hybrid-docker-container-ssh---app-1-1-1-5-local-registry --driver=docker-container --driver-opt network=host && docker context create kamal-hybrid-docker-container-ssh---app-1-1-1-5-local-registry-context --description 'kamal-hybrid-docker-container-ssh---app-1-1-1-5-local-registry host' --docker 'host=ssh://app@1.1.1.5' && docker buildx create --platform linux/arm64 --append --name kamal-hybrid-docker-container-ssh---app-1-1-1-5-local-registry --driver-opt network=host kamal-hybrid-docker-container-ssh---app-1-1-1-5-local-registry-context",
         Join(builder.Create()));
   }

   private Kamal.Commands.Builder NewBuilderCommand(Cfg? additionalConfig = null)
   {
      var raw = additionalConfig is null ? _config : DeepMerge(_config, additionalConfig);
      var config = new KamalConfiguration(raw, version: "123", secrets: _secrets?.Secrets);

      return new Kamal.Commands.Builder(config);
   }
}
