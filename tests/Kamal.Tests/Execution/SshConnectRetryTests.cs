using System.Net.Sockets;
using Kamal.Cli;
using Kamal.Execution;
using Renci.SshNet.Common;

namespace Kamal.Tests.Execution;

/// <summary>
/// SSH connect retry at session open: connect-class opens are retried (3 attempts, 1s then 2s
/// backoff), auth fails once, cancellation aborts backoff, final class stays connect.
/// </summary>
public sealed class SshConnectRetryTests : IDisposable
{
   private readonly Func<TimeSpan, CancellationToken, Task> _originalDelay;
   private readonly TextWriter _originalOut;
   private readonly StringWriter _capturedOut = new();

   public SshConnectRetryTests()
   {
      _originalDelay = SshConnectRetry.DelayAsync;
      SshConnectRetry.DelayAsync = static (_, _) => Task.CompletedTask;
      _originalOut = Console.Out;
      Console.SetOut(_capturedOut);
   }

   public void Dispose()
   {
      SshConnectRetry.DelayAsync = _originalDelay;
      Console.SetOut(_originalOut);
      _capturedOut.Dispose();
   }

   private string Output => _capturedOut.ToString();

   [Fact]
   public void BackoffDelay_IsOneSecondThenTwoSeconds()
   {
      Assert.Equal(TimeSpan.FromSeconds(1), SshConnectRetry.BackoffDelay(1));
      Assert.Equal(TimeSpan.FromSeconds(2), SshConnectRetry.BackoffDelay(2));
      Assert.Equal(3, SshConnectRetry.MaxAttempts);
   }

   [Fact]
   public async Task Connect_SucceedsAfterTransientFailures()
   {
      var attempts = 0;

      var result = await SshConnectRetry.RunAsync("1.2.3.4", _ =>
      {
         attempts++;
         if (attempts < 3)
            throw ConnectFailure("1.2.3.4", "Connection timed out");

         return Task.FromResult("ok");
      });

      Assert.Equal("ok", result);
      Assert.Equal(3, attempts);
      Assert.Contains("1.2.3.4", Output);
      Assert.Contains("attempt 1 of 3", Output);
      Assert.Contains("attempt 2 of 3", Output);
      Assert.Contains("retrying", Output, StringComparison.OrdinalIgnoreCase);
   }

   [Fact]
   public async Task Connect_FailsAfterThreeAttempts_PreservesConnectClass()
   {
      var attempts = 0;
      var delays = new List<TimeSpan>();
      SshConnectRetry.DelayAsync = (delay, _) =>
      {
         delays.Add(delay);
         return Task.CompletedTask;
      };

      var ex = await Assert.ThrowsAsync<ExecuteError>(() =>
         SshConnectRetry.RunAsync<object>("web.example", _ =>
         {
            attempts++;
            throw ConnectFailure("web.example", "Connection refused");
         }));

      Assert.Equal(3, attempts);
      Assert.Equal(2, delays.Count);
      Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)], delays);
      Assert.Equal(FailureClass.Connect, FailureClasses.Classify(ex));
      Assert.Equal(FailureClasses.ExitConnect, FailureClasses.ExitCode(FailureClass.Connect));
      Assert.Contains("web.example", Output);
      Assert.Contains("retrying", Output, StringComparison.OrdinalIgnoreCase);
   }

   [Theory]
   [MemberData(nameof(ConnectClassExceptions))]
   public async Task ConnectClass_IsRetried(Exception failure)
   {
      var attempts = 0;

      await Assert.ThrowsAsync(failure.GetType(), () =>
         SshConnectRetry.RunAsync<object>("host", _ =>
         {
            attempts++;
            throw failure;
         }));

      Assert.Equal(SshConnectRetry.MaxAttempts, attempts);
   }

   public static TheoryData<Exception> ConnectClassExceptions() => new(
      new SshConnectionException("Connection refused"),
      new SshOperationTimeoutException("Connection has timed out"),
      new SocketException((int)SocketError.ConnectionRefused),
      new TimeoutException("timed out"),
      ConnectFailure("h", "Connection reset"),
      SshBackend.WrapConnectFailure("h", new SshConnectionException("dropped")));

   [Theory]
   [MemberData(nameof(AuthClassExceptions))]
   public async Task Auth_FailsOnce_NoRetryLoop(Exception failure)
   {
      var attempts = 0;
      var delays = 0;
      SshConnectRetry.DelayAsync = (_, _) =>
      {
         delays++;
         return Task.CompletedTask;
      };

      var thrown = await Assert.ThrowsAsync(failure.GetType(), () =>
         SshConnectRetry.RunAsync<object>("host", _ =>
         {
            attempts++;
            throw failure;
         }));

      Assert.Equal(1, attempts);
      Assert.Equal(0, delays);
      Assert.Same(failure, thrown);
      Assert.Equal(FailureClass.Auth, FailureClasses.Classify(failure));
      Assert.DoesNotContain("retrying", Output, StringComparison.OrdinalIgnoreCase);
   }

   public static TheoryData<Exception> AuthClassExceptions() => new(
      new SshAuthenticationException("Permission denied (publickey)."),
      new SshPassPhraseNullOrEmptyException("passphrase required"),
      SshBackend.WrapConnectFailure("host", new SshAuthenticationException("Permission denied (publickey).")));

   [Fact]
   public async Task CancellationDuringBackoff_PreventsFurtherOpen()
   {
      var attempts = 0;
      using var cts = new CancellationTokenSource();

      SshConnectRetry.DelayAsync = (_, cancellationToken) =>
      {
         cts.Cancel();
         cancellationToken.ThrowIfCancellationRequested();
         return Task.CompletedTask;
      };

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
         SshConnectRetry.RunAsync<object>("host", _ =>
         {
            attempts++;
            throw ConnectFailure("host", "Connection timed out");
         }, cts.Token));

      Assert.Equal(1, attempts);
   }

   [Fact]
   public async Task OpenIsSingleRetryUnit_NotReenteredPerInnerStep()
   {
      // Jump-plus-target (or any multi-step open) is one open() invocation per attempt.
      var openCalls = 0;

      await Assert.ThrowsAsync<ExecuteError>(() =>
         SshConnectRetry.RunAsync<object>("target", async _ =>
         {
            openCalls++;
            // Simulate jump then target both failing as one unit by throwing once from open.
            await Task.Yield();
            throw ConnectFailure("target", "Connection refused");
         }));

      Assert.Equal(SshConnectRetry.MaxAttempts, openCalls);
   }

   [Fact]
   public async Task NonConnectGeneric_FailsOnce()
   {
      var attempts = 0;

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
         SshConnectRetry.RunAsync<object>("host", _ =>
         {
            attempts++;
            throw new InvalidOperationException("not a transport error");
         }));

      Assert.Equal(1, attempts);
      Assert.DoesNotContain("retrying", Output, StringComparison.OrdinalIgnoreCase);
   }

   private static ExecuteError ConnectFailure(string host, string message) =>
      SshBackend.WrapConnectFailure(host, new SshConnectionException(message));
}
