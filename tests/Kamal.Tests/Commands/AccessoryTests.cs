using Kamal.Configuration;
using Kamal.Tests.Configuration;
using static Kamal.Tests.Commands.Cmd;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/accessory_test.rb.</summary>
[Collection("kamal-config")]
public class AccessoryTests : IDisposable
{
   private readonly Cfg _config;
   private readonly TestSecrets _secrets;

   public AccessoryTests()
   {
      _secrets = new TestSecrets("MYSQL_ROOT_PASSWORD=secret123");

      _config = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["server"] = "private.registry", ["username"] = "dhh", ["password"] = "secret" },
         ["servers"] = L("1.1.1.1"),
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["accessories"] = new Cfg
         {
            ["mysql"] = new Cfg
            {
               ["image"] = "private.registry/mysql:8.0",
               ["host"] = "1.1.1.5",
               ["port"] = "3306",
               ["env"] = new Cfg
               {
                  ["clear"] = new Cfg { ["MYSQL_ROOT_HOST"] = "%" },
                  ["secret"] = L("MYSQL_ROOT_PASSWORD")
               },
               ["options"] = new Cfg { ["cpus"] = "4", ["memory"] = "2GB" }
            },
            ["redis"] = new Cfg
            {
               ["image"] = "redis:latest",
               ["host"] = "1.1.1.6",
               ["port"] = "6379:6379",
               ["labels"] = new Cfg { ["cache"] = "true" },
               ["env"] = new Cfg { ["SOMETHING"] = "else" },
               ["volumes"] = L("/var/lib/redis:/data")
            },
            ["busybox"] = new Cfg
            {
               ["service"] = "custom-busybox",
               ["image"] = "busybox:latest",
               ["registry"] = new Cfg { ["server"] = "other.registry", ["username"] = "user", ["password"] = "pw" },
               ["host"] = "1.1.1.7",
               ["proxy"] = new Cfg { ["host"] = "busybox.example.com" }
            }
         }
      };
   }

   public void Dispose() => _secrets.Dispose();

   [Fact]
   public void Run()
   {
      Assert.Equal(
         "docker run --name app-mysql --detach --restart unless-stopped --network kamal --log-opt max-size=\"10m\" --publish 3306:3306 --env MYSQL_ROOT_HOST=\"%\" --env-file .kamal/apps/app/env/accessories/mysql.env --label service=\"app-mysql\" --cpus \"4\" --memory \"2GB\" private.registry/mysql:8.0",
         Join(NewCommand("mysql").Run()));

      Assert.Equal(
         "docker run --name app-redis --detach --restart unless-stopped --network kamal --log-opt max-size=\"10m\" --publish 6379:6379 --env SOMETHING=\"else\" --env-file .kamal/apps/app/env/accessories/redis.env --volume /var/lib/redis:/data --label service=\"app-redis\" --label cache=\"true\" redis:latest",
         Join(NewCommand("redis").Run()));

      Assert.Equal(
         "docker run --name custom-busybox --detach --restart unless-stopped --network kamal --log-opt max-size=\"10m\" --env-file .kamal/apps/app/env/accessories/busybox.env --label service=\"custom-busybox\" other.registry/busybox:latest",
         Join(NewCommand("busybox").Run()));
   }

   [Fact]
   public void RunWithLoggingConfig()
   {
      _config["logging"] = new Cfg
      {
         ["driver"] = "local",
         ["options"] = new Cfg { ["max-size"] = "100m", ["max-file"] = "3" }
      };

      Assert.Equal(
         "docker run --name custom-busybox --detach --restart unless-stopped --network kamal --log-driver \"local\" --log-opt max-size=\"100m\" --log-opt max-file=\"3\" --env-file .kamal/apps/app/env/accessories/busybox.env --label service=\"custom-busybox\" other.registry/busybox:latest",
         Join(NewCommand("busybox").Run()));
   }

   [Fact]
   public void RunInCustomNetwork()
   {
      ((Cfg)((Cfg)_config["accessories"]!)["mysql"]!)["network"] = "custom";

      Assert.Equal(
         "docker run --name app-mysql --detach --restart unless-stopped --network custom --log-opt max-size=\"10m\" --publish 3306:3306 --env MYSQL_ROOT_HOST=\"%\" --env-file .kamal/apps/app/env/accessories/mysql.env --label service=\"app-mysql\" --cpus \"4\" --memory \"2GB\" private.registry/mysql:8.0",
         Join(NewCommand("mysql").Run()));
   }

   [Fact]
   public void Start()
   {
      Assert.Equal("docker container start app-mysql", Join(NewCommand("mysql").Start()));
   }

   [Fact]
   public void Stop()
   {
      Assert.Equal("docker container stop app-mysql", Join(NewCommand("mysql").Stop()));
   }

   [Fact]
   public void Info()
   {
      Assert.Equal("docker ps --filter label=service=app-mysql", Join(NewCommand("mysql").Info()));
   }

   [Fact]
   public void ExecuteInNewContainer()
   {
      Assert.Equal(
         "docker run --rm --network kamal --env MYSQL_ROOT_HOST=\"%\" --env-file .kamal/apps/app/env/accessories/mysql.env --cpus \"4\" --memory \"2GB\" private.registry/mysql:8.0 mysql -u root",
         Join(NewCommand("mysql").ExecuteInNewContainer(["mysql", "-u", "root"])));
   }

   [Fact]
   public void ExecuteInExistingContainer()
   {
      Assert.Equal(
         "docker exec app-mysql mysql -u root",
         Join(NewCommand("mysql").ExecuteInExistingContainer(["mysql", "-u", "root"])));
   }

   [Fact]
   public void ExecuteInNewContainerOverSsh()
   {
      using var stdin = new StdinScope(tty: true);

      Assert.Contains(
         "docker run -it --rm --network kamal --env MYSQL_ROOT_HOST=\"%\" --env-file .kamal/apps/app/env/accessories/mysql.env --cpus \"4\" --memory \"2GB\" private.registry/mysql:8.0 mysql -u root",
         NewCommand("mysql").ExecuteInNewContainerOverSsh("mysql", "-u", "root"));
   }

   [Fact]
   public void ExecuteInExistingContainerOverSsh()
   {
      using var stdin = new StdinScope(tty: true);

      Assert.Contains(
         "docker exec -it app-mysql mysql -u root",
         NewCommand("mysql").ExecuteInExistingContainerOverSsh("mysql", "-u", "root"));
   }

   [Fact]
   public void ExecuteInExistingContainerWithPipedInputOverSsh()
   {
      using var stdin = new StdinScope(tty: false);

      Assert.Contains(
         "docker exec -i app-mysql mysql -u root",
         NewCommand("mysql").ExecuteInExistingContainerOverSsh("mysql", "-u", "root"));
   }

   [Fact]
   public void Logs()
   {
      Assert.Equal(
         "docker logs app-mysql --timestamps 2>&1",
         Join(NewCommand("mysql").Logs()));

      Assert.Equal(
         "docker logs app-mysql  --since 5m  --tail 100 --timestamps 2>&1 | grep 'thing'",
         Join(NewCommand("mysql").Logs(since: "5m", lines: 100, grep: "thing")));

      Assert.Equal(
         "docker logs app-mysql  --since 5m  --tail 100 --timestamps 2>&1 | grep 'thing' -C 2",
         Join(NewCommand("mysql").Logs(since: "5m", lines: 100, grep: "thing", grepOptions: "-C 2")));

      Assert.Equal(
         "docker logs app-mysql  --since 5m  --tail 100 2>&1 | grep 'thing' -C 2",
         Join(NewCommand("mysql").Logs(timestamps: false, since: "5m", lines: 100, grep: "thing", grepOptions: "-C 2")));
   }

   [Fact]
   public void FollowLogs()
   {
      Assert.Equal(
         "ssh -t root@1.1.1.5 -p 22 'docker logs app-mysql --timestamps --tail 10 --follow 2>&1'",
         NewCommand("mysql").FollowLogs());

      Assert.Equal(
         "ssh -t root@1.1.1.5 -p 22 'docker logs app-mysql --tail 10 --follow 2>&1'",
         NewCommand("mysql").FollowLogs(timestamps: false));
   }

   [Fact]
   public void RemoveContainer()
   {
      Assert.Equal(
         "docker container prune --force --filter label=service=app-mysql",
         Join(NewCommand("mysql").RemoveContainer()));
   }

   [Fact]
   public void PullImage()
   {
      Assert.Equal(
         "docker image pull private.registry/mysql:8.0",
         Join(NewCommand("mysql").PullImage()));
   }

   [Fact]
   public void RemoveImage()
   {
      Assert.Equal(
         "docker image rm --force private.registry/mysql:8.0",
         Join(NewCommand("mysql").RemoveImage()));
   }

   [Fact]
   public void Deploy()
   {
      Assert.Equal(
         "docker exec kamal-proxy kamal-proxy deploy custom-busybox --target=\"172.1.0.2:80\" --host=\"busybox.example.com\" --deploy-timeout=\"30s\" --drain-timeout=\"30s\" --buffer-requests --buffer-responses --log-request-header=\"Cache-Control\" --log-request-header=\"Last-Modified\" --log-request-header=\"User-Agent\"",
         Join(NewCommand("busybox").Deploy(target: "172.1.0.2")));
   }

   [Fact]
   public void Remove()
   {
      Assert.Equal(
         "docker exec kamal-proxy kamal-proxy remove custom-busybox",
         Join(NewCommand("busybox").Remove()));
   }

   private Kamal.Commands.Accessory NewCommand(string accessory)
   {
      return new Kamal.Commands.Accessory(new KamalConfiguration(_config, secrets: _secrets.Secrets), name: accessory);
   }
}
