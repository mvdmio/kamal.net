namespace Kamal.Tests.Cli;

/// <summary>Hook running (port of base.rb's run_hook + pre-connect behavior).</summary>
[Collection("kamal-config")]
public sealed class HookTests
{
   [Fact]
   public async Task PreConnectHookRunsBeforeConnecting()
   {
      using var harness = new CliTestHarness();
      WriteHook(harness, "pre-connect");

      var exitCode = await harness.Run("lock", "status");

      Assert.Equal(0, exitCode);
      Assert.Contains(harness.CommandsOn("localhost"), command => command.Contains("pre-connect"));
   }

   [Fact]
   public async Task HooksAreSkippedWithSkipHooks()
   {
      using var harness = new CliTestHarness();
      WriteHook(harness, "pre-connect");

      var exitCode = await harness.Run("lock", "status", "-H");

      Assert.Equal(0, exitCode);
      Assert.DoesNotContain(harness.CommandsOn("localhost"), command => command.Contains("pre-connect"));
   }

   [Fact]
   public async Task FailingHookAbortsTheCommand()
   {
      using var harness = new CliTestHarness();
      WriteHook(harness, "pre-connect");
      harness.RespondTo("pre-connect", "", exitCode: 1, stderr: "Don't deploy on Fridays\n");

      var exitCode = await harness.Run("lock", "status");

      Assert.Equal(1, exitCode);
      Assert.Contains("Hook `pre-connect` failed:", harness.Output);
   }

   [Fact]
   public async Task PreDeployAndPostDeployHooksRunAroundDeploy()
   {
      using var harness = new CliTestHarness();
      WriteHook(harness, "pre-deploy");
      WriteHook(harness, "post-deploy");

      harness.Responders.Add((host, command) =>
      {
         if (command.Contains("name=^app-web-999$") && harness.CommandsOn(host).Any(c => c.Contains("docker run --detach")))
            return new Kamal.Execution.RunResult(0, "abc12345678\n", "");

         return null;
      });

      var exitCode = await harness.Run("deploy", "--skip-push");

      Assert.Equal(0, exitCode);

      var localCommands = harness.CommandsOn("localhost");
      Assert.Contains(localCommands, command => command.Contains("pre-deploy"));
      Assert.Contains(localCommands, command => command.Contains("post-deploy"));
   }

   private static void WriteHook(CliTestHarness harness, string name)
   {
      var hooksDir = Path.Combine(harness.Dir, ".kamal", "hooks");
      Directory.CreateDirectory(hooksDir);
      File.WriteAllText(Path.Combine(hooksDir, name), "#!/bin/sh\nexit 0\n");
   }
}
