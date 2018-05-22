using Inedo.Extensibility.Configurations;
using Inedo.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inedo.Extensions.Windows.Configurations.PSDsc
{
    [Serializable]
    [DisplayName("Dictionary Configuration")]
    public class DictionaryConfiguration2 : PersistedConfiguration
    {
        private DictionaryConfiguration _config;

        //
        // Summary:
        //     Initializes a new instance of the Inedo.Extensibility.Configurations.DictionaryConfiguration
        //     class.
        public DictionaryConfiguration2()
        {
            _config = new DictionaryConfiguration();
        }


        //
        // Summary:
        //     Initializes a new instance of the Inedo.Extensibility.Configurations.DictionaryConfiguration
        //     class.
        //
        // Parameters:
        //   dictionary:
        //     Dictionary to copy.
        public DictionaryConfiguration2(IDictionary<string, string> dictionary)
        {
            _config = new DictionaryConfiguration(dictionary);
        }

        public void SetConfigKey(string configKey)
        {
            _configKey = configKey;
        }

        string _configKey;
        [ConfigurationKey]
        [Persistent]
        public override string ConfigurationKey
        {
            get
            {
                return _configKey;
            }
        }

        //
        // Summary:
        //     Gets or sets the items.
        [Persistent]
        public IEnumerable<DictionaryConfigurationEntry> Items => _config.Items;
        //
        // Summary:
        //     Gets a value indicating whether this instance has encrypted properties.
        public override bool HasEncryptedProperties => _config.HasEncryptedProperties;

        //
        // Summary:
        //     Returns the properties.
        //
        // Returns:
        //     The properties.
        public override IReadOnlyDictionary<string, string> GetPropertiesForDisplay(bool hideEncrypted)
        {
            return _config.GetPropertiesForDisplay(hideEncrypted);
        }
        //
        // Summary:
        //     Returns a copy as a dictionary.
        //
        // Returns:
        //     Copy as a dictionary.
        public Dictionary<string, string> ToDictionary()
        {
            return _config.ToDictionary();
        }

    }
}
