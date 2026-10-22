using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TVLR_Selain_3;

public partial class SovellusAsetukset : ObservableObject
{
    public static SovellusAsetukset Instance { get; } = new();

    [ObservableProperty] private bool _ainaPaalla = false;
    [ObservableProperty] private int _teema = 1; // 0 = Vaalea, 1 = Tumma
    [ObservableProperty] private bool _autoPaivitys = true;

    public static Dictionary<Key, string> Pikanappaimet { get; set; } = new();

    private readonly string AsetusTiedosto = Path.Combine(AppContext.BaseDirectory, "asetukset.ini");

    public void Lataa()
    {
        if (!File.Exists(AsetusTiedosto))
        {
            KaytaTeemaa();
            return;
        }

        foreach (var rivi in File.ReadAllLines(AsetusTiedosto))
        {
            var osat = rivi.Split('=', 2);
            if (osat.Length != 2) continue;
            var avain = osat[0].Trim();
            var arvo = osat[1].Trim();

            if (avain.Equals("AinaPaalla", StringComparison.OrdinalIgnoreCase) && bool.TryParse(arvo, out bool ap)) AinaPaalla = ap;
            if (avain.Equals("Teema", StringComparison.OrdinalIgnoreCase) && int.TryParse(arvo, out int tm)) Teema = (tm == 0 ? 0 : 1);
            if (avain.Equals("AutoPaivitys", StringComparison.OrdinalIgnoreCase) && bool.TryParse(arvo, out bool aup)) AutoPaivitys = aup;

            if (avain.StartsWith("Pikanappain_", StringComparison.OrdinalIgnoreCase))
            {
                string nappainNimi = avain.Substring("Pikanappain_".Length);
                if (Enum.TryParse<Key>(nappainNimi, true, out var nappain))
                {
                    Pikanappaimet[nappain] = arvo.Replace("\\n", "\n");
                }
            }
        }

        KaytaTeemaa();
    }

    public void Tallenna()
    {
        var rivit = new List<string>
        {
            $"AinaPaalla={AinaPaalla}",
            $"Teema={Teema}",
            $"AutoPaivitys={AutoPaivitys}",
        };

        foreach (var kv in Pikanappaimet)
        {
            rivit.Add($"Pikanappain_{kv.Key}={kv.Value.Replace("\n", "\\n")}");
        }

        File.WriteAllLines(AsetusTiedosto, rivit, Encoding.UTF8);
        KaytaTeemaa();
    }

    public void KaytaTeemaa()
    {
        var app = Application.Current;
        if (app != null)
        {
            app.RequestedThemeVariant = Teema == 0 ? ThemeVariant.Light : ThemeVariant.Dark;
        }

        OnPropertyChanged(nameof(IkkunanTausta));
        OnPropertyChanged(nameof(KenttaTausta));
        OnPropertyChanged(nameof(RiviTausta));
        OnPropertyChanged(nameof(ParillinenRiviTausta));
        OnPropertyChanged(nameof(TekstiVari));
        OnPropertyChanged(nameof(GridViivaVari));
        OnPropertyChanged(nameof(OtsikkoTausta));
        OnPropertyChanged(nameof(ValintaTausta));
    }

    public IBrush IkkunanTausta => Teema == 0
        ? new SolidColorBrush(Color.Parse("#F0F0F0"))
        : new SolidColorBrush(Color.Parse("#181818"));
    
    public IBrush KenttaTausta => Teema == 0
        ? new SolidColorBrush(Color.Parse("#FFFFFF"))
        : new SolidColorBrush(Color.Parse("#252525"));

    public IBrush RiviTausta => Teema == 0
        ? new SolidColorBrush(Color.Parse("#FFFFFF"))
        : new SolidColorBrush(Color.Parse("#181818"));

    public IBrush ParillinenRiviTausta => Teema == 0
        ? new SolidColorBrush(Color.Parse("#F8F8F8"))
        : new SolidColorBrush(Color.Parse("#1E1E1E"));

    public IBrush TekstiVari => Teema == 0
        ? new SolidColorBrush(Color.Parse("#000000"))
        : new SolidColorBrush(Color.Parse("#DCDCDC"));

    public IBrush GridViivaVari => Teema == 0
        ? new SolidColorBrush(Color.Parse("#A0A0A0"))
        : new SolidColorBrush(Color.Parse("#404040"));

    public IBrush OtsikkoTausta => Teema == 0
        ? new SolidColorBrush(Color.Parse("#F0F0F0"))
        : new SolidColorBrush(Color.Parse("#202020"));

    public IBrush ValintaTausta => new SolidColorBrush(Color.Parse("#0078D7"));
}
