namespace LanMonitor.App;

/// <summary>Recorte de texto adaptativo al ancho disponible.</summary>
public static class Txt
{
    public static string Trunc(string s, int n) => n <= 0 ? "" : s.Length <= n ? s : n == 1 ? "…" : s[..(n - 1)] + "…";

    public static string FitLines(string s, int n) => string.Join("\n", s.Split('\n').Select(l => Trunc(l.TrimEnd('\r'), n)));

    public static string Home(string path)
    {
        var h = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(h) ? "~" + path[h.Length..] : path;
    }
}
