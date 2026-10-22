using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace TVLR_Selain_3;

public partial class AsetuksetWindow : Window
{
    public AsetuksetWindow()
    {
        InitializeComponent();
        DataContext = SovellusAsetukset.Instance;
    }

    private void Tallenna_Click(object? sender, RoutedEventArgs e)
    {
        SovellusAsetukset.Instance.Tallenna();
        Close();
    }

    private void Tietoja_Click(object? sender, RoutedEventArgs e)
    {
        var popup = new Window
        {
            Title = "Tietoja",
            Width = 520,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                Text = "Datan lisenssi: CC0-lisenssi: ei tekijänoikeutta. Dataa voi lupaa pyytämättä kopioida, muokata, levittää ja esittää, mukaan lukien kaupallisessa tarkoituksessa.\n\nOhjelmatietojen lähteet:\n\nhttps://elavaarkisto.kokeile.yle.fi/data/\nhttps://telkussa.fi/\nhttps://web.archive.org/web/20160821021501/http://netello.fi/tv?MODULI_pvm=22012001\nhttps://files.mpoli.fi/software/TEXTS/TV-RADIO/\nhttps://web.archive.org/web/20000917221050/http://www.fi/tanaan/tv-opas/\nhttps://web.archive.org/web/19970817131201/http://www.freenet.hut.fi/ohjelmatiedot\nhttps://hs.fi/\n\nTVLR-Selain 3.0.0 \nTelkkari 2026",
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10)
            }
        };
        popup.ShowDialog(this);
    }

    private void Pikanappaimet_Click(object? sender, RoutedEventArgs e)
    {
        var popup = new Window
        {
            Title = "Pikanäppäimet",
            Width = 520,
            Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBox
            {
                Text = "Pikanäppäimet:\n\n" +
                       "Ctrl+F = Siirry hakukenttään\n" +
                       "Ctrl+Alt+F = Etsi ja korosta näkyvistä ohjelmista\n" +
                       "Ctrl+K = Avaa tai sulje kanavavalinta\n" +
                       "Ctrl+P = Ota päivämäärällä haku käyttöön\n" +
                       "Ctrl+A = Ota päivämääräväli haku käyttöön (tai Valitse kaikki hakukentässä)\n" +
                       "Ctrl+Delete = Tyhjennä hakukenttä\n" +
                       "Ctrl+Backspace = Tyhjennä kaikki suodattimet\n" +
                       "Ctrl+T = Uusi välilehti\n" +
                       "Ctrl+W = Sulje välilehti\n" +
                       "Ctrl+1-9 = Vaihda välilehteä\n" +
                       "Ctrl+Tab / Ctrl+Shift+Tab = Seuraava / Edellinen välilehti\n" +
                       "F1-F12 = Kopioi valitut ohjelmat\n" +
                       "Space / Tuplaklikkaus = Avaa ohjelman kuvaus\n" +
                       "Tuplaklikkaus päivämäärää tai kanavaa = Suodata haku kyseiselle päivälle ja kanavalle\n" +
                       "Hiiren oikea painike = Kopioi solun teksti leikepöydälle\n" +
                       "Ctrl + Hiiren rulla = Suurenna / pienennä hakutulosten fonttikokoa\n" +
                       "Esc = Sulje ponnahdusikkunat",
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10)
            }
        };
        popup.ShowDialog(this);
    }

    private async void Kopiointi_Click(object? sender, RoutedEventArgs e)
    {
        var popup = new Window
        {
            Title = "Kopiointipikanäppäimet",
            Width = 720,
            Height = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var panel = new StackPanel { Margin = new Thickness(10), Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "Merkit: {NIMI} {PVM} {KANAVA} {KELLO} {KESTO} {TOIMITUS}\nEsim. \"{NIMI} ({PVM} {KANAVA})\" → Tuunilaisten maailmanvalloitus (20.4.1993 MTV3)",
            TextWrapping = TextWrapping.Wrap
        });

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var textboxes = new Dictionary<Key, TextBox>();

        for (int i = 1; i <= 12; i++)
        {
            Key key = Key.F1 + (i - 1);
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var lbl = new TextBlock
            {
                Text = key.ToString(),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 8)
            };
            Grid.SetRow(lbl, i - 1);
            Grid.SetColumn(lbl, 0);

            var txt = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            if (SovellusAsetukset.Pikanappaimet.TryGetValue(key, out string? val))
            {
                txt.Text = val;
            }

            Grid.SetRow(txt, i - 1);
            Grid.SetColumn(txt, 1);

            textboxes[key] = txt;
            grid.Children.Add(lbl);
            grid.Children.Add(txt);
        }

        var scroll = new ScrollViewer { Content = grid, Height = 280 };
        panel.Children.Add(scroll);

        var btnSave = new Button
        {
            Content = "Tallenna",
            Background = Brushes.DodgerBlue,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        btnSave.Click += (s, ev) =>
        {
            foreach (var kv in textboxes)
            {
                string txtVal = kv.Value.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(txtVal))
                {
                    SovellusAsetukset.Pikanappaimet.Remove(kv.Key);
                }
                else
                {
                    SovellusAsetukset.Pikanappaimet[kv.Key] = txtVal;
                }
            }
            SovellusAsetukset.Instance.Tallenna();
            popup.Close();
        };

        panel.Children.Add(btnSave);
        popup.Content = panel;
        await popup.ShowDialog(this);
    }
}