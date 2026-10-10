using Kamal.Cli;
using Kamal.Execution;
using Renci.SshNet.Common;

namespace Kamal.Tests.Cli;

/// <summary>
/// <c>kamal build pull</c> with a local registry: the SSH tunnel goes only to hosts that do not
/// already run the <c>kamal-docker-registry</c> container.
/// </summary>
[Collection("kamal-config")]
public sealed class BuildCliTests
{
   private const string RegistryLookup = "docker ps --filter name=^kamal-docker-registry$ --quiet";
   private const string ImagePull = "docker pull localhost:5000/dhh/app:999";

   private const string LocalRegistryOneHost =
      """
      service: app
      image: dhh/app
      servers:
        - 1.1.1.1
      registry:
        server: localhost:5000
      builder:
        arch: amd64
      """;

   private const string LocalRegistryTwoHosts =
      """
      service: app
      image: dhh/app
      servers:
        - 1.1.1.1
        - 1.1.1.2
      registry:
        server: localhost:5000
      builder:
        arch: amd64
      """;

   [Fact]
   public async Task PullSkipsTunnelToHostRunningLocalRegistry()
   {
      using var harness = new CliTestHarness(LocalRegistryOneHost);
      harness.RespondTo(RegistryLookup, "abc123\n");

      var exitCode = await harness.Run("build", "pull");

      Assert.Equal(0, exitCode);
      Assert.Empty(harness.ForwardedHosts);
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains(ImagePull));
      Assert.Contains("Skipping local registry port forwarding to 1.1.1.1: kamal-docker-registry is already running there", harness.Output);
   }

   [Fact]
   public async Task PullTunnelsToHostWithoutLocalRegistry()
   {
      using var harness = new CliTestHarness(LocalRegistryOneHost);

      var exitCode = await harness.Run("build", "pull");

      Assert.Equal(0, exitCode);
      Assert.Equal([["1.1.1.1"]], harness.ForwardedHosts);
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains(ImagePull));
   }

   [Fact]
   public async Task PullTunnelsOnlyToHostsWithoutLocalRegistry()
   {
      using var harness = new CliTestHarness(LocalRegistryTwoHosts);
      harness.Responders.Add((host, command) =>
         host == "1.1.1.1" && command.Contains(RegistryLookup) ? new RunResult(0, "abc123\n", "") : null);

      var exitCode = await harness.Run("build", "pull");

      Assert.Equal(0, exitCode);
      Assert.Equal([["1.1.1.2"]], harness.ForwardedHosts);
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains(ImagePull));
      Assert.Contains(harness.CommandsOn("1.1.1.2"), command => command.Contains(ImagePull));
   }

   [Fact]
   public async Task PullTunnelsToHostWhenRegistryLookupFails()
   {
      using var harness = new CliTestHarness(LocalRegistryOneHost);
      harness.RespondTo(RegistryLookup, "abc123\n", exitCode: 1, stderr: "docker: command failed");

      var exitCode = await harness.Run("build", "pull");

      Assert.Equal(0, exitCode);
      Assert.Equal([["1.1.1.1"]], harness.ForwardedHosts);
   }

   [Fact]
   public async Task PullStopsAtRegistryLookupWhenHostCannotBeReached()
   {
      using var harness = new CliTestHarness(LocalRegistryOneHost);
      harness.Responders.Add((_, command) =>
         command.Contains(RegistryLookup) ? throw new SshConnectionException("Connection timed out") : (RunResult?)null);

      var exitCode = await harness.Run("build", "pull");

      Assert.Equal(FailureClasses.ExitConnect, exitCode);
      Assert.Empty(harness.ForwardedHosts);
      Assert.DoesNotContain(harness.AllCommands, command => command.Contains(ImagePull));
   }

   [Fact]
   public async Task PullWithHostedRegistryRunsNoRegistryLookup()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("build", "pull");

      Assert.Equal(0, exitCode);
      Assert.DoesNotContain(harness.AllCommands, command => command.Contains("kamal-docker-registry"));
      Assert.Empty(harness.ForwardedHosts);
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("docker login"));
      Assert.Contains(harness.CommandsOn("1.1.1.2"), command => command.Contains("docker login"));
   }
}
