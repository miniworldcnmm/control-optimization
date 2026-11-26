using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace AndroidConsolizer
{
    internal static class I18n
    {
        private static readonly ConditionalWeakTable<SpriteFont, HashSet<char>> FontCharacters = new();
        private static readonly Dictionary<string, IDictionary<string, Translation>> LocalesByKey = new(StringComparer.Ordinal);
        private static readonly (char Symbol, string Key)[] PlayStationButtons =
        {
            ('×', "controls.button.cross-text"),
            ('○', "controls.button.circle-text"),
            ('□', "controls.button.square-text"),
            ('△', "controls.button.triangle-text")
        };

        private static ITranslationHelper _translations;
        private static IMonitor _monitor;
        private static string _cachedLocale;
        private static bool _reportedFontFallback;

        internal static string Language => ModEntry.Config?.Language switch
        {
            "zh" => "zh",
            "en" => "en",
            _ => "Auto"
        };

        internal static void Init(ITranslationHelper translations, IMonitor monitor)
        {
            _translations = translations;
            _monitor = monitor;
            LocalesByKey.Clear();
            _cachedLocale = null;
            _reportedFontFallback = false;
        }

        /// <summary>Get display text checked against both fonts used by the Mod and GMCM.</summary>
        internal static string Get(string key, object tokens = null, string language = null)
        {
            var fonts = GetFonts();
            language ??= Language;
            if (WantsChinese(language) && !fonts.Supports('中'))
                language = "en";

            string text = ReplaceUnsupportedButtons(GetRaw(key, tokens, language), fonts, language);
            if (fonts.CanRender(text))
                return text;

            if (language != "en")
            {
                text = ReplaceUnsupportedButtons(GetRaw(key, tokens, "en"), fonts, "en");
                if (fonts.CanRender(text))
                    return text;
            }

            // Even a modified English resource or a token can contain unsupported glyphs.
            return FilterUnsupported(text, fonts);
        }

        /// <summary>Check text assembled from multiple translations before rendering.</summary>
        internal static string EnsureRenderable(string text)
        {
            var fonts = GetFonts();
            return fonts.CanRender(text) ? text : FilterUnsupported(text, fonts);
        }

        private static string FilterUnsupported(string text, GameFonts fonts)
        {
            var safe = new StringBuilder(text.Length);
            foreach (char character in text)
            {
                if (fonts.Supports(character))
                    safe.Append(character);
                else if (fonts.Supports('?'))
                    safe.Append('?');
            }
            return safe.ToString();
        }

        /// <summary>Resolve a translation with SMAPI tokens, without applying the font check.</summary>
        internal static string GetRaw(string key, object tokens = null, string language = null)
        {
            if (_translations == null)
                return "";

            language ??= Language;
            if (language != "zh" && language != "en")
            {
                var translation = tokens != null ? _translations.Get(key, tokens) : _translations.Get(key);
                return translation.HasValue() ? translation.ToString() : "";
            }

            string activeLocale = _translations.Locale;
            if (!string.Equals(_cachedLocale, activeLocale, StringComparison.Ordinal))
            {
                LocalesByKey.Clear();
                _cachedLocale = activeLocale;
            }

            if (!LocalesByKey.TryGetValue(key, out var locales))
            {
                locales = _translations.GetInAllLocales(key);
                LocalesByKey[key] = locales;
            }

            if (locales == null)
                return "";

            if (language == "zh" && locales.TryGetValue("zh", out var chinese) && chinese.HasValue())
                return (tokens != null ? chinese.Tokens(tokens) : chinese).ToString();
            if (locales.TryGetValue("default", out var english) && english.HasValue())
                return (tokens != null ? english.Tokens(tokens) : english).ToString();
            return "";
        }

        /// <summary>Report the language fallback once, from menu or configuration lifecycle callbacks.</summary>
        internal static void ReportFontFallback()
        {
            if (_reportedFontFallback || !WantsChinese(Language))
                return;

            var fonts = GetFonts();
            if (fonts.Ready && !fonts.Supports('中'))
            {
                _reportedFontFallback = true;
                _monitor?.Log("The current game fonts cannot display Chinese. Android Consolizer will display English instead.", LogLevel.Info);
            }
        }

        private static bool WantsChinese(string language)
        {
            return language == "zh"
                || (language == "Auto" && _translations?.LocaleEnum == LocalizedContentManager.LanguageCode.zh);
        }

        private static string ReplaceUnsupportedButtons(string text, GameFonts fonts, string language)
        {
            bool hasAnySymbol = false;
            foreach (var (symbol, _) in PlayStationButtons)
            {
                if (text.Contains(symbol))
                {
                    hasAnySymbol = true;
                    break;
                }
            }
            if (!hasAnySymbol)
                return text;

            // Keep PlayStation symbols consistent as a set: if any glyph is missing,
            // replace all four with readable text names instead of mixing symbols and text.
            bool allSymbolsSupported = true;
            foreach (var (symbol, _) in PlayStationButtons)
            {
                if (!fonts.Supports(symbol))
                {
                    allSymbolsSupported = false;
                    break;
                }
            }
            if (allSymbolsSupported)
                return text;

            foreach (var (symbol, key) in PlayStationButtons)
            {
                if (!text.Contains(symbol))
                    continue;

                string label = GetRaw(key, language: language);
                if (label.Length == 0 || !fonts.CanRender(label))
                    label = GetRaw(key, language: "en");
                text = text.Replace(symbol.ToString(), label, StringComparison.Ordinal);
            }
            return text;
        }

        private static GameFonts GetFonts()
        {
            try
            {
                return new GameFonts(Game1.smallFont, Game1.dialogueFont);
            }
            catch
            {
                // Fonts may not be available at startup.
                return default;
            }
        }

        private readonly struct GameFonts
        {
            private readonly SpriteFont _smallFont;
            private readonly SpriteFont _dialogueFont;
            private readonly HashSet<char> _small;
            private readonly HashSet<char> _dialogue;

            internal GameFonts(SpriteFont small, SpriteFont dialogue)
            {
                _smallFont = small;
                _dialogueFont = dialogue;
                _small = GetCharacters(small);
                _dialogue = GetCharacters(dialogue);
            }

            internal bool Ready => _smallFont != null && _dialogueFont != null;

            internal bool Supports(char character)
            {
                if (character == '\n' || character == '\r')
                    return true;

                return Supports(_smallFont, _small, character) && Supports(_dialogueFont, _dialogue, character);
            }

            internal bool CanRender(string text)
            {
                foreach (char character in text)
                {
                    if (!Supports(character))
                        return false;
                }
                return true;
            }

            private static bool Supports(SpriteFont font, HashSet<char> characters, char character)
            {
                if (characters != null)
                    return characters.Contains(character);
                if (character < ' ' || character > '~')
                    return false;
                if (font == null)
                    return true;

                // If the font's glyph table couldn't be read, verify even ASCII at render time.
                try
                {
                    font.MeasureString(character.ToString());
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static HashSet<char> GetCharacters(SpriteFont font)
            {
                if (font == null)
                    return null;
                try
                {
                    return FontCharacters.GetValue(font, loadedFont => new HashSet<char>(loadedFont.Characters));
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
