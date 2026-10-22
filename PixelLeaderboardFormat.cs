using System.Globalization;
using System.Text;

internal static class PixelLeaderboardFormat
{
    private static readonly CultureInfo ScoreCulture = CultureInfo.GetCultureInfo("pt-BR");

    internal static string Score(long pixels) => pixels.ToString("N0", ScoreCulture) + " px";

    internal static string Name(string value, int maxElements)
    {
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(value);
        var builder = new StringBuilder(maxElements + 2);
        int count = 0;
        while (elements.MoveNext())
        {
            if (count++ == maxElements) { builder.Append('…'); break; }
            builder.Append(elements.GetTextElement());
        }
        return builder.ToString().Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }
}
