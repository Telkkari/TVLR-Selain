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

// =====================================================
// TVLR-Selain 2.6.1
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
    ComboBox cboVerkko;
    ComboBox cboToimitus;
    CheckBox chkInterval;
    DateTimePicker dtpAlku, dtpLoppu;
    CheckBox chkPaiva;
    DateTimePicker dtpPaiva;
    Button btnAvaa, btnTyhjenna, btnTietoa, btnPaivita;
    DataGridView grid;
    Label lblStatus;

    private readonly BindingSource _binding = new();

    // Viivyttää tietokantakyselyiden suorittamista jokaisella painalluksella
    private readonly System.Windows.Forms.Timer _searchTimer =
    new System.Windows.Forms.Timer();

    // Yhteinen HttpClient päivitysten tarkistamiseen ja lataamiseen (Refit olis ehkä parempi?)
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    // Päivämäärää tai kanavaa tuplaklikkaamallaa asettaa haun suodatuksen kyseisen kanavan päivämäärälle.
    // Muuten näytetään ohjelman kuvaus.
    private void Grid_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;

        if (grid.Rows[e.RowIndex].DataBoundItem is not TvlrRow row)
            return;

        if (e.ColumnIndex == 0 || e.ColumnIndex == 3)
        {
            txtHaku.Clear();
            cboVerkko.SelectedItem = row.VerkkoNimi;
            chkInterval.Checked = false;
            chkPaiva.Checked = true;
            dtpPaiva.Value = row.Paiva.Date;
            ApplyFilters();
            return;
        }

        string kuvaus = row.TIETOJA.Trim();

        if (string.IsNullOrWhiteSpace(kuvaus))
        {
            kuvaus = "Tälle ohjelmalle ei ole kuvausta.";
        }

        MessageBox.Show(
            this,
            kuvaus,
            row.Nimi,
            MessageBoxButtons.OK,
            MessageBoxIcon.None
        );
    }
    public MainForm()
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        TopMost = SovellusAsetukset.AinaPaalla;
        this.Size = new Size(1835, 720);
        this.StartPosition = FormStartPosition.CenterScreen;
        Text = "TVLR-Selain 2.6.1";
        MinimumSize = new Size(1630, 300);

        _searchTimer.Interval = 50; //ms

        // Suoritetaan haku pienellä viiveellä, jotta tulosten näyttäminen ei olisi raskasta. (ehkä turha kun "lazy loading" käytös?)
        // Varmuuden vuoks jätän, mut pienensin aikaa
        _searchTimer.Tick += (_, __) =>
        {
            _searchTimer.Stop();
            ApplyFilters();
        };

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
        txtHaku.TextChanged += (_, __) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        cboVerkko = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        cboVerkko.Items.AddRange(new object[]
            {
                "Kaikki",
                "YLE TV1",
                "YLE TV2",
                "MTV3",
                "Nelonen",
                "Subtv",
                "Yle Fem",
                "Yle Teema",
                "Yle Extra",
                "YLE24",
                "TV Finland",
                "MTV3+",
                "Urheilukanava"
            });
        cboVerkko.SelectedIndex = 0;
        cboVerkko.SelectedIndexChanged += (_, __) => ApplyFilters();

        cboToimitus = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 240
        };
        cboToimitus.SelectedIndexChanged += (_, __) => ApplyFilters();

        chkInterval = new CheckBox { Text = "Hae päivämäärävälillä", AutoSize = true };
        chkInterval.CheckedChanged += (_, __) => { UpdateDatePickersEnabled(); ApplyFilters(); };

        dtpAlku = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy", Width = 120 };
        dtpAlku.Value = new DateTime(1985, 1, 1);
        dtpAlku.ValueChanged += (_, __) => ApplyFilters();

        dtpLoppu = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy", Width = 120 };
        dtpLoppu.Value = new DateTime(1999, 12, 31);
        dtpLoppu.ValueChanged += (_, __) => ApplyFilters();

        chkPaiva = new CheckBox { Text = "Hae päivämäärällä", AutoSize = true };
        chkPaiva.CheckedChanged += (_, __) => { UpdateDatePickersEnabled(); ApplyFilters(); };

        dtpPaiva = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy", Width = 120 };
        dtpPaiva.Value = new DateTime(1999, 12, 31);
        dtpPaiva.ValueChanged += (_, __) => { if (chkPaiva.Checked) ApplyFilters(); };

        btnTyhjenna = new Button { Text = "Tyhjennä suodattimet", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        btnTyhjenna.Click += (_, __) =>
        {
            txtHaku.Clear();
            cboVerkko.SelectedIndex = 0;
            cboToimitus.SelectedIndex = 0;
            chkInterval.Checked = false;
            dtpAlku.Value = new DateTime(1985, 1, 1);
            dtpLoppu.Value = new DateTime(1999, 12, 31);
            chkPaiva.Checked = false;
            dtpPaiva.Value = new DateTime(1999, 12, 31);
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
        strip.Controls.Add(cboVerkko, 3, 1);
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
        grid.CellDoubleClick += Grid_CellDoubleClick;
        grid.Scroll += Grid_Scroll;
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

        AddColumn("Päivä", nameof(TvlrRow.PaivaStr), 85);
        AddColumn("Kello", nameof(TvlrRow.KelloStr), 60);
        AddColumn("Kesto", nameof(TvlrRow.KestoStr), 70);
        AddColumn("Kanava", nameof(TvlrRow.VerkkoNimi), 100);
        AddColumn("Nimi", nameof(TvlrRow.Nimi), 420, fill: true);
        AddColumn("Tekstitys", nameof(TvlrRow.TEKS), 90);
        AddColumn("Selostus", nameof(TvlrRow.SELO), 90);
        AddColumn("Toimitus", nameof(TvlrRow.TEKI), 160);
        AddColumn("DOCN", nameof(TvlrRow.DOCN), 90);
        grid.DataSource = _binding;
        layout.Controls.Add(grid, 0, 1);

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

        layout.Controls.Add(lblStatus, 0, 2);

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
    }
    // Lazy loading -asetukset
    // Ohjelmatietoja ladataan lisää vasta scrollattaessa
    private int _loadedRows = 0;

    private const int PageSize = 5000;

    private bool _loadingMore = false;

    private async void Grid_Scroll(
        object sender,
        ScrollEventArgs e)
    {
        if (_loadingMore)
            return;

        if (grid.RowCount == 0)
            return;

        int visible =
            grid.DisplayedRowCount(false);

        int first =
            grid.FirstDisplayedScrollingRowIndex;

        if (first + visible >= grid.RowCount - 50)
        {
            _loadingMore = true;

            try
            {
                lblStatus.Text =
                    "Ladataan lisää...";

                var moreRows =
                    await Task.Run(() =>
                        QueryDatabase(
                            PageSize,
                            _loadedRows));

                if (moreRows.Count > 0)
                {
                    var current =
                        (List<TvlrRow>)
                        _binding.DataSource;

                int scrollPos =
                    grid.FirstDisplayedScrollingRowIndex;

                current.AddRange(moreRows);

                _binding.ResetBindings(false);

                if (scrollPos >= 0 &&
                    scrollPos < grid.RowCount)
                {
                    grid.FirstDisplayedScrollingRowIndex =
                        scrollPos;
                }

                _loadedRows += moreRows.Count;
                }

            lblStatus.Text =
                $"Näytetään {_loadedRows:N0} ohjelmaa ({_totalRows:N0} yhteensä).";
            }
            finally
            {
                _loadingMore = false;
            }
        }
    }

    private int _totalRows = 0;

    // Laskee hakusuodatuksilla löytyvien ohjelmien kokonaismäärän
    private int CountDatabaseRows()
    {
        if (string.IsNullOrWhiteSpace(_dbPath))
            return 0;

        using var conn =
            new SqliteConnection($"Data Source={_dbPath}");

        conn.Open();

        var cmd = conn.CreateCommand();

        var sql = @"
        SELECT COUNT(*)
        FROM programs
        WHERE 1=1
        ";

        var term =
            NormalizeSearch(
                txtHaku.Text.Trim());
        var verkko = cboVerkko.SelectedItem?.ToString();
        var toimitus =
            cboToimitus.SelectedItem?.ToString()
            ?? "Kaikki";

        if (!string.IsNullOrWhiteSpace(term)
            && term.Length >= 2)
        {
            sql += " AND searchnimi LIKE $term";
            cmd.Parameters.AddWithValue(
                "$term",
                "%" + term + "%");
        }

        if (!string.IsNullOrWhiteSpace(verkko)
            && verkko != "Kaikki")
        {
            string verkkoNumero =
                verkko switch
                {
                    "YLE TV1" => "1",
                    "YLE TV2" => "2",
                    "MTV3" => "3",
                    "Nelonen" => "4",
                    "Subtv" => "6",
                    "Yle Fem" => "13",
                    "Yle Teema" => "14",
                    "Yle Extra" => "8",
                    "YLE24" => "15",
                    "TV Finland" => "22",
                    "MTV3+" => "30",
                    "Urheilukanava" => "31",
                    _ => verkko
                };

            sql += " AND verkko = $verkko";

            cmd.Parameters.AddWithValue(
                "$verkko",
                verkkoNumero);
        }

        if (!string.IsNullOrEmpty(toimitus)
            && toimitus != "Kaikki")
        {
            sql += " AND teki = $teki";

            cmd.Parameters.AddWithValue(
                "$teki",
                toimitus);
        }

        if (chkPaiva.Checked)
        {
            sql += " AND substr(pvm,1,10) = $pvm";

        cmd.Parameters.AddWithValue(
            "$pvm",
            dtpPaiva.Value.ToString("yyyy-MM-dd"));
        }
        else if (chkInterval.Checked)
        {
            sql +=
                " AND substr(pvm,1,10) BETWEEN $start AND $end";

        cmd.Parameters.AddWithValue(
            "$start",
            dtpAlku.Value.ToString("yyyy-MM-dd"));

        cmd.Parameters.AddWithValue(
            "$end",
            dtpLoppu.Value.ToString("yyyy-MM-dd"));
        }

        cmd.CommandText = sql;

        return Convert.ToInt32(
            cmd.ExecuteScalar());
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
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _searchTimer.Stop();

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

        _conn?.Dispose();

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        EnsureDatabaseCompatibility();
        _dbPath = dbPath;
        using var idx = _conn.CreateCommand();

        idx.CommandText = @"
        CREATE INDEX IF NOT EXISTS idx_nimi ON programs(nimi);
        CREATE INDEX IF NOT EXISTS idx_pvm ON programs(pvm);
        CREATE INDEX IF NOT EXISTS idx_verkko ON programs(verkko);
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

        // SQLite-suorituskykyasetukset
        using (var pragma = _conn.CreateCommand())
        {
            pragma.CommandText = @"
                PRAGMA journal_mode = DELETE;
                PRAGMA synchronous = NORMAL;
                PRAGMA cache_size = -16000;
            ";

            pragma.ExecuteNonQuery();
        }

        PopulateToimitusFromDb();

        try
        {
            ApplyFilters();
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


    private List<TvlrRow> QueryDatabase(
        int limit,
        int offset)
    {
        var list = new List<TvlrRow>();

        if (string.IsNullOrWhiteSpace(_dbPath))
            return list;

        using var conn =
            new SqliteConnection($"Data Source={_dbPath}");

        conn.Open();

        var cmd = conn.CreateCommand();

        var sql = @"
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
        WHERE 1=1
        ";

            var term =
                NormalizeSearch(
                    txtHaku.Text.Trim());
            var verkko = cboVerkko.SelectedItem?.ToString();
            var toimitus = cboToimitus.SelectedItem?.ToString() ?? "Kaikki";

            if (!string.IsNullOrWhiteSpace(term) && term.Length >= 2)
            {
                sql += " AND searchnimi LIKE $term";
                cmd.Parameters.AddWithValue(
                    "$term",
                    "%" + term + "%");
            }

            if (!string.IsNullOrWhiteSpace(verkko) && verkko != "Kaikki")
            {
                string verkkoNumero = verkko switch
                {
                    "YLE TV1" => "1",
                    "YLE TV2" => "2",
                    "MTV3" => "3",
                    "Nelonen" => "4",
                    "Subtv" => "6",
                    "Yle Fem" => "13",
                    "Yle Teema" => "14",
                    "Yle Extra" => "8",
                    "YLE24" => "15",
                    "TV Finland" => "22",
                    "MTV3+" => "30",
                    "Urheilukanava" => "31",
                    _ => verkko
                };

                sql += " AND verkko = $verkko";
                cmd.Parameters.AddWithValue("$verkko", verkkoNumero);
            }

            if (!string.IsNullOrEmpty(toimitus) && toimitus != "Kaikki")
            {
                sql += " AND teki = $teki";
                cmd.Parameters.AddWithValue("$teki", toimitus);
            }

            if (chkPaiva.Checked)
            {
                sql += " AND substr(pvm,1,10) = $pvm";

                cmd.Parameters.AddWithValue(
                    "$pvm",
                    dtpPaiva.Value.ToString("yyyy-MM-dd"));
            }
            else if (chkInterval.Checked)
            {
                sql += " AND substr(pvm,1,10) BETWEEN $start AND $end";

                cmd.Parameters.AddWithValue(
                    "$start",
                    dtpAlku.Value.ToString("yyyy-MM-dd"));

                cmd.Parameters.AddWithValue(
                    "$end",
                    dtpLoppu.Value.ToString("yyyy-MM-dd"));
            }

            sql += " ORDER BY pvm, kello, nimi";
            sql += " LIMIT $limit OFFSET $offset";

            cmd.Parameters.AddWithValue("$limit", limit);
            cmd.Parameters.AddWithValue("$offset", offset);

            cmd.CommandText = sql;

            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                DateTime paiva = DateTime.MinValue;

                DateTime.TryParse(
                    reader["pvm"]?.ToString(),
                    out paiva
                );

                int kesto = 0;

                int.TryParse(
                    reader["kesto"]?.ToString(),
                    out kesto
                );

        string rowVerkko = reader["verkko"]?.ToString() ?? "";

        TimeSpan? kello =
            ParseTime(reader["kello"]?.ToString() ?? "");

        TimeSpan kestoAika =
            TimeSpan.FromSeconds(kesto);

        var row = new TvlrRow
        {
            DOCN = reader["docn"]?.ToString() ?? "",

            Nimi = reader["nimi"]?.ToString() ?? "",

            Paiva = paiva,

            KelloTimeSpan = kello,

            KestoTimeSpan = kestoAika,

            Verkko = rowVerkko,

            TEKS = reader["teks"]?.ToString() ?? "",

            SELO = reader["selo"]?.ToString() ?? "",

            TEKI = reader["teki"]?.ToString() ?? "",

            TIETOJA = reader["tietoja"]?.ToString() ?? "",

            PaivaStr =
                paiva == DateTime.MinValue
                    ? ""
                    : paiva.ToString("dd.MM.yyyy"),

            KelloStr =
                kello.HasValue
                    ? $"{(int)kello.Value.TotalHours:00}:{kello.Value.Minutes:00}"
                    : "",

            KestoStr =
                kestoAika.TotalHours >= 1
                    ? $"{(int)kestoAika.TotalHours}:{kestoAika.Minutes:00}:{kestoAika.Seconds:00}"
                    : $"{kestoAika.Minutes:00}:{kestoAika.Seconds:00}",

            VerkkoNimi = rowVerkko switch
            {
                "1" => "YLE TV1",
                "2" => "YLE TV2",
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
        };

                list.Add(row);
            }

            return list;
        }

    private TimeSpan? ParseTime(string s)
    {
        if (TimeSpan.TryParse(s, out var t))
            return t;
        return null;
    }

    private string NormalizeSearch(string s)
        {
            return s
                .ToLowerInvariant()
                .Replace('ä', 'a')
                .Replace('ö', 'o')
                .Replace('å', 'a');
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
    private async void ApplyFilters()
    {
        try
        {
            _loadedRows = 0;

            Cursor = Cursors.WaitCursor;

            grid.Enabled = false;

            lblStatus.Text = "Ladataan...";

            _totalRows =
            await Task.Run(CountDatabaseRows);

            var rows =
                await Task.Run(() =>
                    QueryDatabase(PageSize, 0));

            _loadedRows = rows.Count;

            _binding.DataSource = rows;

            grid.ClearSelection();

        lblStatus.Text =
            $"Näytetään {_loadedRows:N0} ohjelmaa ({_totalRows:N0} yhteensä).";
        }
        finally
        {
            grid.Enabled = true;

            Cursor = Cursors.Default;
        }
    }
    private async Task DownloadFileAsync(string url, string path)
    {
        using var response = await Http.GetAsync(url);

        response.EnsureSuccessStatusCode();

        await using var fs = File.Create(path);

        await response.Content.CopyToAsync(fs);
    }

    // Tarkistaa palvelimelta tietokantapäivitykset (vihdoin tuli hyödyllistä käyttöä 15v. vanhalle tietokoneelle xD)
    private async void BtnPaivita_Click(object sender, EventArgs e)
    {
        try
        {

            lblStatus.Text = "Tarkistetaan päivityksiä...";

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
                MessageBox.Show("Tietokanta on ajan tasalla.");
                lblStatus.Text = "Tietokanta ajan tasalla.";
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

                lblStatus.Text =
                    $"Ladataan päivitystä v{update.version} @ https://telkkari.tv/tvlr/{update.file}";

                // Yhdistää ladatun päivitystietokannan pääkantaan (TVLR.db)
                int inserted =
                    await Task.Run(() => MergeDatabases(tempDb));

                totalInserted += inserted;
                duplicatesRemoved +=
                    await Task.Run(RemoveDuplicates);

                File.Delete(tempDb);
            }

            lblStatus.Text = "Optimoidaan tietokantaa...";

            await Task.Run(OptimizeDatabase);

            ApplyFilters();

            File.WriteAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "dbversio.txt"),
                serverInfo.version.ToString());

            lblStatus.Text = "Optimoidaan tietokantaa...";

            OptimizeDatabase();

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
            tietoja,
            searchnimi
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
            tietoja,
            LOWER(
                REPLACE(
                    REPLACE(
                        REPLACE(
                            REPLACE(
                                REPLACE(nimi,'Ä','a'),
                            'ä','a'),
                        'Ö','o'),
                    'ö','o'),
                'å','a')
            )
        FROM newdb.programs;
    ";

    int inserted = insertCmd.ExecuteNonQuery();

    using var detachCmd = _conn.CreateCommand();

    detachCmd.CommandText = "DETACH DATABASE newdb;";

    detachCmd.ExecuteNonQuery();

    return inserted;
}

}public class TvlrRow
{

    public string DOCN { get; set; } = "";
    public string Nimi { get; set; } = "";
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
