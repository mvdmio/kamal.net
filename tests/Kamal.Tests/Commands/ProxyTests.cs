using Kamal.Configuration;
using static Kamal.Tests.Commands.Cmd;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/proxy_test.rb.</summary>
[Collection("kamal-config")]
public class ProxyTests
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
   public void Run()
   {
      Assert.Equal(
         $"echo $(cat .kamal/proxy/options 2> /dev/null || echo \"--publish 80:80 --publish 443:443 --log-opt max-size=10m\") $(cat .kamal/proxy/image 2> /dev/null || echo \"basecamp/kamal-proxy\"):$(cat .kamal/proxy/image_version 2> /dev/null || echo \"{ProxyRun.MinimumVersion}\") $(cat .kamal/proxy/run_command 2> /dev/null || echo \"\") | xargs docker run --name kamal-proxy --network kamal --detach --restart unless-stopped --volume kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy --volume $PWD/.kamal/proxy/apps-config:/home/kamal-proxy/.apps-config",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void RunWithoutConfiguration()
   {
      _config.Remove("proxy");

      Assert.Equal(
         $"echo $(cat .kamal/proxy/options 2> /dev/null || echo \"--publish 80:80 --publish 443:443 --log-opt max-size=10m\") $(cat .kamal/proxy/image 2> /dev/null || echo \"basecamp/kamal-proxy\"):$(cat .kamal/proxy/image_version 2> /dev/null || echo \"{ProxyRun.MinimumVersion}\") $(cat .kamal/proxy/run_command 2> /dev/null || echo \"\") | xargs docker run --name kamal-proxy --network kamal --detach --restart unless-stopped --volume kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy --volume $PWD/.kamal/proxy/apps-config:/home/kamal-proxy/.apps-config",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void ProxyStart()
   {
      Assert.Equal("docker container start kamal-proxy", Join(NewCommand().Start()));
   }

   [Fact]
   public void ProxyStop()
   {
      Assert.Equal("docker container stop kamal-proxy", Join(NewCommand().Stop()));
   }

   [Fact]
   public void ProxyInfo()
   {
      Assert.Equal("docker ps --filter 'name=^kamal-proxy$'", Join(NewCommand().Info()));
   }

   [Fact]
   public void ProxyLogs()
   {
      Assert.Equal("docker logs kamal-proxy --timestamps 2>&1", Join(NewCommand().Logs()));
   }

   [Fact]
   public void ProxyLogsSince2h()
   {
      Assert.Equal("docker logs kamal-proxy --since 2h --timestamps 2>&1", Join(NewCommand().Logs(since: "2h")));
   }

   [Fact]
   public void ProxyLogsLast10Lines()
   {
      Assert.Equal("docker logs kamal-proxy --tail 10 --timestamps 2>&1", Join(NewCommand().Logs(lines: 10)));
   }

   [Fact]
   public void ProxyLogsWithoutTimestamps()
   {
      Assert.Equal("docker logs kamal-proxy 2>&1", Join(NewCommand().Logs(timestamps: false)));
   }

   [Fact]
   public void ProxyLogsWithGrepHello()
   {
      Assert.Equal("docker logs kamal-proxy --timestamps 2>&1 | grep 'hello!'", Join(NewCommand().Logs(grep: "hello!")));
   }

   [Fact]
   public void ProxyRemoveContainer()
   {
      Assert.Equal(
         "docker container prune --force --filter label=org.opencontainers.image.title=kamal-proxy",
         Join(NewCommand().RemoveContainer()));
   }

   [Fact]
   public void ProxyRemoveImage()
   {
      Assert.Equal(
         "docker image prune --all --force --filter label=org.opencontainers.image.title=kamal-proxy",
         Join(NewCommand().RemoveImage()));
   }

   [Fact]
   public void ProxyFollowLogs()
   {
      Assert.Equal(
         "ssh -t root@1.1.1.1 -p 22 'docker logs kamal-proxy --timestamps --tail 10 --follow 2>&1'",
         NewCommand().FollowLogs(host: "1.1.1.1"));
   }

   [Fact]
   public void ProxyFollowLogsWithGrepHello()
   {
      Assert.Equal(
         "ssh -t root@1.1.1.1 -p 22 'docker logs kamal-proxy --timestamps --tail 10 --follow 2>&1 | grep \"hello!\"'",
         NewCommand().FollowLogs(host: "1.1.1.1", grep: "hello!"));
   }

   [Fact]
   public void Version()
   {
      Assert.Equal(
         "docker inspect kamal-proxy --format '{{.Config.Image}}' | awk -F: '{print $NF}'",
         Join(NewCommand().Version()));
   }

   [Fact]
   public void EnsureProxyDirectory()
   {
      Assert.Equal("mkdir -p .kamal/proxy", Join(NewCommand().EnsureProxyDirectory()));
   }

   [Fact]
   public void ReadBootOptions()
   {
      Assert.Equal(
         "cat .kamal/proxy/options 2> /dev/null || echo \"--publish 80:80 --publish 443:443 --log-opt max-size=10m\"",
         Join(NewCommand().ReadBootOptions()));
   }

   [Fact]
   public void ReadImage()
   {
      Assert.Equal(
         "cat .kamal/proxy/image 2> /dev/null || echo \"basecamp/kamal-proxy\"",
         Join(NewCommand().ReadImage()));
   }

   [Fact]
   public void ReadImageVersion()
   {
      Assert.Equal(
         $"cat .kamal/proxy/image_version 2> /dev/null || echo \"{ProxyRun.MinimumVersion}\"",
         Join(NewCommand().ReadImageVersion()));
   }

   [Fact]
   public void ReadRunCommand()
   {
      Assert.Equal(
         "cat .kamal/proxy/run_command 2> /dev/null || echo \"\"",
         Join(NewCommand().ReadRunCommand()));
   }

   [Fact]
   public void ResetBootOptions()
   {
      Assert.Equal("rm .kamal/proxy/options", Join(NewCommand().ResetBootOptions()));
   }

   [Fact]
   public void ResetImage()
   {
      Assert.Equal("rm .kamal/proxy/image", Join(NewCommand().ResetImage()));
   }

   [Fact]
   public void ResetImageVersion()
   {
      Assert.Equal("rm .kamal/proxy/image_version", Join(NewCommand().ResetImageVersion()));
   }

   [Fact]
   public void EnsureAppsConfigDirectory()
   {
      Assert.Equal("mkdir -p .kamal/proxy/apps-config", Join(NewCommand().EnsureAppsConfigDirectory()));
   }

   [Fact]
   public void ResetRunCommand()
   {
      Assert.Equal("rm .kamal/proxy/run_command", Join(NewCommand().ResetRunCommand()));
   }

   [Fact]
   public void RegistryRunConfig()
   {
      _config["proxy"] = new Cfg { ["run"] = new Cfg { ["registry"] = "registry:4443" } };
      Assert.Equal(
         "docker run --name kamal-proxy --network kamal --detach --restart unless-stopped --volume kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy --volume $PWD/.kamal/proxy/apps-config:/home/kamal-proxy/.apps-config --publish 80:80 --publish 443:443 --log-opt max-size=10m registry:4443/basecamp/kamal-proxy:v0.9.2 kamal-proxy run",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void RepositoryRunConfig()
   {
      _config["proxy"] = new Cfg { ["run"] = new Cfg { ["repository"] = "custom/repo" } };
      Assert.Equal(
         "docker run --name kamal-proxy --network kamal --detach --restart unless-stopped --volume kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy --volume $PWD/.kamal/proxy/apps-config:/home/kamal-proxy/.apps-config --publish 80:80 --publish 443:443 --log-opt max-size=10m custom/repo:v0.9.2 kamal-proxy run",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void ImageVersionRunConfig()
   {
      _config["proxy"] = new Cfg { ["run"] = new Cfg { ["version"] = "v1.2.3" } };
      Assert.Equal(
         "docker run --name kamal-proxy --network kamal --detach --restart unless-stopped --volume kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy --volume $PWD/.kamal/proxy/apps-config:/home/kamal-proxy/.apps-config --publish 80:80 --publish 443:443 --log-opt max-size=10m basecamp/kamal-proxy:v1.2.3 kamal-proxy run",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void BindIpsRunConfig()
   {
      _config["proxy"] = new Cfg { ["run"] = new Cfg { ["bind_ips"] = L("0.0.0.0", "127.0.0.1") } };
      Assert.Equal(
         "docker run --name kamal-proxy --network kamal --detach --restart unless-stopped --volume kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy --volume $PWD/.kamal/proxy/apps-config:/home/kamal-proxy/.apps-config --publish 0.0.0.0:80:80 --publish 0.0.0.0:443:443 --publish 127.0.0.1:80:80 --publish 127.0.0.1:443:443 --log-opt max-size=10m basecamp/kamal-proxy:v0.9.2 kamal-proxy run",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void LogMaxSizeRunConfig()
   {
      _config["proxy"] = new Cfg { ["run"] = new Cfg { ["log_max_size"] = "50m" } };
      Assert.Equal(
         "docker run --name kamal-proxy --network kamal --detach --restart unless-stopped --volume kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy --volume $PWD/.kamal/proxy/apps-config:/home/kamal-proxy/.apps-config --publish 80:80 --publish 443:443 --log-opt max-size=50m basecamp/kamal-proxy:v0.9.2 kamal-proxy run",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void DebugRunConfig()
   {
      _config["proxy"] = new Cfg { ["run"] = new Cfg { ["debug"] = true } };
      Assert.Equal(
         "docker run --name kamal-proxy --network kamal --detach --restart unless-stopped --volume kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy --volume $PWD/.kamal/proxy/apps-config:/home/kamal-proxy/.apps-config --publish 80:80 --publish 443:443 --log-opt max-size=10m basecamp/kamal-proxy:v0.9.2 kamal-proxy run --debug",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void MetricsPortRunConfig()
   {
      _config["proxy"] = new Cfg { ["run"] = new Cfg { ["metrics_port"] = 9090 } };
      Assert.Equal(
         "docker run --name kamal-proxy --network kamal --detach --restart unless-stopped --volume kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy --volume $PWD/.kamal/proxy/apps-config:/home/kamal-proxy/.apps-config --publish 80:80 --publish 443:443 --log-opt max-size=10m --expose=9090 basecamp/kamal-proxy:v0.9.2 kamal-proxy run --metrics-port \"9090\"",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void DontPublishRunConfig()
   {
      _config["proxy"] = new Cfg { ["run"] = new Cfg { ["publish"] = false } };
      Assert.Equal(
         "docker run --name kamal-proxy --network kamal --detach --restart unless-stopped --volume kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy --volume $PWD/.kamal/proxy/apps-config:/home/kamal-proxy/.apps-config --log-opt max-size=10m basecamp/kamal-proxy:v0.9.2 kamal-proxy run",
         Join(NewCommand().Run()));
   }

   private Kamal.Commands.Proxy NewCommand()
   {
      return new Kamal.Commands.Proxy(new KamalConfiguration(_config, version: "123"), host: "1.1.1.1");
   }
}
