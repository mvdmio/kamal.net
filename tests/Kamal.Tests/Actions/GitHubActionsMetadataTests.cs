namespace Kamal.Tests.Actions;

/// <summary>
/// Lightweight validation of composite Action metadata under <c>actions/</c>
/// (inputs/structure). Full end-to-end deploy on real hosts is out of band.
/// </summary>
public class GitHubActionsMetadataTests
{
   [Fact]
   public void SetupActionDeclaresInstallPathAndSshInputs()
   {
      var yaml = ReadActionYml("setup");

      Assert.Contains("name: Setup Kamal.NET", yaml);
      Assert.Contains("using: composite", yaml);
      Assert.Contains("version:", yaml);
      Assert.Contains("dotnet-version:", yaml);
      Assert.Contains("ssh-private-key:", yaml);
      Assert.Contains("skip-dotnet-setup:", yaml);
      Assert.Contains("install.sh", yaml);
      Assert.Contains("configure-ssh.sh", yaml);
      Assert.Contains("actions/setup-dotnet@v6", yaml);
   }

   [Fact]
   public void DeployActionComposesSetupThenKamalDeploy()
   {
      var yaml = ReadActionYml("deploy");

      Assert.Contains("name: Deploy with Kamal.NET", yaml);
      Assert.Contains("using: composite", yaml);
      Assert.Contains("destination:", yaml);
      Assert.Contains("args:", yaml);
      Assert.Contains("retry:", yaml);
      Assert.Contains("working-directory:", yaml);
      Assert.Contains("ssh-private-key:", yaml);
      Assert.Contains("version:", yaml);
      Assert.Contains("dotnet-version:", yaml);
      Assert.Contains("skip-dotnet-setup:", yaml);
      // Same setup path as actions/setup (shared scripts — nested composite uses is unreliable by tag).
      Assert.Contains("../setup/install.sh", yaml);
      Assert.Contains("../setup/configure-ssh.sh", yaml);
      Assert.Contains("deploy.sh", yaml);
      Assert.Contains("reuses actions/setup scripts", yaml);
   }

   [Fact]
   public void ActionScriptsExistAndAreNonEmpty()
   {
      foreach (var relative in new[]
               {
                  Path.Combine("actions", "setup", "install.sh"),
                  Path.Combine("actions", "setup", "configure-ssh.sh"),
                  Path.Combine("actions", "deploy", "deploy.sh")
               })
      {
         var path = Path.Combine(RepoRoot, relative);
         Assert.True(File.Exists(path), $"missing {relative}");
         Assert.True(new FileInfo(path).Length > 0, $"empty {relative}");
         Assert.StartsWith("#!/usr/bin/env bash", File.ReadAllText(path));
      }
   }

   private static string ReadActionYml(string actionName)
   {
      var path = Path.Combine(RepoRoot, "actions", actionName, "action.yml");
      Assert.True(File.Exists(path), $"missing {path}");
      return File.ReadAllText(path);
   }

   private static string RepoRoot
   {
      get
      {
         var dir = new DirectoryInfo(AppContext.BaseDirectory);

         while (dir is not null)
         {
            if (File.Exists(Path.Combine(dir.FullName, "Kamal.slnx"))
                && Directory.Exists(Path.Combine(dir.FullName, "actions")))
               return dir.FullName;

            dir = dir.Parent;
         }

         throw new InvalidOperationException(
            "Could not locate repository root (Kamal.slnx + actions/) from " + AppContext.BaseDirectory);
      }
   }
}
