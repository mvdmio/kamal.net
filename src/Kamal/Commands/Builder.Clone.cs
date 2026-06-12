using Kamal.Secrets;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::Builder::Clone</c>: git clone build contexts.</summary>
public sealed partial class Builder
{
   public object[] Clone()
   {
      return Git(["clone", EscapedRoot, "--recurse-submodules"], path: Shellwords.Escape(Config.Builder.CloneDirectory));
   }

   public object[][] CloneResetSteps()
   {
      return
      [
         Git(["remote", "set-url", "origin", EscapedRoot], path: EscapedBuildDirectory),
         Git(["fetch", "origin"], path: EscapedBuildDirectory),
         Git(["reset", "--hard", Utils.Git.Revision], path: EscapedBuildDirectory),
         Git(["clean", "-fdx"], path: EscapedBuildDirectory),
         Git(["submodule", "update", "--init"], path: EscapedBuildDirectory)
      ];
   }

   public object[] CloneStatus() => Git(["status", "--porcelain"], path: EscapedBuildDirectory);

   public object[] CloneRevision() => Git(["rev-parse", "HEAD"], path: EscapedBuildDirectory);

   public string EscapedRoot => Shellwords.Escape(Utils.Git.Root);

   public string EscapedBuildDirectory => Shellwords.Escape(Config.Builder.BuildDirectory);
}
