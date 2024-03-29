using System.Collections.Generic;
using Colossal;

namespace CS2M.Settings
{
    public class SettingsEn : IDictionarySource
    {
        private readonly Settings _settings;

        public SettingsEn(Settings settings)
        {
            _settings = settings;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>()
            {
                {
                    _settings.GetSettingsLocaleID(),
                    "CS2M"
                },
                {
                    _settings.GetOptionTabLocaleID("CS2M"),
                    "CS2M"
                },
                {
                    _settings.GetOptionGroupLocaleID("General Settings"),
                    "General Settings"
                },
                {
                    _settings.GetOptionLabelLocaleID("LoggingLevel"),
                    "Logging Level"
                },
                {
                    _settings.GetOptionDescLocaleID("LoggingLevel"),
                    "Set the logging level of the CS2M mod. Note that levels Trace or Debug may impair performance."
                }
            };
        }

        public void Unload()
        {
        }
    }
    
    public class SettingsDe : IDictionarySource
    {
        private readonly Settings _settings;

        public SettingsDe(Settings settings)
        {
            _settings = settings;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>()
            {
                {
                    _settings.GetSettingsLocaleID(),
                    "CS2M"
                },
                {
                    _settings.GetOptionTabLocaleID("CS2M"),
                    "CS2M"
                },
                {
                    _settings.GetOptionGroupLocaleID("General Settings"),
                    "Allgemeine Einstellungen"
                },
                {
                    _settings.GetOptionLabelLocaleID("LoggingLevel"),
                    "Logging Level"
                },
                {
                    _settings.GetOptionDescLocaleID("LoggingLevel"),
                    "Setzt das Logging Level der CS2M Mod. Beachte, dass die Level Debug und Trace die Performance beeinträchtigen können."
                }
            };
        }

        public void Unload()
        {
        }
    }
}
