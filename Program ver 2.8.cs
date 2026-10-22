using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

// =====================================================
// TVLR-Selain 2.8
// =====================================================

internal static class Program
    {
    [STAThread]
    static void Main()
    {
        NativeMethods.SetCurrentProcessExplicitAppUserModelID("TVLRSelain");
        try
        {
            SovellusAsetukset.Lataa();
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.ToString(),
                "KAATUI :(",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }
}

public class MainForm : Form
{
    TextBox txtHaku;
    Button btnKanavat;

    ContextMenuStrip kanavaMenu;
    ComboBox cboToimitus;
    CheckBox chkInterval;
    DateTimePicker dtpAlku, dtpLoppu;
    CheckBox chkPaiva;
    DateTimePicker dtpPaiva;
    Button btnAvaa, btnTyhjenna, btnTietoa, btnPaivita;
    DataGridView grid;
    Label lblStatus;




    private bool _initializing = true;


    private void KanavaItem_CheckedChanged(
        object sender,
        EventArgs e)
    {

        if (_initializing)
            return;
        if (sender is not ToolStripMenuItem changedItem)
            return;

        // Kaikki valittuna, kaikki muut pois
        if (changedItem.Text == "Kaikki" && changedItem.Checked)
        {
            foreach (ToolStripMenuItem item in kanavaMenu.Items)
            {
                if (item.Text != "Kaikki")
                    item.Checked = false;
            }
        }
        else if (changedItem.Text != "Kaikki" && changedItem.Checked)
        {
            ((ToolStripMenuItem)kanavaMenu.Items[0]).Checked = false;
        }

        // Jos ei mitään valittuna -> Kaikki
        bool anyChecked = false;

        foreach (ToolStripMenuItem item in kanavaMenu.Items)
        {
            if (item.Text != "Kaikki" && item.Checked)
            {
                anyChecked = true;
                break;
            }
        }
        

        if (!anyChecked)
        {
            ((ToolStripMenuItem)kanavaMenu.Items[0]).Checked = true;
        }

        UpdateKanavaButtonText();

        ApplyFilters();
    }

    private void UpdateKanavaButtonText()
    {
        var selected = new List<string>();

        foreach (ToolStripMenuItem item in kanavaMenu.Items)
        {
            if (item.Checked)
                selected.Add(item.Text);
        }

        if (selected.Contains("Kaikki"))
        {
            btnKanavat.Text = "Kaikki";
        }
        else if (selected.Count <= 2)
        {
            btnKanavat.Text =
                string.Join(", ", selected);
        }
        else
        {
            btnKanavat.Text =
                $"{selected.Count} kanavaa valittu";
        }
    }
    private void Grid_MouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Middle)
        {
            _autoScrollMode =
                !_autoScrollMode;

            _autoScrollPoint = e.Location;

            grid.Cursor =
                _autoScrollMode
                    ? Cursors.NoMove2D
                    : Cursors.Default;
        }
    }

    private void Grid_ColumnHeaderMouseClick(
        
        object sender,
        DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0)
            return;

        string columnName =
            grid.Columns[e.ColumnIndex]
                .DataPropertyName;

        // Päivä klikattuna
        if (columnName == nameof(TvlrRow.PaivaStr))
        {
            _currentSort = "date";

            _sortNewestFirst =
                !_sortNewestFirst;

            ApplyFilters();
        }

        // Kesto klikattuna
        else if (columnName == nameof(TvlrRow.KestoStr))
        {
            _currentSort = "duration";

            _sortLongestFirst =
                !_sortLongestFirst;

            ApplyFilters();
        }
    }

    private bool _autoScrollMode = false;

    private Point _autoScrollPoint;

    private readonly System.Windows.Forms.Timer _autoScrollTimer =
        new System.Windows.Forms.Timer();

    private readonly BindingSource _binding = new();

    // Yhteinen HttpClient päivitysten tarkistamiseen ja lataamiseen (Refit olis ehkä parempi?)
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    // Päivämäärää tai kanavaa tuplaklikkaamallaa asettaa haun suodatuksen kyseisen kanavan päivämäärälle.
    // Muuten näytetään ohjelman kuvaus.
    private void Grid_CellDoubleClick(
        object sender,
        DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;

        if (grid.Rows[e.RowIndex].DataBoundItem is not TvlrRow row)
            return;

        // päivä tai kanava rivi
        if (e.ColumnIndex == 0 || e.ColumnIndex == 3)
        {
            txtHaku.Clear();

        foreach (ToolStripMenuItem item in kanavaMenu.Items)
        {
            item.Checked =
                item.Text == row.VerkkoNimi;
        }

        UpdateKanavaButtonText();

            chkInterval.Checked = false;

            chkPaiva.Checked = true;

            dtpPaiva.Value =
                row.Paiva.Date;

            ApplyFilters();

            return;
        }

        // Muuten avaa tietoja
        string kuvaus =
            row.TIETOJA?.Trim();

        if (string.IsNullOrWhiteSpace(kuvaus))
        {
            kuvaus =
                "Tälle ohjelmalle ei ole kuvausta.";
        }

        MessageBox.Show(
            this,
            kuvaus,
            row.Nimi,
            MessageBoxButtons.OK,
            MessageBoxIcon.None);
    }
    public MainForm()
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        TopMost = SovellusAsetukset.AinaPaalla;
        this.Size = new Size(1850, 720);
        this.StartPosition = FormStartPosition.CenterScreen;
        Text = "TVLR-Selain 2.8";
        MinimumSize = new Size(1630, 300);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var strip = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 10,
            RowCount = 1,
            AutoSize = true
        };
        
        for (int i = 0; i < strip.ColumnCount; i++)
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        btnAvaa = new Button { Text = "Avaa tietokanta", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        btnAvaa.Click += BtnAvaa_Click;

        txtHaku = new TextBox { PlaceholderText = "Hae ohjelman nimellä…", Width = 260 };
        txtHaku.TextChanged += (_, __) => ApplyFilters();
        txtHaku.TabStop = false;

        txtHaku.PreviewKeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Tab)
            {
                e.IsInputKey = true;

                if (grid.Rows.Count > 0)
                {
                    grid.Focus();

                    // Ohjelman nimi sarake
                    grid.CurrentCell =
                        grid.Rows[0].Cells[4];
                }
            }
        };

        txtHaku.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Tab)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        };
        btnKanavat = new Button
        {
            Text = "Kaikki",
            Width = 140,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft
        };

        kanavaMenu = new ContextMenuStrip();

        string[] kanavat =
        {
            "Kaikki",
            "YLE TV1",
            "YLE TV2",
            "MTV1",
            "MTV2",
            "MTV3",
            "Nelonen",
            "TVTV!",
            "Subtv",
            "Yle Fem",
            "Yle Teema",
            "Yle Extra",
            "YLE24",
            "TV Finland",
            "MTV3+",
            "Urheilukanava"
        };

        foreach (string kanava in kanavat)
        {
            var item = new ToolStripMenuItem(kanava)
            {
                CheckOnClick = true
            };

            item.CheckedChanged += KanavaItem_CheckedChanged;

            kanavaMenu.Items.Add(item);
        }

        // Kaikki oletuksena valittuna
        ((ToolStripMenuItem)kanavaMenu.Items[0]).Checked = true;

        btnKanavat.Click += (_, __) =>
        {
            if (kanavaMenu.Visible)
            {
                kanavaMenu.Close();
            }
            else
            {
                kanavaMenu.Show(
                    btnKanavat,
                    0,
                    btnKanavat.Height);
            }
        };

        cboToimitus = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 240
        };
        cboToimitus.SelectedIndexChanged += (_, __) => ApplyFilters();

        chkInterval = new CheckBox { Text = "Hae päivämäärävälillä", AutoSize = true };
        chkInterval.CheckedChanged += (_, __) => { UpdateDatePickersEnabled(); ApplyFilters(); };

        dtpAlku = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd.MM.yyyy",
            Width = 100,
        };

        dtpAlku.Value = new DateTime(1985, 1, 1);
        dtpAlku.ValueChanged += (_, __) => ApplyFilters();

        dtpLoppu = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "dd.MM.yyyy",
            Width = 100,
        };

        dtpLoppu.Value = new DateTime(2007, 12, 31);
        dtpLoppu.ValueChanged += (_, __) => ApplyFilters();

        chkPaiva = new CheckBox { Text = "Hae päivämäärällä", AutoSize = true };
        chkPaiva.CheckedChanged += (_, __) => { UpdateDatePickersEnabled(); ApplyFilters(); };

        dtpPaiva = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy", Width = 120 };
        dtpPaiva.Value = new DateTime(1996, 12, 31);
        dtpPaiva.ValueChanged += (_, __) => { if (chkPaiva.Checked) ApplyFilters(); };

        btnTyhjenna = new Button { Text = "Tyhjennä suodattimet", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        btnTyhjenna.Click += (_, __) =>
        {
            txtHaku.Clear();

        foreach (ToolStripMenuItem item in kanavaMenu.Items)
        {
            item.Checked =
                item.Text == "Kaikki";
        }

        UpdateKanavaButtonText();

            cboToimitus.SelectedIndex = 0;

            chkInterval.Checked = false;

            dtpAlku.Value =
                new DateTime(1985, 1, 1);

            dtpLoppu.Value =
                new DateTime(2007, 12, 31);

            chkPaiva.Checked = false;

            dtpPaiva.Value =
                new DateTime(1996, 12, 31);

            // Resetoi järjestys uudeksi
            _currentSort = "date";

            _sortNewestFirst = false;

            _sortLongestFirst = false;

            ApplyFilters();
        };

        btnPaivita = new Button
        {
            Text = "Tarkista päivitykset",
            AutoSize = true,
            Padding = new Padding(10, 6, 10, 6)
        };

        btnPaivita.Click += BtnPaivita_Click;
            
        btnTietoa = new Button
        {
            Text = "Asetukset",
            AutoSize = true,
            Padding = new Padding(10, 6, 10, 6)
        };

        btnTietoa.Click += (_, __) =>
        {
            using var asetukset = new AsetuksetForm();
            asetukset.ShowDialog(this);
        };

        strip.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strip.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strip.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        strip.Controls.Add(new Label { Text = "Haku", AutoSize = true }, 1, 0);
        strip.Controls.Add(new Label { Text = "Toimitus", AutoSize = true }, 2, 0);
        strip.Controls.Add(new Label { Text = "Kanava", AutoSize = true }, 3, 0);
        strip.Controls.Add(new Label { Text = "Alku pvm", AutoSize = true }, 5, 0);
        strip.Controls.Add(new Label { Text = "Loppu pvm", AutoSize = true }, 6, 0);
        strip.Controls.Add(new Label { Text = "Päivämäärä", AutoSize = true }, 8, 0);

        strip.Controls.Add(btnAvaa, 0, 1);
        strip.Controls.Add(txtHaku, 1, 1);
        strip.Controls.Add(cboToimitus, 2, 1);
        strip.Controls.Add(btnKanavat, 3, 1);
        strip.Controls.Add(chkInterval, 4, 1);
        strip.Controls.Add(dtpAlku, 5, 1);
        strip.Controls.Add(dtpLoppu, 6, 1);
        strip.Controls.Add(chkPaiva, 7, 1);
        strip.Controls.Add(dtpPaiva, 8, 1);

        var btnPanel = new FlowLayoutPanel { AutoSize = true };
        btnPanel.Controls.Add(btnTyhjenna);
        btnPanel.Controls.Add(btnPaivita);
        btnPanel.Controls.Add(btnTietoa);
        strip.Controls.Add(btnPanel, 9, 1);

        layout.Controls.Add(strip, 0, 0);

        // Dataruudukko ohjelmatietojen näyttämiseen
        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoGenerateColumns = false,

            RowHeadersVisible = false,

            AlternatingRowsDefaultCellStyle =
                new DataGridViewCellStyle
                {
                    BackColor = Color.FromArgb(248, 248, 248)
                },

            BorderStyle = BorderStyle.None,

            CellBorderStyle =
                DataGridViewCellBorderStyle.SingleHorizontal,

            ColumnHeadersDefaultCellStyle =
                new DataGridViewCellStyle
                {
                    Font = new Font(
                        SystemFonts.DefaultFont,
                        FontStyle.Bold),

                    WrapMode =
                        DataGridViewTriState.False
                }
        };
        grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
        grid.CellDoubleClick += Grid_CellDoubleClick;

        grid.KeyDown += Grid_KeyDown;
        grid.MouseDown += Grid_MouseDown;
        grid.KeyDown += (s, e) =>
        {
            // Välttää CTRL+A valitsevan kaikki solut
            if (e.Control && e.KeyCode == Keys.A)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        };

        grid.ClipboardCopyMode =
            DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.MultiSelect = true;

        grid.AutoSizeColumnsMode =
            DataGridViewAutoSizeColumnsMode.None;

        grid.AutoSizeRowsMode =
            DataGridViewAutoSizeRowsMode.None;

        grid.AllowUserToOrderColumns = false;

        grid.StandardTab = true;

        // Vähentää DataGridView:n välkkymistä
        typeof(DataGridView)
            .GetProperty(
                "DoubleBuffered",
                BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(grid, true);
        grid.TabStop = true;

        AddColumn("Päivä", nameof(TvlrRow.PaivaStr), 85);
        AddColumn("Kello", nameof(TvlrRow.KelloStr), 60);
        AddColumn("Kesto", nameof(TvlrRow.KestoStr), 70);
        AddColumn("Kanava", nameof(TvlrRow.VerkkoNimi), 100);
        AddColumn("Nimi", nameof(TvlrRow.Nimi), 420, fill: true);
        AddColumn("Tekstitys", nameof(TvlrRow.TEKS), 90);
        AddColumn("Selostus", nameof(TvlrRow.SELO), 90);
        AddColumn("Toimitus", nameof(TvlrRow.TEKI), 160);
        grid.DataSource = _binding;
        layout.Controls.Add(grid, 0, 1);

        _autoScrollTimer.Interval = 16;

        _autoScrollTimer.Tick += (_, __) =>
        {
            if (!_autoScrollMode)
                return;

            Point mouse =
                grid.PointToClient(Cursor.Position);

            int deltaY =
                mouse.Y - _autoScrollPoint.Y;

            // Deadzone hiiren ympäril
            if (Math.Abs(deltaY) < 12)
                return;

            int scrollAmount =
                deltaY / 18;

            int firstRow =
                grid.FirstDisplayedScrollingRowIndex;

            if (firstRow < 0)
                return;

            int newRow =
                firstRow + scrollAmount;

            newRow = Math.Max(
                0,
                Math.Min(
                    newRow,
                    grid.RowCount - 1));

            if (newRow != firstRow)
            {
                grid.FirstDisplayedScrollingRowIndex =
                    newRow;
            }
        };

        _autoScrollTimer.Start();
        
    // Tumma ja vaalea teema 
    if (SovellusAsetukset.TummaTeema)
        {
            grid.EnableHeadersVisualStyles = false;

            grid.BackgroundColor = Teema.TummaTausta;
            grid.GridColor = Color.FromArgb(64, 64, 64);

            grid.ColumnHeadersDefaultCellStyle.BackColor = Teema.TummaPaneeli;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Teema.TummaTeksti;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Teema.TummaPaneeli;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Teema.TummaTeksti;

            grid.DefaultCellStyle.BackColor = Teema.TummaTausta;
            grid.DefaultCellStyle.ForeColor = Teema.TummaTeksti;

            grid.DefaultCellStyle.SelectionBackColor = Teema.ValintaTausta;
            grid.DefaultCellStyle.SelectionForeColor = Teema.ValintaTeksti;

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(30, 30, 30);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Teema.TummaTeksti;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Teema.ValintaTausta;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Teema.ValintaTeksti;

            grid.RowHeadersDefaultCellStyle.BackColor = Teema.TummaPaneeli;
            grid.RowHeadersDefaultCellStyle.ForeColor = Teema.TummaTeksti;
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = Teema.ValintaTausta;
            grid.RowHeadersDefaultCellStyle.SelectionForeColor = Teema.ValintaTeksti;
        }
        else
        {
            grid.EnableHeadersVisualStyles = true;

            grid.BackgroundColor = SystemColors.Window;
            grid.GridColor = SystemColors.ControlDark;

            grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SystemColors.Control;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = SystemColors.ControlText;

            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = Color.Black;

            grid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
            grid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;

            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 248, 248);
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Color.Black;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;

            grid.RowHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            grid.RowHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
            grid.RowHeadersDefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
            grid.RowHeadersDefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
        }


        lblStatus = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(2, 8, 2, 8),
            TabStop = false
        };

        layout.Controls.Add(lblStatus, 0, 3);

        UpdateDatePickersEnabled();

        TryAutoLoadData();
        if (SovellusAsetukset.TummaTeema)
        {
            this.BackColor = Teema.TummaTausta;
            this.ForeColor = Teema.TummaTeksti;
        }
        else
        {
            this.BackColor = SystemColors.Control;
            this.ForeColor = SystemColors.ControlText;
        }
        if (SovellusAsetukset.AutoPaivitys)
        {
            Shown += async (_, __) =>
            {
                await Task.Delay(1000);

                BtnPaivita_Click(null, EventArgs.Empty);
            };
        }
        _initializing = false;
    }
    private void AddColumn(string header, string dataProp, int width, bool fill = false)
    {
        var col = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = dataProp,
            Width = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
        };
        grid.Columns.Add(col);
    }
    
    private void UpdateDatePickersEnabled()
    {
        dtpPaiva.Enabled = chkPaiva.Checked;
        bool intervalEnabled = chkInterval.Checked && !chkPaiva.Checked;
        dtpAlku.Enabled = intervalEnabled;
        dtpLoppu.Enabled = intervalEnabled;
    }
    private SqliteConnection _conn;
    private string _dbPath = "";

    private List<TvlrRow> _allPrograms = new();
    private bool _sortNewestFirst = false;

    private bool _sortLongestFirst = false;

    private string _currentSort = "date";
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _binding.DataSource = null;

        grid.DataSource = null;

        _conn?.Close();
        _conn?.Dispose();
        _conn = null;

        base.OnFormClosing(e);
    }

    // Avaa SQLite-tietokannan ja alustaa indeksit (Paljon optimoitavaa vielä)
    private void LoadFromDatabase(string dbPath)
    {
        _conn?.Close();
        _conn?.Dispose();

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();

        // SQLite asetukset
        using (var pragma = _conn.CreateCommand())
        {
            pragma.CommandText = @"
                PRAGMA journal_mode=DELETE;
                PRAGMA synchronous=NORMAL;
                PRAGMA cache_size=-16000;
                PRAGMA busy_timeout=10000;
            ";

            pragma.ExecuteNonQuery();
        }

        _dbPath = dbPath;

        EnsureDatabaseCompatibility();

        using var idx = _conn.CreateCommand();

        idx.CommandText = @"
        CREATE INDEX IF NOT EXISTS idx_nimi_nocase
        ON programs(nimi COLLATE NOCASE);

        CREATE INDEX IF NOT EXISTS idx_pvm
        ON programs(pvm);

        CREATE INDEX IF NOT EXISTS idx_verkko
        ON programs(verkko);

        CREATE UNIQUE INDEX IF NOT EXISTS idx_unique_program
        ON programs
        (
            docn,
            nimi,
            pvm,
            kello,
            kesto,
            verkko,
            teks,
            selo,
            teki,
            tietoja
        );
        ";

        idx.ExecuteNonQuery();

        _allPrograms = LoadAllPrograms();

        PopulateToimitusFromDb();

        ApplyFilters();
    }

    private void EnsureDatabaseCompatibility()
    {
        if (_conn == null)
            return;

        using var checkCmd = _conn.CreateCommand();

        checkCmd.CommandText = @"
            PRAGMA table_info(programs);
        ";

        bool hasTietoja = false;

        using (var reader = checkCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                string column =
                    reader["name"]?.ToString() ?? "";

                if (column.Equals(
                    "tietoja",
                    StringComparison.OrdinalIgnoreCase))
                {
                    hasTietoja = true;
                    break;
                }
            }
        }

        if (!hasTietoja)
        {
            using var alterCmd = _conn.CreateCommand();

            alterCmd.CommandText =
                "ALTER TABLE programs ADD COLUMN tietoja TEXT DEFAULT '';";

            alterCmd.ExecuteNonQuery();
        }
    }
       private void PopulateToimitusFromDb()
    {
        if (string.IsNullOrWhiteSpace(_dbPath))
            return;

        using var conn =
            new SqliteConnection($"Data Source={_dbPath}");

        conn.Open();

        var cmd = conn.CreateCommand();

        cmd.CommandText =
            "SELECT DISTINCT teki FROM programs WHERE teki != '' ORDER BY teki";

        var list = new List<string> { "Kaikki" };

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }

        cboToimitus.DataSource = list;

        if (cboToimitus.Items.Count > 0)
            cboToimitus.SelectedIndex = 0;
    }

    private List<TvlrRow> LoadAllPrograms()
    {
        var rows =
            new List<TvlrRow>();

        using var conn =
            new SqliteConnection(
                $"Data Source={_dbPath}");

        conn.Open();

        string sql = @"
        SELECT
            docn,
            nimi,
            pvm,
            kello,
            kesto,
            verkko,
            teks,
            selo,
            teki,
            tietoja
        FROM programs
        ";

        using var cmd =
            new SqliteCommand(sql, conn);

        using var reader =
            cmd.ExecuteReader();

        while (reader.Read())
        {
            string nimi =
                reader["nimi"]?.ToString() ?? "";

            DateTime paiva = DateTime.MinValue;

            DateTime.TryParse(
                reader["pvm"]?.ToString(),
                out paiva);

            int kesto = 0;

            int.TryParse(
                reader["kesto"]?.ToString(),
                out kesto);

            string rowVerkko =
                reader["verkko"]?.ToString() ?? "";

            TimeSpan? kelloAika =
                ParseTime(
                    reader["kello"]?.ToString() ?? "");

            TimeSpan kestoAika =
                TimeSpan.FromSeconds(kesto);

            rows.Add(new TvlrRow
            {
                DOCN =
                    reader["docn"]?.ToString() ?? "",

                Nimi = nimi,

                NimiNormalized =
                    NormalizeSearch(nimi),

                Paiva = paiva,

                KelloTimeSpan = kelloAika,

                KestoTimeSpan = kestoAika,

                Verkko = rowVerkko,

                TEKS =
                    reader["teks"]?.ToString() ?? "",

                SELO =
                    reader["selo"]?.ToString() ?? "",

                TEKI =
                    reader["teki"]?.ToString() ?? "",

                TIETOJA =
                    reader["tietoja"]?.ToString() ?? "",

                PaivaStr =
                    paiva == DateTime.MinValue
                        ? ""
                        : paiva.ToString("dd.MM.yyyy"),

                KelloStr =
                    kelloAika.HasValue
                        ? $"{(int)kelloAika.Value.TotalHours:00}:{kelloAika.Value.Minutes:00}"
                        : "",

                KestoStr =
                    kestoAika.TotalHours >= 1
                        ? $"{(int)kestoAika.TotalHours}:{kestoAika.Minutes:00}:{kestoAika.Seconds:00}"
                        : $"{kestoAika.Minutes:00}:{kestoAika.Seconds:00}",

                VerkkoNimi = rowVerkko switch
                {
                    "1" =>
                        paiva < new DateTime(1993, 1, 1)
                        && reader["teki"]?.ToString() == "MTV"
                            ? "MTV1"
                            : "YLE TV1",

                    "2" =>
                        paiva < new DateTime(1993, 1, 1)
                        && reader["teki"]?.ToString() == "MTV"
                            ? "MTV2"
                            : "YLE TV2",

                    "3" => "MTV3",
                    "4" => "Nelonen",

                    "6" => paiva < new DateTime(2001, 8, 15)
                        ? "TVTV!"
                        : "Subtv",

                    "13" => "Yle Fem",
                    "14" => "Yle Teema",
                    "8"  => "Yle Extra",
                    "15" => "YLE24",
                    "22" => "TV Finland",
                    "30" => "MTV3+",
                    "31" => "Urheilukanava",

                    _ => rowVerkko
                }
            });
        }

        return rows;
    }
    private TimeSpan? ParseTime(string s)
    {
        if (TimeSpan.TryParse(s, out var t))
            return t;
        return null;
    }

    private string NormalizeSearch(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";

        s = s.ToLowerInvariant();

        var normalized =
            s.Normalize(NormalizationForm.FormD);

        var sb = new StringBuilder();

        foreach (char c in normalized)
        {
            var category =
                System.Globalization.CharUnicodeInfo
                    .GetUnicodeCategory(c);

            if (category ==
                System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }

        return sb.ToString();
    }

    // Yrittää avata TVLR.db-tiedoston automaattisesti
    private void TryAutoLoadData()
    {
        string db = Path.Combine(AppContext.BaseDirectory, "TVLR.db");

        if (File.Exists(db))
        {
            LoadFromDatabase(db);
            return;
        }

        lblStatus.Text = "TVLR.db ei löytynyt.";
    }

    private void BtnAvaa_Click(object sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "SQLite DB (*.db)|*.db",
            Title = "Valitse tietokanta (.db)"
        };

        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                LoadFromDatabase(ofd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Virhe:\n" + ex.Message);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }

    // Suorittaa haun ja päivittää näkymän
    private void ApplyFilters()
    {

        if (_allPrograms == null)
            return;
        try
        {
            Cursor = Cursors.WaitCursor;

            string search =
                NormalizeSearch(txtHaku.Text);

            IEnumerable<TvlrRow> rows =
                _allPrograms;

            if (!string.IsNullOrWhiteSpace(search))
            {
                rows = rows.Where(x =>
                    x.NimiNormalized.Contains(search));
            }

            var selectedChannels =
                kanavaMenu.Items
                    .OfType<ToolStripMenuItem>()
                    .Where(x => x.Checked)
                    .Select(x => x.Text)
                    .ToList();

            if (!selectedChannels.Contains("Kaikki"))
            {
                rows = rows.Where(x =>
                {
                    foreach (string verkko in selectedChannels)
                    {
                        if (verkko == "MTV1")
                        {
                            if (
                                x.Verkko == "1" &&
                                x.TEKI == "MTV" &&
                                x.Paiva < new DateTime(1993, 1, 1))
                                return true;
                        }
                        else if (verkko == "MTV2")
                        {
                            if (
                                x.Verkko == "2" &&
                                x.TEKI == "MTV" &&
                                x.Paiva < new DateTime(1993, 1, 1))
                                return true;
                        }
                        else if (x.VerkkoNimi == verkko)
                        {
                            return true;
                        }
                    }

                    return false;
                });
            }

            string toimitus =
                cboToimitus.SelectedItem?.ToString() ?? "Kaikki";

            if (toimitus != "Kaikki")
            {
                rows = rows.Where(x =>
                    x.TEKI == toimitus);
            }

            // Päivämäärähaku
            if (chkPaiva.Checked)
            {
                DateTime selectedDate =
                    dtpPaiva.Value.Date;

                rows = rows.Where(x =>
                    x.Paiva.Date == selectedDate);
            }

            // Päivämäärävälihaku
            else if (chkInterval.Checked)
            {
                DateTime alku =
                    dtpAlku.Value.Date;

                DateTime loppu =
                    dtpLoppu.Value.Date;

                rows = rows.Where(x =>
                    x.Paiva.Date >= alku &&
                    x.Paiva.Date <= loppu);
            }

            List<TvlrRow> finalRows;

            if (_currentSort == "duration")
            {
                finalRows =
                    (_sortLongestFirst
                        ? rows
                            .OrderByDescending(x => x.KestoTimeSpan)
                            .ThenBy(x => x.Paiva)
                        : rows
                            .OrderBy(x => x.KestoTimeSpan)
                            .ThenBy(x => x.Paiva))
                    .ToList();
            }
            else
            {
                finalRows =
                    (_sortNewestFirst
                        ? rows
                            .OrderByDescending(x => x.Paiva)
                            .ThenByDescending(x => x.KelloTimeSpan)
                        : rows
                            .OrderBy(x => x.Paiva)
                            .ThenBy(x => x.KelloTimeSpan))
                    .ToList();
            }

            _binding.DataSource =
                finalRows;

            lblStatus.Text =
                $"Näytetään {finalRows.Count:N0} ohjelmaa.";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        
        // Älä triggeröi kun käyttäjä kirjoittaa hakukenttään
        if (txtHaku.Focused)
            return;

        if (e.KeyCode != Keys.Space)
            return;

        if (grid.CurrentRow?.DataBoundItem is not TvlrRow row)
            return;

        string kuvaus = row.TIETOJA?.Trim();

        if (string.IsNullOrWhiteSpace(kuvaus))
        {
            kuvaus = "Tälle ohjelmalle ei ole kuvausta.";
        }

        MessageBox.Show(
            this,
            kuvaus,
            row.Nimi,
            MessageBoxButtons.OK,
            MessageBoxIcon.None);

        e.Handled = true;
    }

    protected override bool ProcessCmdKey(
        ref Message msg,
        Keys keyData)
    {
        // Ctrl+F = haku
        if (keyData == (Keys.Control | Keys.F))
        {
            txtHaku.Focus();
            txtHaku.SelectAll();
            return true;
        }

        // Ctrl+T = toimitus
        if (keyData == (Keys.Control | Keys.T))
        {
            if (cboToimitus.DroppedDown)
            {
                cboToimitus.DroppedDown = false;

                grid.Focus();
            }
            else
            {
                cboToimitus.Focus();

                cboToimitus.DroppedDown = true;
            }

            return true;
        }

        // Ctrl+K = kanavat
        if (keyData == (Keys.Control | Keys.K))
        {
            if (kanavaMenu.Visible)
            {
                kanavaMenu.Close(
                    ToolStripDropDownCloseReason.AppClicked);

                grid.Focus();
            }
            else
            {
                kanavaMenu.Show(
                    btnKanavat,
                    0,
                    btnKanavat.Height);

                btnKanavat.Focus();
            }

            return true;
        }

        // Ctrl+A = päivämääräväli
        if (keyData == (Keys.Control | Keys.A))
        {
            bool enable =
                !chkInterval.Checked;

            chkInterval.Checked = enable;

            if (enable)
            {
                chkPaiva.Checked = false;

                dtpAlku.Focus();

                SendKeys.Send("{HOME}");
            }

            return true;
        }

        // Ctrl+P = päivä
        if (keyData == (Keys.Control | Keys.P))
        {
            bool enable =
                !chkPaiva.Checked;

            chkPaiva.Checked = enable;

            if (enable)
            {
                chkInterval.Checked = false;

                dtpPaiva.Focus();
            }

            return true;
        }

        // Ctrl+back = tyhjennä suodattimet
        if (keyData == (Keys.Control | Keys.Back))
        {
            btnTyhjenna.PerformClick();
            return true;
        }

        return base.ProcessCmdKey(
            ref msg,
            keyData);
    }

    private async Task DownloadFileAsync(string url, string path)
    {
        using var response = await Http.GetAsync(url);

        response.EnsureSuccessStatusCode();

        await using var fs = File.Create(path);

        await response.Content.CopyToAsync(fs);
    }

    private void SafeDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            GC.Collect();
            GC.WaitForPendingFinalizers();

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    File.Delete(path);

                    if (!File.Exists(path))
                        return;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
        }
        catch
        {
        }
    }

    // Tarkistaa palvelimelta tietokantapäivitykset (vihdoin tuli hyödyllistä käyttöä 15v. vanhalle tietokoneelle xD)
    private async void BtnPaivita_Click(object sender, EventArgs e)
    {
        try
        {

            lblStatus.Text = "Tarkistetaan päivityksiä...";
            // Paikallinen päivitys

            string appDir =
                AppContext.BaseDirectory;

            var localUpdates =
                Directory.GetFiles(appDir, "*.db")
                    .Where(x =>
                        Path.GetFileName(x)
                            .StartsWith("update_v"))
                    .OrderBy(x => x)
                    .ToList();

            if (localUpdates.Count > 0)
            {
                int totalInsertedLocal = 0;
                int duplicatesRemovedLocal = 0;

                foreach (string localDb in localUpdates)
                {
                    string fileName =
                        Path.GetFileName(localDb);

                    lblStatus.Text =
                        $"Asennetaan paikallinen päivitys: {fileName}";

                    int inserted =
                        await Task.Run(() => MergeDatabases(localDb));

                    totalInsertedLocal += inserted;

                    duplicatesRemovedLocal +=
                        await Task.Run(RemoveDuplicates);

                    // Poista paikallinen päivityspaketti asennuksen jälkeen
                    try
                    {
                        File.Delete(localDb);
                    }
                    catch
                    {
                    }
                }

                lblStatus.Text =
                    "Optimoidaan tietokantaa...";

                await Task.Run(OptimizeDatabase);

                int newestLocalVersion = 0;

                foreach (string localDb in localUpdates)
                {
                    string name =
                        Path.GetFileNameWithoutExtension(localDb);

                    // update_v123 -> 123
                    string number =
                        name.Replace("update_v", "");

                    if (int.TryParse(number, out int v))
                    {
                        if (v > newestLocalVersion)
                            newestLocalVersion = v;
                    }
                }

                if (newestLocalVersion > 0)
                {
                    File.WriteAllText(
                        Path.Combine(
                            AppContext.BaseDirectory,
                            "dbversio.txt"),
                        newestLocalVersion.ToString());
                }

                lblStatus.Text =
                    "Ladataan päivitettyä tietokantaa...";

                _allPrograms = LoadAllPrograms();

                ApplyFilters();

                MessageBox.Show(
                    $"Paikalliset päivitykset asennettu!\n\n" +
                    $"Lisättiin {totalInsertedLocal:N0} ohjelmaa.\n" +
                    $"Poistettiin {duplicatesRemovedLocal:N0} kaksoiskappaletta.",
                    "Paikallinen päivitys",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            string json =
                await Http.GetStringAsync(
                    "https://telkkari.tv/tvlr/version.json");

            var serverInfo =
                JsonSerializer.Deserialize<VersionInfo>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (serverInfo == null)
            {
                MessageBox.Show("Virhe version tarkistuksessa.");
                return;
            }

            int localVersion = GetLocalDbVersion();

            serverInfo.updates.Sort(
                (a, b) => a.version.CompareTo(b.version));

            if (serverInfo.version <= localVersion)
            {
                lblStatus.Text =
                    "Tietokanta on ajan tasalla.";

                return;
            }

            var updatesToInstall =
                serverInfo.updates
                    .Where(x => x.version > localVersion)
                    .OrderBy(x => x.version)
                    .Select(x =>
                        $"v{x.version} - {x.description}")
                    .ToList();

            string updateList =
                string.Join(" → ", updatesToInstall);

            var result = MessageBox.Show(
                $"Uusia ohjelmatietoja löytyi.\n\n" +
                $"Nykyinen versio: {localVersion}\n" +
                $"Uusin versio: {serverInfo.version}\n\n" +
                $"Päivitykset:\n{updateList}\n\n" +
                $"Ladataanko?",
                "Päivitys",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
                return;

            int startingVersion = localVersion;
            int totalInserted = 0;
            int duplicatesRemoved = 0;
                lblStatus.Text = "Poistetaan vanhoja kaksoiskappaleita...";
                duplicatesRemoved +=
                    await Task.Run(RemoveDuplicates);
            foreach (var update in serverInfo.updates.OrderBy(x => x.version))
            {
                if (update.version <= startingVersion)
                    continue;

                lblStatus.Text =
                    $"Ladataan päivitystä v{update.version} @ https://telkkari.tv/tvlr/{update.file}";

                string tempDb =
                    Path.Combine(
                        Path.GetTempPath(),
                        update.file);

                string updateUrl =
                    $"https://telkkari.tv/tvlr/{update.file}";

                await DownloadFileAsync(updateUrl, tempDb);

                // Yhdistää ladatun päivitystietokannan pääkantaan (TVLR.db)
                int inserted =
                    await Task.Run(() => MergeDatabases(tempDb));

                totalInserted += inserted;
                duplicatesRemoved +=
                    await Task.Run(RemoveDuplicates);
                GC.Collect();
                GC.WaitForPendingFinalizers();

                await Task.Delay(300);

                SafeDelete(tempDb);
                SafeDelete(tempDb + ".tmp");

                if (File.Exists(tempDb + ".tmp"))
                {
                    File.Delete(tempDb + ".tmp");
                }
            }

            lblStatus.Text = "Optimoidaan tietokantaa...";

            await Task.Run(OptimizeDatabase);

            File.WriteAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "dbversio.txt"),
                serverInfo.version.ToString());

            lblStatus.Text = "Ladataan päivitettyä tietokantaa...";

            // Ladataan tietokanta uudelleen muistiin
            _allPrograms = LoadAllPrograms();

            ApplyFilters();

            lblStatus.Text = "Päivitys valmis.";

                MessageBox.Show(
                    $"Tietokanta päivitetty!\n\n" +
                    $"Lisättiin yhteensä {totalInserted:N0} ohjelmaa.\n" +
                    $"Poistettiin {duplicatesRemoved:N0} kaksoiskappaletta.",
                "Valmis",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Päivitys epäonnistui.\n\n" + ex.Message,
                "Virhe",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            lblStatus.Text = "Päivitys epäonnistui.";
        }
    }

    // Hakee paikallisen tietokannan version
    private int RemoveDuplicates()
    {
        if (_conn == null)
            return 0;

        using var countCmd = _conn.CreateCommand();

        countCmd.CommandText = @"
            SELECT COUNT(*)
            FROM programs;
        ";

        int before =
            Convert.ToInt32(countCmd.ExecuteScalar());

        using var deleteCmd = _conn.CreateCommand();

        deleteCmd.CommandText = @"
            DELETE FROM programs
            WHERE rowid NOT IN
            (
                SELECT MIN(rowid)
                FROM programs
                GROUP BY
                    docn,
                    nimi,
                    pvm,
                    kello,
                    kesto,
                    verkko,
                    teks,
                    selo,
                    teki,
                    tietoja
            );
        ";

        deleteCmd.ExecuteNonQuery();

        using var countAfterCmd = _conn.CreateCommand();

        countAfterCmd.CommandText = @"
            SELECT COUNT(*)
            FROM programs;
        ";

        int after =
            Convert.ToInt32(countAfterCmd.ExecuteScalar());

        return before - after;
    }
private void OptimizeDatabase()
{
    if (_conn == null)
        return;

    using (var vacuumCmd = _conn.CreateCommand())
    {
        vacuumCmd.CommandText = "VACUUM;";
        vacuumCmd.ExecuteNonQuery();
    }

    using (var reindexCmd = _conn.CreateCommand())
    {
        reindexCmd.CommandText = "REINDEX;";
        reindexCmd.ExecuteNonQuery();
    }

    using (var analyzeCmd = _conn.CreateCommand())
    {
        analyzeCmd.CommandText = "ANALYZE;";
        analyzeCmd.ExecuteNonQuery();
    }
}

private int GetLocalDbVersion()
{
    string path =
        Path.Combine(AppContext.BaseDirectory, "dbversio.txt");

    if (!File.Exists(path))
        return 0;

    if (int.TryParse(File.ReadAllText(path), out int v))
        return v;

    return 0;
}

private int MergeDatabases(string downloadedDb)
{
    if (_conn == null)
        return 0;

    // SQLite tykkää enemmän oikeasta tiedostosta
    // kuin temp/download streamista
    File.Copy(
        downloadedDb,
        downloadedDb + ".tmp",
        true);

    downloadedDb =
        downloadedDb + ".tmp";

    using var attachCmd = _conn.CreateCommand();

    attachCmd.CommandText =
        $"ATTACH DATABASE '{downloadedDb.Replace("'", "''")}' AS newdb;";

    attachCmd.ExecuteNonQuery();

    using var insertCmd = _conn.CreateCommand();

    insertCmd.CommandText = @"
        INSERT OR IGNORE INTO programs
        (
            docn,
            nimi,
            pvm,
            kello,
            kesto,
            verkko,
            teks,
            selo,
            teki,
            tietoja
        )
        SELECT
            docn,
            nimi,
            pvm,
            kello,
            kesto,
            verkko,
            teks,
            selo,
            teki,
            tietoja
        FROM newdb.programs;
    ";

    int inserted =
        insertCmd.ExecuteNonQuery();

    using var detachCmd =
        _conn.CreateCommand();

    detachCmd.CommandText =
        "DETACH DATABASE newdb;";

    detachCmd.ExecuteNonQuery();

    try
    {
        File.Delete(downloadedDb);
    }
    catch
    {
    }

    return inserted;
    }

}public class TvlrRow
{

    public string DOCN { get; set; } = "";
    public string Nimi { get; set; } = "";
    public string NimiNormalized { get; set; } = "";
    public string TEKS { get; set; } = "";
    public string SELO { get; set; } = "";
    public string TEKI { get; set; } = "";
    public string TIETOJA { get; set; } = "";

    public DateTime Paiva { get; set; } = DateTime.MinValue;
    public TimeSpan? KelloTimeSpan { get; set; }
    public TimeSpan? KestoTimeSpan { get; set; }
    public string Verkko { get; set; } = "";

    public string VerkkoNimi { get; set; } = "";

    public string PaivaStr { get; set; } = "";

    public string KelloStr { get; set; } = "";

    public string KestoStr { get; set; } = "";
}

public class VersionInfo
{
    public int version { get; set; }

    public List<UpdateInfo> updates { get; set; } = new();
}

public class UpdateInfo
{
    public int version { get; set; }

    public string file { get; set; } = "";

    public string description { get; set; } = "";
}
public class AsetuksetForm : Form
{
    // Ohjelman asetukset
    Button btnTallenna;
    CheckBox chkAinaPaalla;
    CheckBox chkTummaTeema;

    CheckBox chkAutoPaivitys;
    Button btnTietoja;

    public AsetuksetForm()
    {
        TopMost = SovellusAsetukset.AinaPaalla; 
        Text = "Asetukset";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 190);
        
        var asettelu = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3
        };

        chkAinaPaalla = new CheckBox
        {
            Text = "Näytä aina päällimmäisenä",
            AutoSize = true,
            Checked = SovellusAsetukset.AinaPaalla
        };
        chkTummaTeema = new CheckBox
        {
            Text = "Tumma teema",
            AutoSize = true,
            Checked = SovellusAsetukset.TummaTeema
        };
        chkAutoPaivitys = new CheckBox
        {
            Text = "Tarkista tietokannan päivitykset ohjelman käynnistyessä",
            AutoSize = true,
            Checked = SovellusAsetukset.AutoPaivitys
        };

        asettelu.Controls.Add(chkTummaTeema);
        asettelu.Controls.Add(chkAutoPaivitys);


        asettelu.Controls.Add(chkAinaPaalla);
        btnTietoja = new Button
        {
            Text = "Tietoja",
            Size = new Size(100, 25),
        };

        btnTietoja.Click += (_, __) =>
        {
            MessageBox.Show(
                "Datan lisenssi: CC0-lisenssi: ei tekijänoikeutta. Dataa voi lupaa pyytämättä kopioida, muokata, levittää ja esittää, mukaan lukien kaupallisessa tarkoituksessa.\n\n" +
                "Ohjelmatietojen lähteet:\n\n" +
                "https://elavaarkisto.kokeile.yle.fi/data/\n\n" +
                "https://telkussa.fi/\n\n" +
                "https://web.archive.org/web/20160821021501/http://netello.fi/tv?MODULI_pvm=22012001\n\n" +
                "https://files.mpoli.fi/software/TEXTS/TV-RADIO/\n\n"+
                "https://web.archive.org/web/20000917221050/http://www.fi/tanaan/tv-opas/\n\n"+
                "https://web.archive.org/web/19970817131201/http://www.freenet.hut.fi/ohjelmatiedot\n\n"+
                "https://hs.fi/\n\n"+
                "Telkkari 2026",
                "Tietoja",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        };

        asettelu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        asettelu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        asettelu.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Controls.Add(asettelu);

        var painikePaneeli = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 10, 0, 0)
        };

        btnTallenna = new Button { Text = "Tallenna", Size = new Size(100, 25), };
        btnTallenna.Click += BtnTallenna_Click;
        painikePaneeli.Controls.Add(btnTietoja);
        painikePaneeli.Controls.Add(btnTallenna);
        asettelu.Controls.Add(painikePaneeli);
    }

    private void BtnTallenna_Click(object sender, EventArgs e)
    {
        bool vanhaTeema = SovellusAsetukset.TummaTeema;

        SovellusAsetukset.AinaPaalla = chkAinaPaalla.Checked;
        SovellusAsetukset.TummaTeema = chkTummaTeema.Checked;
        SovellusAsetukset.AutoPaivitys =
            chkAutoPaivitys.Checked;

        SovellusAsetukset.Tallenna();

        if (Owner != null)
            Owner.TopMost = SovellusAsetukset.AinaPaalla;

        if (vanhaTeema != SovellusAsetukset.TummaTeema)
        {
            var r = MessageBox.Show(
                "Teeman vaihtaminen vaatii ohjelman uudelleenkäynnistyksen.\n\nKäynnistetäänkö nyt?",
                "Vaihda teema",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (r == DialogResult.Yes)
            {
                Application.Restart();
                Environment.Exit(0);
            }
        }

        Close();
    }
}

public static class Teema
{
    public static readonly Color TummaTausta = Color.FromArgb(24, 24, 24);
    public static readonly Color TummaPaneeli = Color.FromArgb(32, 32, 32);
    public static readonly Color TummaTeksti = Color.Gainsboro;

    public static readonly Color ValintaTausta = Color.FromArgb(0, 120, 215);
    public static readonly Color ValintaTeksti = Color.White;
}

// Windows API -kutsut  (sori Linux ja Mac :( )
static class NativeMethods
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);
}

public static class SovellusAsetukset
{
    // Ohjelman asetusten tallennus ja lataus
    public static bool AinaPaalla { get; set; }
    public static bool TummaTeema { get; set; }

    public static bool AutoPaivitys { get; set; } = true;

    private static readonly string AsetusTiedosto =
        Path.Combine(AppContext.BaseDirectory, "asetukset.ini");

    public static void Lataa()
    {
        if (!File.Exists(AsetusTiedosto))
            return;

        foreach (var rivi in File.ReadAllLines(AsetusTiedosto))
        {
            var osat = rivi.Split('=', 2);
            if (osat.Length != 2) continue;

            var avain = osat[0].Trim();
            var arvo = osat[1].Trim();

            if (avain.Equals("AinaPaalla", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(arvo, out bool tulos))
                    AinaPaalla = tulos;
            }
            else if (avain.Equals("TummaTeema", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(arvo, out bool tulos))
                    TummaTeema = tulos;
            }
            else if (avain.Equals("AutoPaivitys", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(arvo, out bool tulos))
                    AutoPaivitys = tulos;
            }
        }
    }
    public static void Tallenna()
    {
        var rivit = new[]
        {
            $"AinaPaalla={AinaPaalla}",
            $"TummaTeema={TummaTeema}",
            $"AutoPaivitys={AutoPaivitys}"

        };

        File.WriteAllLines(AsetusTiedosto, rivit, Encoding.UTF8);
    }
}
