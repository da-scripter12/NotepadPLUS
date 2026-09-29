namespace NotepadPLUS;

public partial class Form1 : Form
{
    public Form1()
    {
        InitializeComponent();
        this.Text = "Notepad+";
        this.Icon = new Icon(Path.Combine(AppContext.BaseDirectory, "icon.ico");
    }
}
