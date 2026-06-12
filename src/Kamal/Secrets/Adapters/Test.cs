namespace Kamal.Secrets.Adapters;

/// <summary>
/// Test adapter: returns each secret reversed, with LPAREN/RPAREN placeholders replaced by parentheses.
/// Port of <c>Kamal::Secrets::Adapters::Test</c>.
/// </summary>
public class Test : AdapterBase
{
   protected override string? Login(string? account)
   {
      return null;
   }

   protected override Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session)
   {
      return PrefixedSecrets(secrets, from).ToDictionary(
         secret => secret,
         secret =>
         {
            var chars = secret.Replace("LPAREN", "(").Replace("RPAREN", ")").ToCharArray();
            Array.Reverse(chars);
            return new string(chars);
         });
   }

   protected override void CheckDependencies()
   {
      // no op
   }
}
