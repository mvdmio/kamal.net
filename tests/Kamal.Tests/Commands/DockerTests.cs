using Kamal.Configuration;
using Kamal.Tests.Configuration;
using static Kamal.Tests.Commands.Cmd;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/docker_test.rb.</summary>
[Collection("kamal-config")]
public class DockerTests
{
   private readonly Kamal.Commands.Docker _docker;

   public DockerTests()
   {
      var config = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
         ["servers"] = L("1.1.1.1"),
         ["builder"] = new Cfg { ["arch"] = "amd64" }
      };

      _docker = new Kamal.Commands.Docker(new KamalConfiguration(config));
   }

   [Fact]
   public void Install()
   {
      Assert.Equal("sh -c 'curl -fsSL https://get.docker.com || wget -O - https://get.docker.com || echo \"exit 1\"' | sh", Join(_docker.Install()));
   }

   [Fact]
   public void Installed()
   {
      Assert.Equal("docker -v", Join(_docker.Installed()));
   }

   [Fact]
   public void Running()
   {
      Assert.Equal("docker version", Join(_docker.Running()));
   }

   [Fact]
   public void Superuser()
   {
      Assert.Equal("[ \"${EUID:-$(id -u)}\" -eq 0 ] || sudo -nl usermod >/dev/null", Join(_docker.Superuser()));
   }

   [Fact]
   public void Root()
   {
      Assert.Equal("[ \"${EUID:-$(id -u)}\" -eq 0 ]", Join(_docker.Root()));
   }

   [Fact]
   public void InDockerGroup()
   {
      Assert.Equal("id -nG \"${USER:-$(id -un)}\" | grep -qw docker", Join(_docker.InDockerGroup()));
   }

   [Fact]
   public void AddToDockerGroup()
   {
      Assert.Equal("sudo -n usermod -aG docker \"${USER:-$(id -un)}\"", Join(_docker.AddToDockerGroup()));
   }

   [Fact]
   public void RefreshSession()
   {
      Assert.Equal("kill -HUP $PPID", Join(_docker.RefreshSession()));
   }
}
