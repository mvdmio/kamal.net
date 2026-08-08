using System.CommandLine;
using Kamal.Cli;

namespace Kamal.Tests.Cli;

/// <summary>Every command and option in the tree parses cleanly and is wired to an action.</summary>
[Collection("kamal-config")]
public sealed class ParseTreeTests
{
   public static TheoryData<string> CommandLines() => new(
      "setup --skip-push --no-cache",
      "deploy -P --no-cache --retry 3",
      "redeploy --skip-push",
      "rollback 123",
      "details",
      "audit",
      "config",
      "docs",
      "docs proxy",
      "init --bundle",
      "remove -y",
      "upgrade -y --rolling",
      "version",
      "accessory boot all",
      "accessory upload db",
      "accessory directories db",
      "accessory reboot db",
      "accessory start db",
      "accessory stop db",
      "accessory restart db",
      "accessory details all",
      "accessory exec db ls -i --reuse",
      "accessory exec db -- sh -c SELECT",
      "accessory logs db --since 1h --lines 10 --grep foo --follow --skip-timestamps",
      "accessory pull_image db",
      "accessory remove db -y",
      "accessory remove_container db",
      "accessory remove_image db",
      "accessory remove_service_directory db",
      "accessory upgrade all --rolling -y",
      "app boot",
      "app start",
      "app stop",
      "app details",
      "app exec -i --reuse ls",
      "app exec --detach ls -e FOO=bar",
      "app containers",
      "app stale_containers --stop",
      "app images",
      "app logs -s 1h -n 5 -g needle --follow -T --container-id abc",
      "app remove",
      "app live",
      "app maintenance --drain-timeout 30 --message down",
      "app remove_container 123",
      "app remove_containers",
      "app remove_images",
      "app remove_app_directories",
      "app version",
      "build deliver",
      "build push --output registry --no-cache",
      "build pull",
      "build create",
      "build remove",
      "build details",
      "build dev --output docker",
      "lock status",
      "lock acquire -m msg",
      "lock release",
      "proxy boot",
      "proxy boot_config set --publish --http-port 8080 --https-port 8443 --log-max-size 10m --metrics-port 9090 --debug",
      "proxy boot_config get",
      "proxy boot_config reset",
      "proxy reboot --rolling -y",
      "proxy start",
      "proxy stop",
      "proxy restart",
      "proxy details",
      "proxy logs -s 1h -n 5 -g needle -f -T",
      "proxy remove --force",
      "proxy remove_container",
      "proxy remove_image",
      "proxy remove_proxy_directory",
      "proxy upgrade --rolling -y",
      "prune all",
      "prune images",
      "prune containers --retain 3",
      "registry setup -L -R",
      "registry remove -L",
      "registry login",
      "registry logout -R",
      "secrets fetch --adapter test --account acc SECRET1 SECRET2",
      "secrets extract PASS {}",
      "secrets print",
      "server bootstrap",
      "server exec ls -i",
      "app boot -v --version 1 -p -h 1.1.1.1 -r web -c foo.yml -d staging -H",
      "app boot -q");

   [Theory]
   [MemberData(nameof(CommandLines))]
   public void CommandLineParses(string commandLine)
   {
      var root = KamalCli.BuildRootCommand();
      var parseResult = root.Parse(commandLine);

      Assert.Empty(parseResult.Errors);
      Assert.NotNull(parseResult.CommandResult.Command.Action);
   }

   [Fact]
   public void UnknownCommandReportsParseError()
   {
      var root = KamalCli.BuildRootCommand();
      var parseResult = root.Parse("definitely_not_a_command");

      Assert.NotEmpty(parseResult.Errors);
   }

   [Fact]
   public void LockAcquireRequiresMessage()
   {
      var root = KamalCli.BuildRootCommand();
      var parseResult = root.Parse("lock acquire");

      Assert.NotEmpty(parseResult.Errors);
   }

   [Fact]
   public void SecretsFetchRequiresAdapter()
   {
      var root = KamalCli.BuildRootCommand();
      var parseResult = root.Parse("secrets fetch SECRET");

      Assert.NotEmpty(parseResult.Errors);
   }

   [Fact]
   public void AccessoryExecEndOfOptionsKeepsGuestDashCInCmd()
   {
      var root = KamalCli.BuildRootCommand();
      var parseResult = root.Parse("accessory exec db -- sh -c SELECT");

      Assert.Empty(parseResult.Errors);
      Assert.Equal(["sh", "-c", "SELECT"], GetExecCmd(parseResult));
   }

   [Fact]
   public void AccessoryExecWithoutEndOfOptionsBindsDashCAsConfigFile()
   {
      var root = KamalCli.BuildRootCommand();
      // Recursive global -c/--config-file still wins when not protected by --.
      var parseResult = root.Parse("accessory exec db sh -c other.yml");

      Assert.Empty(parseResult.Errors);
      Assert.Equal(["sh"], GetExecCmd(parseResult));
      Assert.Equal("other.yml", parseResult.GetValue(ConfigFileOption(root)));
   }

   [Fact]
   public void AccessoryExecGlobalConfigFileStillWorksWithEndOfOptions()
   {
      var root = KamalCli.BuildRootCommand();
      var parseResult = root.Parse("-c staging.yml accessory exec db -- sh -c SELECT");

      Assert.Empty(parseResult.Errors);
      Assert.Equal("staging.yml", parseResult.GetValue(ConfigFileOption(root)));
      Assert.Equal(["sh", "-c", "SELECT"], GetExecCmd(parseResult));
   }

   private static string[] GetExecCmd(ParseResult parseResult)
   {
      var cmdArg = parseResult.CommandResult.Command.Arguments.OfType<Argument<string[]>>()
         .Single(a => a.Name is "cmd" or "CMD");
      return parseResult.GetValue(cmdArg) ?? [];
   }

   private static Option<string> ConfigFileOption(RootCommand root) =>
      root.Options.OfType<Option<string>>().Single(o =>
         o.Name is "config-file" or "--config-file" || o.Aliases.Contains("-c") || o.Aliases.Contains("--config-file"));
}
