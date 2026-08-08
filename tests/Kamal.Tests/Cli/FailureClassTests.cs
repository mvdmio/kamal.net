using System.Net.Sockets;
using Kamal.Cli;
using Kamal.Execution;
using Kamal.Tests.Execution;
using Kamal.Utils;
using Renci.SshNet.Common;

namespace Kamal.Tests.Cli;

/// <summary>
/// Failure classes, greppable markers, and public exit codes (spec step 04).
/// Classification unit cases plus CLI-level exits for each public class.
/// </summary>
[Collection("kamal-config")]
public sealed class FailureClassTests
{
   [Theory]
   [InlineData(typeof(BuildError), FailureClass.Build, FailureClasses.ExitBuild)]
   [InlineData(typeof(LockError), FailureClass.Lock, FailureClasses.ExitLock)]
   [InlineData(typeof(HealthcheckError), FailureClass.Healthcheck, FailureClasses.ExitHealthcheck)]
   [InlineData(typeof(BootError), FailureClass.Healthcheck, FailureClasses.ExitHealthcheck)]
   public void Classify_MapsKnownCliErrors(Type exceptionType, FailureClass expectedClass, int expectedExit)
   {
      var exception = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

      Assert.Equal(expectedClass, FailureClasses.Classify(exception));
      Assert.Equal(expectedExit, FailureClasses.ExitCode(expectedClass));
      Assert.Equal($"kamal.failure_class={FailureClasses.Name(expectedClass)}", FailureClasses.Marker(expectedClass));
   }

   [Fact]
   public void Classify_SshAuthenticationIsAuth()
   {
      var inner = new SshAuthenticationException("Permission denied (publickey).");
      var error = new ExecuteError("1.1.1.1", "SSH authentication failed for 1.1.1.1: Permission denied (publickey).", innerException: inner);

      Assert.Equal(FailureClass.Auth, FailureClasses.Classify(error));
      Assert.Equal(FailureClass.Auth, FailureClasses.Classify(inner));
   }

   [Fact]
   public void Classify_SshConnectionAndTimeoutAreConnect()
   {
      Assert.Equal(FailureClass.Connect, FailureClasses.Classify(new SshConnectionException("Connection reset")));
      Assert.Equal(FailureClass.Connect, FailureClasses.Classify(new SshOperationTimeoutException("timed out")));
      Assert.Equal(FailureClass.Connect, FailureClasses.Classify(new SocketException((int)SocketError.ConnectionRefused)));
      Assert.Equal(FailureClass.Connect, FailureClasses.Classify(
         new ExecuteError("1.1.1.1", "Could not establish a connected SSH session to 1.1.1.1 after 3 attempts.")));
   }

   [Fact]
   public void Classify_GenericForUnrelatedFailures()
   {
      Assert.Equal(FailureClass.Generic, FailureClasses.Classify(new InvalidOperationException("nope")));
      Assert.Equal(FailureClass.Generic, FailureClasses.Classify(new HookError("hook failed")));
      Assert.Equal(FailureClass.Generic, FailureClasses.Classify(new ExecuteError("1.1.1.1", "Command failed with exit status 1")));
   }

   [Fact]
   public void Classify_PrefersAuthOverConnectInMultipleExecuteError()
   {
      var errors = new[]
      {
         new ExecuteError("1.1.1.1", "SSH connection failed", innerException: new SshConnectionException("reset")),
         new ExecuteError("1.1.1.2", "SSH authentication failed", innerException: new SshAuthenticationException("denied"))
      };

      Assert.Equal(FailureClass.Auth, FailureClasses.Classify(new MultipleExecuteError(errors)));
   }

   [Fact]
   public void SshBackend_WrapConnectFailure_PreservesAuthVsConnect()
   {
      var auth = SshBackend.WrapConnectFailure("host", new SshAuthenticationException("Permission denied (publickey)."));
      Assert.Contains("SSH authentication failed", auth.Message);
      Assert.Equal(FailureClass.Auth, FailureClasses.Classify(auth));

      var connect = SshBackend.WrapConnectFailure("host", new SshConnectionException("Connection refused"));
      Assert.Contains("SSH connection failed", connect.Message);
      Assert.Equal(FailureClass.Connect, FailureClasses.Classify(connect));
   }

   [Fact]
   public async Task Deploy_EmitsPhaseMarkersSeparateFromFailureClass()
   {
      using var harness = new CliTestHarness();
      harness.Responders.Add((host, command) =>
      {
         if (command.Contains("name=^app-web-999$") && harness.CommandsOn(host).Any(c => c.Contains("docker run --detach")))
            return new RunResult(0, "abc12345678\n", "");

         return null;
      });

      Assert.Equal(0, await harness.Run("deploy", "--skip-push"));

      Assert.Contains(DeployPhase.Marker(DeployPhase.Build), harness.Output);
      Assert.Contains(DeployPhase.Marker(DeployPhase.Connect), harness.Output);
      Assert.Contains(DeployPhase.Marker(DeployPhase.Boot), harness.Output);
      Assert.DoesNotContain("kamal.failure_class=", harness.Output);
   }

   [Fact]
   public async Task Deploy_LockFailure_Exits40WithMarker()
   {
      using var harness = new CliTestHarness();
      harness.RespondTo("mkdir .kamal/lock-app", "", exitCode: 1, stderr: "mkdir: cannot create directory '.kamal/lock-app': File exists\n");

      var exitCode = await harness.Run("deploy", "--skip-push");

      Assert.Equal(FailureClasses.ExitLock, exitCode);
      Assert.Contains(FailureClasses.Marker(FailureClass.Lock), harness.Output);
      Assert.Contains(DeployPhase.Marker(DeployPhase.Build), harness.Output);
   }

   [Fact]
   public async Task AppBoot_MissingEndpoint_Exits30WithHealthcheckMarker()
   {
      using var harness = new CliTestHarness(
         """
         service: app
         image: dhh/app
         servers:
           - 1.1.1.1
         registry:
           username: user
           password: pw
         builder:
           arch: amd64
         """);

      var exitCode = await harness.Run("app", "boot", "--version", "999");

      Assert.Equal(FailureClasses.ExitHealthcheck, exitCode);
      Assert.Contains(FailureClasses.Marker(FailureClass.Healthcheck), harness.Output);
      Assert.Contains(DeployPhase.Marker(DeployPhase.Boot), harness.Output);
   }

   [Fact]
   public async Task RemoteAuthFailure_Exits11WithAuthMarker()
   {
      using var harness = new CliTestHarness();
      Coordinator.BackendFactory = host => new FakeBackend(host, (_, _) =>
         throw new SshAuthenticationException("Permission denied (publickey)."));

      var exitCode = await harness.Run("app", "details");

      Assert.Equal(FailureClasses.ExitAuth, exitCode);
      Assert.Contains(FailureClasses.Marker(FailureClass.Auth), harness.Output);
      Assert.Contains("Permission denied (publickey)", harness.Output);
   }

   [Fact]
   public async Task RemoteConnectFailure_Exits10WithConnectMarker()
   {
      using var harness = new CliTestHarness();
      Coordinator.BackendFactory = host => new FakeBackend(host, (_, _) =>
         throw new SshConnectionException("Connection timed out"));

      var exitCode = await harness.Run("app", "details");

      Assert.Equal(FailureClasses.ExitConnect, exitCode);
      Assert.Contains(FailureClasses.Marker(FailureClass.Connect), harness.Output);
   }

   [Fact]
   public async Task BuildError_Exits20WithBuildMarker()
   {
      using var harness = new CliTestHarness();
      // Force the git-clone builder path; dirty clone status raises BuildError from Validate.
      Git.Runner = new Kamal.Tests.Configuration.FakeGitRunner { UsedResult = true };

      Coordinator.LocalBackendFactory = () => new FakeBackend("localhost", (_, command) =>
      {
         // Satisfy EnsureDockerInstalled (any failure there becomes DependencyError).
         if (command.Contains("docker --version") || command.Contains("buildx version"))
            return new RunResult(0, "Docker version 24.0.0\ngithub.com/docker/buildx v0.11.0\n", "");

         // First clone succeeds; status porcelain non-empty → BuildError ("clone is dirty").
         if (command.Contains("status") && command.Contains("porcelain"))
            return new RunResult(0, " M dirty-file\n", "");

         return new RunResult(0, "", "");
      });

      var exitCode = await harness.Run("build", "push");

      Assert.Equal(FailureClasses.ExitBuild, exitCode);
      Assert.Contains(FailureClasses.Marker(FailureClass.Build), harness.Output);
      Assert.Contains("BuildError", harness.Output);
   }

   [Fact]
   public async Task GenericFailure_Exits1WithGenericMarker()
   {
      using var harness = new CliTestHarness();
      WriteHook(harness, "pre-connect");
      harness.RespondTo("pre-connect", "", exitCode: 1, stderr: "Don't deploy on Fridays\n");

      var exitCode = await harness.Run("lock", "status");

      Assert.Equal(FailureClasses.ExitGeneric, exitCode);
      Assert.Contains(FailureClasses.Marker(FailureClass.Generic), harness.Output);
      Assert.Contains("Hook `pre-connect` failed:", harness.Output);
   }

   [Fact]
   public async Task HealthcheckError_Exits30()
   {
      // Direct report path for HealthcheckError (barrier halt); BootError is covered via app boot.
      var exitCode = await CaptureReport(new HealthcheckError("Halted at barrier"));

      Assert.Equal(FailureClasses.ExitHealthcheck, exitCode);
   }

   private static async Task<int> CaptureReport(Exception exception)
   {
      var originalOut = Console.Out;
      var originalErr = Console.Error;

      try
      {
         using var output = new StringWriter();
         Console.SetOut(output);
         Console.SetError(output);

         return await Task.FromResult(KamalCli.ReportFailure(exception));
      }
      finally
      {
         Console.SetOut(originalOut);
         Console.SetError(originalErr);
      }
   }

   private static void WriteHook(CliTestHarness harness, string name)
   {
      var hooksDir = Path.Combine(harness.Dir, ".kamal", "hooks");
      Directory.CreateDirectory(hooksDir);
      File.WriteAllText(Path.Combine(hooksDir, name), "#!/bin/sh\nexit 0\n");
   }
}
