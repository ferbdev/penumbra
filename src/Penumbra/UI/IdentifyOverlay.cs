using System.Drawing;
using System.Windows.Forms;
using Penumbra.Monitors;
using Timer = System.Windows.Forms.Timer;

namespace Penumbra.UI;

// Em WinForms porque posiciona em pixels físicos, sem converter DPI por monitor.
public static class IdentifyOverlay
{
    public static void Show(IEnumerable<MonitorInfo> monitors)
    {
        var number = 1;
        foreach (var monitor in monitors)
        {
            var badge = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                Opacity = 0.88,
                BackColor = Color.FromArgb(20, 24, 32),
                Bounds = new Rectangle(monitor.Bounds.Left + 40, monitor.Bounds.Top + 40, 360, 170)
            };
            badge.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = $"{number++}\n{monitor.DisplayLabel}"
            });
            badge.Show();

            var timer = new Timer { Interval = 3000 };
            timer.Tick += (_, _) =>
            {
                timer.Dispose();
                badge.Close();
            };
            timer.Start();
        }
    }
}
