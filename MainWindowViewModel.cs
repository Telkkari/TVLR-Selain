using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;

namespace TVLR_Selain_3;

public partial class KanavaValinta : ObservableObject
{
    public string Nimi { get; }
    private readonly Action<KanavaValinta> _onSelectionChanged;

    public KanavaValinta(string nimi, Action<KanavaValinta> onSelectionChanged)
    {
        Nimi = nimi;
        _onSelectionChanged = onSelectionChanged;
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) _onSelectionChanged(this); }
    }
}

public partial class Valilehti : ObservableObject
{
    [ObservableProperty] private string _otsikko = "";
    public string Haku = "";
    public bool HaeKuvauksesta = false;
    public decimal VuosiArvo = 1996;
    public bool HaeVuodella = false;
    public string Toimitus = "Kaikki";
    public List<string> ValitutKanavat = new() { "Kaikki" };
    public DateTime? AlkuPvm = new DateTime(1985, 1, 1);
    public DateTime? LoppuPvm = new DateTime(2007, 12, 31);
    public bool HaeValilla = false;
    public DateTime? TarkkaPaiva = new DateTime(1996, 12, 31);
    public bool HaePaivalla = false;
    public int ViikonpaivaIndeksi = 0;
    public bool HaeViikonpaivalla = false;
    public string KelloAlkuText = "0:00";
    public string KelloLoppuText = "23:59";
    public bool HaeKellonajalla = false;

    public string Lajittelu = "date";
    public bool UusinEnsin = false;
    public bool PisinEnsin = false;
    public string Korostus = "";

    public string MuistettuDocn = "";
    public DateTime? MuistettuPaiva = null;
    public List<TvlrRow>? TallennetutTulokset = null;
}

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private SqliteConnection? _conn;
    private string _dbPath = "";
    private TvlrRow[] _allPrograms = Array.Empty<TvlrRow>();
    private CancellationTokenSource? _hakuViiveCancel;

    [ObservableProperty] private bool _onkoTietokantaLatautumassa = true;
    public bool IsLoadingState { get; private set; } = false;
    public event Action<TvlrRow>? PyydaSkrollaus;

    [ObservableProperty] private string _ikkunaOtsikko = "TVLR-Selain 3";
    [ObservableProperty] private IEnumerable<TvlrRow> _naytettavatOhjelmat = Array.Empty<TvlrRow>();
    [ObservableProperty] private TvlrRow? _valittuOhjelma;
    [ObservableProperty] private string _statusTeksti = "Ladataan tietokantaa muistiin...";

    // Päivämäärä / Kesto
    [ObservableProperty] private string _currentSort = "date";
    [ObservableProperty] private bool _sortNewestFirst = false;
    [ObservableProperty] private bool _sortLongestFirst = false;

    // Tuloksista haku ja korostus (Ctrl+Alt+F)
    [ObservableProperty] private string _korostusHaku = "";
    [ObservableProperty] private bool _onkoKorostusPalkkiNakyvilla = false;
    [ObservableProperty] private string _korostusSijaintiTeksti = "";
    private readonly List<int> _highlightRowIndices = new();
    private int _currentHighlightIndex = -1;

    partial void OnValittuOhjelmaChanged(TvlrRow? value)
    {
        if (!IsLoadingState && value != null && !string.IsNullOrEmpty(value.DOCN) && AktiivinenValilehti != null)
        {
            AktiivinenValilehti.MuistettuDocn = value.DOCN;
            AktiivinenValilehti.MuistettuPaiva = value.Paiva;
        }
    }

    public ObservableCollection<Valilehti> Valilehdet { get; } = new();

    private Valilehti? _aktiivinenValilehti;
    public Valilehti? AktiivinenValilehti
    {
        get => _aktiivinenValilehti;
        set
        {
            if (_aktiivinenValilehti == value) return;

            if (_aktiivinenValilehti != null && value != null)
            {
                TallennaTila(_aktiivinenValilehti);
            }

            if (SetProperty(ref _aktiivinenValilehti, value))
            {
                if (value != null)
                {
                    LataaTila(value);
                }
            }
        }
    }

    [RelayCommand]
    public void LisaaValilehti()
    {
        if (Valilehdet.Count >= 9) return;

        var uusi = new Valilehti();
        Valilehdet.Add(uusi);
        PaivitaValilehtienNumerot();
        AktiivinenValilehti = uusi;
    }

    [RelayCommand]
    public void SuljeValilehti(Valilehti? v)
    {
        if (v == null || Valilehdet.Count <= 1) return;

        int idx = Valilehdet.IndexOf(v);
        if (idx < 0) return;

        bool wasActive = (v == AktiivinenValilehti);

        if (wasActive)
        {
            int nextIdx = (idx == Valilehdet.Count - 1) ? idx - 1 : idx + 1;
            var nextTab = Valilehdet[nextIdx];

            // Vaihdetaan suoraan seuraavaan aktiiviseen välilehteen ilman poistuvan tilan tallennusta
            _aktiivinenValilehti = nextTab;
            OnPropertyChanged(nameof(AktiivinenValilehti));
            LataaTila(nextTab);
        }

        Valilehdet.Remove(v);
        PaivitaValilehtienNumerot();
    }

    public void ValitseValilehti(int indeksi)
    {
        if (indeksi >= 0 && indeksi < Valilehdet.Count)
        {
            AktiivinenValilehti = Valilehdet[indeksi];
        }
    }

    public void SeuraavaValilehti()
    {
        if (Valilehdet.Count <= 1 || AktiivinenValilehti == null) return;
        int curIdx = Valilehdet.IndexOf(AktiivinenValilehti);
        int nextIdx = (curIdx + 1) % Valilehdet.Count;
        ValitseValilehti(nextIdx);
    }

    public void EdellinenValilehti()
    {
        if (Valilehdet.Count <= 1 || AktiivinenValilehti == null) return;
        int curIdx = Valilehdet.IndexOf(AktiivinenValilehti);
        int prevIdx = (curIdx - 1 + Valilehdet.Count) % Valilehdet.Count;
        ValitseValilehti(prevIdx);
    }

    public void PaivitaValilehtienNumerot()
    {
        for (int i = 0; i < Valilehdet.Count; i++)
        {
            var tab = Valilehdet[i];
            int num = i + 1;

            string displayText = !string.IsNullOrWhiteSpace(tab.Haku)
                ? tab.Haku.Trim()
                : $"Välilehti {num}";

            if (displayText.Length > 18) displayText = displayText.Substring(0, 18) + "…";
            tab.Otsikko = $"{num}: {displayText}";
        }
    }

    private void PaivitaAktiivisenOtsikko()
    {
        if (AktiivinenValilehti == null || IsLoadingState) return;
        AktiivinenValilehti.Haku = Haku;
        PaivitaValilehtienNumerot();
    }

    private void TallennaTila(Valilehti t)
    {
        t.Haku = Haku;
        t.HaeKuvauksesta = HaeKuvauksesta;
        t.VuosiArvo = VuosiArvo;
        t.HaeVuodella = HaeVuodella;
        t.Toimitus = Toimitus;
        t.ValitutKanavat = KanavatLista.Where(k => k.IsSelected).Select(k => k.Nimi).ToList();
        t.AlkuPvm = AlkuPvm;
        t.LoppuPvm = LoppuPvm;
        t.HaeValilla = HaeValilla;
        t.TarkkaPaiva = TarkkaPaiva;
        t.HaePaivalla = HaePaivalla;
        t.ViikonpaivaIndeksi = ViikonpaivaIndeksi;
        t.HaeViikonpaivalla = HaeViikonpaivalla;
        t.KelloAlkuText = KelloAlkuText;
        t.KelloLoppuText = KelloLoppuText;
        t.HaeKellonajalla = HaeKellonajalla;

        t.Lajittelu = CurrentSort;
        t.UusinEnsin = SortNewestFirst;
        t.PisinEnsin = SortLongestFirst;
        t.Korostus = KorostusHaku;

        if (ValittuOhjelma != null && !string.IsNullOrEmpty(ValittuOhjelma.DOCN))
        {
            t.MuistettuDocn = ValittuOhjelma.DOCN;
            t.MuistettuPaiva = ValittuOhjelma.Paiva;
        }

        if (NaytettavatOhjelmat is List<TvlrRow> currentList)
        {
            t.TallennetutTulokset = currentList;
        }
    }

    private void LataaTila(Valilehti t)
    {
        IsLoadingState = true;

        Haku = t.Haku;
        HaeKuvauksesta = t.HaeKuvauksesta;
        VuosiArvo = t.VuosiArvo;
        HaeVuodella = t.HaeVuodella;
        Toimitus = t.Toimitus;

        _isUpdatingKanavat = true;
        foreach (var k in KanavatLista)
            k.IsSelected = t.ValitutKanavat.Contains(k.Nimi);
        PaivitaKanavaTeksti();
        _isUpdatingKanavat = false;

        AlkuPvm = t.AlkuPvm;
        LoppuPvm = t.LoppuPvm;
        HaeValilla = t.HaeValilla;
        TarkkaPaiva = t.TarkkaPaiva;
        HaePaivalla = t.HaePaivalla;
        ViikonpaivaIndeksi = t.ViikonpaivaIndeksi;
        HaeViikonpaivalla = t.HaeViikonpaivalla;
        KelloAlkuText = t.KelloAlkuText;
        KelloLoppuText = t.KelloLoppuText;
        HaeKellonajalla = t.HaeKellonajalla;

        CurrentSort = t.Lajittelu;
        SortNewestFirst = t.UusinEnsin;
        SortLongestFirst = t.PisinEnsin;
        KorostusHaku = t.Korostus;

        IsLoadingState = false;

        if (t.TallennetutTulokset != null && t.TallennetutTulokset.Count > 0)
        {
            NaytettavatOhjelmat = t.TallennetutTulokset;
            StatusTeksti = $"Näytetään {t.TallennetutTulokset.Count:N0} ohjelmaa.";
            
            if (!string.IsNullOrWhiteSpace(t.Korostus))
            {
                PaivitaKorostukset(t.TallennetutTulokset);
            }
            else
            {
                OnkoKorostusPalkkiNakyvilla = false;
                _highlightRowIndices.Clear();
                _currentHighlightIndex = -1;
                KorostusSijaintiTeksti = "";
            }

            TvlrRow? loydetty = null;
            if (!string.IsNullOrEmpty(t.MuistettuDocn))
            {
                loydetty = t.TallennetutTulokset.FirstOrDefault(x => x.DOCN == t.MuistettuDocn);
                if (loydetty == null && t.MuistettuPaiva.HasValue && t.TallennetutTulokset.Count > 0)
                {
                    loydetty = t.TallennetutTulokset
                        .OrderBy(x => Math.Abs((x.Paiva - t.MuistettuPaiva.Value).TotalSeconds))
                        .FirstOrDefault();
                }
            }

            if (loydetty == null && t.TallennetutTulokset.Count > 0)
            {
                loydetty = t.TallennetutTulokset[0];
            }

            if (loydetty != null)
            {
                ValittuOhjelma = loydetty;
                PyydaSkrollaus?.Invoke(loydetty);
            }
            else
            {
                ValittuOhjelma = null;
            }
        }
        else
        {
            TeeSuodatusHeti(valitseValmisRiviDocn: t.MuistettuDocn);
        }

        PaivitaIkkunaOtsikko();
    }

    private string _haku = "";
    public string Haku
    {
        get => _haku;
        set
        {
            if (SetProperty(ref _haku, value))
            {
                if (!string.IsNullOrWhiteSpace(KorostusHaku))
                {
                    SuljeKorostus();
                }
                PaivitaAktiivisenOtsikko();
                if (!IsLoadingState) PaivitaIkkunaOtsikko();
                TeeSuodatusHeti();
            }
        }
    }

    private bool _haeKuvauksesta;
    public bool HaeKuvauksesta { get => _haeKuvauksesta; set { if (SetProperty(ref _haeKuvauksesta, value)) TeeSuodatusHeti(); } }

    private bool _haeVuodella;
    public bool HaeVuodella { get => _haeVuodella; set { if (SetProperty(ref _haeVuodella, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }

    private decimal _vuosiArvo = 1996;
    public decimal VuosiArvo { get => _vuosiArvo; set { if (SetProperty(ref _vuosiArvo, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }

    [ObservableProperty] private List<string> _toimitukset = new() { "Kaikki" };
    private string _toimitus = "Kaikki";
    public string Toimitus { get => _toimitus; set { if (SetProperty(ref _toimitus, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }

    public ObservableCollection<KanavaValinta> KanavatLista { get; } = new();
    [ObservableProperty] private string _valitutKanavatTeksti = "Kaikki";
    private bool _isUpdatingKanavat = false;

    private bool _haeValilla;
    public bool HaeValilla { get => _haeValilla; set { if (SetProperty(ref _haeValilla, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }
    private DateTime? _alkuPvm = new DateTime(1985, 1, 1);
    public DateTime? AlkuPvm { get => _alkuPvm; set { if (SetProperty(ref _alkuPvm, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }
    private DateTime? _loppuPvm = new DateTime(2007, 12, 31);
    public DateTime? LoppuPvm { get => _loppuPvm; set { if (SetProperty(ref _loppuPvm, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }

    private bool _haePaivalla;
    public bool HaePaivalla { get => _haePaivalla; set { if (SetProperty(ref _haePaivalla, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }
    private DateTime? _tarkkaPaiva = new DateTime(1996, 12, 31);
    public DateTime? TarkkaPaiva { get => _tarkkaPaiva; set { if (SetProperty(ref _tarkkaPaiva, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }

    private bool _haeViikonpaivalla;
    public bool HaeViikonpaivalla { get => _haeViikonpaivalla; set { if (SetProperty(ref _haeViikonpaivalla, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }
    private int _viikonpaivaIndeksi = 0;
    public int ViikonpaivaIndeksi { get => _viikonpaivaIndeksi; set { if (SetProperty(ref _viikonpaivaIndeksi, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }

    private bool _haeKellonajalla;
    public bool HaeKellonajalla { get => _haeKellonajalla; set { if (SetProperty(ref _haeKellonajalla, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }
    private string _kelloAlkuText = "0:00";
    public string KelloAlkuText { get => _kelloAlkuText; set { if (SetProperty(ref _kelloAlkuText, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }
    private string _kelloLoppuText = "23:59";
    public string KelloLoppuText { get => _kelloLoppuText; set { if (SetProperty(ref _kelloLoppuText, value)) { if (!IsLoadingState) PaivitaIkkunaOtsikko(); TeeSuodatusHeti(); } } }
    
    // HAKUTULOSTEN SKAALAUS
    [ObservableProperty] private double _hakutuloksetFonttikoko = 13.0;
    public double HakutuloksetRivikorkeus => Math.Max(22, HakutuloksetFonttikoko + 13.0);

    partial void OnHakutuloksetFonttikokoChanged(double value)
    {
        OnPropertyChanged(nameof(HakutuloksetRivikorkeus));
    }

    public void SuurennaFonttikokoa()
    {
        if (HakutuloksetFonttikoko < 22.0)
            HakutuloksetFonttikoko += 1.0;
    }

    public void PienennaFonttikokoa()
    {
        if (HakutuloksetFonttikoko > 10.0)
            HakutuloksetFonttikoko -= 1.0;
    }

    public MainWindowViewModel()
    {
        string[] kanavat = { "Kaikki", "YLE TV1", "YLE TV2", "MTV1", "MTV2", "Kolmoskanava", "MTV3", "Nelonen", "TVTV!", "Subtv", "Yle Fem", "Yle Teema", "Yle Extra", "YLE24", "TV Finland", "MTV3+", "Urheilukanava" };
        foreach (var k in kanavat) KanavatLista.Add(new KanavaValinta(k, OnKanavaSelectionChanged));

        _isUpdatingKanavat = true;
        KanavatLista[0].IsSelected = true;
        _isUpdatingKanavat = false;

        LisaaValilehti();
        _ = Task.Run(LataaTietokantaTaustallaAsync);
    }
    
    // LAJITTELU
    public void LajittelePaivanMukaan()
    {
        CurrentSort = "date";
        SortNewestFirst = !SortNewestFirst;
        TeeSuodatusHeti();
    }

    public void LajitteleKestonMukaan()
    {
        CurrentSort = "duration";
        SortLongestFirst = !SortLongestFirst;
        TeeSuodatusHeti();
    }
    
    public void AsetaKanavaJaPaiva(string verkkoNimi, DateTime paiva, string docn)
    {
        IsLoadingState = true;
        Haku = "";

        _isUpdatingKanavat = true;
        bool anySelected = false;
        foreach (var k in KanavatLista)
        {
            bool match = string.Equals(k.Nimi, verkkoNimi, StringComparison.OrdinalIgnoreCase);
            k.IsSelected = match;
            if (match) anySelected = true;
        }
        if (!anySelected)
        {
            var kaikki = KanavatLista.FirstOrDefault(k => k.Nimi == "Kaikki");
            if (kaikki != null) kaikki.IsSelected = true;
        }
        PaivitaKanavaTeksti();
        _isUpdatingKanavat = false;

        HaeValilla = false;
        HaePaivalla = true;
        TarkkaPaiva = paiva.Date;

        if (AktiivinenValilehti != null)
        {
            AktiivinenValilehti.MuistettuDocn = docn;
            AktiivinenValilehti.MuistettuPaiva = paiva;
        }

        IsLoadingState = false;
        TeeSuodatusHeti(valitseValmisRiviDocn: docn);
        PaivitaIkkunaOtsikko();
    }
    
    // KOROSTUSHAKU (Ctrl+Alt+F)
    public void AsetaKorostusHaku(string text)
    {
        KorostusHaku = text?.Trim() ?? "";
        if (AktiivinenValilehti != null) AktiivinenValilehti.Korostus = KorostusHaku;

        if (NaytettavatOhjelmat is List<TvlrRow> list)
        {
            PaivitaKorostukset(list);
            NaytettavatOhjelmat = new List<TvlrRow>(list);
            if (_highlightRowIndices.Count > 0)
            {
                SiirryKorostukseen();
            }
        }
    }

    private void PaivitaKorostukset(List<TvlrRow>? list)
    {
        if (list == null) return;

        bool hasKorostus = !string.IsNullOrWhiteSpace(KorostusHaku);

        if (!hasKorostus)
        {
            if (_highlightRowIndices.Count > 0)
            {
                foreach (int idx in _highlightRowIndices)
                {
                    if (idx >= 0 && idx < list.Count)
                    {
                        list[idx].OnkoKorostettu = false;
                    }
                }
                _highlightRowIndices.Clear();
            }
            OnkoKorostusPalkkiNakyvilla = false;
            _currentHighlightIndex = -1;
            KorostusSijaintiTeksti = "";
            return;
        }

        _highlightRowIndices.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            var row = list[i];
            bool match = row.Nimi.Contains(KorostusHaku, StringComparison.OrdinalIgnoreCase);
            row.OnkoKorostettu = match;
            if (match)
            {
                _highlightRowIndices.Add(i);
            }
        }

        OnkoKorostusPalkkiNakyvilla = _highlightRowIndices.Count > 0;
        if (_highlightRowIndices.Count == 0)
        {
            _currentHighlightIndex = -1;
            KorostusSijaintiTeksti = "";
        }
        else
        {
            _currentHighlightIndex = 0;
            KorostusSijaintiTeksti = $"1 / {_highlightRowIndices.Count}";
        }
    }

    [RelayCommand]
    public void SeuraavaKorostus()
    {
        if (_highlightRowIndices.Count == 0) return;
        _currentHighlightIndex++;
        if (_currentHighlightIndex >= _highlightRowIndices.Count) _currentHighlightIndex = 0;
        SiirryKorostukseen();
    }

    [RelayCommand]
    public void EdellinenKorostus()
    {
        if (_highlightRowIndices.Count == 0) return;
        _currentHighlightIndex--;
        if (_currentHighlightIndex < 0) _currentHighlightIndex = _highlightRowIndices.Count - 1;
        SiirryKorostukseen();
    }

    private void SiirryKorostukseen()
    {
        if (NaytettavatOhjelmat is List<TvlrRow> list && _currentHighlightIndex >= 0 && _currentHighlightIndex < _highlightRowIndices.Count)
        {
            int rowIdx = _highlightRowIndices[_currentHighlightIndex];
            if (rowIdx >= 0 && rowIdx < list.Count)
            {
                var row = list[rowIdx];
                ValittuOhjelma = row;
                PyydaSkrollaus?.Invoke(row);
                KorostusSijaintiTeksti = $"{_currentHighlightIndex + 1} / {_highlightRowIndices.Count}";
            }
        }
    }

    [RelayCommand]
    public void SuljeKorostus()
    {
        KorostusHaku = "";
        if (AktiivinenValilehti != null) AktiivinenValilehti.Korostus = "";
        if (NaytettavatOhjelmat is List<TvlrRow> list)
        {
            PaivitaKorostukset(list);
            NaytettavatOhjelmat = new List<TvlrRow>(list);
        }
        else
        {
            OnkoKorostusPalkkiNakyvilla = false;
            _highlightRowIndices.Clear();
            _currentHighlightIndex = -1;
            KorostusSijaintiTeksti = "";
        }
    }

    [RelayCommand]
    public void Tyhjenna()
    {
        string? keepDocn = ValittuOhjelma?.DOCN ?? AktiivinenValilehti?.MuistettuDocn;
        DateTime? keepDate = ValittuOhjelma?.Paiva ?? AktiivinenValilehti?.MuistettuPaiva;

        IsLoadingState = true;
        Haku = "";
        HaeKuvauksesta = false;
        HaeVuodella = false;
        VuosiArvo = 1996;
        Toimitus = "Kaikki";

        _isUpdatingKanavat = true;
        for (int i = 0; i < KanavatLista.Count; i++)
        {
            KanavatLista[i].IsSelected = (i == 0);
        }
        PaivitaKanavaTeksti();
        _isUpdatingKanavat = false;

        HaeValilla = false;
        AlkuPvm = new DateTime(1985, 1, 1);
        LoppuPvm = new DateTime(2007, 12, 31);
        HaePaivalla = false;
        TarkkaPaiva = new DateTime(1996, 12, 31);
        HaeViikonpaivalla = false;
        ViikonpaivaIndeksi = 0;
        HaeKellonajalla = false;
        KelloAlkuText = "0:00";
        KelloLoppuText = "23:59";

        CurrentSort = "date";
        SortNewestFirst = false;
        SortLongestFirst = false;

        SuljeKorostus();

        if (AktiivinenValilehti != null)
        {
            AktiivinenValilehti.MuistettuDocn = keepDocn ?? "";
            AktiivinenValilehti.MuistettuPaiva = keepDate;
        }

        IsLoadingState = false;
        TeeSuodatusHeti(valitseValmisRiviDocn: keepDocn);
        PaivitaIkkunaOtsikko();
    }

    private void OnKanavaSelectionChanged(KanavaValinta muuttunut)
    {
        if (_isUpdatingKanavat) return;
        _isUpdatingKanavat = true;

        if (muuttunut.Nimi == "Kaikki" && muuttunut.IsSelected)
        {
            foreach (var k in KanavatLista.Where(k => k.Nimi != "Kaikki")) k.IsSelected = false;
        }
        else if (muuttunut.Nimi != "Kaikki" && muuttunut.IsSelected)
        {
            var kaikki = KanavatLista.FirstOrDefault(k => k.Nimi == "Kaikki");
            if (kaikki != null) kaikki.IsSelected = false;
        }
        else if (!muuttunut.IsSelected)
        {
            if (!KanavatLista.Any(k => k.IsSelected))
            {
                var kaikki = KanavatLista.FirstOrDefault(k => k.Nimi == "Kaikki");
                if (kaikki != null) kaikki.IsSelected = true;
            }
        }

        PaivitaKanavaTeksti();
        _isUpdatingKanavat = false;
        if (!IsLoadingState) PaivitaIkkunaOtsikko();
        TeeSuodatusHeti();
    }

    private void PaivitaKanavaTeksti()
    {
        var valitut = KanavatLista.Where(k => k.IsSelected).Select(k => k.Nimi).ToList();
        if (valitut.Contains("Kaikki") || valitut.Count == 0) ValitutKanavatTeksti = "Kaikki";
        else if (valitut.Count <= 2) ValitutKanavatTeksti = string.Join(", ", valitut);
        else ValitutKanavatTeksti = $"{valitut.Count} kanavaa valittu";
    }

    public void PaivitaIkkunaOtsikko()
    {
        const string appName = "TVLR-Selain 3";
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(Haku))
            parts.Add(Haku.Trim());

        if (HaePaivalla && TarkkaPaiva.HasValue)
            parts.Add(TarkkaPaiva.Value.ToString("dd.MM.yyyy"));
        else if (HaeValilla && AlkuPvm.HasValue && LoppuPvm.HasValue)
            parts.Add($"{AlkuPvm.Value:dd.MM.yyyy}–{LoppuPvm.Value:dd.MM.yyyy}");

        if (HaeViikonpaivalla)
        {
            string[] vkopvNimet = { "maanantai", "tiistai", "keskiviikko", "torstai", "perjantai", "lauantai", "sunnuntai" };
            if (ViikonpaivaIndeksi >= 0 && ViikonpaivaIndeksi < vkopvNimet.Length)
                parts.Add(vkopvNimet[ViikonpaivaIndeksi]);
        }

        if (HaeVuodella)
            parts.Add(((int)VuosiArvo).ToString());

        if (HaeKellonajalla)
        {
            string a = KelloAlkuText.Trim();
            string b = KelloLoppuText.Trim();
            if (!string.IsNullOrWhiteSpace(a) || !string.IsNullOrWhiteSpace(b))
                parts.Add($"{a}–{b}");
        }

        var valitut = KanavatLista.Where(k => k.IsSelected).Select(k => k.Nimi).ToList();
        if (!valitut.Contains("Kaikki") && valitut.Count > 0)
            parts.Add(string.Join(", ", valitut));

        if (Toimitus != "Kaikki" && !string.IsNullOrWhiteSpace(Toimitus))
            parts.Add(Toimitus);

        IkkunaOtsikko = parts.Count > 0
            ? $"{string.Join(" - ", parts)} - {appName}"
            : appName;
    }

    private record SearchSnapshot(
        string NormalizedSearch,
        string RawSearch,
        bool HasSearch,
        bool SearchDesc,
        bool FilterYear,
        int TargetYear,
        string TargetToimitus,
        bool FilterToimitus,
        HashSet<string> SelectedChannels,
        bool FilterChannels,
        bool FilterWeekday,
        int TargetDay,
        bool HasValidTime,
        TimeSpan AlkuKello,
        TimeSpan LoppuKello,
        bool FilterSingleDate,
        DateTime SingleDate,
        bool FilterRangeDate,
        DateTime RangeStart,
        DateTime RangeEnd,
        string SortType,
        bool SortNewestFirst,
        bool SortLongestFirst,
        string Korostus
    );

    private long _hakuSeq = 0;

    private void TeeSuodatusHeti(string? valitseValmisRiviDocn = null)
    {
        if (IsLoadingState) return;
        valitseValmisRiviDocn ??= ValittuOhjelma?.DOCN ?? AktiivinenValilehti?.MuistettuDocn;
        SuoritaHakuAsync(valitseValmisRiviDocn);
    }

    private async void SuoritaHakuAsync(string? valitseValmisRiviDocn)
    {
        if (OnkoTietokantaLatautumassa) return;

        string? targetDocn = valitseValmisRiviDocn ?? ValittuOhjelma?.DOCN ?? AktiivinenValilehti?.MuistettuDocn;
        DateTime? targetDate = ValittuOhjelma?.Paiva ?? AktiivinenValilehti?.MuistettuPaiva;

        long mySeq = Interlocked.Increment(ref _hakuSeq);
        _hakuViiveCancel?.Cancel();
        _hakuViiveCancel = new CancellationTokenSource();
        var token = _hakuViiveCancel.Token;

        try
        {
            var snapshot = OtaTilannevedos();
            var tulokset = await Task.Run(() => SuoritaSuodatusTaustalla(snapshot, token), token);

            if (token.IsCancellationRequested || mySeq != Volatile.Read(ref _hakuSeq)) return;

            NaytettavatOhjelmat = tulokset;
            StatusTeksti = $"Näytetään {tulokset.Count:N0} ohjelmaa.";
            PaivitaKorostukset(tulokset);

            if (AktiivinenValilehti != null)
            {
                AktiivinenValilehti.TallennetutTulokset = tulokset;
            }

            TvlrRow? valittava = null;
            if (!string.IsNullOrEmpty(targetDocn))
            {
                valittava = tulokset.FirstOrDefault(x => x.DOCN == targetDocn);
            }

            if (valittava == null && targetDate.HasValue && tulokset.Count > 0)
            {
                valittava = tulokset
                    .OrderBy(x => Math.Abs((x.Paiva - targetDate.Value).TotalSeconds))
                    .FirstOrDefault();
            }

            if (valittava == null && tulokset.Count > 0)
            {
                valittava = tulokset[0];
            }

            if (valittava != null)
            {
                ValittuOhjelma = valittava;
                if (AktiivinenValilehti != null)
                {
                    AktiivinenValilehti.MuistettuDocn = valittava.DOCN;
                    AktiivinenValilehti.MuistettuPaiva = valittava.Paiva;
                }
                PyydaSkrollaus?.Invoke(valittava);
            }
        }
        catch (OperationCanceledException) { }
    }

    [GeneratedRegex(@"(?i)\byear:(\d{4})\b")]
    private static partial Regex InlineYearRegex();

    private SearchSnapshot OtaTilannevedos()
    {
        string rawSearch = Haku?.Trim() ?? "";
        int? inlineYear = null;
        string textWithoutYear = rawSearch;
        var yearMatch = InlineYearRegex().Match(rawSearch);
        if (yearMatch.Success)
        {
            if (int.TryParse(yearMatch.Groups[1].Value, out int yVal))
                inlineYear = yVal;
            textWithoutYear = rawSearch.Remove(yearMatch.Index, yearMatch.Length).Trim();
        }

        string normalizedSearch = !string.IsNullOrWhiteSpace(textWithoutYear) ? NormalizeSearch(textWithoutYear) : "";

        bool filterYear = HaeVuodella || inlineYear.HasValue;
        int targetYear = HaeVuodella ? (int)VuosiArvo : (inlineYear ?? 1996);

        var channels = KanavatLista.Where(k => k.IsSelected).Select(k => k.Nimi).ToHashSet();
        bool filterChannels = !channels.Contains("Kaikki") && channels.Count > 0;

        int[] dayMap = { 1, 2, 3, 4, 5, 6, 0 };
        int targetDay = HaeViikonpaivalla && ViikonpaivaIndeksi >= 0 && ViikonpaivaIndeksi < dayMap.Length ? dayMap[ViikonpaivaIndeksi] : -1;

        TimeSpan? alkuKello = HaeKellonajalla ? ParseTime(KelloAlkuText) : null;
        TimeSpan? loppuKello = HaeKellonajalla ? ParseTime(KelloLoppuText) : null;
        bool hasValidTime = HaeKellonajalla && alkuKello.HasValue && loppuKello.HasValue;

        bool filterSingleDate = HaePaivalla && TarkkaPaiva.HasValue;
        DateTime singleDate = filterSingleDate ? TarkkaPaiva!.Value.Date : DateTime.MinValue;

        bool filterRangeDate = !filterSingleDate && HaeValilla && AlkuPvm.HasValue && LoppuPvm.HasValue;
        DateTime rangeStart = filterRangeDate ? AlkuPvm!.Value.Date : DateTime.MinValue;
        DateTime rangeEnd = filterRangeDate ? LoppuPvm!.Value.Date : DateTime.MinValue;

        return new SearchSnapshot(
            normalizedSearch,
            textWithoutYear,
            !string.IsNullOrEmpty(normalizedSearch),
            HaeKuvauksesta,
            filterYear,
            targetYear,
            (Toimitus != "Kaikki" && !string.IsNullOrWhiteSpace(Toimitus)) ? Toimitus : "",
            (Toimitus != "Kaikki" && !string.IsNullOrWhiteSpace(Toimitus)),
            channels,
            filterChannels,
            HaeViikonpaivalla,
            targetDay,
            hasValidTime,
            alkuKello ?? TimeSpan.Zero,
            loppuKello ?? TimeSpan.Zero,
            filterSingleDate,
            singleDate,
            filterRangeDate,
            rangeStart,
            rangeEnd,
            CurrentSort,
            SortNewestFirst,
            SortLongestFirst,
            KorostusHaku
        );
    }

    private List<TvlrRow> SuoritaSuodatusTaustalla(SearchSnapshot s, CancellationToken token)
    {
        if (_allPrograms.Length == 0) return new List<TvlrRow>(0);

        int coreCount = Environment.ProcessorCount;
        var items = _allPrograms;
        int totalItems = items.Length;
        int chunkSize = (totalItems + coreCount - 1) / coreCount;

        var resultsPerCore = new List<TvlrRow>[coreCount];

        Parallel.For(0, coreCount, new ParallelOptions { CancellationToken = token }, core =>
        {
            int start = core * chunkSize;
            int end = Math.Min(start + chunkSize, totalItems);
            if (start >= end)
            {
                resultsPerCore[core] = new List<TvlrRow>(0);
                return;
            }

            var localList = new List<TvlrRow>(Math.Min(1024, end - start));

            for (int i = start; i < end; i++)
            {
                if ((i & 255) == 0 && token.IsCancellationRequested) return;

                var x = items[i];

                if (s.FilterYear && x.Vuosi != s.TargetYear) continue;
                if (s.FilterSingleDate && x.Paiva != s.SingleDate) continue;
                if (s.FilterRangeDate && (x.Paiva < s.RangeStart || x.Paiva > s.RangeEnd)) continue;
                if (s.FilterWeekday && x.Viikonpaiva != s.TargetDay) continue;

                if (s.HasValidTime && x.KelloTimeSpan.HasValue)
                {
                    var kello = x.KelloTimeSpan.Value;
                    if (kello < s.AlkuKello || kello > s.LoppuKello) continue;
                }

                if (s.FilterChannels && !s.SelectedChannels.Contains(x.VerkkoNimi)) continue;
                if (s.FilterToimitus && x.TEKI != s.TargetToimitus) continue;

                if (s.HasSearch)
                {
                    bool match = x.NimiNormalized.Contains(s.NormalizedSearch);
                    if (!match && s.SearchDesc && !string.IsNullOrEmpty(x.TIETOJA))
                    {
                        match = x.TIETOJA.Contains(s.RawSearch, StringComparison.OrdinalIgnoreCase);
                    }
                    if (!match) continue;
                }

                localList.Add(x);
            }

            resultsPerCore[core] = localList;
        });

        if (token.IsCancellationRequested) return new List<TvlrRow>(0);

        int matchCount = 0;
        for (int i = 0; i < coreCount; i++)
        {
            if (resultsPerCore[i] != null) matchCount += resultsPerCore[i].Count;
        }

        var finalRows = new List<TvlrRow>(matchCount);
        for (int i = 0; i < coreCount; i++)
        {
            if (resultsPerCore[i] != null && resultsPerCore[i].Count > 0)
            {
                finalRows.AddRange(resultsPerCore[i]);
            }
        }
        
        if (s.SortType == "duration")
        {
            if (s.SortLongestFirst)
            {
                finalRows.Sort((a, b) =>
                {
                    int c = Nullable.Compare(b.KestoTimeSpan, a.KestoTimeSpan);
                    return c != 0 ? c : a.Paiva.CompareTo(b.Paiva);
                });
            }
            else
            {
                finalRows.Sort((a, b) =>
                {
                    int c = Nullable.Compare(a.KestoTimeSpan, b.KestoTimeSpan);
                    return c != 0 ? c : a.Paiva.CompareTo(b.Paiva);
                });
            }
        }
        else if (s.SortNewestFirst)
        {
            finalRows.Reverse();
        }
        
        if (!string.IsNullOrWhiteSpace(s.Korostus))
        {
            for (int i = 0; i < finalRows.Count; i++)
            {
                var row = finalRows[i];
                row.OnkoKorostettu = row.Nimi.Contains(s.Korostus, StringComparison.OrdinalIgnoreCase);
            }
        }

        return finalRows;
    }
    
    // TIETOKANNAN LATAUS & HALLINTA
    private async Task LataaTietokantaTaustallaAsync()
    {
        string db = Path.Combine(AppContext.BaseDirectory, "TVLR.db");
        _dbPath = db;
        if (!File.Exists(db))
        {
            Dispatcher.UIThread.Post(() =>
            {
                OnkoTietokantaLatautumassa = false;
                StatusTeksti = "TVLR.db tietokantaa ei löytynyt.";
            });
            return;
        }

        await LataaTietokantaPolustaAsync(db);
    }

    public async Task LataaTietokantaPolustaAsync(string dbPath)
    {
        _dbPath = dbPath;

        Dispatcher.UIThread.Post(() =>
        {
            OnkoTietokantaLatautumassa = true;
            StatusTeksti = "Valmistellaan tietokantaa muistiin...";
        });

        try
        {
            _conn?.Close();
            _conn?.Dispose();

            _conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared");
            _conn.Open();

            EnsureDatabaseCompatibility(_conn);

            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = @"
                    PRAGMA cache_size = -64000;
                    PRAGMA mmap_size = 268435456;
                    PRAGMA temp_store = MEMORY;
                ";
                cmd.ExecuteNonQuery();
            }

            int totalRows = 0;
            using (var countCmd = _conn.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM programs;";
                var scalar = countCmd.ExecuteScalar();
                if (scalar != null) totalRows = Convert.ToInt32(scalar);
            }

            _allPrograms = LoadAllPrograms(totalRows);
            var toimituksetList = HaeToimituksetDb();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                OnkoTietokantaLatautumassa = false;
                Toimitukset = toimituksetList;
                TeeSuodatusHeti();
                PaivitaIkkunaOtsikko();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                OnkoTietokantaLatautumassa = false;
                StatusTeksti = $"Virhe ladattaessa tietokantaa: {ex.Message}";
            });
        }
    }

    private static void EnsureDatabaseCompatibility(SqliteConnection conn)
    {
        try
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "PRAGMA table_info(programs);";
            bool hasTietoja = false;
            using (var reader = checkCmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string col = reader["name"]?.ToString() ?? "";
                    if (col.Equals("tietoja", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTietoja = true;
                        break;
                    }
                }
            }

            if (!hasTietoja)
            {
                using var alterCmd = conn.CreateCommand();
                alterCmd.CommandText = "ALTER TABLE programs ADD COLUMN tietoja TEXT DEFAULT '';";
                alterCmd.ExecuteNonQuery();
            }
        }
        catch { }
    }

    private List<string> HaeToimituksetDb()
    {
        if (_conn == null) return new List<string> { "Kaikki" };
        var list = new List<string> { "Kaikki" };
        string sql = "SELECT DISTINCT teki FROM programs WHERE teki != '' ORDER BY teki";
        using var cmd = new SqliteCommand(sql, _conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    private static readonly Dictionary<string, string> StringPool = new(2048, StringComparer.Ordinal);
    private static string Pool(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (StringPool.TryGetValue(s, out var existing)) return existing;
        StringPool[s] = s;
        return s;
    }

    private static readonly string[] WeekdayNames = { "su", "ma", "ti", "ke", "to", "pe", "la" };
    private static string GetWeekdayShortFi(DateTime dt)
    {
        int dow = (int)dt.DayOfWeek;
        return (dow >= 0 && dow < WeekdayNames.Length) ? WeekdayNames[dow] : "";
    }

    private static TimeSpan? ParseTime(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var span = s.AsSpan().Trim();
        int sep = span.IndexOfAny('.', ':');
        if (sep > 0)
        {
            if (int.TryParse(span[..sep], out int h) && int.TryParse(span[(sep + 1)..], out int m))
            {
                return new TimeSpan(h, m, 0);
            }
        }
        else if (int.TryParse(span, out int hOnly))
        {
            return new TimeSpan(hOnly, 0, 0);
        }
        return null;
    }

    private TvlrRow[] LoadAllPrograms(int totalRows)
    {
        var rows = new List<TvlrRow>(totalRows > 0 ? totalRows : 1000000);
        string sql = "SELECT docn, nimi, pvm, kello, kesto, verkko, teks, selo, teki, tietoja FROM programs";

        using var cmd = new SqliteCommand(sql, _conn);
        using var reader = cmd.ExecuteReader();

        int ordDocn = reader.GetOrdinal("docn");
        int ordNimi = reader.GetOrdinal("nimi");
        int ordPvm = reader.GetOrdinal("pvm");
        int ordKello = reader.GetOrdinal("kello");
        int ordKesto = reader.GetOrdinal("kesto");
        int ordVerkko = reader.GetOrdinal("verkko");
        int ordTeks = reader.GetOrdinal("teks");
        int ordSelo = reader.GetOrdinal("selo");
        int ordTeki = reader.GetOrdinal("teki");
        int ordTietoja = reader.GetOrdinal("tietoja");

        var sw = Stopwatch.StartNew();
        int counter = 0;
        const int updateStep = 30000;

        while (reader.Read())
        {
            counter++;

            if (counter % updateStep == 0 && totalRows > 0)
            {
                double elapsedSec = sw.Elapsed.TotalSeconds;
                if (elapsedSec > 0.25)
                {
                    double rowsPerSec = counter / elapsedSec;
                    double remainingSec = Math.Max(0, (totalRows - counter) / rowsPerSec);
                    int pct = (int)((counter / (double)totalRows) * 100);

                    string etaTeksti = remainingSec < 60
                        ? $"{Math.Ceiling(remainingSec)} s"
                        : $"{(int)(remainingSec / 60)} min {Math.Ceiling(remainingSec % 60)} s";

                    Dispatcher.UIThread.Post(() =>
                    {
                        StatusTeksti = $"Ladataan tietokantaa muistiin... {pct} % ({counter:N0} / {totalRows:N0}) – ETA: ~{etaTeksti}";
                    });
                }
            }

            string nimi = reader.IsDBNull(ordNimi) ? "" : reader.GetString(ordNimi);
            string pvmStr = reader.IsDBNull(ordPvm) ? "" : reader.GetString(ordPvm);
            DateTime.TryParse(pvmStr, out DateTime paiva);

            int kesto = reader.IsDBNull(ordKesto) ? 0 : Convert.ToInt32(reader.GetValue(ordKesto));
            string rowVerkko = Pool(reader.IsDBNull(ordVerkko) ? "" : reader.GetString(ordVerkko));
            string kelloStrRaw = reader.IsDBNull(ordKello) ? "" : reader.GetString(ordKello);
            string teki = Pool(reader.IsDBNull(ordTeki) ? "" : reader.GetString(ordTeki));

            TimeSpan? kelloAika = ParseTime(kelloStrRaw);
            TimeSpan kestoAika = TimeSpan.FromSeconds(kesto);

            string docn = Pool(reader.IsDBNull(ordDocn) ? "" : reader.GetString(ordDocn));
            string teks = Pool(reader.IsDBNull(ordTeks) ? "" : reader.GetString(ordTeks));
            string selo = Pool(reader.IsDBNull(ordSelo) ? "" : reader.GetString(ordSelo));
            string verkkoNimi = Pool(HaeVanhaKanavaNimi(rowVerkko, paiva, teki));

            string vkopvStr = paiva != DateTime.MinValue ? GetWeekdayShortFi(paiva) : "";
            string paivaStr = paiva != DateTime.MinValue ? Pool($"{paiva:dd.MM.yyyy}") : "";
            string kelloStr = kelloAika.HasValue ? Pool($"{(int)kelloAika.Value.TotalHours:00}:{kelloAika.Value.Minutes:00}") : "";
            string kestoStr = Pool(kestoAika.TotalHours >= 1 ? $"{(int)kestoAika.TotalHours}:{kestoAika.Minutes:00}:{kestoAika.Seconds:00}" : $"{kestoAika.Minutes:00}:{kestoAika.Seconds:00}");

            rows.Add(new TvlrRow
            {
                DOCN = docn,
                Nimi = nimi,
                NimiNormalized = NormalizeSearch(nimi),
                Paiva = paiva,
                Vuosi = paiva.Year,
                Viikonpaiva = (byte)paiva.DayOfWeek,
                KelloTimeSpan = kelloAika,
                KestoTimeSpan = kestoAika,
                Verkko = rowVerkko,
                TEKS = teks,
                SELO = selo,
                TEKI = teki,
                TIETOJA = reader.IsDBNull(ordTietoja) ? "" : reader.GetString(ordTietoja),

                VkopvStr = vkopvStr,
                PaivaStr = paivaStr,
                KelloStr = kelloStr,
                KestoStr = kestoStr,
                VerkkoNimi = verkkoNimi
            });
        }

        Dispatcher.UIThread.Post(() => StatusTeksti = "Järjestetään ohjelmatietoja muistissa...");

        rows.Sort((a, b) =>
        {
            int c = a.Paiva.CompareTo(b.Paiva);
            return c != 0 ? c : Nullable.Compare(a.KelloTimeSpan, b.KelloTimeSpan);
        });

        StringPool.Clear();
        return rows.ToArray();
    }

    private string HaeVanhaKanavaNimi(string verkko, DateTime paiva, string? teki)
    {
        return verkko switch
        {
            "1" => paiva < new DateTime(1993, 1, 1) && teki == "MTV" ? "MTV1" : "YLE TV1",
            "2" => paiva < new DateTime(1993, 1, 1) && teki == "MTV" ? "MTV2" : "YLE TV2",
            "3" => paiva <= new DateTime(1992, 12, 31) ? "Kolmoskanava" : "MTV3",
            "4" => "Nelonen",
            "6" => paiva < new DateTime(2001, 8, 15) ? "TVTV!" : "Subtv",
            "13" => "Yle Fem",
            "14" => "Yle Teema",
            "8" => "Yle Extra",
            "15" => "YLE24",
            "22" => "TV Finland",
            "30" => "MTV3+",
            "31" => "Urheilukanava",
            _ => verkko
        };
    }

    private string NormalizeSearch(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var normalized = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
        }
        return sb.ToString();
    }
    
    // TIETOKANNAN PÄIVITYS
    private int MergeDatabases(string downloadedDb)
    {
        if (_conn == null) return 0;

        string tempCopy = downloadedDb + ".tmp";
        File.Copy(downloadedDb, tempCopy, true);

        using var attachCmd = _conn.CreateCommand();
        attachCmd.CommandText = $"ATTACH DATABASE '{tempCopy.Replace("'", "''")}' AS newdb;";
        attachCmd.ExecuteNonQuery();

        using var insertCmd = _conn.CreateCommand();
        insertCmd.CommandText = @"
            INSERT OR IGNORE INTO programs
            (
                docn, nimi, pvm, kello, kesto, verkko, teks, selo, teki, tietoja
            )
            SELECT
                docn, nimi, pvm, kello, kesto, verkko, teks, selo, teki, tietoja
            FROM newdb.programs;
        ";
        int inserted = insertCmd.ExecuteNonQuery();

        using var detachCmd = _conn.CreateCommand();
        detachCmd.CommandText = "DETACH DATABASE newdb;";
        detachCmd.ExecuteNonQuery();

        try { File.Delete(tempCopy); } catch { }
        return inserted;
    }

    private int RemoveDuplicates()
    {
        if (_conn == null) return 0;

        using var countCmd = _conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM programs;";
        int before = Convert.ToInt32(countCmd.ExecuteScalar());

        using var deleteCmd = _conn.CreateCommand();
        deleteCmd.CommandText = @"
            DELETE FROM programs
            WHERE rowid NOT IN
            (
                SELECT MIN(rowid)
                FROM programs
                GROUP BY
                    docn, nimi, pvm, kello, kesto, verkko, teks, selo, teki, tietoja
            );
        ";
        deleteCmd.ExecuteNonQuery();

        using var countAfterCmd = _conn.CreateCommand();
        countAfterCmd.CommandText = "SELECT COUNT(*) FROM programs;";
        int after = Convert.ToInt32(countAfterCmd.ExecuteScalar());

        return before - after;
    }

    private void OptimizeDatabase()
    {
        if (_conn == null) return;
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

    private static int GetLocalDbVersion()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "dbversio.txt");
        if (!File.Exists(path)) return 0;
        if (int.TryParse(File.ReadAllText(path).Trim(), out int v)) return v;
        return 0;
    }

    private static void SetLocalDbVersion(int v)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "dbversio.txt");
        File.WriteAllText(path, v.ToString());
    }

    private async Task DownloadFileAsync(string url, string path)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var fs = File.Create(path);
        await response.Content.CopyToAsync(fs);
    }

    public async Task TarkistaPaivityksetAsync(Func<string, string, Task<bool>> vahvistaKysely, Action<string, string> naytaViesti, bool manuaalinen)
    {
        try
        {
            StatusTeksti = "Tarkistetaan päivityksiä...";

            string appDir = AppContext.BaseDirectory;
            var localUpdates = Directory.GetFiles(appDir, "*.db")
                .Where(x => Path.GetFileName(x).StartsWith("update_v", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x)
                .ToList();

            if (localUpdates.Count > 0)
            {
                int totalInsertedLocal = 0;
                int duplicatesRemovedLocal = 0;

                foreach (string localDb in localUpdates)
                {
                    string fileName = Path.GetFileName(localDb);
                    StatusTeksti = $"Asennetaan paikallinen päivitys: {fileName}...";

                    int inserted = await Task.Run(() => MergeDatabases(localDb));
                    totalInsertedLocal += inserted;

                    duplicatesRemovedLocal += await Task.Run(RemoveDuplicates);

                    try { File.Delete(localDb); } catch { }
                }

                StatusTeksti = "Optimoidaan tietokantaa...";
                await Task.Run(OptimizeDatabase);

                int newestLocalVersion = 0;
                foreach (string localDb in localUpdates)
                {
                    string name = Path.GetFileNameWithoutExtension(localDb);
                    string number = name.Replace("update_v", "", StringComparison.OrdinalIgnoreCase);
                    if (int.TryParse(number, out int v) && v > newestLocalVersion)
                        newestLocalVersion = v;
                }

                if (newestLocalVersion > 0)
                {
                    SetLocalDbVersion(newestLocalVersion);
                }

                StatusTeksti = "Ladataan päivitettyä tietokantaa muistiin...";
                if (!string.IsNullOrEmpty(_dbPath))
                {
                    await LataaTietokantaPolustaAsync(_dbPath);
                }

                naytaViesti(
                    $"Paikalliset päivitykset asennettu!\n\nLisättiin {totalInsertedLocal:N0} ohjelmaa.\nPoistettiin {duplicatesRemovedLocal:N0} kaksoiskappaletta.",
                    "Paikallinen päivitys"
                );
                return;
            }

            // Verkkopäivitys
            string json = await Http.GetStringAsync("https://telkkari.tv/tvlr/version.json");
            var serverInfo = System.Text.Json.JsonSerializer.Deserialize<VersionInfo>(
                json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );

            if (serverInfo == null)
            {
                if (manuaalinen) naytaViesti("Virhe version tarkistuksessa.", "Virhe");
                StatusTeksti = "Päivitystietojen luku epäonnistui.";
                return;
            }

            int localVersion = GetLocalDbVersion();
            serverInfo.updates.Sort((a, b) => a.version.CompareTo(b.version));

            if (serverInfo.version <= localVersion)
            {
                StatusTeksti = "Tietokanta on ajan tasalla.";
                return;
            }

            var updatesToInstall = serverInfo.updates
                .Where(x => x.version > localVersion)
                .OrderBy(x => x.version)
                .Select(x => $"v{x.version} - {x.description}")
                .ToList();

            string updateList = string.Join("\n", updatesToInstall);

            bool haluaaLadata = await vahvistaKysely(
                $"Uusia ohjelmatietoja löytyi!\n\nNykyinen versio: {localVersion}\nUusin versio: {serverInfo.version}\n\nPäivitykset:\n{updateList}\n\nLadataanko ja asennetaanko päivitykset nyt?",
                "Tietokannan päivitys"
            );

            if (!haluaaLadata)
            {
                StatusTeksti = $"Näytetään {NaytettavatOhjelmat.Count():N0} ohjelmaa.";
                return;
            }

            int startingVersion = localVersion;
            int totalInserted = 0;
            int duplicatesRemoved = 0;

            StatusTeksti = "Poistetaan vanhoja kaksoiskappaleita...";
            duplicatesRemoved += await Task.Run(RemoveDuplicates);

            foreach (var update in serverInfo.updates.OrderBy(x => x.version))
            {
                if (update.version <= startingVersion) continue;

                StatusTeksti = $"Ladataan päivitystä v{update.version}...";
                string tempDb = Path.Combine(Path.GetTempPath(), update.file);
                string updateUrl = $"https://telkkari.tv/tvlr/{update.file}";

                await DownloadFileAsync(updateUrl, tempDb);

                int inserted = await Task.Run(() => MergeDatabases(tempDb));
                totalInserted += inserted;
                duplicatesRemoved += await Task.Run(RemoveDuplicates);

                GC.Collect();
                GC.WaitForPendingFinalizers();

                try { File.Delete(tempDb); } catch { }
                try { File.Delete(tempDb + ".tmp"); } catch { }
            }

            StatusTeksti = "Optimoidaan tietokantaa...";
            await Task.Run(OptimizeDatabase);

            SetLocalDbVersion(serverInfo.version);

            StatusTeksti = "Ladataan päivitettyä tietokantaa muistiin...";
            if (!string.IsNullOrEmpty(_dbPath))
            {
                await LataaTietokantaPolustaAsync(_dbPath);
            }

            naytaViesti(
                $"Tietokanta päivitetty onnistuneesti!\n\nLisättiin yhteensä {totalInserted:N0} ohjelmaa.\nPoistettiin {duplicatesRemoved:N0} kaksoiskappaletta.",
                "Päivitys valmis"
            );
        }
        catch (Exception ex)
        {
            StatusTeksti = "Päivitys epäonnistui.";
            if (manuaalinen)
            {
                naytaViesti($"Päivitys epäonnistui: {ex.Message}", "Virhe");
            }
        }
    }
}