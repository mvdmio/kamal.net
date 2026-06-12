using Kamal.Execution;
using Kamal.Utils;

namespace Kamal.Tests.Execution;

[Collection("kamal-config")]
public class BackendBaseTests
{
   [Fact]
   public async Task ExecuteJoinsTokensWithUnredactedSensitiveValues()
   {
      var backend = new FakeBackend("1.1.1.1");

      await backend.Execute(["docker", "login", "-p", new Sensitive("secret123", "[REDACTED]")]);

      Assert.Equal("docker login -p secret123", Assert.Single(backend.Commands));
   }

   [Fact]
   public async Task ExecuteRaisesExecuteErrorOnNonZeroExit()
   {
      var backend = new FakeBackend("1.1.1.1", (_, _) => new RunResult(3, "", "boom\n"));

      var error = await Assert.ThrowsAsync<ExecuteError>(() => backend.Execute(["false"]));

      Assert.Equal("1.1.1.1", error.Host);
      Assert.Equal(3, error.ExitCode);
      Assert.Equal("boom\n", error.Stderr);
      Assert.Contains("false", error.Message);
   }

   [Fact]
   public async Task ExecuteErrorCarriesRedactedCommandOnly()
   {
      var backend = new FakeBackend("1.1.1.1", (_, _) => new RunResult(1, "", ""));

      var error = await Assert.ThrowsAsync<ExecuteError>(
         () => backend.Execute(["docker", "login", "-p", new Sensitive("secret123", "[REDACTED]")]));

      Assert.Contains("[REDACTED]", error.Command);
      Assert.DoesNotContain("secret123", error.Command);
      Assert.DoesNotContain("secret123", error.Message);
   }

   [Fact]
   public async Task ExecuteDoesNotRaiseWhenDisabled()
   {
      var backend = new FakeBackend("1.1.1.1", (_, _) => new RunResult(1, "", ""));

      await backend.Execute(["false"], raiseOnNonZeroExit: false);
   }

   [Fact]
   public async Task CaptureStripsOutput()
   {
      var backend = new FakeBackend("1.1.1.1", (_, _) => new RunResult(0, "  v1.2.3 \n", ""));

      Assert.Equal("v1.2.3", await backend.Capture(["app", "version"]));
   }

   [Fact]
   public async Task CaptureWithInfoAndDebugReturnIdenticalOutput()
   {
      var backend = new FakeBackend("1.1.1.1", (_, _) => new RunResult(0, "output\n", ""));

      Assert.Equal("output", await backend.CaptureWithInfo(["cmd"]));
      Assert.Equal("output", await backend.CaptureWithDebug(["cmd"]));
   }

   [Fact]
   public async Task CaptureWithPrettyJsonPrettyPrints()
   {
      var backend = new FakeBackend("1.1.1.1", (_, _) => new RunResult(0, """{"name":"app","replicas":2}""", ""));

      var pretty = await backend.CaptureWithPrettyJson(["docker", "inspect"]);

      Assert.Contains("\"name\": \"app\"", pretty);
      Assert.Contains("\n", pretty);
   }

   [Fact]
   public async Task TestReturnsExitCodeAsBool()
   {
      var ok = new FakeBackend("1.1.1.1", (_, _) => new RunResult(0, "", ""));
      var failing = new FakeBackend("1.1.1.1", (_, _) => new RunResult(1, "", ""));

      Assert.True(await ok.Test(["true"]));
      Assert.False(await failing.Test(["false"]));
   }

   [Fact]
   public async Task UnexpectedExceptionsAreWrappedInExecuteError()
   {
      var backend = new FakeBackend("1.1.1.1", (_, _) => throw new InvalidOperationException("socket closed"));

      var error = await Assert.ThrowsAsync<ExecuteError>(() => backend.Execute(["uptime"]));

      Assert.Equal("1.1.1.1", error.Host);
      Assert.Contains("socket closed", error.Message);
      Assert.IsType<InvalidOperationException>(error.InnerException);
   }
}
