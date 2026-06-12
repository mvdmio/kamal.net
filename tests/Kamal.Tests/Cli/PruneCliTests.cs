namespace Kamal.Tests.Cli;

/// <summary>Port of the high-value parts of <c>test/cli/prune_test.rb</c>.</summary>
[Collection("kamal-config")]
public sealed class PruneCliTests
{
   [Fact]
   public async Task AllPrunesContainersThenImagesUnderTheLock()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("prune", "all");

      Assert.Equal(0, exitCode);

      var commands = harness.CommandsOn("1.1.1.1");

      int IndexOf(string fragment) => commands.FindIndex(command => command.Contains(fragment));

      var acquireLock = IndexOf("mkdir .kamal/lock-app");
      var containers = IndexOf("tail -n +6");
      var images = IndexOf("docker image prune --force --filter label=service=app");
      var releaseLock = IndexOf("rm -r .kamal/lock-app");

      Assert.True(acquireLock >= 0);
      Assert.True(containers > acquireLock, "containers pruned under the lock");
      Assert.True(images > containers, "images pruned after containers");
      Assert.True(releaseLock > images, "lock released last");
   }

   [Fact]
   public async Task ContainersHonorsRetainOption()
   {
      using var harness = new CliTestHarness();

      Assert.Equal(0, await harness.Run("prune", "containers", "--retain", "10"));

      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("tail -n +11"));
   }

   [Fact]
   public async Task ContainersRejectsRetainBelowOne()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("prune", "containers", "--retain", "0");

      Assert.Equal(1, exitCode);
      Assert.Contains("retain must be at least 1", harness.Output);
   }
}
