using Kamal.Execution;
using Kamal.Output;

namespace Kamal;

/// <summary>
/// The global commander instance, the .NET equivalent of Ruby's <c>KAMAL</c> constant
/// (<c>Kamal::Cli</c> works against this single instance).
/// </summary>
public static class KamalRuntime
{
   public static Commander Commander { get; private set; } = new();

   /// <summary>Replaces the global commander and restores the execution/output globals (for tests).</summary>
   public static void Reset()
   {
      Commander = new Commander();
      KamalOutput.Reset();
      Coordinator.Reset();
   }
}
