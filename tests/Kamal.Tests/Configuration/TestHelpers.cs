using Kamal.Secrets;
using Kamal.Utils;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>
/// Serializes the configuration tests: they share process-global state
/// (VERSION / KAMAL_DESTINATION env vars, Git.Runner, KamalUtils.DockerArchOverride).
/// </summary>
[CollectionDefinition("kamal-config", DisableParallelization = true)]
public class KamalConfigCollection
{
}

public static class TestConfig
{
   /// <summary>Builds a list the way the YAML loader would (List&lt;object?&gt;).</summary>
   public static List<object?> L(params object?[] items) => items.ToList();

   /// <summary>The base deploy used throughout the Ruby configuration tests.</summary>
   public static Cfg BaseDeploy()
   {
      return new Cfg
      {
         ["service"] = "app",
         ["image"] = "dhh/app",
         ["registry"] = new Cfg { ["username"] = "dhh", ["password"] = "secret" },
         ["builder"] = new Cfg { ["arch"] = "amd64" },
         ["env"] = new Cfg { ["REDIS_URL"] = "redis://x/y" },
         ["servers"] = L("1.1.1.1", "1.1.1.2"),
         ["volumes"] = L("/local/path:/container/path")
      };
   }

   /// <summary>Maps a mixed argument list (strings/Sensitive/ints) to strings, like Ruby's <c>map(&amp;:to_s)</c>.</summary>
   public static List<string> S(IEnumerable<object> args) => args.Select(arg => arg.ToString() ?? "").ToList();
}

/// <summary>
/// Port of the Ruby test helper's <c>with_test_secrets</c>: writes secrets files in a temp
/// directory and exposes a <see cref="KamalSecrets"/> pointed at them (instead of chdir-ing).
/// </summary>
public sealed class TestSecrets : IDisposable
{
   private readonly string _tmpDir;

   public TestSecrets(string contents)
   {
      _tmpDir = Path.Combine(Path.GetTempPath(), "kamal-config-tests-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(Path.Combine(_tmpDir, ".kamal"));
      File.WriteAllText(SecretsPath, contents);

      Secrets = new KamalSecrets(secretsPath: SecretsPath);
   }

   public KamalSecrets Secrets { get; }

   public string SecretsPath => Path.Combine(_tmpDir, ".kamal", "secrets");

   public void Dispose()
   {
      try
      {
         Directory.Delete(_tmpDir, recursive: true);
      }
      catch (IOException)
      {
      }
   }
}

/// <summary>Sets an environment variable for the duration of a test.</summary>
public sealed class EnvVarScope : IDisposable
{
   private readonly string _name;
   private readonly string? _original;

   public EnvVarScope(string name, string? value)
   {
      _name = name;
      _original = Environment.GetEnvironmentVariable(name);
      Environment.SetEnvironmentVariable(name, value);
   }

   public void Dispose()
   {
      Environment.SetEnvironmentVariable(_name, _original);
   }
}

/// <summary>A fake <see cref="IGitRunner"/> with canned outputs per git argument string.</summary>
public sealed class FakeGitRunner : IGitRunner
{
   public bool UsedResult { get; set; } = true;

   public Dictionary<string, string> Outputs { get; } = new();

   public bool Used() => UsedResult;

   public string Capture(string args) => Outputs.TryGetValue(args, out var output) ? output : "";
}

/// <summary>Swaps <see cref="Git.Runner"/> for a fake and restores it afterwards.</summary>
public sealed class GitScope : IDisposable
{
   private readonly IGitRunner _original;

   public GitScope(IGitRunner fake)
   {
      _original = Git.Runner;
      Git.Runner = fake;
   }

   public void Dispose()
   {
      Git.Runner = _original;
   }
}

/// <summary>Creates a temp directory with fixture files for CreateFrom tests.</summary>
public sealed class FixtureDir : IDisposable
{
   private readonly string _dir;

   public FixtureDir()
   {
      _dir = Path.Combine(Path.GetTempPath(), "kamal-fixtures-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(_dir);
   }

   public string Write(string name, string contents)
   {
      var path = Path.Combine(_dir, name);
      File.WriteAllText(path, contents);
      return path;
   }

   public string PathOf(string name) => Path.Combine(_dir, name);

   public void Dispose()
   {
      try
      {
         Directory.Delete(_dir, recursive: true);
      }
      catch (IOException)
      {
      }
   }
}
