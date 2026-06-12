using System.Text.Json.Nodes;

namespace Kamal.Secrets.Adapters;

/// <summary>
/// Passbolt adapter, shelling out to the <c>passbolt</c> CLI.
/// Port of <c>Kamal::Secrets::Adapters::Passbolt</c>, including its folder resolution:
/// secrets may be nested in folders (<c>folder/subfolder/SECRET</c>), which are resolved
/// to folder ids via <c>passbolt list folders</c> before fetching resources.
/// </summary>
public class Passbolt : AdapterBase
{
   public override bool RequiresAccount => false;

   protected override string? Login(string? account)
   {
      if (!Run("passbolt verify").Success)
         throw new InvalidOperationException("Failed to login to Passbolt");

      return null;
   }

   protected override Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session)
   {
      var prefixedSecrets = PrefixedSecrets(secrets, from);
      if (prefixedSecrets.Count == 0)
         throw new ArgumentException("No secrets given to fetch");

      var secretNames = prefixedSecrets.Select(secret => secret.Split('/').Last()).ToList();
      var folders = SecretsGetFolders(prefixedSecrets);

      // Build filter conditions for each secret with its corresponding folder.
      var filterConditions = new List<string>();
      foreach (var secret in prefixedSecrets)
      {
         var parts = secret.Split('/');
         var secretName = parts.Last();

         if (parts.Length > 1)
         {
            // Get the folder path without the secret name and find the most nested folder for it.
            var folderPath = parts[..^1];
            JsonNode? currentFolder = null;
            var currentPath = new List<string>();

            foreach (var folderName in folderPath)
            {
               currentPath.Add(folderName);
               var joinedPath = string.Join("/", currentPath);
               var matchingFolder = folders.FirstOrDefault(folder => GetFolderPath(folder, folders) == joinedPath);
               if (matchingFolder != null)
                  currentFolder = matchingFolder;
            }

            if (currentFolder != null)
               filterConditions.Add($"(Name == {InspectEscaped(secretName)} && FolderParentID == {InspectEscaped(Id(currentFolder))})");
         }
         else
         {
            // Root level secrets (no folders).
            filterConditions.Add($"Name == {InspectEscaped(secretName)}");
         }
      }

      var filterCondition = filterConditions.Count > 0 ? $"--filter '{string.Join(" || ", filterConditions)}'" : "";
      var folderArgs = string.Join(" ", folders.Select(folder => $"--folder {Shellwords.Escape(Id(folder))}"));

      var result = Run($"passbolt list resources {filterCondition} {folderArgs} --json");
      if (!result.Success)
         throw new InvalidOperationException($"Could not read {RubyString.InspectList(prefixedSecrets)} from Passbolt");

      var items = JsonNode.Parse(result.Stdout)!.AsArray();
      var foundNames = items.Select(item => item!["name"]!.GetValue<string>()).ToList();
      var missingSecrets = secretNames.Where(name => !foundNames.Contains(name)).ToList();
      if (missingSecrets.Count > 0)
         throw new InvalidOperationException($"Could not find the following secrets in Passbolt: {string.Join(", ", missingSecrets)}");

      var results = new Dictionary<string, string>();
      foreach (var item in items)
         results[item!["name"]!.GetValue<string>()] = item["password"]?.GetValue<string>() ?? "";

      return results;
   }

   protected override void CheckDependencies()
   {
      if (!Run("passbolt --version 2> /dev/null").Success)
         throw new InvalidOperationException("Passbolt CLI is not installed");
   }

   private List<JsonNode> SecretsGetFolders(IReadOnlyList<string> secrets)
   {
      // Extract all folder paths (both parent and nested).
      var folderPaths = secrets
         .Where(secret => secret.Contains('/'))
         .Select(secret => secret.Split('/')[..^1])
         .DistinctBy(path => string.Join("/", path))
         .ToList();

      if (folderPaths.Count == 0)
         return new List<JsonNode>();

      var allFolders = new List<JsonNode>();

      // First get all top-level folders.
      var parentFolders = folderPaths.Select(path => path[0]).Distinct().ToList();
      var filterCondition = $"--filter '{string.Join(" || ", parentFolders.Select(name => $"Name == {InspectEscaped(name)}"))}'";

      var fetchFolders = Run($"passbolt list folders {filterCondition} --json");
      if (!fetchFolders.Success)
         throw new InvalidOperationException("Could not read folders from Passbolt");

      var parentFolderItems = JsonNode.Parse(fetchFolders.Stdout)!.AsArray().Select(node => node!).ToList();
      allFolders.AddRange(parentFolderItems);

      // Get nested folders for each parent.
      foreach (var path in folderPaths)
      {
         if (path.Length <= 1)
            continue; // Skip non-nested folders.

         var parent = path[0];
         var parentFolder = parentFolderItems.FirstOrDefault(folder => folder["name"]?.GetValue<string>() == parent);
         if (parentFolder == null)
            continue;

         // For each nested level, get the folders using the parent's id.
         var currentParent = parentFolder;
         foreach (var folderName in path[1..])
         {
            var nestedFilter = $"--filter 'Name == {InspectEscaped(folderName)} && FolderParentID == {InspectEscaped(Id(currentParent))}'";
            var fetchNested = Run($"passbolt list folders {nestedFilter} --json");
            if (!fetchNested.Success)
               continue;

            var nestedFolders = JsonNode.Parse(fetchNested.Stdout)!.AsArray().Select(node => node!).ToList();
            if (nestedFolders.Count == 0)
               break;

            allFolders.AddRange(nestedFolders);
            currentParent = nestedFolders[0];
         }
      }

      // Check if we found all required folders.
      var foundPaths = allFolders.Select(folder => GetFolderPath(folder, allFolders)).ToList();
      var missingPaths = folderPaths.Select(path => string.Join("/", path)).Where(path => !foundPaths.Contains(path)).ToList();
      if (missingPaths.Count > 0)
         throw new InvalidOperationException($"Could not find the following folders in Passbolt: {string.Join(", ", missingPaths)}");

      return allFolders;
   }

   private static string GetFolderPath(JsonNode folder, List<JsonNode> allFolders, List<string>? path = null)
   {
      path ??= new List<string>();
      path.Insert(0, folder["name"]?.GetValue<string>() ?? "");

      var parentId = folder["folder_parent_id"]?.GetValue<string>() ?? "";
      if (parentId.Length == 0)
         return string.Join("/", path);

      var parent = allFolders.FirstOrDefault(f => f["id"]?.GetValue<string>() == parentId);
      if (parent == null)
         return string.Join("/", path);

      return GetFolderPath(parent, allFolders, path);
   }

   private static string Id(JsonNode folder)
   {
      return folder["id"]?.GetValue<string>() ?? "";
   }

   private static string InspectEscaped(string value)
   {
      // Ruby: value.shellescape.inspect
      return RubyString.Inspect(Shellwords.Escape(value));
   }
}
