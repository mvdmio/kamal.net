using Kamal.Secrets.Dotenv;

namespace Kamal.Tests.Secrets;

[Collection("inline-command-substitution")]
public class DotenvParserTests
{
   [Fact]
   public void ParsesBasicKeyValuePairs()
   {
      var result = DotenvParser.Parse("FOO=bar\nBAZ=qux");

      Assert.Equal(new Dictionary<string, string> { ["FOO"] = "bar", ["BAZ"] = "qux" }, result);
   }

   [Fact]
   public void IgnoresCommentsAndBlankLines()
   {
      var result = DotenvParser.Parse("# a comment\n\nFOO=bar # trailing comment\n  # indented comment\n");

      Assert.Equal(new Dictionary<string, string> { ["FOO"] = "bar" }, result);
   }

   [Fact]
   public void SupportsExportPrefixAndColonSeparator()
   {
      var result = DotenvParser.Parse("export FOO=bar\nBAZ: qux");

      Assert.Equal(new Dictionary<string, string> { ["FOO"] = "bar", ["BAZ"] = "qux" }, result);
   }

   [Fact]
   public void ExportOfDefinedVariableIsAllowed()
   {
      var result = DotenvParser.Parse("FOO=bar\nexport FOO");

      Assert.Equal(new Dictionary<string, string> { ["FOO"] = "bar" }, result);
   }

   [Fact]
   public void ExportOfUnsetVariableThrows()
   {
      var error = Assert.Throws<DotenvFormatException>(() => DotenvParser.Parse("export MISSING"));

      Assert.Equal("Line \"export MISSING\" has an unset variable", error.Message);
   }

   [Fact]
   public void SingleQuotedValuesAreLiteral()
   {
      var result = DotenvParser.Parse("FOO='single $VAR \\n'");

      Assert.Equal("single $VAR \\n", result["FOO"]);
   }

   [Fact]
   public void DoubleQuotedValuesKeepPadding()
   {
      var result = DotenvParser.Parse("M=\"  padded  \"\nL= spaced ");

      Assert.Equal("  padded  ", result["M"]);
      Assert.Equal("spaced", result["L"]);
   }

   [Fact]
   public void DoubleQuotedBackslashNStaysLiteralByDefault()
   {
      // dotenv 3.x default (non-legacy) line break mode keeps \n literal.
      var result = DotenvParser.Parse("F=\"x\\ny\"");

      Assert.Equal("x\\ny", result["F"]);
   }

   [Fact]
   public void LegacyLinebreakModeExpandsNewlines()
   {
      var result = DotenvParser.Parse("DOTENV_LINEBREAK_MODE=legacy\nF=\"x\\ny\"");

      Assert.Equal("x\ny", result["F"]);
   }

   [Fact]
   public void MultilineDoubleQuotedValues()
   {
      var result = DotenvParser.Parse("E=\"multi\nline\"\nNEXT=1");

      Assert.Equal("multi\nline", result["E"]);
      Assert.Equal("1", result["NEXT"]);
   }

   [Fact]
   public void VariableSubstitution()
   {
      var result = DotenvParser.Parse("A=1\nB=$A\nC=${A}2\nD=\\$A");

      Assert.Equal("1", result["A"]);
      Assert.Equal("1", result["B"]);
      Assert.Equal("12", result["C"]);
      Assert.Equal("$A", result["D"]);
   }

   [Fact]
   public void UndefinedVariablesBecomeEmpty()
   {
      var result = DotenvParser.Parse("J=$KAMALNET_UNDEFINED_XYZ-suffix");

      Assert.Equal("-suffix", result["J"]);
   }

   [Fact]
   public void EnvironmentVariablesAreUsedForSubstitution()
   {
      Environment.SetEnvironmentVariable("KAMALNET_DOTENV_SUB", "ABC");

      try
      {
         var result = DotenvParser.Parse("B=$KAMALNET_DOTENV_SUB", overwrite: true);
         Assert.Equal("ABC", result["B"]);
      }
      finally
      {
         Environment.SetEnvironmentVariable("KAMALNET_DOTENV_SUB", null);
      }
   }

   [Fact]
   public void ParsedValuesWinOverEnvironmentInSubstitution()
   {
      Environment.SetEnvironmentVariable("KAMALNET_DOTENV_PRIO", "FROM_ENV");

      try
      {
         var result = DotenvParser.Parse("KAMALNET_DOTENV_PRIO=FROM_FILE\nB=$KAMALNET_DOTENV_PRIO", overwrite: true);
         Assert.Equal("FROM_FILE", result["B"]);
      }
      finally
      {
         Environment.SetEnvironmentVariable("KAMALNET_DOTENV_PRIO", null);
      }
   }

   [Fact]
   public void WithoutOverwriteEnvironmentValuesWin()
   {
      Environment.SetEnvironmentVariable("KAMALNET_DOTENV_KEEP", "FROM_ENV");

      try
      {
         Assert.Equal("FROM_ENV", DotenvParser.Parse("KAMALNET_DOTENV_KEEP=FROM_FILE")["KAMALNET_DOTENV_KEEP"]);
         Assert.Equal("FROM_FILE", DotenvParser.Parse("KAMALNET_DOTENV_KEEP=FROM_FILE", overwrite: true)["KAMALNET_DOTENV_KEEP"]);
      }
      finally
      {
         Environment.SetEnvironmentVariable("KAMALNET_DOTENV_KEEP", null);
      }
   }

   [Fact]
   public void UnescapesBackslashesInUnquotedAndDoubleQuotedValues()
   {
      var result = DotenvParser.Parse("I=a\\ b\\$c\nH=\"esc \\\" quote\"");

      Assert.Equal("a b$c", result["I"]);
      Assert.Equal("esc \" quote", result["H"]);
   }

   [Fact]
   public void CommandSubstitution()
   {
      var result = DotenvParser.Parse("SECRET=$(echo ABC)");

      Assert.Equal("ABC", result["SECRET"]);
   }

   [Fact]
   public void EscapedCommandSubstitutionIsLeftAlone()
   {
      var result = DotenvParser.Parse("N=\\$(echo hi)x");

      Assert.Equal("$(echo hi)x", result["N"]);
   }

   [Fact]
   public void InterpolationCanBeDisabled()
   {
      var result = DotenvParser.Parse("A=1\nB=$A\nC=$(echo hi)", interpolate: false);

      Assert.Equal("$A", result["B"]);
      Assert.Equal("$(echo hi)", result["C"]);
   }

   [Fact]
   public void BareKeysParseAsEmptyValues()
   {
      var result = DotenvParser.Parse("BARE\nK=");

      Assert.Equal("", result["BARE"]);
      Assert.Equal("", result["K"]);
   }

   [Fact]
   public void UnterminatedQuoteFallsBackToUnquotedParsing()
   {
      var result = DotenvParser.Parse("J='unterminated\nK=2");

      Assert.Equal("'unterminated", result["J"]);
      Assert.Equal("2", result["K"]);
   }

   [Fact]
   public void NormalizesWindowsLineEndings()
   {
      var result = DotenvParser.Parse("S=1\r\nT=2");

      Assert.Equal(new Dictionary<string, string> { ["S"] = "1", ["T"] = "2" }, result);
   }

   [Fact]
   public void LaterValuesOverwriteEarlierOnes()
   {
      var result = DotenvParser.Parse("FOO=1\nFOO=2");

      Assert.Equal("2", result["FOO"]);
   }
}
