using System.Diagnostics;

namespace LanMonitor.Core.Events;

public static class Notifier
{
    public static void Send(string title, string body)
    {
        try
        {
            var psi = new ProcessStartInfo("notify-send") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            psi.ArgumentList.Add("-a"); psi.ArgumentList.Add("Lan-Monitor");
            psi.ArgumentList.Add(title); psi.ArgumentList.Add(body);
            Process.Start(psi);
        }
        catch { /* notify-send no instalado */ }
    }
}
