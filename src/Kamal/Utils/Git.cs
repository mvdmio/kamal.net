using Kamal.Secrets;

namespace Kamal.Utils;

/// <summary>
/// Runs git commands for <see cref="Git"/>; replaceable in tests (Ruby tests stub the backticks).
/// </summary>
public interface IGitRunner
{
   /// <summary>Whether the current directory is inside a git repository (<c>git rev-parse</c> succeeds).</summary>
   bool Used();

   /// <summary>Runs <c>git &lt;args&gt;</c> and returns stdout ("" on failure, like backticks with a dead command).</summary>
   string Capture(string args);
}

/// <summary>Default <see cref="IGitRunner"/> shelling out to the local git binary.</summary>
public sealed class ShellGitRunner : IGitRunner
{
   public bool Used()
   {
      try
      {
         return ShellRunner.Run("git rev-parse").Success;
      }
      catch
      {
         return false;
      }
   }

   public string Capture(string args)
   {
      try
      {
         return ShellRunner.Run($"git {args}").Stdout;
      }
      catch
      {
         return "";
      }
   }
}

/// <summary>Port of <c>Kamal::Git</c>: static helpers shelling out to git.</summary>
public static class Git
{
   /// <summary>The runner used to execute git; tests can swap in a fake.</summary>
   public static IGitRunner Runner { get; set; } = new ShellGitRunner();

   /// <summary>Port of <c>Kamal::Git.used?</c>.</summary>
   public static bool Used => Runner.Used();

   /// <summary>Port of <c>Kamal::Git.user_name</c>.</summary>
   public static string UserName => Runner.Capture("config user.name").Trim();

   /// <summary>Port of <c>Kamal::Git.email</c>.</summary>
   public static string Email => Runner.Capture("config user.email").Trim();

   /// <summary>Port of <c>Kamal::Git.revision</c>.</summary>
   public static string Revision => Runner.Capture("rev-parse HEAD").Trim();

   /// <summary>Port of <c>Kamal::Git.uncommitted_changes</c>.</summary>
   public static string UncommittedChanges => Runner.Capture("status --porcelain").Trim();

   /// <summary>Port of <c>Kamal::Git.root</c>.</summary>
   public static string Root => Runner.Capture("rev-parse --show-toplevel").Trim();

   /// <summary>Port of <c>Kamal::Git.uncommitted_files</c>: relative paths of files with uncommitted changes.</summary>
   public static List<string> UncommittedFiles => Lines(Runner.Capture("ls-files --modified"));

   /// <summary>Port of <c>Kamal::Git.untracked_files</c>: relative paths of untracked files, including gitignored files.</summary>
   public static List<string> UntrackedFiles => Lines(Runner.Capture("ls-files --others"));

   private static List<string> Lines(string output)
   {
      return output
         .Split('\n', StringSplitOptions.RemoveEmptyEntries)
         .Select(line => line.Trim())
         .Where(line => line.Length > 0)
         .ToList();
   }
}
