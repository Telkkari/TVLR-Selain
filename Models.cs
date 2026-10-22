using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TVLR_Selain_3;

public class TvlrRow
{
    public string DOCN { get; set; } = "";
    public string Nimi { get; set; } = "";
    public string NimiNormalized { get; set; } = "";
    public string TEKS { get; set; } = "";
    public string SELO { get; set; } = "";
    public string TEKI { get; set; } = "";
    public string TIETOJA { get; set; } = "";

    public DateTime Paiva { get; set; } = DateTime.MinValue;
    public int Vuosi { get; set; }
    public byte Viikonpaiva { get; set; }
    public TimeSpan? KelloTimeSpan { get; set; }
    public TimeSpan? KestoTimeSpan { get; set; }
    public string Verkko { get; set; } = "";
    public string VerkkoNimi { get; set; } = "";

    public string VkopvStr { get; set; } = "";
    public string PaivaStr { get; set; } = "";
    public string KelloStr { get; set; } = "";
    public string KestoStr { get; set; } = "";

    public string KuvausIkoni => string.IsNullOrWhiteSpace(TIETOJA) ? "" : "📄";
    public string KuvausVihje => string.IsNullOrWhiteSpace(TIETOJA) ? "" : "Ohjelmalla on kuvaustiedot (tuplaklikkaa avataksesi)";

    // Tekstitys- ja selostusvihjeet
    public string TeksKuvaus => DecodeTeksTooltip(TEKS);
    public string SeloKuvaus => DecodaSeloTooltip(SELO);

    // Hakusanojen korostus
    public bool OnkoKorostettu { get; set; }

    private static readonly ConcurrentDictionary<string, string> TeksCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> SeloCache = new(StringComparer.Ordinal);

    public static string DecodeTeksTooltip(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        return TeksCache.GetOrAdd(code, static c =>
        {
            var clean = c.Trim().ToUpperInvariant().Replace(" ", "");
            var parts = new List<string>();
            for (int i = 0; i + 2 <= clean.Length; i += 2)
                parts.Add(clean.Substring(i, 2));

            var lines = new List<string>();
            foreach (var part in parts)
            {
                string desc = part switch
                {
                    "SC" => "SC – Scan titling (tiedostomuotoinen, arkistoitu tekstitys)",
                    "TC" => "TC – Time coded (aikakoodattu tekstitys)",
                    "YT" => "YT – Teksti-TV tekstitys",
                    "MA" => "MA – Tekstitys materiaalilla",
                    "TG" => "TG – Tekstigeneraattori",
                    "SY" => "SY – Tekstitys teksti-TV:ssä",
                    "EL" => "EL – Ei lähetystekstitystä",
                    "SU" => "SU – Suomi",
                    "RU" => "RU – Ruotsi",
                    "VA" => "VA – Vähemmistö",
                    "VI" => "VI – Viittoma",
                    "MU" => "MU – Muut",
                    _ => $"{part} – (tuntematon koodi)"
                };
                lines.Add(desc);
            }
            return string.Join("\n", lines);
        });
    }

    public static string DecodaSeloTooltip(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        return SeloCache.GetOrAdd(code, static c =>
        {
            var clean = c.Trim().ToUpperInvariant().Replace(" ", "");
            var parts = new List<string>();
            for (int i = 0; i + 2 <= clean.Length; i += 2)
                parts.Add(clean.Substring(i, 2));

            var lines = new List<string>();
            foreach (var part in parts)
            {
                string desc = part switch
                {
                    "MA" => "MA – Materiassa (selostus filmillä tai kuvanauhalla)",
                    "SU" => "SU – Suora selostus / Suomi",
                    "RU" => "RU – Ruotsi",
                    "VA" => "VA – Vähemmistö",
                    "VI" => "VI – Viittoma",
                    "MU" => "MU – Muut",
                    _ => $"{part} – (tuntematon koodi)"
                };
                lines.Add(desc);
            }
            return string.Join("\n", lines);
        });
    }
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