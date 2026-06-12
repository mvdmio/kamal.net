using Kamal.Execution;
using Kamal.Output;
using Kamal.Utils;

namespace Kamal.Tests.Execution;

[Collection("kamal-config")]
public sealed class OutputTests : IDisposable
{
   public void Dispose()
   {
      KamalOutput.Reset();
   }

   [Fact]
   public async Task SensitiveTokensAreRedactedInCommandLogs()
   {
      var writer = new StringWriter();
      KamalOutput.Logger = new ConsoleKamalLogger(writer);
      KamalOutput.Verbosity = Verbosity.Info;

      var backend = new FakeBackend("1.1.1.1");
      await backend.Execute(["docker", "login", "-p", new Sensitive("secret123", "[REDACTED]")]);

      var output = writer.ToString();
      Assert.Contains("Running docker login -p [REDACTED] on 1.1.1.1", output);
      Assert.DoesNotContain("secret123", output);

      // The command itself executed unredacted.
      Assert.Equal("docker login -p secret123", Assert.Single(backend.Commands));
   }

   [Fact]
   public void VerbosityGatesConsoleOutput()
   {
      var writer = new StringWriter();
      var logger = new ConsoleKamalLogger(writer);
      KamalOutput.Verbosity = Verbosity.Info;

      logger.Log(Verbosity.Debug, "hidden");
      logger.Log(Verbosity.Info, "shown");

      var output = writer.ToString();
      Assert.DoesNotContain("hidden", output);
      Assert.Contains("  INFO [local] shown", output);

      KamalOutput.Verbosity = Verbosity.Debug;
      logger.Log(Verbosity.Debug, "now visible");
      Assert.Contains(" DEBUG [local] now visible", writer.ToString());
   }

   [Fact]
   public async Task DebugVerbosityShowsCommandOutputLinesPrefixedPerHost()
   {
      var writer = new StringWriter();
      KamalOutput.Logger = new ConsoleKamalLogger(writer);
      KamalOutput.Verbosity = Verbosity.Debug;

      var backend = new FakeBackend("1.1.1.9", (_, _) => new RunResult(0, "line one\nline two\n", ""));
      await backend.Execute(["docker", "ps"]);

      var output = writer.ToString();
      Assert.Contains(" DEBUG [1.1.1.9] \tline one", output);
      Assert.Contains(" DEBUG [1.1.1.9] \tline two", output);
   }

   [Fact]
   public void BroadcastSinkReceivesLinesRegardlessOfVerbosity()
   {
      var writer = new StringWriter();
      var broadcast = new List<string>();
      var logger = new ConsoleKamalLogger(writer, broadcast.Add);
      KamalOutput.Verbosity = Verbosity.Error;

      logger.Log(Verbosity.Debug, "quiet line");

      Assert.DoesNotContain("quiet line", writer.ToString());
      Assert.Contains(broadcast, line => line.Contains("quiet line"));
   }

   [Fact]
   public void BroadcastLoggerFansOutToAllLoggers()
   {
      var broadcast = new BroadcastLogger();
      var first = new RecordingBroadcastLogger();
      var second = new RecordingBroadcastLogger();
      broadcast.BroadcastTo(first);
      broadcast.BroadcastTo(second);

      var payload = new ModifyPayload("deploy", null, null, ["1.1.1.1"]);
      broadcast.Start(payload);
      broadcast.Append("hello\n");
      broadcast.Finish(payload, 1.0, null);
      broadcast.Close();

      foreach (var logger in new[] { first, second })
      {
         Assert.True(logger.Started);
         Assert.Equal("hello\n", Assert.Single(logger.Messages));
         Assert.True(logger.Finished);
         Assert.True(logger.Closed);
      }
   }

   [Fact]
   public void FileLoggerWritesAppendedLinesAndCompletionTrailer()
   {
      var dir = Path.Combine(Path.GetTempPath(), "kamal-file-logger-" + Guid.NewGuid().ToString("N"));

      try
      {
         var logger = new FileLogger(dir);
         var payload = new ModifyPayload("deploy", "boot", "staging", ["1.1.1.1"]);

         logger.Append("ignored before start\n");
         logger.OnStart(payload);
         logger.Append("Running docker ps on 1.1.1.1\n");
         logger.OnFinish(payload, 2.34, null);

         var file = Assert.Single(Directory.GetFiles(dir, "*.log"));
         Assert.Contains("staging_deploy_boot", Path.GetFileName(file));

         var contents = File.ReadAllText(file);
         Assert.DoesNotContain("ignored before start", contents);
         Assert.Contains("Running docker ps on 1.1.1.1", contents);
         Assert.Contains("# Completed in 2.3s", contents);
      }
      finally
      {
         Directory.Delete(dir, recursive: true);
      }
   }

   [Fact]
   public void FileLoggerRecordsFailures()
   {
      var dir = Path.Combine(Path.GetTempPath(), "kamal-file-logger-" + Guid.NewGuid().ToString("N"));

      try
      {
         var logger = new FileLogger(dir);
         var payload = new ModifyPayload("deploy", null, null, ["1.1.1.1"]);

         logger.OnStart(payload);
         logger.OnFinish(payload, 0.5, new ExecuteError("1.1.1.1", "container did not boot"));

         var file = Assert.Single(Directory.GetFiles(dir, "*.log"));
         Assert.Contains("# FAILED: ExecuteError: container did not boot (0.5s)", File.ReadAllText(file));
      }
      finally
      {
         Directory.Delete(dir, recursive: true);
      }
   }

   private sealed class RecordingBroadcastLogger : IBroadcastLogger
   {
      public bool Started { get; private set; }
      public bool Finished { get; private set; }
      public bool Closed { get; private set; }
      public List<string> Messages { get; } = new();

      public void OnStart(ModifyPayload payload) => Started = true;

      public void OnFinish(ModifyPayload payload, double runtimeSeconds, Exception? exception) => Finished = true;

      public void Append(string message) => Messages.Add(message);

      public void OnClose() => Closed = true;
   }
}
