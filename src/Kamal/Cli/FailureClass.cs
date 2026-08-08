using System.Net.Sockets;
using System.Reflection;
using Kamal.Execution;
using Renci.SshNet.Common;

namespace Kamal.Cli;

/// <summary>
/// Public failure class contract: why a run stopped. Each class maps to a stable process exit
/// code and a greppable log marker (<c>kamal.failure_class=…</c>). Distinct from
/// <see cref="DeployPhase"/> markers even when names overlap (e.g. connect).
/// </summary>
public enum FailureClass
{
   Generic,
   Connect,
   Auth,
   Build,
   Healthcheck,
   Lock
}

/// <summary>
/// Named deploy phases logged as <c>kamal.phase=…</c>, separate from failure classes.
/// </summary>
public static class DeployPhase
{
   public const string Connect = "connect";
   public const string Build = "build";
   public const string Boot = "boot";

   /// <summary>Greppable phase marker line, e.g. <c>kamal.phase=build</c>.</summary>
   public static string Marker(string phase) => $"kamal.phase={phase}";

   /// <summary>Writes a greppable phase marker to stdout (always, not gated by verbosity).</summary>
   public static void Emit(string phase) => Console.WriteLine(Marker(phase));
}

/// <summary>Exit codes, log markers, and exception classification for <see cref="FailureClass"/>.</summary>
public static class FailureClasses
{
   public const int ExitGeneric = 1;
   public const int ExitConnect = 10;
   public const int ExitAuth = 11;
   public const int ExitBuild = 20;
   public const int ExitHealthcheck = 30;
   public const int ExitLock = 40;

   /// <summary>
   /// Single metadata table for name, exit code, and aggregation specificity.
   /// Adding a class here keeps Name/ExitCode/Specificity in lockstep.
   /// </summary>
   private static readonly Dictionary<FailureClass, FailureClassInfo> Info = new()
   {
      [FailureClass.Generic] = new("generic", ExitGeneric, Specificity: 0),
      [FailureClass.Connect] = new("connect", ExitConnect, Specificity: 10),
      [FailureClass.Auth] = new("auth", ExitAuth, Specificity: 50),
      [FailureClass.Build] = new("build", ExitBuild, Specificity: 20),
      [FailureClass.Healthcheck] = new("healthcheck", ExitHealthcheck, Specificity: 30),
      [FailureClass.Lock] = new("lock", ExitLock, Specificity: 40)
   };

   private readonly record struct FailureClassInfo(string Name, int ExitCode, int Specificity);

   private static FailureClassInfo Meta(FailureClass failureClass) =>
      Info.TryGetValue(failureClass, out var info) ? info : Info[FailureClass.Generic];

   /// <summary>Stable lowercase name used in markers and docs.</summary>
   public static string Name(FailureClass failureClass) => Meta(failureClass).Name;

   public static int ExitCode(FailureClass failureClass) => Meta(failureClass).ExitCode;

   /// <summary>Greppable failure-class marker line, e.g. <c>kamal.failure_class=lock</c>.</summary>
   public static string Marker(FailureClass failureClass) => $"kamal.failure_class={Name(failureClass)}";

   /// <summary>Writes a greppable failure-class marker to stdout.</summary>
   public static void Emit(FailureClass failureClass) => Console.WriteLine(Marker(failureClass));

   /// <summary>
   /// Maps an exception tree to a failure class. Walks inners, <see cref="MultipleExecuteError"/>
   /// children, and SSH.NET / transport types; falls back to message heuristics on
   /// <see cref="ExecuteError"/> when typed inners are missing.
   /// </summary>
   public static FailureClass Classify(Exception exception)
   {
      var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
      return ClassifyCore(exception, seen) ?? FailureClass.Generic;
   }

   private static FailureClass? ClassifyCore(Exception? exception, HashSet<Exception> seen)
   {
      while (exception is not null && seen.Add(exception))
      {
         switch (exception)
         {
            case BuildError:
               return FailureClass.Build;
            case LockError:
               return FailureClass.Lock;
            case HealthcheckError:
            case BootError:
               return FailureClass.Healthcheck;
            case AuthError:
            case SshAuthenticationException:
            case SshPassPhraseNullOrEmptyException:
               return FailureClass.Auth;
            case SshConnectionException:
            case SshOperationTimeoutException:
            case SocketException:
            case TimeoutException:
               return FailureClass.Connect;
            case MultipleExecuteError multiple:
            {
               FailureClass? best = null;

               foreach (var error in multiple.Errors)
               {
                  var classified = ClassifyCore(error, seen);

                  if (classified is { } candidate && candidate != FailureClass.Generic)
                  {
                     if (best is null || Specificity(candidate) > Specificity(best.Value))
                        best = candidate;
                  }
               }

               if (best is not null)
                  return best;

               break;
            }
            case AggregateException aggregate:
            {
               FailureClass? best = null;

               foreach (var inner in aggregate.InnerExceptions)
               {
                  var classified = ClassifyCore(inner, seen);

                  if (classified is { } candidate && candidate != FailureClass.Generic)
                  {
                     if (best is null || Specificity(candidate) > Specificity(best.Value))
                        best = candidate;
                  }
               }

               if (best is not null)
                  return best;

               break;
            }
            case TargetInvocationException { InnerException: { } inner }:
               exception = inner;
               continue;
            case ExecuteError execute:
            {
               // Classify inners here and return — do not rely on the while-loop walk, which
               // would skip inners already added to `seen` by the recursive call.
               var deeper = ClassifyCore(execute.InnerException, seen);

               if (deeper is not null && deeper != FailureClass.Generic)
                  return deeper;

               if (LooksLikeAuth(execute))
                  return FailureClass.Auth;

               if (LooksLikeConnect(execute))
                  return FailureClass.Connect;

               break;
            }
         }

         exception = exception.InnerException;
      }

      return null;
   }

   /// <summary>Higher wins when aggregating multi-host failures (auth over connect, etc.).</summary>
   private static int Specificity(FailureClass failureClass) => Meta(failureClass).Specificity;

   private static bool LooksLikeAuth(ExecuteError error)
   {
      var text = Combined(error);

      return Contains(text, "Permission denied (publickey")
         || Contains(text, "Authentication failed")
         || Contains(text, "SSH authentication failed")
         || Contains(text, "No supported authentication methods")
         || Contains(text, "Too many authentication failures")
         || Contains(text, "Permission denied (keyboard-interactive")
         || Contains(text, "Permission denied (password");
   }

   private static bool LooksLikeConnect(ExecuteError error)
   {
      var text = Combined(error);

      return Contains(text, "Could not establish a connected SSH session")
         || Contains(text, "SSH connection failed")
         || Contains(text, "Connection refused")
         || Contains(text, "Connection timed out")
         || Contains(text, "Connection reset")
         || Contains(text, "No route to host")
         || Contains(text, "Network is unreachable")
         || Contains(text, "Name or service not known")
         || Contains(text, "Temporary failure in name resolution")
         || Contains(text, "No such host is known")
         || Contains(text, "actively refused")
         || Contains(text, "timed out");
   }

   private static string Combined(ExecuteError error) => $"{error.Message}\n{error.Stderr}";

   private static bool Contains(string text, string fragment) =>
      text.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
