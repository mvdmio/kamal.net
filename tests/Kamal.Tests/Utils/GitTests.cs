using Kamal.Tests.Configuration;
using Kamal.Utils;

namespace Kamal.Tests.Utils;

/// <summary>Port of test/git_test.rb.</summary>
[Collection("kamal-config")]
public class GitTests
{
   [Fact]
   public void UncommittedChangesExist()
   {
      var fake = new FakeGitRunner();
      fake.Outputs["status --porcelain"] = "M   file\n";

      using (new GitScope(fake))
         Assert.Equal("M   file", Git.UncommittedChanges);
   }

   [Fact]
   public void UncommittedChangesDoNotExist()
   {
      var fake = new FakeGitRunner();
      fake.Outputs["status --porcelain"] = "";

      using (new GitScope(fake))
         Assert.Equal("", Git.UncommittedChanges);
   }
}
