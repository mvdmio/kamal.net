using Kamal.Utils;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Utils;

/// <summary>Port of test/utils_test.rb.</summary>
public class KamalUtilsTests
{
   [Fact]
   public void Argumentize()
   {
      var args = KamalUtils.Argumentize("--label", new Cfg
      {
         ["foo"] = "`bar`",
         ["baz"] = "qux",
         ["quux"] = null,
         ["quuz"] = false
      });

      Assert.Equal(
         ["--label", "foo=\"\\`bar\\`\"", "--label", "baz=\"qux\"", "--label", "quux", "--label", "quuz=false"],
         args);
   }

   [Fact]
   public void ArgumentizeWithRedacted()
   {
      var args = KamalUtils.Argumentize("--label", new Cfg { ["foo"] = "bar" }, sensitive: true);

      var sensitive = Assert.IsType<Sensitive>(args.Last());
      Assert.Equal("foo=\"bar\"", sensitive.ToString());
      Assert.Equal("foo=[REDACTED]", sensitive.Redaction);
   }

   [Fact]
   public void Optionize()
   {
      Assert.Equal(
         ["--foo", "\"bar\"", "--baz", "\"qux\"", "--quux"],
         KamalUtils.Optionize(new Cfg { ["foo"] = "bar", ["baz"] = "qux", ["quux"] = true }));
   }

   [Fact]
   public void OptionizeWith()
   {
      Assert.Equal(
         ["--foo=\"bar\"", "--baz=\"qux\"", "--quux"],
         KamalUtils.Optionize(new Cfg { ["foo"] = "bar", ["baz"] = "qux", ["quux"] = true }, with: "="));
   }

   [Fact]
   public void NoRedactionFromToString()
   {
      Assert.Equal("secret", KamalUtils.MakeSensitive("secret").ToString());
   }

   [Fact]
   public void RedactFromInspect()
   {
      Assert.Equal("[REDACTED]", KamalUtils.MakeSensitive("secret").Inspect);
   }

   [Fact]
   public void SensitiveIsRedactable()
   {
      Assert.IsAssignableFrom<IRedactable>(KamalUtils.MakeSensitive("secret"));
   }

   [Fact]
   public void RedactedUnwrapsSensitiveValues()
   {
      Assert.Equal("[REDACTED]", KamalUtils.Redacted(KamalUtils.MakeSensitive("secret")));

      var redactedDict = (IDictionary<string, object?>)KamalUtils.Redacted(new Cfg
      {
         ["password"] = KamalUtils.MakeSensitive("secret"),
         ["plain"] = "visible"
      })!;
      Assert.Equal("[REDACTED]", redactedDict["password"]);
      Assert.Equal("visible", redactedDict["plain"]);

      var redactedList = (List<object?>)KamalUtils.Redacted(new List<object?> { KamalUtils.MakeSensitive("secret"), "visible" })!;
      Assert.Equal(["[REDACTED]", "visible"], redactedList);
   }

   [Fact]
   public void EscapeShellValue()
   {
      Assert.Equal("\"foo\"", KamalUtils.EscapeShellValue("foo"));
      Assert.Equal("\"\\`foo\\`\"", KamalUtils.EscapeShellValue("`foo`"));

      Assert.Equal("\"${PWD}\"", KamalUtils.EscapeShellValue("${PWD}"));
      Assert.Equal("\"${cat /etc/hostname}\"", KamalUtils.EscapeShellValue("${cat /etc/hostname}"));
      Assert.Equal("\"\\${PWD]\"", KamalUtils.EscapeShellValue("${PWD]"));
      Assert.Equal("\"\\$(PWD)\"", KamalUtils.EscapeShellValue("$(PWD)"));
      Assert.Equal("\"\\$PWD\"", KamalUtils.EscapeShellValue("$PWD"));

      Assert.Equal(
         "\"^(https?://)www.example.com/(.*)\\$\"",
         KamalUtils.EscapeShellValue("^(https?://)www.example.com/(.*)$"));
      Assert.Equal(
         "\"https://example.com/\\$2\"",
         KamalUtils.EscapeShellValue("https://example.com/$2"));
   }

   [Fact]
   public void FilterSpecificItems()
   {
      Assert.Equal(["web1", "web2"], KamalUtils.FilterSpecificItems(["web*"], ["web1", "web2", "worker1"]));
      Assert.Equal(["web1"], KamalUtils.FilterSpecificItems(["web1"], ["web1", "web2"]));
      Assert.Equal(["web1", "worker1"], KamalUtils.FilterSpecificItems(["{web1,worker1}"], ["web1", "web2", "worker1"]));
      Assert.Empty(KamalUtils.FilterSpecificItems(["missing"], ["web1"]));

      // fnmatch negation: [!...] excludes the listed characters (POSIX glob), not matches them.
      Assert.Equal(["db1"], KamalUtils.FilterSpecificItems(["[!w]*"], ["web1", "db1"]));
   }

   [Fact]
   public void JoinCommands()
   {
      Assert.Equal("a b", KamalUtils.JoinCommands([" a ", "b\n"]));
   }

   [Fact]
   public void OlderVersion()
   {
      Assert.True(KamalUtils.OlderVersion("v0.8.0", "v0.9.2"));
      Assert.False(KamalUtils.OlderVersion("v0.9.2", "v0.9.2"));
      Assert.False(KamalUtils.OlderVersion("v1.0.0", "v0.9.2"));
      Assert.True(KamalUtils.OlderVersion("1.3.0", "2.11.0"));
      Assert.True(KamalUtils.OlderVersion("2.11.0", "10000.0.0"));
   }
}
