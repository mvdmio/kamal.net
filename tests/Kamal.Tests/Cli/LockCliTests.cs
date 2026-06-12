namespace Kamal.Tests.Cli;

/// <summary>Port of the high-value parts of <c>test/cli/lock_test.rb</c>.</summary>
[Collection("kamal-config")]
public sealed class LockCliTests
{
   [Fact]
   public async Task AcquireCreatesLockOnPrimaryHost()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("lock", "acquire", "-m", "Maintenance in progress");

      Assert.Equal(0, exitCode);
      Assert.Contains("Acquired the deploy lock", harness.Output);

      // ensure_run_directory on all hosts, then the lock on the primary host only.
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("mkdir -p .kamal"));
      Assert.Contains(harness.CommandsOn("1.1.1.2"), command => command.Contains("mkdir -p .kamal"));
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("mkdir .kamal/lock-app"));
      Assert.DoesNotContain(harness.CommandsOn("1.1.1.2"), command => command.Contains("mkdir .kamal/lock-app"));
   }

   [Fact]
   public async Task AcquireWhenAlreadyLockedReportsLockError()
   {
      using var harness = new CliTestHarness();
      harness.RespondTo("mkdir .kamal/lock-app", "", exitCode: 1, stderr: "mkdir: cannot create directory '.kamal/lock-app': File exists\n");

      var exitCode = await harness.Run("lock", "acquire", "-m", "Maintenance");

      Assert.Equal(1, exitCode);
      Assert.Contains("Deploy lock already in place!", harness.Output);
      Assert.Contains("Deploy lock found. Run 'kamal lock help' for more information", harness.Output);
   }

   [Fact]
   public async Task ReleaseRemovesTheLock()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("lock", "release");

      Assert.Equal(0, exitCode);
      Assert.Contains("Released the deploy lock", harness.Output);
      Assert.Contains(harness.CommandsOn("1.1.1.1"), command => command.Contains("rm .kamal/lock-app/details") && command.Contains("rm -r .kamal/lock-app"));
   }

   [Fact]
   public async Task ReleaseWithoutLockSaysSo()
   {
      using var harness = new CliTestHarness();
      harness.RespondTo("rm .kamal/lock-app/details", "", exitCode: 1, stderr: "rm: cannot remove '.kamal/lock-app/details': No such file or directory\n");

      var exitCode = await harness.Run("lock", "release");

      Assert.Equal(0, exitCode);
      Assert.Contains("There is no deploy lock", harness.Output);
   }

   [Fact]
   public async Task StatusShowsLockDetails()
   {
      using var harness = new CliTestHarness();
      harness.RespondTo("base64 -d", "Locked by: deployer at 2024-01-01\nMessage: Maintenance\n");

      var exitCode = await harness.Run("lock", "status");

      Assert.Equal(0, exitCode);
      Assert.Contains("Locked by: deployer", harness.Output);
      Assert.Contains("Message: Maintenance", harness.Output);
   }
}
