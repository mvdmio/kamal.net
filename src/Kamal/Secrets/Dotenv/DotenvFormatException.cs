namespace Kamal.Secrets.Dotenv;

/// <summary>
/// Raised when encountering a syntax error while parsing a .env file.
/// Port of Ruby dotenv's <c>Dotenv::FormatError</c>.
/// </summary>
public class DotenvFormatException : FormatException
{
   public DotenvFormatException(string message)
      : base(message)
   {
   }
}
