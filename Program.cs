using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Data.Sqlite;
using System.Reflection;
using System.Runtime.InteropServices;


// =====================================================
// TVLR-Selain 2.4
// =====================================================


internal static class Program
    {
    [STAThread]
    static void Main()
    {
        Application.SetCompatibleTextRenderingDefault(false);

        NativeMethods.SetCurrentProcessExplicitAppUserModelID("TVLRSelain");

        try
        {
            SovellusAsetukset.Lataa();

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
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

//Teeman asetus
public void AsetaTeema(Control juuri, bool tumma)
{
    Color bg = tumma ? Teema.TummaTausta : SystemColors.Control;
    Color fg = tumma ? Teema.TummaTeksti : SystemColors.ControlText;

    if (juuri is Form or Panel or TableLayoutPanel or FlowLayoutPanel)
    {
        juuri.BackColor = bg;
        juuri.ForeColor = fg;
    }

    foreach (Control c in juuri.Controls)
    {
        if (c is DataGridView)
            continue;
        if (c is TableLayoutPanel or FlowLayoutPanel)
            c.BackColor = tumma ? Teema.TummaTausta : SystemColors.Control;
        if (c is Label or CheckBox or RadioButton)
        {
            c.BackColor = bg;
            c.ForeColor = fg;
        }
        else if (c is TextBox tb)
        {
            tb.BackColor = tumma ? Color.FromArgb(32, 32, 32) : Color.White;
            tb.ForeColor = tumma ? Teema.TummaTeksti : Color.Black;
        }
        else if (c is Button b)
        {
            b.UseVisualStyleBackColor = false;
            b.BackColor = tumma ? Color.FromArgb(45, 45, 45) : SystemColors.Control;
            b.ForeColor = tumma ? Teema.TummaTeksti : SystemColors.ControlText;
        }
        else if (c is ComboBox cb)
        {
            cb.BackColor = tumma ? Color.FromArgb(32, 32, 32) : Color.White;
            cb.ForeColor = tumma ? Teema.TummaTeksti : Color.Black;
            cb.FlatStyle = FlatStyle.Popup;
        }
        else if (c is CheckBox)
        {
            c.ForeColor = tumma ? Teema.TummaTeksti : SystemColors.ControlText;
        }
        else if (c is DateTimePicker dtp)
        {
            if (tumma)
            {
                dtp.CalendarMonthBackground = Teema.TummaTausta;
                dtp.CalendarForeColor = Teema.TummaTeksti;
            }
        }
        AsetaTeema(c, tumma);
    }
}

    TextBox txtHaku;
    ComboBox cboVerkko;
    ComboBox cboToimitus;
    CheckBox chkInterval;
    DateTimePicker dtpAlku, dtpLoppu;
    CheckBox chkPaiva;
    DateTimePicker dtpPaiva;

    Button btnAvaa, btnTyhjenna, btnTietoa;
    DataGridView grid;
    Label lblStatus;

    private readonly BindingSource _binding = new();
    private readonly Timer _searchTimer = new Timer();
    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0)
            return;

        if (grid.Rows[e.RowIndex].DataBoundItem is not TvlrRow row)
            return;

        string kuvaus = GetTietoja(row.DOCN).Trim();

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
        this.Size = new Size(1630, 720);
        this.StartPosition = FormStartPosition.CenterScreen;
        Text = "TVLR-Selain 2.4";
        MinimumSize = new Size(1630, 250);

        _searchTimer.Interval = 150;

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

        btnAvaa = new Button { Text = "Avaa data…", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
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

        chkInterval = new CheckBox { Text = "Hae aikavälillä", AutoSize = true };
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
        btnPanel.Controls.Add(btnTietoa);
        strip.Controls.Add(btnPanel, 9, 1);

        layout.Controls.Add(strip, 0, 0);

        grid = new DataGridView
        {
            
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(248, 248, 248)
            },
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                WrapMode = DataGridViewTriState.False
            }
        };
        grid.CellDoubleClick += Grid_CellDoubleClick;
        grid.ClipboardCopyMode =
            DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.MultiSelect = true;

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
    }
    private string GetTietoja(string docn)
    {
        if (_conn == null)
            return "";

        using var cmd = _conn.CreateCommand();

        cmd.CommandText =
            "SELECT tietoja FROM programs WHERE docn = $docn LIMIT 1";

        cmd.Parameters.AddWithValue("$docn", docn);

        var result = cmd.ExecuteScalar();

        return result?.ToString() ?? "";
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
    private SqliteConnection? _conn;

    private void LoadFromDatabase(string dbPath)
    {

        _conn?.Dispose();

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();

        using (var pragma = _conn.CreateCommand())
        {
            pragma.CommandText = @"
                PRAGMA journal_mode = DELETE;
                PRAGMA synchronous = NORMAL;
                PRAGMA temp_store = MEMORY;
                PRAGMA cache_size = -128000;
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
                "CRASH",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }
    private void PopulateToimitusFromDb()
    {
        if (_conn == null)
            return;

        var cmd = _conn.CreateCommand();

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
    private List<TvlrRow> QueryDatabase()
        {
            var list = new List<TvlrRow>();

            if (_conn == null)
                return list;

            var cmd = _conn.CreateCommand();

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
                teki
            FROM programs
            WHERE 1=1
            ";

            var term = txtHaku.Text.Trim();
            var verkko = cboVerkko.SelectedItem?.ToString();
            var toimitus = cboToimitus.SelectedItem?.ToString() ?? "Kaikki";

            if (!string.IsNullOrWhiteSpace(term) && term.Length >= 2)
            {
                sql += " AND nimi LIKE $term";
                cmd.Parameters.AddWithValue("$term", term + "%");
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
                sql += " AND pvm = $pvm";
                cmd.Parameters.AddWithValue("$pvm", dtpPaiva.Value.Date);
            }
            else if (chkInterval.Checked)
            {
                sql += " AND pvm BETWEEN $start AND $end";
                cmd.Parameters.AddWithValue("$start", dtpAlku.Value.Date);
                cmd.Parameters.AddWithValue("$end", dtpLoppu.Value.Date);
            }

            sql += " ORDER BY pvm, kello, nimi";

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

                var row = new TvlrRow
                {
                    DOCN = reader["docn"]?.ToString() ?? "",

                    Nimi = reader["nimi"]?.ToString() ?? "",

                    Paiva = paiva,

                    KelloTimeSpan = ParseTime(
                        reader["kello"]?.ToString() ?? ""
                    ),

                    KestoTimeSpan = TimeSpan.FromSeconds(kesto),

                    Verkko = reader["verkko"]?.ToString() ?? "",

                    TEKS = reader["teks"]?.ToString() ?? "",

                    SELO = reader["selo"]?.ToString() ?? "",

                    TEKI = reader["teki"]?.ToString() ?? ""
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


    private void BtnAvaa_Click(object? sender, EventArgs e)
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

    private void ApplyFilters()
    {
        var rows = QueryDatabase();

        _binding.DataSource = rows;

        lblStatus.Text = $"Näytetään {rows.Count:N0} ohjelmaa.";
    }
}
public class TvlrRow
{
    public string DOCN { get; set; } = "";
    public string Nimi { get; set; } = "";
    public string TEKS { get; set; } = "";
    public string SELO { get; set; } = "";
    public string TEKI { get; set; } = "";

    public DateTime Paiva { get; set; } = DateTime.MinValue;
    public TimeSpan? KelloTimeSpan { get; set; }
    public TimeSpan? KestoTimeSpan { get; set; }
    public string Verkko { get; set; } = "";

    public string VerkkoNimi
    {
        get
        {
            return Verkko switch
            {
                "1" => "YLE TV1",
                "2" => "YLE TV2",
                "3" => "MTV3",
                "4" => "Nelonen",
                "6" => "Subtv",
                "13" => "Yle Fem",
                "14" => "Yle Teema",
                "15" => "YLE24",
                "22" => "TV Finland",
                "30" => "MTV3+",
                "31" => "Urheilukanava",
                _ => Verkko
            };
        }
    }

    public string PaivaStr => Paiva == DateTime.MinValue ? "" : Paiva.ToString("dd.MM.yyyy");
    public string KelloStr => KelloTimeSpan.HasValue ? $"{(int)KelloTimeSpan.Value.TotalHours:00}:{KelloTimeSpan.Value.Minutes:00}" : "";
    public string KestoStr
    {
        get
        {
            if (KestoTimeSpan == null) return "";
            var t = KestoTimeSpan.Value;
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        }
    }
}
public class AsetuksetForm : Form
{
    Button btnTallenna;
    CheckBox chkAinaPaalla;
    CheckBox chkTummaTeema;
    Button btnTietoja;

    public AsetuksetForm()
    {
        TopMost = SovellusAsetukset.AinaPaalla; 
        Text = "Asetukset";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 120);
        
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

        asettelu.Controls.Add(chkTummaTeema);


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
                "https://elavaarkisto.kokeile.yle.fi/data/\n\n" +
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

    public static readonly Color VaaleaTausta = SystemColors.Control;
    public static readonly Color VaaleaTeksti = SystemColors.ControlText;
}
static class NativeMethods
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SetCurrentProcessExplicitAppUserModelID(string AppID);
}
public static class SovellusAsetukset
{
    public static bool AinaPaalla { get; set; }
    public static bool TummaTeema { get; set; }

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
        }
    }
    public static void Tallenna()
    {
        var rivit = new[]
        {
            $"AinaPaalla={AinaPaalla}",
            $"TummaTeema={TummaTeema}"
        };

        File.WriteAllLines(AsetusTiedosto, rivit, Encoding.UTF8);
    }
}
