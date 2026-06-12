using Kamal.Configuration;
using static Kamal.Tests.Commands.Cmd;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/prune_test.rb.</summary>
[Collection("kamal-config")]
public class PruneTests
{
   private readonly Cfg _config = new()
   {
      ["service"] = "app",
      ["image"] = "dhh/app",
      ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
      ["servers"] = L("1.1.1.1"),
      ["builder"] = new Cfg { ["arch"] = "amd64" }
   };

   [Fact]
   public void DanglingImages()
   {
      Assert.Equal(
         "docker image prune --force --filter label=service=app",
         Join(NewCommand().DanglingImages()));
   }

   [Fact]
   public void TaggedImages()
   {
      Assert.Equal(
         "docker image ls --filter label=service=app --format '{{.ID}} {{.Repository}}:{{.Tag}}' | grep -v -w \"$(docker container ls -a --format '{{.Image}}\\|' --filter label=service=app | tr -d '\\n')dhh/app:latest\\|dhh/app:<none>\" | while read image tag; do docker rmi $tag; done",
         Join(NewCommand().TaggedImages()));
   }

   [Fact]
   public void AppContainers()
   {
      Assert.Equal(
         "docker ps -q -a --filter label=service=app --filter status=created --filter status=exited --filter status=dead | tail -n +6 | while read container_id; do docker rm $container_id; done",
         Join(NewCommand().AppContainers(retain: 5)));

      Assert.Equal(
         "docker ps -q -a --filter label=service=app --filter status=created --filter status=exited --filter status=dead | tail -n +4 | while read container_id; do docker rm $container_id; done",
         Join(NewCommand().AppContainers(retain: 3)));
   }

   private Kamal.Commands.Prune NewCommand()
   {
      return new Kamal.Commands.Prune(new KamalConfiguration(_config, version: "123"));
   }
}
