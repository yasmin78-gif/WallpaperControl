using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Threading;

namespace WallpaperControl
{
    /// <summary>
    /// Associates a supported culture code with its localized language-name resource.
    /// </summary>
    /// <param name="Code">The supported language code.</param>
    /// <param name="DisplayNameResourceKey">The resource key for the language's display name.</param>
    internal sealed record SupportedLanguage(
        string Code,
        string DisplayNameResourceKey);

    internal static class Localization
    {
        private const string AppRegistryPath =
            @"Software\WallpaperControl";

        private const string LanguageValueName =
            "Language";

        private static readonly ResourceManager
            resourceManager =
                new ResourceManager(
                    "WallpaperControl.Strings",
                    Assembly.GetExecutingAssembly());

        private static readonly SupportedLanguage[]
            configuredLanguages =
            {
                new("de", "LanguageNameGerman"),
                new("en", "LanguageNameEnglish"),
                new("fr", "LanguageNameFrench"),
                new("es", "LanguageNameSpanish"),
                new("ja", "LanguageNameJapanese")
            };

        private static readonly List<SupportedLanguage>
            availableLanguages = new();

        public static IReadOnlyList<SupportedLanguage>
            AvailableLanguages =>
                availableLanguages;

        public static string CurrentLanguage
        {
            get;
            private set;
        } = "de";

        public static CultureInfo CurrentCulture =>
            CultureInfo.GetCultureInfo(
                CurrentLanguage);

        /// <summary>
        /// Loads available translations and applies the saved language or a supported fallback.
        /// </summary>
        public static void Initialize()
        {
            RefreshAvailableLanguages();

            string savedLanguage =
                LoadSavedLanguage();

            if (!IsLanguageAvailable(savedLanguage))
            {
                string installed =
                    CultureInfo
                        .InstalledUICulture
                        .TwoLetterISOLanguageName;

                savedLanguage =
                    IsLanguageAvailable(installed)
                    ? installed
                    : GetFallbackLanguage();
            }

            ApplyLanguage(
                savedLanguage,
                save: false);

#if DEBUG
            ValidateResources();
#endif
        }

        /// <summary>
        /// Rebuilds the language list from resource sets available in the current installation.
        /// </summary>
        public static void RefreshAvailableLanguages()
        {
            availableLanguages.Clear();

            foreach (SupportedLanguage language
                in configuredLanguages)
            {
                if (HasResourcesForLanguage(
                    language.Code))
                {
                    availableLanguages.Add(
                        language);
                }
            }

            // German is the neutral resource and the fallback language.
            // When neutral resources exist, de must always be available.
            if (HasNeutralResources() &&
                !availableLanguages.Exists(
                    x => x.Code == "de"))
            {
                availableLanguages.Insert(
                    0,
                    new SupportedLanguage(
                        "de",
                        "LanguageNameGerman"));
            }
        }

        /// <summary>
        /// Checks whether the requested language has an available resource set.
        /// </summary>
        /// <param name="language">The requested language or culture code.</param>
        /// <returns>True when the normalized language has an available resource set.</returns>
        public static bool IsLanguageAvailable(
            string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return false;
            }

            string normalized =
                NormalizeLanguageCode(
                    language);

            foreach (SupportedLanguage item
                in availableLanguages)
            {
                if (string.Equals(
                    item.Code,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Applies and persists the requested language using the supported fallback rules.
        /// </summary>
        /// <param name="language">The requested language or culture code.</param>
        public static void SetLanguage(
            string language)
        {
            RefreshAvailableLanguages();

            string normalized =
                NormalizeLanguageCode(
                    language);

            if (!IsLanguageAvailable(normalized))
            {
                normalized =
                    GetFallbackLanguage();
            }

            ApplyLanguage(
                normalized,
                save: true);
        }

        /// <summary>
        /// Looks up a localized string using the requested or current language and the resource fallback rules.
        /// </summary>
        /// <param name="key">The localization resource key.</param>
        /// <returns>The localized string or the existing fallback for a missing resource.</returns>
        public static string Get(
            string key)
        {
            return Get(
                key,
                CurrentLanguage);
        }

        /// <summary>
        /// Looks up a localized string using the requested or current language and the resource fallback rules.
        /// </summary>
        /// <param name="key">The localization resource key.</param>
        /// <param name="language">The requested language or culture code.</param>
        /// <returns>The localized string or the existing fallback for a missing resource.</returns>
        public static string Get(
            string key,
            string language)
        {
            string normalized =
                NormalizeLanguageCode(
                    language);

            if (!IsLanguageAvailable(normalized))
            {
                normalized =
                    GetFallbackLanguage();
            }

            CultureInfo culture =
                CultureInfo.GetCultureInfo(
                    normalized);

            return resourceManager.GetString(
                       key,
                       culture) ??
                   resourceManager.GetString(
                       key,
                       CultureInfo
                           .GetCultureInfo("de")) ??
                   key;
        }

        /// <summary>
        /// Updates the active language and culture, optionally persisting the choice.
        /// </summary>
        /// <param name="language">The requested language or culture code.</param>
        /// <param name="save">True to persist the selected language.</param>
        private static void ApplyLanguage(
            string language,
            bool save)
        {
            CurrentLanguage =
                NormalizeLanguageCode(
                    language);

            CultureInfo culture =
                CultureInfo.GetCultureInfo(
                    CurrentLanguage);

            CultureInfo.CurrentUICulture =
                culture;

            Thread.CurrentThread.CurrentUICulture =
                culture;

            if (save)
            {
                SaveLanguage(
                    CurrentLanguage);
            }
        }

        /// <summary>
        /// Reads the saved language preference when the application registry key is available.
        /// </summary>
        /// <returns>The stored language code, or null when no preference can be read.</returns>
        private static string LoadSavedLanguage()
        {
            try
            {
                using RegistryKey? key =
                    Registry.CurrentUser.OpenSubKey(
                        AppRegistryPath);

                return NormalizeLanguageCode(
                    key?.GetValue(
                        LanguageValueName)
                    as string);
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Persists the language choice without interrupting the interface if the registry is unavailable.
        /// </summary>
        /// <param name="language">The requested language or culture code.</param>
        private static void SaveLanguage(
            string language)
        {
            try
            {
                using RegistryKey key =
                    Registry.CurrentUser.CreateSubKey(
                        AppRegistryPath);

                key.SetValue(
                    LanguageValueName,
                    language,
                    RegistryValueKind.String);
            }
            catch (Exception ex) { SettingsPersistence.ReportFailure("Could not persist the language preference.", ex); }
        }

        /// <summary>
        /// Chooses an available fallback language for missing or unsupported preferences.
        /// </summary>
        /// <returns>An available fallback language code.</returns>
        private static string GetFallbackLanguage()
        {
            if (availableLanguages.Exists(
                x => x.Code == "de"))
            {
                return "de";
            }

            if (availableLanguages.Count > 0)
            {
                return availableLanguages[0].Code;
            }

            return "de";
        }

        /// <summary>
        /// Normalizes a language or culture name to its supported two-letter language code.
        /// </summary>
        /// <param name="language">The requested language or culture code.</param>
        /// <returns>The normalized two-letter language code or the existing fallback for empty input.</returns>
        private static string NormalizeLanguageCode(
            string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return "";
            }

            try
            {
                CultureInfo culture =
                    CultureInfo.GetCultureInfo(
                        language);

                return culture
                    .TwoLetterISOLanguageName
                    .ToLowerInvariant();
            }
            catch
            {
                string value =
                    language
                        .Trim()
                        .ToLowerInvariant();

                int separator =
                    value.IndexOfAny(
                        new[] { '-', '_' });

                return separator > 0
                    ? value[..separator]
                    : value;
            }
        }

        /// <summary>
        /// Checks translated resources and format placeholders against the neutral resource set in debug builds.
        /// </summary>
        private static void ValidateResources()
        {
            ResourceSet? neutralSet =
                resourceManager.GetResourceSet(
                    CultureInfo.GetCultureInfo("de"),
                    createIfNotExists: true,
                    tryParents: true);

            if (neutralSet == null)
            {
                throw new InvalidOperationException(
                    "Localization audit: neutral resource set is missing.");
            }

            Dictionary<string, string> neutral =
                new(StringComparer.Ordinal);

            foreach (System.Collections.DictionaryEntry entry
                in neutralSet)
            {
                if (entry.Key is string key)
                {
                    neutral[key] =
                        entry.Value?.ToString() ?? "";
                }
            }

            List<string> errors = new();

            foreach (SupportedLanguage language
                in availableLanguages)
            {
                if (language.Code == "de")
                {
                    continue;
                }

                ResourceSet? set =
                    resourceManager.GetResourceSet(
                        CultureInfo.GetCultureInfo(
                            language.Code),
                        createIfNotExists: true,
                        tryParents: false);

                if (set == null)
                {
                    errors.Add(
                        $"{language.Code}: resource set missing");
                    continue;
                }

                Dictionary<string, string> translated =
                    new(StringComparer.Ordinal);

                foreach (System.Collections.DictionaryEntry entry
                    in set)
                {
                    if (entry.Key is string key)
                    {
                        translated[key] =
                            entry.Value?.ToString() ?? "";
                    }
                }

                foreach ((string key, string neutralValue)
                    in neutral)
                {
                    if (!translated.TryGetValue(
                        key,
                        out string? translatedValue))
                    {
                        errors.Add(
                            $"{language.Code}: missing key '{key}'");
                        continue;
                    }

                    string neutralPlaceholders =
                        GetPlaceholderSignature(
                            neutralValue);

                    string translatedPlaceholders =
                        GetPlaceholderSignature(
                            translatedValue);

                    if (!string.Equals(
                        neutralPlaceholders,
                        translatedPlaceholders,
                        StringComparison.Ordinal))
                    {
                        errors.Add(
                            $"{language.Code}: placeholders differ for '{key}' " +
                            $"({neutralPlaceholders} != {translatedPlaceholders})");
                    }
                }

                foreach (string key in translated.Keys)
                {
                    if (!neutral.ContainsKey(key))
                    {
                        errors.Add(
                            $"{language.Code}: extra key '{key}'");
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Localization audit failed:\n" +
                    string.Join("\n", errors));
            }
        }

        /// <summary>
        /// Extracts the format-placeholder signature used to compare translated resource strings.
        /// </summary>
        /// <param name="value">The resource text whose composite-format placeholders are inspected.</param>
        /// <returns>The normalized sequence of format placeholders used to validate translation compatibility.</returns>
        private static string GetPlaceholderSignature(
            string value)
        {
            MatchCollection matches =
                Regex.Matches(
                    value,
                    @"\{\d+(?:[^}]*)\}");

            List<string> placeholders = new();

            foreach (Match match in matches)
            {
                placeholders.Add(
                    match.Value);
            }

            placeholders.Sort(
                StringComparer.Ordinal);

            return string.Join(
                "|",
                placeholders);
        }

        /// <summary>
        /// Checks whether the requested language has its own resource set without parent fallback.
        /// </summary>
        /// <param name="languageCode">The language code used for localized text.</param>
        /// <returns>True when the requested language has a resource set without parent fallback.</returns>
        private static bool HasResourcesForLanguage(string languageCode)
        {
            string code =
                NormalizeLanguageCode(
                    languageCode);

            if (code == "de")
            {
                return HasNeutralResources();
            }

            try
            {
                CultureInfo culture =
                    CultureInfo.GetCultureInfo(
                        code);

                ResourceSet? set =
                    resourceManager.GetResourceSet(
                        culture,
                        createIfNotExists: true,
                        tryParents: false);

                return set != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks whether the assembly contains the neutral translation resources.
        /// </summary>
        /// <returns>True when neutral translation resources are available.</returns>
        private static bool HasNeutralResources()
        {
            try
            {
                return resourceManager.GetResourceSet(
                           CultureInfo
                               .GetCultureInfo("de"),
                           createIfNotExists: true,
                           tryParents: true) != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
