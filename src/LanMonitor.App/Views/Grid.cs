using System.Data;
using System.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LanMonitor.App.Views;

/// <summary>TableView de solo lectura con reconstrucción de datos conservando la fila seleccionada.</summary>
public class Grid : TableView
{
    public Grid()
    {
        Width = Dim.Fill(); Height = Dim.Fill();
        FullRowSelect = true; MultiSelect = false;
        Style.AlwaysShowHeaders = true;
        Style.ExpandLastColumn = true;
        Style.ShowVerticalCellLines = false;
        Style.ShowVerticalHeaderLines = false;
    }

    public int SelectedRow => Value?.SelectedCell.Y ?? -1;

    public void SetData(string[] headers, IReadOnlyList<string[]> rows)
    {
        int sel = Math.Max(0, SelectedRow);
        var dt = new DataTable();
        foreach (var h in headers) dt.Columns.Add(h, typeof(string));
        foreach (var r in rows) dt.Rows.Add(r.Cast<object>().ToArray());
        Table = new DataTableSource(dt);
        if (rows.Count > 0) Value = new TableSelection(new Point(0, Math.Min(sel, rows.Count - 1)));
        SetNeedsDraw();
    }
}
