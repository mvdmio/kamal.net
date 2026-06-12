using System.Text;

namespace Kamal.Secrets;

/// <summary>
/// Port of Ruby's <c>Shellwords</c> module (<c>shellescape</c> / <c>shellsplit</c>),
/// which the secrets adapters use to build and parse POSIX shell command lines.
/// </summary>
public static class Shellwords
{
   /// <summary>
   /// Ruby's <c>String#shellescape</c>: backslash-escapes every character outside
   /// <c>[A-Za-z0-9_\-.,:+/@\n]</c> and wraps newlines in single quotes.
   /// </summary>
   public static string Escape(string str)
   {
      if (str.Length == 0)
         return "''";

      var sb = new StringBuilder(str.Length);
      foreach (var c in str)
      {
         if (c == '\n')
            sb.Append("'\n'");
         else if (IsSafe(c))
            sb.Append(c);
         else
            sb.Append('\\').Append(c);
      }

      return sb.ToString();
   }

   /// <summary>
   /// Ruby's <c>Shellwords.shellsplit</c>: splits a command line into words the way
   /// a POSIX shell would, honoring single quotes, double quotes and backslash escapes.
   /// Throws <see cref="ArgumentException"/> on an unmatched quote.
   /// </summary>
   public static string[] Split(string line)
   {
      var words = new List<string>();
      var field = new StringBuilder();
      var hasField = false;
      var i = 0;

      void PushField()
      {
         words.Add(field.ToString());
         field.Clear();
         hasField = false;
      }

      while (i < line.Length)
      {
         var c = line[i];

         if (char.IsWhiteSpace(c))
         {
            if (hasField)
               PushField();
            i++;
            continue;
         }

         hasField = true;

         if (c == '\'')
         {
            var close = line.IndexOf('\'', i + 1);
            if (close < 0)
               throw new ArgumentException($"Unmatched quote: {RubyString.Inspect(line)}");
            field.Append(line, i + 1, close - i - 1);
            i = close + 1;
         }
         else if (c == '"')
         {
            i++;
            var closed = false;
            while (i < line.Length)
            {
               var d = line[i];
               if (d == '"')
               {
                  closed = true;
                  i++;
                  break;
               }

               if (d == '\\' && i + 1 < line.Length)
               {
                  var next = line[i + 1];
                  // Inside double quotes only \$ \` \" \\ and \<newline> are unescaped.
                  if (next is '$' or '`' or '"' or '\\' or '\n')
                     field.Append(next);
                  else
                     field.Append(d).Append(next);
                  i += 2;
               }
               else
               {
                  field.Append(d);
                  i++;
               }
            }

            if (!closed)
               throw new ArgumentException($"Unmatched quote: {RubyString.Inspect(line)}");
         }
         else if (c == '\\')
         {
            // Backslash escapes the next character (or stands alone at end of string).
            if (i + 1 < line.Length)
            {
               field.Append(line[i + 1]);
               i += 2;
            }
            else
            {
               i++;
            }
         }
         else
         {
            var start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '\\' && line[i] != '\'' && line[i] != '"')
               i++;
            field.Append(line, start, i - start);
         }
      }

      if (hasField)
         PushField();

      return words.ToArray();
   }

   private static bool IsSafe(char c)
   {
      return c is >= 'A' and <= 'Z'
          or >= 'a' and <= 'z'
          or >= '0' and <= '9'
          or '_' or '-' or '.' or ',' or ':' or '+' or '/' or '@' or '\n';
   }
}
