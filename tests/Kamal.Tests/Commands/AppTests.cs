using System.Text.RegularExpressions;
using Kamal.Configuration;
using Kamal.Tests.Configuration;
using static Kamal.Tests.Commands.Cmd;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>Port of test/commands/app_test.rb.</summary>
[Collection("kamal-config")]
public class AppTests : IDisposable
{
   private readonly Cfg _config;
   private readonly TestSecrets _secrets;
   private string? _destination;

   public AppTests()
   {
      _secrets = new TestSecrets("RAILS_MASTER_KEY=456");

      _config = new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
         ["servers"] = new Cfg { ["web"] = L("1.1.1.1"), ["workers"] = L("1.1.1.2") },
         ["env"] = new Cfg { ["secret"] = L("RAILS_MASTER_KEY") },
         ["builder"] = new Cfg { ["arch"] = "amd64" }
      };
   }

   public void Dispose() => _secrets.Dispose();

   [Fact]
   public void Run()
   {
      Assert.Equal(
         "docker run --detach --restart unless-stopped --name app-web-999 --network kamal --env KAMAL_CONTAINER_NAME=\"app-web-999\" --env KAMAL_VERSION=\"999\" --env KAMAL_HOST=\"1.1.1.1\" --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" --label service=\"app\" --label role=\"web\" --label destination dhh/app:999",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void RunWithDestination()
   {
      _destination = "staging";
      Assert.Equal(
         "docker run --detach --restart unless-stopped --name app-web-staging-999 --network kamal --env KAMAL_CONTAINER_NAME=\"app-web-staging-999\" --env KAMAL_VERSION=\"999\" --env KAMAL_HOST=\"1.1.1.1\" --env KAMAL_DESTINATION=\"staging\" --env-file .kamal/apps/app-staging/env/roles/web.env --log-opt max-size=\"10m\" --label service=\"app\" --label role=\"web\" --label destination=\"staging\" dhh/app:999",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void RunWithHostname()
   {
      Assert.Equal(
         "docker run --detach --restart unless-stopped --name app-web-999 --network kamal --hostname myhost --env KAMAL_CONTAINER_NAME=\"app-web-999\" --env KAMAL_VERSION=\"999\" --env KAMAL_HOST=\"1.1.1.1\" --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" --label service=\"app\" --label role=\"web\" --label destination dhh/app:999",
         Join(NewCommand().Run(hostname: "myhost")));
   }

   [Fact]
   public void RunWithVolumes()
   {
      _config["volumes"] = L("/local/path:/container/path");

      Assert.Equal(
         "docker run --detach --restart unless-stopped --name app-web-999 --network kamal --env KAMAL_CONTAINER_NAME=\"app-web-999\" --env KAMAL_VERSION=\"999\" --env KAMAL_HOST=\"1.1.1.1\" --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" --volume /local/path:/container/path --label service=\"app\" --label role=\"web\" --label destination dhh/app:999",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void RunWithCustomOptions()
   {
      _config["servers"] = new Cfg
      {
         ["web"] = L("1.1.1.1"),
         ["jobs"] = new Cfg
         {
            ["hosts"] = L("1.1.1.2"),
            ["cmd"] = "bin/jobs",
            ["options"] = new Cfg { ["mount"] = "somewhere", ["cap-add"] = true }
         }
      };

      Assert.Equal(
         "docker run --detach --restart unless-stopped --name app-jobs-999 --network kamal --env KAMAL_CONTAINER_NAME=\"app-jobs-999\" --env KAMAL_VERSION=\"999\" --env KAMAL_HOST=\"1.1.1.2\" --env-file .kamal/apps/app/env/roles/jobs.env --log-opt max-size=\"10m\" --label service=\"app\" --label role=\"jobs\" --label destination --mount \"somewhere\" --cap-add dhh/app:999 bin/jobs",
         Join(NewCommand(role: "jobs", host: "1.1.1.2").Run()));
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
         "docker run --detach --restart unless-stopped --name app-web-999 --network kamal --env KAMAL_CONTAINER_NAME=\"app-web-999\" --env KAMAL_VERSION=\"999\" --env KAMAL_HOST=\"1.1.1.1\" --env-file .kamal/apps/app/env/roles/web.env --log-driver \"local\" --log-opt max-size=\"100m\" --log-opt max-file=\"3\" --label service=\"app\" --label role=\"web\" --label destination dhh/app:999",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void RunWithRoleLoggingConfig()
   {
      _config["logging"] = new Cfg
      {
         ["driver"] = "local",
         ["options"] = new Cfg { ["max-size"] = "10m", ["max-file"] = "3" }
      };
      _config["servers"] = new Cfg
      {
         ["web"] = new Cfg
         {
            ["hosts"] = L("1.1.1.1"),
            ["logging"] = new Cfg { ["driver"] = "local", ["options"] = new Cfg { ["max-size"] = "100m" } }
         }
      };

      Assert.Equal(
         "docker run --detach --restart unless-stopped --name app-web-999 --network kamal --env KAMAL_CONTAINER_NAME=\"app-web-999\" --env KAMAL_VERSION=\"999\" --env KAMAL_HOST=\"1.1.1.1\" --env-file .kamal/apps/app/env/roles/web.env --log-driver \"local\" --log-opt max-size=\"100m\" --log-opt max-file=\"3\" --label service=\"app\" --label role=\"web\" --label destination dhh/app:999",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void RunWithTags()
   {
      _config["servers"] = L(new Cfg { ["1.1.1.1"] = "tag1" });
      ((Cfg)_config["env"]!)["tags"] = new Cfg { ["tag1"] = new Cfg { ["ENV1"] = "value1" } };

      Assert.Equal(
         "docker run --detach --restart unless-stopped --name app-web-999 --network kamal --env KAMAL_CONTAINER_NAME=\"app-web-999\" --env KAMAL_VERSION=\"999\" --env KAMAL_HOST=\"1.1.1.1\" --env ENV1=\"value1\" --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" --label service=\"app\" --label role=\"web\" --label destination dhh/app:999",
         Join(NewCommand().Run()));
   }

   [Fact]
   public void Start()
   {
      Assert.Equal("docker start app-web-999", Join(NewCommand().Start()));
   }

   [Fact]
   public void StartWithDestination()
   {
      _destination = "staging";
      Assert.Equal("docker start app-web-staging-999", Join(NewCommand().Start()));
   }

   [Fact]
   public void Stop()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | xargs docker stop",
         Join(NewCommand().Stop()));
   }

   [Fact]
   public void StopWithCustomDrainTimeout()
   {
      _config["drain_timeout"] = 20;
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | xargs docker stop",
         Join(NewCommand().Stop()));

      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=workers --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=workers --filter status=running --filter status=restarting' | head -1 | xargs docker stop -t 20",
         Join(NewCommand(role: "workers").Stop()));
   }

   [Fact]
   public void StopWithVersion()
   {
      Assert.Equal(
         "docker container ls --all --filter 'name=^app-web-123$' --quiet | xargs docker stop",
         Join(NewCommand().Stop(version: "123")));
   }

   [Fact]
   public void Info()
   {
      Assert.Equal(
         "docker ps --filter label=service=app --filter label=destination= --filter label=role=web",
         Join(NewCommand().Info()));
   }

   [Fact]
   public void InfoWithDestination()
   {
      _destination = "staging";
      Assert.Equal(
         "docker ps --filter label=service=app --filter label=destination=staging --filter label=role=web",
         Join(NewCommand().Info()));
   }

   [Fact]
   public void Deploy()
   {
      Assert.Equal(
         "docker exec kamal-proxy kamal-proxy deploy app-web --target=\"172.1.0.2:80\" --deploy-timeout=\"30s\" --drain-timeout=\"30s\" --buffer-requests --buffer-responses --log-request-header=\"Cache-Control\" --log-request-header=\"Last-Modified\" --log-request-header=\"User-Agent\"",
         Join(NewCommand().Deploy(target: "172.1.0.2")));
   }

   [Fact]
   public void DeployWithSsl()
   {
      _config["proxy"] = new Cfg { ["ssl"] = true, ["host"] = "example.com" };

      Assert.Equal(
         "docker exec kamal-proxy kamal-proxy deploy app-web --target=\"172.1.0.2:80\" --host=\"example.com\" --tls --deploy-timeout=\"30s\" --drain-timeout=\"30s\" --buffer-requests --buffer-responses --log-request-header=\"Cache-Control\" --log-request-header=\"Last-Modified\" --log-request-header=\"User-Agent\"",
         Join(NewCommand().Deploy(target: "172.1.0.2")));
   }

   [Fact]
   public void DeployWithSslTargetingMultipleHosts()
   {
      _config["proxy"] = new Cfg { ["ssl"] = true, ["hosts"] = L("example.com", "anotherexample.com") };

      Assert.Equal(
         "docker exec kamal-proxy kamal-proxy deploy app-web --target=\"172.1.0.2:80\" --host=\"example.com\" --host=\"anotherexample.com\" --tls --deploy-timeout=\"30s\" --drain-timeout=\"30s\" --buffer-requests --buffer-responses --log-request-header=\"Cache-Control\" --log-request-header=\"Last-Modified\" --log-request-header=\"User-Agent\"",
         Join(NewCommand().Deploy(target: "172.1.0.2")));
   }

   [Fact]
   public void DeployWithSslFalse()
   {
      _config["proxy"] = new Cfg { ["ssl"] = false };

      Assert.Equal(
         "docker exec kamal-proxy kamal-proxy deploy app-web --target=\"172.1.0.2:80\" --deploy-timeout=\"30s\" --drain-timeout=\"30s\" --buffer-requests --buffer-responses --log-request-header=\"Cache-Control\" --log-request-header=\"Last-Modified\" --log-request-header=\"User-Agent\"",
         Join(NewCommand().Deploy(target: "172.1.0.2")));
   }

   [Fact]
   public void Remove()
   {
      Assert.Equal(
         "docker exec kamal-proxy kamal-proxy remove app-web",
         Join(NewCommand().Remove()));
   }

   [Fact]
   public void Logs()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | xargs docker logs --timestamps 2>&1",
         Join(NewCommand().Logs()));
   }

   [Fact]
   public void LogsWithContainerId()
   {
      Assert.Equal(
         "echo C137 | xargs docker logs --timestamps 2>&1",
         Join(NewCommand().Logs(containerId: "C137")));
   }

   [Fact]
   public void LogsWithSince()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | xargs docker logs --timestamps --since 5m 2>&1",
         Join(NewCommand().Logs(since: "5m")));
   }

   [Fact]
   public void LogsWithLines()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | xargs docker logs --timestamps --tail 100 2>&1",
         Join(NewCommand().Logs(lines: "100")));
   }

   [Fact]
   public void LogsWithSinceAndLines()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | xargs docker logs --timestamps --since 5m --tail 100 2>&1",
         Join(NewCommand().Logs(since: "5m", lines: "100")));
   }

   [Fact]
   public void LogsWithGrep()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | xargs docker logs --timestamps 2>&1 | grep 'my-id'",
         Join(NewCommand().Logs(grep: "my-id")));
   }

   [Fact]
   public void LogsWithGrepAndGrepOptions()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | xargs docker logs --timestamps 2>&1 | grep 'my-id' -C 2",
         Join(NewCommand().Logs(grep: "my-id", grepOptions: "-C 2")));
   }

   [Fact]
   public void LogsWithSinceGrepAndGrepOptions()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | xargs docker logs --timestamps --since 5m 2>&1 | grep 'my-id' -C 2",
         Join(NewCommand().Logs(since: "5m", grep: "my-id", grepOptions: "-C 2")));
   }

   [Fact]
   public void LogsWithSinceAndGrep()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | xargs docker logs --timestamps --since 5m 2>&1 | grep 'my-id'",
         Join(NewCommand().Logs(since: "5m", grep: "my-id")));
   }

   [Fact]
   public void FollowLogs()
   {
      Assert.Equal(
         "ssh -t root@app-1 -p 22 'sh -c '\\''docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''\\'\\'''\\''{{.ID}}'\\''\\'\\'''\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting'\\'' | head -1 | xargs docker logs --timestamps --follow 2>&1'",
         NewCommand().FollowLogs(host: "app-1"));

      Assert.Equal(
         "ssh -t root@app-1 -p 22 'sh -c '\\''docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''\\'\\'''\\''{{.ID}}'\\''\\'\\'''\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting'\\'' | head -1 | xargs docker logs --timestamps --follow 2>&1 | grep \"Completed\"'",
         NewCommand().FollowLogs(host: "app-1", grep: "Completed"));

      Assert.Equal(
         "ssh -t root@app-1 -p 22 'echo ID321 | xargs docker logs --timestamps --follow 2>&1'",
         NewCommand().FollowLogs(host: "app-1", containerId: "ID321"));

      Assert.Equal(
         "ssh -t root@app-1 -p 22 'sh -c '\\''docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''\\'\\'''\\''{{.ID}}'\\''\\'\\'''\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting'\\'' | head -1 | xargs docker logs --timestamps --tail 123 --follow 2>&1'",
         NewCommand().FollowLogs(host: "app-1", lines: 123));

      Assert.Equal(
         "ssh -t root@app-1 -p 22 'sh -c '\\''docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''\\'\\'''\\''{{.ID}}'\\''\\'\\'''\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting'\\'' | head -1 | xargs docker logs --timestamps --tail 123 --follow 2>&1 | grep \"Completed\"'",
         NewCommand().FollowLogs(host: "app-1", lines: 123, grep: "Completed"));

      Assert.Equal(
         "ssh -t root@app-1 -p 22 'sh -c '\\''docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''\\'\\'''\\''{{.ID}}'\\''\\'\\'''\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting'\\'' | head -1 | xargs docker logs --tail 123 --follow 2>&1 | grep \"Completed\"'",
         NewCommand().FollowLogs(host: "app-1", timestamps: false, lines: 123, grep: "Completed"));
   }

   [Fact]
   public void FollowLogsWithSshKeys()
   {
      _config["ssh"] = new Cfg { ["keys"] = L("path_to_key.pem") };
      Assert.Equal(
         "ssh -i path_to_key.pem -t root@app-1 -p 22 'sh -c '\\''docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''\\'\\'''\\''{{.ID}}'\\''\\'\\'''\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting'\\'' | head -1 | xargs docker logs --timestamps --follow 2>&1'",
         NewCommand().FollowLogs(host: "app-1"));
   }

   [Fact]
   public void FollowLogsWithSshProxyCommand()
   {
      _config["ssh"] = new Cfg { ["proxy_command"] = "ssh -W %h:%p user@proxy-server" };
      Assert.Equal(
         "ssh -o ProxyCommand='ssh -W %h:%p user@proxy-server' -t root@app-1 -p 22 'sh -c '\\''docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''\\'\\'''\\''{{.ID}}'\\''\\'\\'''\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting'\\'' | head -1 | xargs docker logs --timestamps --follow 2>&1'",
         NewCommand().FollowLogs(host: "app-1"));
   }

   [Fact]
   public void FollowLogsWithSshConfigFile()
   {
      _config["ssh"] = new Cfg { ["config"] = "~/.ssh/custom_config" };
      Assert.Equal(
         "ssh -F ~/.ssh/custom_config -t root@app-1 -p 22 'sh -c '\\''docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''\\'\\'''\\''{{.ID}}'\\''\\'\\'''\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting'\\'' | head -1 | xargs docker logs --timestamps --follow 2>&1'",
         NewCommand().FollowLogs(host: "app-1"));
   }

   [Fact]
   public void ExecuteInNewContainer()
   {
      Assert.Matches(
         new Regex("docker run --rm --name app-web-exec-999-[0-9a-f]{6} --network kamal --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" dhh/app:999 bin/rails db:setup"),
         Join(NewCommand().ExecuteInNewContainer(["bin/rails", "db:setup"], env: new Cfg())));
   }

   [Fact]
   public void ExecuteInNewContainerWithLogging()
   {
      _config["logging"] = new Cfg
      {
         ["driver"] = "local",
         ["options"] = new Cfg { ["max-size"] = "100m", ["max-file"] = "3" }
      };

      Assert.Matches(
         new Regex("docker run --rm --name app-web-exec-999-[0-9a-f]{6} --network kamal --env-file .kamal/apps/app/env/roles/web.env --log-driver \"local\" --log-opt max-size=\"100m\" --log-opt max-file=\"3\" dhh/app:999 bin/rails db:setup"),
         Join(NewCommand().ExecuteInNewContainer(["bin/rails", "db:setup"], env: new Cfg())));
   }

   [Fact]
   public void ExecuteInNewContainerWithEnv()
   {
      Assert.Matches(
         new Regex("docker run --rm --name app-web-exec-999-[0-9a-f]{6} --network kamal --env-file .kamal/apps/app/env/roles/web.env --env foo=\"bar\" --log-opt max-size=\"10m\" dhh/app:999 bin/rails db:setup"),
         Join(NewCommand().ExecuteInNewContainer(["bin/rails", "db:setup"], env: new Cfg { ["foo"] = "bar" })));
   }

   [Fact]
   public void ExecuteInNewDetachedContainer()
   {
      Assert.Matches(
         new Regex("docker run --detach --name app-web-exec-999-[0-9a-f]{6} --network kamal --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" dhh/app:999 bin/rails db:setup"),
         Join(NewCommand().ExecuteInNewContainer(["bin/rails", "db:setup"], detach: true, env: new Cfg())));
   }

   [Fact]
   public void ExecuteInNewContainerWithTags()
   {
      _config["servers"] = L(new Cfg { ["1.1.1.1"] = "tag1" });
      ((Cfg)_config["env"]!)["tags"] = new Cfg { ["tag1"] = new Cfg { ["ENV1"] = "value1" } };

      Assert.Matches(
         new Regex("docker run --rm --name app-web-exec-999-[0-9a-f]{6} --network kamal --env ENV1=\"value1\" --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" dhh/app:999 bin/rails db:setup"),
         Join(NewCommand().ExecuteInNewContainer(["bin/rails", "db:setup"], env: new Cfg())));
   }

   [Fact]
   public void ExecuteInNewContainerWithCustomOptions()
   {
      _config["servers"] = new Cfg
      {
         ["web"] = new Cfg
         {
            ["hosts"] = L("1.1.1.1"),
            ["options"] = new Cfg { ["mount"] = "somewhere", ["cap-add"] = true }
         }
      };

      Assert.Matches(
         new Regex("docker run --rm --name app-web-exec-999-[0-9a-f]{6} --network kamal --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" --mount \"somewhere\" --cap-add dhh/app:999 bin/rails db:setup"),
         Join(NewCommand().ExecuteInNewContainer(["bin/rails", "db:setup"], env: new Cfg())));
   }

   [Fact]
   public void ExecuteInExistingContainer()
   {
      Assert.Equal(
         "docker exec app-web-999 bin/rails db:setup",
         Join(NewCommand().ExecuteInExistingContainer(["bin/rails", "db:setup"], env: new Cfg())));
   }

   [Fact]
   public void ExecuteInExistingContainerWithEnv()
   {
      Assert.Equal(
         "docker exec --env foo=\"bar\" app-web-999 bin/rails db:setup",
         Join(NewCommand().ExecuteInExistingContainer(["bin/rails", "db:setup"], env: new Cfg { ["foo"] = "bar" })));
   }

   [Fact]
   public void ExecuteInNewContainerOverSsh()
   {
      using var stdin = new StdinScope(tty: true);

      Assert.Matches(
         new Regex("docker run -it --rm --name app-web-exec-999-[0-9a-f]{6} --network kamal --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" dhh/app:999 bin/rails c"),
         NewCommand().ExecuteInNewContainerOverSsh(["bin/rails", "c"], env: new Cfg()));
   }

   [Fact]
   public void ExecuteInNewContainerOverSshWithTags()
   {
      _config["servers"] = L(new Cfg { ["1.1.1.1"] = "tag1" });
      ((Cfg)_config["env"]!)["tags"] = new Cfg { ["tag1"] = new Cfg { ["ENV1"] = "value1" } };
      using var stdin = new StdinScope(tty: true);

      Assert.Matches(
         new Regex("ssh -t root@1.1.1.1 -p 22 'docker run -it --rm --name app-web-exec-999-[0-9a-f]{6} --network kamal --env ENV1=\"value1\" --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" dhh/app:999 bin/rails c'"),
         NewCommand().ExecuteInNewContainerOverSsh(["bin/rails", "c"], env: new Cfg()));
   }

   [Fact]
   public void ExecuteInNewContainerWithCustomOptionsOverSsh()
   {
      _config["servers"] = new Cfg
      {
         ["web"] = new Cfg
         {
            ["hosts"] = L("1.1.1.1"),
            ["options"] = new Cfg { ["mount"] = "somewhere", ["cap-add"] = true }
         }
      };
      using var stdin = new StdinScope(tty: true);

      Assert.Matches(
         new Regex("docker run -it --rm --name app-web-exec-999-[0-9a-f]{6} --network kamal --env-file .kamal/apps/app/env/roles/web.env --log-opt max-size=\"10m\" --mount \"somewhere\" --cap-add dhh/app:999 bin/rails c"),
         NewCommand().ExecuteInNewContainerOverSsh(["bin/rails", "c"], env: new Cfg()));
   }

   [Fact]
   public void ExecuteInExistingContainerOverSsh()
   {
      using var stdin = new StdinScope(tty: true);

      Assert.Matches(
         new Regex("docker exec -it app-web-999 bin/rails c"),
         NewCommand().ExecuteInExistingContainerOverSsh(["bin/rails", "c"], env: new Cfg()));
   }

   [Fact]
   public void ExecuteInExistingContainerWithPipedInputOverSsh()
   {
      using var stdin = new StdinScope(tty: false);

      Assert.Matches(
         new Regex("docker exec -i app-web-999 bin/rails c"),
         NewCommand().ExecuteInExistingContainerOverSsh(["bin/rails", "c"], env: new Cfg()));
   }

   [Fact]
   public void RunOverSsh()
   {
      Assert.Equal("ssh -t root@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithCustomUser()
   {
      _config["ssh"] = new Cfg { ["user"] = "app" };
      Assert.Equal("ssh -t app@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithCustomPort()
   {
      _config["ssh"] = new Cfg { ["port"] = "2222" };
      Assert.Equal("ssh -t root@1.1.1.1 -p 2222 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithProxy()
   {
      _config["ssh"] = new Cfg { ["proxy"] = "2.2.2.2" };
      Assert.Equal("ssh -J root@2.2.2.2 -t root@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithProxyUser()
   {
      _config["ssh"] = new Cfg { ["proxy"] = "app@2.2.2.2" };
      Assert.Equal("ssh -J app@2.2.2.2 -t root@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithCustomUserWithProxy()
   {
      _config["ssh"] = new Cfg { ["user"] = "app", ["proxy"] = "2.2.2.2" };
      Assert.Equal("ssh -J root@2.2.2.2 -t app@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithKeysConfig()
   {
      _config["ssh"] = new Cfg { ["keys"] = L("path_to_key.pem") };
      Assert.Equal("ssh -i path_to_key.pem -t root@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithKeysConfigWithKeysOnly()
   {
      _config["ssh"] = new Cfg { ["keys"] = L("path_to_key.pem"), ["keys_only"] = true };
      Assert.Equal("ssh -i path_to_key.pem -o IdentitiesOnly=yes -t root@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithProxyCommand()
   {
      _config["ssh"] = new Cfg { ["proxy_command"] = "ssh -W %h:%p user@proxy-server" };
      Assert.Equal("ssh -o ProxyCommand='ssh -W %h:%p user@proxy-server' -t root@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithConfigFile()
   {
      _config["ssh"] = new Cfg { ["config"] = "~/.ssh/custom_config" };
      Assert.Equal("ssh -F ~/.ssh/custom_config -t root@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithMultipleConfigFiles()
   {
      _config["ssh"] = new Cfg { ["config"] = L("~/.ssh/config1", "~/.ssh/config2") };
      Assert.Equal("ssh -F ~/.ssh/config1 -F ~/.ssh/config2 -t root@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithConfigFalse()
   {
      _config["ssh"] = new Cfg { ["config"] = false };
      Assert.Equal("ssh -F /dev/null -t root@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void RunOverSshWithConfigTrue()
   {
      _config["ssh"] = new Cfg { ["config"] = true };
      Assert.Equal("ssh -t root@1.1.1.1 -p 22 'ls'", NewCommand().RunOverSsh("ls", host: "1.1.1.1"));
   }

   [Fact]
   public void CurrentRunningContainerId()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1",
         Join(NewCommand().CurrentRunningContainerId()));
   }

   [Fact]
   public void CurrentRunningContainerIdWithDestination()
   {
      _destination = "staging";
      Assert.Equal(
         "sh -c 'docker ps --latest --quiet --filter label=service=app --filter label=destination=staging --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest-staging --format '\\''{{.ID}}'\\'') ; docker ps --latest --quiet --filter label=service=app --filter label=destination=staging --filter label=role=web --filter status=running --filter status=restarting' | head -1",
         Join(NewCommand().CurrentRunningContainerId()));
   }

   [Fact]
   public void ContainerIdFor()
   {
      Assert.Equal(
         "docker container ls --all --filter 'name=^app-999$' --quiet",
         Join(NewCommand().ContainerIdFor(containerName: "app-999")));
   }

   [Fact]
   public void CurrentRunningVersion()
   {
      Assert.Equal(
         "sh -c 'docker ps --latest --format '\\''{{.Names}}'\\'' --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --filter ancestor=$(docker image ls --filter reference=dhh/app:latest --format '\\''{{.ID}}'\\'') ; docker ps --latest --format '\\''{{.Names}}'\\'' --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting' | head -1 | while read line; do echo ${line#app-web-}; done",
         Join(NewCommand().CurrentRunningVersion()));
   }

   [Fact]
   public void ListVersions()
   {
      Assert.Equal(
         "docker ps --filter label=service=app --filter label=destination= --filter label=role=web --format \"{{.Names}}\" | while read line; do echo ${line#app-web-}; done",
         Join(NewCommand().ListVersions()));

      Assert.Equal(
         "docker ps --filter label=service=app --filter label=destination= --filter label=role=web --filter status=running --filter status=restarting --latest --format \"{{.Names}}\" | while read line; do echo ${line#app-web-}; done",
         Join(NewCommand().ListVersions(["--latest"], statuses: ["running", "restarting"])));
   }

   [Fact]
   public void ListContainers()
   {
      Assert.Equal(
         "docker container ls --all --filter label=service=app --filter label=destination= --filter label=role=web",
         Join(NewCommand().ListContainers()));
   }

   [Fact]
   public void ListContainersWithDestination()
   {
      _destination = "staging";
      Assert.Equal(
         "docker container ls --all --filter label=service=app --filter label=destination=staging --filter label=role=web",
         Join(NewCommand().ListContainers()));
   }

   [Fact]
   public void ListContainerNames()
   {
      Assert.Equal(
         "docker container ls --all --filter label=service=app --filter label=destination= --filter label=role=web --format '{{ .Names }}'",
         Join(NewCommand().ListContainerNames()));
   }

   [Fact]
   public void RemoveContainer()
   {
      Assert.Equal(
         "docker container ls --all --filter 'name=^app-web-999$' --quiet | xargs docker container rm",
         Join(NewCommand().RemoveContainer(version: "999")));
   }

   [Fact]
   public void RemoveContainerWithDestination()
   {
      _destination = "staging";
      Assert.Equal(
         "docker container ls --all --filter 'name=^app-web-staging-999$' --quiet | xargs docker container rm",
         Join(NewCommand().RemoveContainer(version: "999")));
   }

   [Fact]
   public void RemoveContainers()
   {
      Assert.Equal(
         "docker container prune --force --filter label=service=app --filter label=destination= --filter label=role=web",
         Join(NewCommand().RemoveContainers()));
   }

   [Fact]
   public void RemoveContainersWithDestination()
   {
      _destination = "staging";
      Assert.Equal(
         "docker container prune --force --filter label=service=app --filter label=destination=staging --filter label=role=web",
         Join(NewCommand().RemoveContainers()));
   }

   [Fact]
   public void ListImages()
   {
      Assert.Equal("docker image ls dhh/app", Join(NewCommand().ListImages()));
   }

   [Fact]
   public void RemoveImages()
   {
      Assert.Equal(
         "docker image prune --all --force --filter label=service=app",
         Join(NewCommand().RemoveImages()));
   }

   [Fact]
   public void RemoveImagesWithDestination()
   {
      _destination = "staging";
      Assert.Equal(
         "docker image prune --all --force --filter label=service=app",
         Join(NewCommand().RemoveImages()));
   }

   [Fact]
   public void TagLatestImage()
   {
      Assert.Equal("docker tag dhh/app:999 dhh/app:latest", Join(NewCommand().TagLatestImage()));
   }

   [Fact]
   public void TagLatestImageWithDestination()
   {
      _destination = "staging";
      Assert.Equal("docker tag dhh/app:999 dhh/app:latest-staging", Join(NewCommand().TagLatestImage()));
   }

   [Fact]
   public void ExtractAssets()
   {
      Assert.Equal(
         "mkdir -p .kamal/apps/app/assets/extracted/web-999 && " +
         "docker container rm app-web-assets 2> /dev/null || true && " +
         "docker container create --name app-web-assets dhh/app:999 && " +
         "docker container cp -L app-web-assets:/public/assets/. .kamal/apps/app/assets/extracted/web-999 && " +
         "docker container rm app-web-assets",
         Join(NewCommand(additionalConfig: new Cfg { ["asset_path"] = "/public/assets" }).ExtractAssets()));
   }

   [Fact]
   public void SyncAssetVolumes()
   {
      Assert.Equal(
         "mkdir -p .kamal/apps/app/assets/volumes/web-999 ; " +
         "cp -rnT .kamal/apps/app/assets/extracted/web-999 .kamal/apps/app/assets/volumes/web-999",
         Join(NewCommand(additionalConfig: new Cfg { ["asset_path"] = "/public/assets" }).SyncAssetVolumes()));

      Assert.Equal(
         "mkdir -p .kamal/apps/app/assets/volumes/web-999 ; " +
         "cp -rnT .kamal/apps/app/assets/extracted/web-999 .kamal/apps/app/assets/volumes/web-999 ; " +
         "cp -rnT .kamal/apps/app/assets/extracted/web-999 .kamal/apps/app/assets/volumes/web-998 || true ; " +
         "cp -rnT .kamal/apps/app/assets/extracted/web-998 .kamal/apps/app/assets/volumes/web-999 || true",
         Join(NewCommand(additionalConfig: new Cfg { ["asset_path"] = "/public/assets" }).SyncAssetVolumes(oldVersion: "998")));
   }

   [Fact]
   public void CleanUpAssets()
   {
      Assert.Equal(
         "find .kamal/apps/app/assets/extracted -maxdepth 1 -name 'web-*' ! -name web-999 -exec rm -rf \"{}\" + ; " +
         "find .kamal/apps/app/assets/volumes -maxdepth 1 -name 'web-*' ! -name web-999 -exec rm -rf \"{}\" +",
         Join(NewCommand(additionalConfig: new Cfg { ["asset_path"] = "/public/assets" }).CleanUpAssets()));
   }

   [Fact]
   public void Live()
   {
      Assert.Equal("docker exec kamal-proxy kamal-proxy resume app-web", Join(NewCommand().Live()));
   }

   [Fact]
   public void Maintenance()
   {
      Assert.Equal("docker exec kamal-proxy kamal-proxy stop app-web", Join(NewCommand().Maintenance()));
   }

   [Fact]
   public void MaintenanceWithOptions()
   {
      Assert.Equal(
         "docker exec kamal-proxy kamal-proxy stop app-web --drain-timeout=\"10s\" --message=\"Hi\"",
         Join(NewCommand().Maintenance(drainTimeout: 10, message: "Hi")));
   }

   [Fact]
   public void RemoveProxyAppDirectory()
   {
      Assert.Equal("rm -r .kamal/proxy/apps-config/app", Join(NewCommand().RemoveProxyAppDirectory()));
   }

   private Kamal.Commands.App NewCommand(string role = "web", string host = "1.1.1.1", Cfg? additionalConfig = null)
   {
      var raw = additionalConfig is null ? _config : DeepMerge(_config, additionalConfig);
      var config = new KamalConfiguration(raw, destination: _destination, version: "999", secrets: _secrets.Secrets);

      return new Kamal.Commands.App(config, role: config.Role(role), host: host);
   }
}
