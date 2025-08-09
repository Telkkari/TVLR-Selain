using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;


internal static class Program
{
    [STAThread]
    static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public class MainForm : Form
{
    TextBox txtHaku;
    ComboBox cboVerkko;
    CheckBox chkInterval;
    DateTimePicker dtpAlku, dtpLoppu;
    CheckBox chkPaiva;
    DateTimePicker dtpPaiva;

    Button btnAvaa, btnTyhjenna, btnTietoa;
    DataGridView grid;
    Label lblStatus;

    private readonly BindingList<TvlrRow> _allRows = new();
    private readonly BindingList<TvlrRow> _viewRows = new();

    public MainForm()
    {
        this.Size = new Size(1400, 720);
        this.StartPosition = FormStartPosition.CenterScreen;
        Text = "TV-lähetysrekisteri selain 1985–1999 2.0";
        MinimumSize = new Size(1200, 720);
        Icon = SystemIcons.Information;

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
            ColumnCount = 9,
            RowCount = 2,
            AutoSize = true
        };
        for (int i = 0; i < strip.ColumnCount; i++)
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        btnAvaa = new Button { Text = "Avaa data…", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        btnAvaa.Click += BtnAvaa_Click;

        txtHaku = new TextBox { PlaceholderText = "Hae ohjelman nimellä…", Width = 260 };
        txtHaku.TextChanged += (_, __) => ApplyFilters();

        cboVerkko = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        cboVerkko.Items.AddRange(new object[] { "1 ja 2", "1", "2" });
        cboVerkko.SelectedIndex = 0;
        cboVerkko.SelectedIndexChanged += (_, __) => ApplyFilters();

        chkInterval = new CheckBox { Text = "Hae aikavälillä", AutoSize = true };
        chkInterval.CheckedChanged += (_, __) => { UpdateDatePickersEnabled(); ApplyFilters(); };

        dtpAlku = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 120 };
        dtpAlku.Value = new DateTime(1985, 1, 1);
        dtpAlku.ValueChanged += (_, __) => ApplyFilters();

        dtpLoppu = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 120 };
        dtpLoppu.Value = new DateTime(1999, 12, 31);
        dtpLoppu.ValueChanged += (_, __) => ApplyFilters();

        chkPaiva = new CheckBox { Text = "Hae päivämäärällä", AutoSize = true };
        chkPaiva.CheckedChanged += (_, __) => { UpdateDatePickersEnabled(); ApplyFilters(); };

        dtpPaiva = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 120 };
        dtpPaiva.Value = new DateTime(1999, 12, 31);
        dtpPaiva.ValueChanged += (_, __) => { if (chkPaiva.Checked) ApplyFilters(); };

        btnTyhjenna = new Button { Text = "Tyhjennä suodattimet", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        btnTyhjenna.Click += (_, __) =>
        {
            txtHaku.Clear();
            cboVerkko.SelectedIndex = 0;
            chkInterval.Checked = false;
            dtpAlku.Value = new DateTime(1985, 1, 1);
            dtpLoppu.Value = new DateTime(1999, 12, 31);
            chkPaiva.Checked = false;
            dtpPaiva.Value = new DateTime(1999, 12, 31);
            ApplyFilters();
        };

        btnTietoa = new Button { Text = "Tietoja", AutoSize = true, Padding = new Padding(10, 6, 10, 6) };
        btnTietoa.Click += (_, __) =>
        {
            MessageBox.Show(
                "Datan lisenssi: CC0-lisenssi: ei tekijänoikeutta. Dataa voi lupaa pyytämättä kopioida, muokata, levittää ja esittää, mukaan lukien kaupallisessa tarkoituksessa.\n\n" +
                "https://elavaarkisto.kokeile.yle.fi/data/\n\n" +                
                "Telkkari 2025",
                "Tietoja", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        strip.Controls.Add(new Label { Text = "Avaa data", AutoSize = true }, 0, 0);
        strip.Controls.Add(new Label { Text = "Haku", AutoSize = true }, 1, 0);
        strip.Controls.Add(new Label { Text = "Verkko", AutoSize = true }, 2, 0);
        strip.Controls.Add(new Label { Text = "", AutoSize = true }, 3, 0);
        strip.Controls.Add(new Label { Text = "Alku pvm", AutoSize = true }, 4, 0);
        strip.Controls.Add(new Label { Text = "Loppu pvm", AutoSize = true }, 5, 0);
        strip.Controls.Add(new Label { Text = "", AutoSize = true }, 6, 0);
        strip.Controls.Add(new Label { Text = "Päivämäärä", AutoSize = true }, 7, 0);

        strip.Controls.Add(btnAvaa, 0, 1);
        strip.Controls.Add(txtHaku, 1, 1);
        strip.Controls.Add(cboVerkko, 2, 1);
        strip.Controls.Add(chkInterval, 3, 1);
        strip.Controls.Add(dtpAlku, 4, 1);
        strip.Controls.Add(dtpLoppu, 5, 1);
        strip.Controls.Add(chkPaiva, 6, 1);
        strip.Controls.Add(dtpPaiva, 7, 1);

        var btnPanel = new FlowLayoutPanel { AutoSize = true };
        btnPanel.Controls.Add(btnTyhjenna);
        btnPanel.Controls.Add(btnTietoa);
        strip.Controls.Add(btnPanel, 8, 1);

        layout.Controls.Add(strip, 0, 0);

        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(248, 248, 248) },
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                WrapMode = DataGridViewTriState.False
            }
        };
        AddColumn("Päivä", nameof(TvlrRow.PaivaStr), 85);
        AddColumn("Kello", nameof(TvlrRow.KelloStr), 60);
        AddColumn("Kesto", nameof(TvlrRow.KestoStr), 70);
        AddColumn("Verkko", nameof(TvlrRow.Verkko), 60);
        AddColumn("Nimi", nameof(TvlrRow.Nimi), 420, fill: true);
        AddColumn("Tekstitys", nameof(TvlrRow.TEKS), 90);
        AddColumn("Selostus", nameof(TvlrRow.SELO), 90);
        AddColumn("Toimitus", nameof(TvlrRow.TEKI), 160);
        AddColumn("DOCN", nameof(TvlrRow.DOCN), 90);
        grid.DataSource = _viewRows;
        layout.Controls.Add(grid, 0, 1);

        lblStatus = new Label { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(2, 8, 2, 8) };
        layout.Controls.Add(lblStatus, 0, 2);

        UpdateDatePickersEnabled();
        UpdateStatus();

        TryAutoLoadData();
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

    private void TryAutoLoadData()
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string csv = Path.Combine(baseDir, "TVLR_combi_publicV1.csv");
            string zip = Path.Combine(baseDir, "tvlahetysrekisteri1985-1999.zip");

            if (File.Exists(csv))
            {
                LoadIntoGrid(LoadTvlr(csv));
                return;
            }
            if (File.Exists(zip))
            {
                LoadIntoGrid(LoadTvlr(zip));
                return;
            }

            lblStatus.Text = "Dataa ei löytynyt sovelluskansiosta. Aseta TVLR_combi_publicV1.csv tai tvlahetysrekisteri1985-1999.zip samaan kansioon, tai avaa itse.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Automaattinen lataus epäonnistui:\n" + ex.Message, "Virhe", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadIntoGrid(List<TvlrRow> rows)
    {
        _allRows.Clear();
        foreach (var r in rows) _allRows.Add(r);
        ApplyFilters();
        lblStatus.Text = $"Luettiin {rows.Count:N0} riviä. {(_viewRows.Count == rows.Count ? "Ei suodatusta." : "")}";
    }

    private void BtnAvaa_Click(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "ZIP tai CSV|*.zip;*.csv",
            Title = "Valitse Lähetysrekisteri-data (zip tai csv)"
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                LoadIntoGrid(LoadTvlr(ofd.FileName));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Virhe datan lukemisessa:\n" + ex.Message, "Virhe", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }

    private void ApplyFilters()
    {
        var term = (txtHaku.Text ?? "").Trim();
        var verkkoFilter = cboVerkko.SelectedItem?.ToString() ?? "Kaikki";

        DateTime start, end;
        bool useDateFilter = false;

        if (chkPaiva.Checked)
        {
            start = dtpPaiva.Value.Date;
            end = start;
            useDateFilter = true;
        }
        else if (chkInterval.Checked)
        {
            start = dtpAlku.Value.Date;
            end = dtpLoppu.Value.Date;
            useDateFilter = true;
        }
        else
        {
            start = DateTime.MinValue.Date;
            end = DateTime.MaxValue.Date;
        }

        IEnumerable<TvlrRow> q = _allRows;

        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.ToLowerInvariant();
            q = q.Where(r => (r.Nimi ?? "").ToLowerInvariant().Contains(t));
        }

        if (verkkoFilter == "1" || verkkoFilter == "2")
        {
            q = q.Where(r => r.Verkko == verkkoFilter);
        }

        if (useDateFilter)
        {
            q = q.Where(r => r.Paiva >= start && r.Paiva <= end);
        }

        var arr = q.OrderBy(r => r.Paiva)
                   .ThenBy(r => r.KelloTimeSpan ?? TimeSpan.Zero)
                   .ThenBy(r => r.Nimi)
                   .ToArray();

        _viewRows.RaiseListChangedEvents = false;
        _viewRows.Clear();
        foreach (var r in arr) _viewRows.Add(r);
        _viewRows.RaiseListChangedEvents = true;
        _viewRows.ResetBindings();

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        string rangeText = chkPaiva.Checked
            ? $"Päivä: {dtpPaiva.Value:yyyy-MM-dd}"
            : (chkInterval.Checked
                ? $"Aikaväli: {dtpAlku.Value:yyyy-MM-dd} – {dtpLoppu.Value:yyyy-MM-dd}"
                : "Ei päiväsuodatusta");

        lblStatus.Text = _allRows.Count == 0
            ? "Lataa data automaattisesti tai avaa tiedosto."
            : $"Näytetään {_viewRows.Count:N0}/{_allRows.Count:N0} ohjelmaa. {rangeText}.";
    }

    private List<TvlrRow> LoadTvlr(string path)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            var entry = zip.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .OrderByDescending(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(e => e.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (entry == null) throw new InvalidOperationException("ZIP-arkistossa ei ole csv-tiedostoja.");

            using var s = entry.Open();
            using var mem = new MemoryStream();
            s.CopyTo(mem);
            mem.Position = 0;
            return LoadTvlrFromStream(mem, guessEncodings: true);
        }
        else
        {
            using var fs = File.OpenRead(path);
            using var mem = new MemoryStream();
            fs.CopyTo(mem);
            mem.Position = 0;
            return LoadTvlrFromStream(mem, guessEncodings: true);
        }
    }

    private List<TvlrRow> LoadTvlrFromStream(Stream stream, bool guessEncodings)
    {
        var encodings = guessEncodings
            ? new[] {
                new UTF8Encoding(false),
                Encoding.UTF8,
                Encoding.GetEncoding(1252),
                Encoding.Latin1,
                Encoding.GetEncoding(28605)
              }
            : new[] { Encoding.UTF8 };

        foreach (var enc in encodings)
        {
            stream.Position = 0;
            try
            {
                using var parser = new TextFieldParser(stream, enc, detectEncoding: true)
                {
                    TextFieldType = FieldType.Delimited,
                    Delimiters = new[] { "," },
                    HasFieldsEnclosedInQuotes = true,
                    TrimWhiteSpace = false
                };
                var rows = ParseRows(parser);
                if (rows.Count > 0) return rows;
            }
            catch
            {

            }
        }
        return new List<TvlrRow>();
    }

    private List<TvlrRow> ParseRows(TextFieldParser parser)
    {
        var list = new List<TvlrRow>(200_000);
        bool headerSkipped = false;

        while (!parser.EndOfData)
        {
            string[]? parts = parser.ReadFields();
            if (parts == null) continue;

            if (!headerSkipped && parts.Length >= 2 && parts[0].Equals("DOCN", StringComparison.OrdinalIgnoreCase))
            {
                headerSkipped = true;
                continue;
            }
            headerSkipped = true;

            if (parts.Length < 8)
            {
                Array.Resize(ref parts, 8);
                for (int i = 0; i < 8; i++) parts[i] ??= string.Empty;
            }
            else if (parts.Length > 8)
            {
                parts[7] = string.Join(",", parts.Skip(7));
                Array.Resize(ref parts, 8);
            }

            var row = new TvlrRow
            {
                DOCN     = (parts[0] ?? "").Trim(),
                Nimi     = (parts[1] ?? "").Trim(),
                KEST_raw = (parts[2] ?? "").Trim(),
                LPVM_raw = (parts[3] ?? "").Trim(),
                TEKS     = (parts[4] ?? "").Trim(),
                SELO     = (parts[5] ?? "").Trim(),
                LISA     = (parts[6] ?? "").Trim(),
                TEKI     = (parts[7] ?? "").Trim()
            };

            row.Paiva         = ParseDateYyyyMmDd(row.LPVM_raw) ?? DateTime.MinValue;
            row.KelloTimeSpan = ExtractTimeFromLISA(row.LISA);
            row.Verkko        = ExtractVerkkoFromLISA(row.LISA);
            row.KestoTimeSpan = ParseMmmss(row.KEST_raw);

            list.Add(row);
        }
        return list;
    }

    private static DateTime? ParseDateYyyyMmDd(string s)
    {
        if (DateTime.TryParseExact(s, new[] { "yyyyMMdd", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.Date;
        return null;
    }

    private static TimeSpan? ParseMmmss(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var digits = new string(s.Where(char.IsDigit).ToArray());
        if (digits.Length < 3) return null;

        string secStr = digits[^2..];
        string minStr = digits[..^2];

        if (!int.TryParse(secStr, NumberStyles.None, CultureInfo.InvariantCulture, out int ss)) return null;
        if (!long.TryParse(minStr, NumberStyles.None, CultureInfo.InvariantCulture, out long mm)) return null;

        ss = Math.Clamp(ss, 0, 59);
        if (mm < 0) mm = 0;

        double totalSeconds = mm * 60.0 + ss;
        try { return TimeSpan.FromSeconds(totalSeconds); }
        catch { return TimeSpan.MaxValue; }
    }

    private static TimeSpan? ExtractTimeFromLISA(string lisa)
    {
        if (string.IsNullOrEmpty(lisa)) return null;
        var m = Regex.Match(lisa, @"Kello:\s*(\d{1,2}):(\d{1,2})");
        if (m.Success)
        {
            int hh = int.TryParse(m.Groups[1].Value, out var h) ? Math.Clamp(h, 0, 23) : 0;
            int mm = int.TryParse(m.Groups[2].Value, out var mi) ? Math.Clamp(mi, 0, 59) : 0;
            return new TimeSpan(hh, mm, 0);
        }
        return null;
    }

    private static string ExtractVerkkoFromLISA(string lisa)
    {
        if (string.IsNullOrEmpty(lisa)) return "";
        var m = Regex.Match(lisa, @"Verkko:\s*(\d)");
        if (m.Success)
        {
            var v = m.Groups[1].Value;
            if (v is "1" or "2") return v;
        }
        if (lisa.Contains("TV1", StringComparison.OrdinalIgnoreCase)) return "1";
        if (lisa.Contains("TV2", StringComparison.OrdinalIgnoreCase)) return "2";
        return "";
    }
}

public class TvlrRow
{
    public string DOCN { get; set; } = "";
    public string Nimi { get; set; } = "";
    public string KEST_raw { get; set; } = "";
    public string LPVM_raw { get; set; } = "";
    public string TEKS { get; set; } = "";
    public string SELO { get; set; } = "";
    public string LISA { get; set; } = "";
    public string TEKI { get; set; } = "";

    public DateTime Paiva { get; set; } = DateTime.MinValue;
    public TimeSpan? KelloTimeSpan { get; set; }
    public TimeSpan? KestoTimeSpan { get; set; }
    public string Verkko { get; set; } = "";

    public string PaivaStr => Paiva == DateTime.MinValue ? "" : Paiva.ToString("yyyy-MM-dd");
    public string KelloStr => KelloTimeSpan.HasValue ? $"{(int)KelloTimeSpan.Value.TotalHours:00}:{KelloTimeSpan.Value.Minutes:00}" : "";
    public string KestoStr
    {
        get
        {
            if (KestoTimeSpan == null || KestoTimeSpan == TimeSpan.MaxValue) return "";
            var t = KestoTimeSpan.Value;
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        }
    }
}

