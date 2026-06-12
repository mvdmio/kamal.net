namespace Kamal.Output;

/// <summary>
/// The payload of a <c>modify.kamal</c> instrumentation event (ActiveSupport::Notifications in
/// Ruby): which CLI command is modifying which hosts.
/// </summary>
public sealed record ModifyPayload(string Command, string? Subcommand, string? Destination, IReadOnlyList<string> Hosts);

/// <summary>
/// Port of <c>Kamal::Output::BaseLogger</c>'s surface: a destination that receives modify
/// lifecycle events plus appended log lines.
/// </summary>
public interface IBroadcastLogger
{
   void OnStart(ModifyPayload payload);

   void OnFinish(ModifyPayload payload, double runtimeSeconds, Exception? exception);

   void Append(string message);

   void OnClose();
}

/// <summary>
/// Port of the <c>ActiveSupport::BroadcastLogger</c> the Commander fans output into:
/// every appended line and lifecycle event reaches all registered loggers.
/// </summary>
public sealed class BroadcastLogger
{
   private readonly List<IBroadcastLogger> _loggers = new();
   private readonly System.Threading.Lock _lock = new();

   public void BroadcastTo(IBroadcastLogger logger)
   {
      lock (_lock)
         _loggers.Add(logger);
   }

   public void Start(ModifyPayload payload)
   {
      foreach (var logger in Snapshot())
         logger.OnStart(payload);
   }

   public void Finish(ModifyPayload payload, double runtimeSeconds, Exception? exception)
   {
      foreach (var logger in Snapshot())
         logger.OnFinish(payload, runtimeSeconds, exception);
   }

   public void Append(string message)
   {
      foreach (var logger in Snapshot())
         logger.Append(message);
   }

   public void Close()
   {
      foreach (var logger in Snapshot())
         logger.OnClose();
   }

   private IBroadcastLogger[] Snapshot()
   {
      lock (_lock)
         return _loggers.ToArray();
   }
}
