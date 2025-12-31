using System.Drawing;
using System.Windows.Forms;

public class Splash : Form
{
    private Label lbl;

    public Splash(string message)
    {
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Color.FromArgb(30, 30, 30);
        this.Size = new Size(400, 200);
        this.TopMost = true;

        lbl = new Label
        {
            Text = message,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        };


        this.Controls.Add(lbl);
    }

    public void UpdateMessage(string msg)
    {
        lbl.Text = msg;
        lbl.Refresh();
    }
}
