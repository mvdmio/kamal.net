namespace Kamal.Output;

/// <summary>
/// Port of <c>Kamal::Output::FileLogger</c>: on modify start it opens a timestamped log file
/// under the configured directory, appended lines stream into it, and on finish it writes a
/// trailer ("# Completed in Xs" or "# FAILED: ...") and announces where the logs were written.
/// Appends while no file is open are ignored (matching the Ruby <c>@file&amp;.print</c>).
/// </summary>
public sealed class FileLogger : IBroadcastLogger
{
   private readonly System.Threading.Lock _lock = new();
   private StreamWriter? _file;
   private string? _filePath;

   public FileLogger(string path)
   {
      Path = path;
   }

   /// <summary>The directory log files are written into.</summary>
   public string Path { get; }

   public void OnStart(ModifyPayload payload)
   {
      lock (_lock)
      {
         Directory.CreateDirectory(Path);
         _filePath = System.IO.Path.Combine(Path, FilenameFor(payload));
         _file = new StreamWriter(new FileStream(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
      }
   }

   public void OnFinish(ModifyPayload payload, double runtimeSeconds, Exception? exception)
   {
      lock (_lock)
      {
         if (_file is null)
            return;

         var runtime = Math.Round(runtimeSeconds, 1);

         if (exception is not null)
            _file.WriteLine($"# FAILED: {exception.GetType().Name}: {exception.Message} ({runtime}s)");
         else
            _file.WriteLine($"# Completed in {runtime}s");

         _file.Dispose();
         _file = null;

         Console.WriteLine($"Logs written to {_filePath}");
      }
   }

   public void Append(string message)
   {
      lock (_lock)
         _file?.Write(message);
   }

   public void OnClose()
   {
      lock (_lock)
      {
         _file?.Dispose();
         _file = null;
      }
   }

   private static string FilenameFor(ModifyPayload payload)
   {
      var command = string.Join("_", new[] { payload.Command, payload.Subcommand }.Where(part => !string.IsNullOrEmpty(part)));
      var parts = new[] { DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss"), payload.Destination, command }
         .Where(part => !string.IsNullOrEmpty(part));

      return string.Join("_", parts) + ".log";
   }
}
