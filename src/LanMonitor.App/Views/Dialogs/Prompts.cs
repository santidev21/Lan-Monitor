using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views.Dialogs;

public static class Prompts
{
    /// <summary>Pide un texto. Devuelve null si se cancela.</summary>
    public static string? Input(IApplication app, string title, string label, string initial = "")
    {
        var d = new Dialog { Title = title, Width = Math.Min(60, Math.Max(30, app.Screen.Width - 4)) };
        var lbl = new Label { Text = label, X = 1, Y = 0 };
        var tf = new TextField { Text = initial, X = 1, Y = 1, Width = Dim.Fill(1) };
        d.Add(lbl, tf);
        d.AddButton(new Button { Text = "Cancelar" });
        d.AddButton(new Button { Text = "OK" });
        app.Run(d);
        var ok = d.Result == 1;
        var text = tf.Text ?? "";
        d.Dispose();
        return ok ? text : null;
    }

    public static int Choose(IApplication app, string title, string message, params string[] buttons)
        => MessageBox.Query(app, title, message, buttons) ?? -1;

    public static void Info(IApplication app, string title, string message) => MessageBox.Query(app, title, message, "OK");
}
