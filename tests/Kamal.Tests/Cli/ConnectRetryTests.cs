using System.Net.Sockets;
using Kamal.Cli;
using Kamal.Execution;
using Kamal.Tests.Execution;
using Renci.SshNet.Common;

namespace Kamal.Tests.Cli;

/// <summary>
/// Opt-in connect-only deploy retry (spec step 05): off by default, <c>--retry [N]</c>,
/// connect-class failures re-run full deploy up to N with backoff; auth/build/healthcheck/lock never retried.
/// </summary>
[Collection("kamal-config")]
public sealed class ConnectRetryTests : IDisposable
{
   private readonly Func<TimeSpan, CancellationToken, Task> _originalDelay;

   public ConnectRetryTests()
   {
      _originalDelay = ConnectRetry.DelayAsync;
      ConnectRetry.DelayAsync = static (_, _) => Task.CompletedTask;
   }

   public void Dispose()
   {
      ConnectRetry.DelayAsync = _originalDelay;
   }

   [Theory]
   [InlineData(false, null, 1)]
   [InlineData(true, null, 3)]
   [InlineData(true, 5, 5)]
   [InlineData(true, 0, 1)]
   [InlineData(true, 1, 1)]
   public void ResolveMaxAttempts_MapsFlagAndN(bool present, int? explicitAttempts, int expected)
   {
      Assert.Equal(expected, ConnectRetry.ResolveMaxAttempts(present, explicitAttempts));
   }

   [Fact]
   public void BackoffDelay_IsExponentialCappedAt30s()
   {
      Assert.Equal(TimeSpan.FromSeconds(1), ConnectRetry.BackoffDelay(1));
      Assert.Equal(TimeSpan.FromSeconds(2), ConnectRetry.BackoffDelay(2));
      Assert.Equal(TimeSpan.FromSeconds(4), ConnectRetry.BackoffDelay(3));
      Assert.Equal(TimeSpan.FromSeconds(16), ConnectRetry.BackoffDelay(5));
      Assert.Equal(TimeSpan.FromSeconds(30), ConnectRetry.BackoffDelay(10));
   }

   [Fact]
   public async Task RunAsync_Disabled_DoesNotRetryConnect()
   {
      var attempts = 0;

      var ex = await Assert.ThrowsAsync<SshConnectionException>(() =>
         ConnectRetry.RunAsync(1, () =>
         {
            attempts++;
            throw new SshConnectionException("Connection timed out");
         }));

      Assert.Equal(1, attempts);
      Assert.Equal(FailureClass.Connect, FailureClasses.Classify(ex));
   }

   [Fact]
   public async Task RunAsync_Connect_RetriesUpToNThenRethrows()
   {
      var attempts = 0;
      var delays = new List<TimeSpan>();
      ConnectRetry.DelayAsync = (delay, _) =>
      {
         delays.Add(delay);
         return Task.CompletedTask;
      };

      var ex = await Assert.ThrowsAsync<SshConnectionException>(() =>
         ConnectRetry.RunAsync(3, () =>
         {
            attempts++;
            throw new SshConnectionException("Connection refused");
         }));

      Assert.Equal(3, attempts);
      Assert.Equal(2, delays.Count);
      Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
      Assert.Equal(FailureClass.Connect, FailureClasses.Classify(ex));
   }

   [Fact]
   public async Task RunAsync_Connect_SucceedsAfterTransientFailures()
   {
      var attempts = 0;

      await ConnectRetry.RunAsync(3, () =>
      {
         attempts++;
         if (attempts < 3)
            throw new SshConnectionException("Connection reset");
         return Task.CompletedTask;
      });

      Assert.Equal(3, attempts);
   }

   [Theory]
   [MemberData(nameof(NonRetryableExceptions))]
   public async Task RunAsync_NonConnect_FailsOnce(Exception failure)
   {
      var attempts = 0;
      var delays = 0;
      ConnectRetry.DelayAsync = (_, _) =>
      {
         delays++;
         return Task.CompletedTask;
      };

      var thrown = await Assert.ThrowsAsync(failure.GetType(), () =>
         ConnectRetry.RunAsync(5, () =>
         {
            attempts++;
            throw failure;
         }));

      Assert.Equal(1, attempts);
      Assert.Equal(0, delays);
      Assert.Same(failure, thrown);
      Assert.NotEqual(FailureClass.Connect, FailureClasses.Classify(failure));
   }

   public static TheoryData<Exception> NonRetryableExceptions() => new(
      new SshAuthenticationException("Permission denied (publickey)."),
      new BuildError("build failed"),
      new HealthcheckError("unhealthy"),
      new BootError("no endpoint"),
      new LockError("Deploy lock found"),
      new InvalidOperationException("generic"));

   [Fact]
   public async Task RunAsync_ResetsConnectedFlagBetweenAttempts()
   {
      KamalRuntime.Reset();
      var connectedSnapshots = new List<bool>();

      await Assert.ThrowsAsync<SshConnectionException>(() =>
         ConnectRetry.RunAsync(2, () =>
         {
            connectedSnapshots.Add(KamalRuntime.Commander.Connected);
            KamalRuntime.Commander.Connected = true;
            throw new SshConnectionException("timed out");
         }));

      Assert.Equal([false, false], connectedSnapshots);
   }

   [Fact]
   public async Task Deploy_WithoutRetry_ConnectFailsOnce()
   {
      using var harness = new CliTestHarness();
      Coordinator.BackendFactory = _ =>
         new FakeBackend("host", (_, _) => throw new SshConnectionException("Connection timed out"));

      var exitCode = await harness.Run("deploy", "--skip-push");

      Assert.Equal(FailureClasses.ExitConnect, exitCode);
      Assert.Contains(FailureClasses.Marker(FailureClass.Connect), harness.Output);
      Assert.Equal(1, CountOccurrences(harness.Output, DeployPhase.Marker(DeployPhase.Build)));
      Assert.DoesNotContain("retrying", harness.Output, StringComparison.OrdinalIgnoreCase);
   }

   [Fact]
   public async Task Deploy_WithRetry_ConnectAttemptedUpToDefaultThree()
   {
      using var harness = new CliTestHarness();
      var delays = new List<TimeSpan>();
      ConnectRetry.DelayAsync = (delay, _) =>
      {
         delays.Add(delay);
         return Task.CompletedTask;
      };

      Coordinator.BackendFactory = _ =>
         new FakeBackend("host", (_, _) => throw new SshConnectionException("Connection timed out"));

      var exitCode = await harness.Run("deploy", "--skip-push", "--retry");

      Assert.Equal(FailureClasses.ExitConnect, exitCode);
      Assert.Contains(FailureClasses.Marker(FailureClass.Connect), harness.Output);
      Assert.Equal(3, CountOccurrences(harness.Output, DeployPhase.Marker(DeployPhase.Build)));
      Assert.Equal(2, delays.Count);
      Assert.Contains("retrying", harness.Output, StringComparison.OrdinalIgnoreCase);
   }

   [Fact]
   public async Task Deploy_WithRetryN_UsesCustomAttemptCount()
   {
      using var harness = new CliTestHarness();
      Coordinator.BackendFactory = _ =>
         new FakeBackend("host", (_, _) => throw new SocketException((int)SocketError.ConnectionRefused));

      var exitCode = await harness.Run("deploy", "--skip-push", "--retry", "2");

      Assert.Equal(FailureClasses.ExitConnect, exitCode);
      Assert.Equal(2, CountOccurrences(harness.Output, DeployPhase.Marker(DeployPhase.Build)));
   }

   [Fact]
   public async Task Deploy_WithRetry_AuthFailsOnce()
   {
      using var harness = new CliTestHarness();
      Coordinator.BackendFactory = _ =>
         new FakeBackend("host", (_, _) => throw new SshAuthenticationException("Permission denied (publickey)."));

      var exitCode = await harness.Run("deploy", "--skip-push", "--retry", "5");

      Assert.Equal(FailureClasses.ExitAuth, exitCode);
      Assert.Contains(FailureClasses.Marker(FailureClass.Auth), harness.Output);
      Assert.Equal(1, CountOccurrences(harness.Output, DeployPhase.Marker(DeployPhase.Build)));
      Assert.DoesNotContain("retrying", harness.Output, StringComparison.OrdinalIgnoreCase);
   }

   [Fact]
   public async Task Deploy_WithRetry_LockFailsOnce()
   {
      using var harness = new CliTestHarness();
      harness.RespondTo("mkdir .kamal/lock-app", "", exitCode: 1, stderr: "mkdir: cannot create directory '.kamal/lock-app': File exists\n");

      var exitCode = await harness.Run("deploy", "--skip-push", "--retry", "4");

      Assert.Equal(FailureClasses.ExitLock, exitCode);
      Assert.Contains(FailureClasses.Marker(FailureClass.Lock), harness.Output);
      Assert.Equal(1, CountOccurrences(harness.Output, DeployPhase.Marker(DeployPhase.Build)));
   }

   [Fact]
   public async Task Deploy_WithRetry_SucceedsAfterTransientConnect()
   {
      using var harness = new CliTestHarness();
      var failedOnce = false;

      Coordinator.BackendFactory = host => new FakeBackend(host, (h, command) =>
      {
         if (!failedOnce)
         {
            failedOnce = true;
            throw new SshConnectionException("Connection timed out");
         }

         harness.Commands.Enqueue((h, command));

         if (command.Contains("name=^app-web-999$")
             && harness.Commands.Any(c => c.Host == h && c.Command.Contains("docker run --detach")))
            return new RunResult(0, "abc12345678\n", "");

         return new RunResult(0, "", "");
      });

      Coordinator.LocalBackendFactory = () => new FakeBackend("localhost", (h, command) =>
      {
         harness.Commands.Enqueue((h, command));
         return new RunResult(0, "", "");
      });

      var exitCode = await harness.Run("deploy", "--skip-push", "--retry", "3");

      Assert.Equal(0, exitCode);
      Assert.Contains("retrying", harness.Output, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain("kamal.failure_class=", harness.Output);
      Assert.True(CountOccurrences(harness.Output, DeployPhase.Marker(DeployPhase.Build)) >= 2);
   }

   private static int CountOccurrences(string text, string fragment)
   {
      var count = 0;
      var index = 0;

      while ((index = text.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
      {
         count++;
         index += fragment.Length;
      }

      return count;
   }
}
