using Kamal.Execution;

namespace Kamal.Output;

/// <summary>
/// Pragmatic port of <c>Kamal::Output::Formatter</c> (an SSHKit pretty formatter): receives
/// command lifecycle events and free-form messages. Commands arrive already redacted —
/// <see cref="Kamal.Utils.Sensitive"/> tokens never reach a logger unredacted.
/// </summary>
public interface IKamalLogger
{
   /// <summary>A command is starting on a host; logged at the command's verbosity.</summary>
   void CommandStart(string host, string redactedCommand, Verbosity verbosity);

   /// <summary>A line of command output ("stdout" or "stderr"); logged at DEBUG.</summary>
   void CommandData(string host, string stream, string line);

   /// <summary>A command finished; logged at the command's verbosity.</summary>
   void CommandExit(string host, string redactedCommand, int exitCode, double runtimeSeconds, Verbosity verbosity);

   /// <summary>A free-form message.</summary>
   void Log(Verbosity verbosity, string message, string? host = null);
}

/// <summary>
/// Global output settings, the stand-in for <c>SSHKit.config.output_verbosity</c> and
/// <c>SSHKit.config.output</c>. The Commander adjusts these (verbosity flags,
/// <c>with_verbosity</c>, output logger wiring).
/// </summary>
public static class KamalOutput
{
   public static Verbosity Verbosity { get; set; } = Verbosity.Info;

   public static IKamalLogger Logger { get; set; } = new ConsoleKamalLogger();

   public static void Reset()
   {
      Verbosity = Verbosity.Info;
      Logger = new ConsoleKamalLogger();
   }
}

/// <summary>
/// Writes SSHKit-pretty-style lines (<c>"  INFO [host] Running ... on host"</c>) to a
/// <see cref="TextWriter"/>, honoring <see cref="KamalOutput.Verbosity"/>. Every formatted line
/// is also forwarded to the optional broadcast sink regardless of verbosity, mirroring
/// <c>Kamal::Output::Formatter#write_message</c> (file loggers receive everything).
/// </summary>
public sealed class ConsoleKamalLogger : IKamalLogger
{
   private readonly TextWriter _output;
   private readonly Action<string>? _broadcast;
   private readonly System.Threading.Lock _lock = new();

   public ConsoleKamalLogger(TextWriter? output = null, Action<string>? broadcast = null)
   {
      _output = output ?? Console.Out;
      _broadcast = broadcast;
   }

   public void CommandStart(string host, string redactedCommand, Verbosity verbosity)
   {
      Log(verbosity, $"Running {redactedCommand} on {host}", host);
   }

   public void CommandData(string host, string stream, string line)
   {
      Log(Verbosity.Debug, $"\t{line}", host);
   }

   public void CommandExit(string host, string redactedCommand, int exitCode, double runtimeSeconds, Verbosity verbosity)
   {
      var status = exitCode == 0 ? "successful" : "failed";
      Log(verbosity, $"Finished in {runtimeSeconds:0.000} seconds with exit status {exitCode} ({status})", host);
   }

   public void Log(Verbosity verbosity, string message, string? host = null)
   {
      var line = $"{verbosity.Label()} [{host ?? "local"}] {message}";

      lock (_lock)
      {
         if (verbosity >= KamalOutput.Verbosity)
            _output.WriteLine(line);

         _broadcast?.Invoke(line + "\n");
      }
   }
}
