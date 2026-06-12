using Kamal.Secrets;

namespace Kamal.Tests.Secrets;

/// <summary>
/// Test double for adapter shell calls, the equivalent of stubbing Ruby backticks in the
/// upstream Kamal test suite. Commands must be stubbed with their exact command line;
/// stubbing the same command multiple times returns the outputs in sequence
/// (the last one repeats), matching mocha's sequential returns.
/// </summary>
public sealed class FakeShell
{
   private readonly Dictionary<string, Queue<ShellResult>> _stubs = new();

   public List<string> Commands { get; } = new();

   public FakeShell Stub(string command, string output = "", bool success = true)
   {
      if (!_stubs.TryGetValue(command, out var queue))
         _stubs[command] = queue = new Queue<ShellResult>();

      queue.Enqueue(new ShellResult(success ? 0 : 1, output, ""));
      return this;
   }

   public ShellResult Run(string command)
   {
      Commands.Add(command);

      if (_stubs.TryGetValue(command, out var queue))
         return queue.Count > 1 ? queue.Dequeue() : queue.Peek();

      throw new InvalidOperationException($"Unexpected shell command: {command}");
   }
}
