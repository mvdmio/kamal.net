using Kamal.Secrets;

namespace Kamal.Tests.Secrets;

public class ShellwordsTests
{
   [Fact]
   public void SplitsOnWhitespace()
   {
      Assert.Equal(new[] { "a", "b", "c" }, Shellwords.Split("a  b\tc"));
   }

   [Fact]
   public void HandlesQuotesAndEscapes()
   {
      Assert.Equal(
         new[] { "here", "are", "four words", "of one", "and", "a sentence" },
         Shellwords.Split("here are \"four words\" 'of one' and a\\ sentence"));
   }

   [Fact]
   public void UnescapesInsideDoubleQuotes()
   {
      Assert.Equal(new[] { "say \"hi\" $now" }, Shellwords.Split("\"say \\\"hi\\\" \\$now\""));
   }

   [Fact]
   public void KeepsNonSpecialEscapesInsideDoubleQuotes()
   {
      Assert.Equal(new[] { @"a\nb" }, Shellwords.Split("\"a\\nb\""));
   }

   [Fact]
   public void ConcatenatesAdjacentTokens()
   {
      Assert.Equal(new[] { "ab cd" }, Shellwords.Split("a\"b c\"d"));
   }

   [Fact]
   public void ThrowsOnUnmatchedQuote()
   {
      Assert.Throws<ArgumentException>(() => Shellwords.Split("a 'unclosed"));
      Assert.Throws<ArgumentException>(() => Shellwords.Split("a \"unclosed"));
   }

   [Fact]
   public void EscapesUnsafeCharacters()
   {
      Assert.Equal("''", Shellwords.Escape(""));
      Assert.Equal("simple-value_1.0,ok:+/@", Shellwords.Escape("simple-value_1.0,ok:+/@"));
      Assert.Equal("a\\ b\\'c\\\"d\\$e", Shellwords.Escape("a b'c\"d$e"));
      Assert.Equal("a'\n'b", Shellwords.Escape("a\nb"));
   }
}
