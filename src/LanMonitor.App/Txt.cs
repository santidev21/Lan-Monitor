namespace LanMonitor.App;

/// <summary>Recorte de texto adaptativo al ancho disponible.</summary>
public static class Txt
{
    public static string Trunc(string s, int n) => n <= 0 ? "" : s.Length <= n ? s : n == 1 ? "…" : s[..(n - 1)] + "…";

    public static string FitLines(string s, int n) => string.Join("\n", s.Split('\n').Select(l => Trunc(l.TrimEnd('\r'), n)));

    /// <summary>Ajuste de línea por palabras; `indent` sangra las líneas de continuación.</summary>
    public static IEnumerable<string> Wrap(string s, int width, string indent = "")
    {
        width = Math.Max(10, width);
        var line = "";
        foreach (var word in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width) { yield return line; line = indent + word; }
            else line = line.Length == 0 ? word : line + " " + word;
        }
        if (line.Length > 0) yield return line;
    }

    public static string Home(string path)
    {
        var h = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(h) ? "~" + path[h.Length..] : path;
    }
}
