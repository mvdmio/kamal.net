using Kamal.Secrets.Dotenv;

namespace Kamal.Tests.Secrets;

[Collection("inline-command-substitution")]
public class InlineCommandSubstitutionTests : IDisposable
{
   public void Dispose()
   {
      InlineCommandSubstitution.KamalSecretsCommandHandler = null;
      InlineCommandSubstitution.CommandExecutor = null;
   }

   [Fact]
   public void InlinesKamalSecretsCommands()
   {
      string[]? receivedArgs = null;
      InlineCommandSubstitution.KamalSecretsCommandHandler = args =>
      {
         receivedArgs = args;
         return "results";
      };

      var substituted = InlineCommandSubstitution.Substitute("FOO=$(kamal secrets fetch ...)", null);

      Assert.Equal("FOO=results", substituted);
      Assert.Equal(new[] { "secrets", "fetch", "...", "--inline" }, receivedArgs);
   }

   [Fact]
   public void ExecutesOtherCommands()
   {
      InlineCommandSubstitution.CommandExecutor = command =>
      {
         Assert.Equal("blah", command);
         return "results";
      };

      var substituted = InlineCommandSubstitution.Substitute("FOO=$(blah)", null);

      Assert.Equal("FOO=results", substituted);
   }

   [Fact]
   public void HandlesEscapedParenthesesInCommandArguments()
   {
      var commandWithEscapedParens = "kamal secrets extract KEY1 \\{\\\"KEY1\\\":\\\"pass\\)word\\\"\\}";

      string[]? receivedArgs = null;
      InlineCommandSubstitution.KamalSecretsCommandHandler = args =>
      {
         receivedArgs = args;
         return "pass)word";
      };

      var substituted = InlineCommandSubstitution.Substitute($"KEY1=$({commandWithEscapedParens})", null);

      Assert.Equal("KEY1=pass)word", substituted);
      Assert.NotNull(receivedArgs);
      Assert.Equal(new[] { "secrets", "extract", "KEY1" }, receivedArgs.Take(3));
      Assert.Equal("{\"KEY1\":\"pass)word\"}", receivedArgs[3]); // shellsplit should unescape
      Assert.Equal("--inline", receivedArgs[4]);
   }

   [Fact]
   public void EscapedCommandSubstitutionIsNotExecuted()
   {
      InlineCommandSubstitution.CommandExecutor = _ => throw new InvalidOperationException("should not execute");

      var substituted = InlineCommandSubstitution.Substitute("FOO=\\$(blah)", null);

      Assert.Equal("FOO=$(blah)", substituted);
   }

   [Fact]
   public void NestedParenthesesAreKeptInTheCommand()
   {
      InlineCommandSubstitution.CommandExecutor = command =>
      {
         Assert.Equal("echo $(echo hi)", command);
         return "hi\n";
      };

      var substituted = InlineCommandSubstitution.Substitute("FOO=$(echo $(echo hi))", null);

      Assert.Equal("FOO=hi", substituted);
   }

   [Fact]
   public void SubstitutesVariablesInsideTheCommand()
   {
      InlineCommandSubstitution.CommandExecutor = command =>
      {
         Assert.Equal("echo hello", command);
         return "hello";
      };

      var substituted = InlineCommandSubstitution.Substitute("FOO=$(echo $GREETING)", new Dictionary<string, string> { ["GREETING"] = "hello" });

      Assert.Equal("FOO=hello", substituted);
   }

   [Fact]
   public void UnbalancedParenthesesAreLeftVerbatim()
   {
      InlineCommandSubstitution.CommandExecutor = _ => throw new InvalidOperationException("should not execute");

      Assert.Equal("FOO=$(blah", InlineCommandSubstitution.Substitute("FOO=$(blah", null));
      Assert.Equal("FOO=$()", InlineCommandSubstitution.Substitute("FOO=$()", null));
   }
}
