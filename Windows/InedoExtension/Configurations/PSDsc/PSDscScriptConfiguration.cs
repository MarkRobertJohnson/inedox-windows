using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Serialization;
using Inedo.Web;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inedo.Extensions.Windows.Configurations.PSDsc
{
    [Serializable]
    [DisplayName("PSDsc Script Configuration")]
    public class PSDscScriptConfiguration : PersistedConfiguration
    {
        [Persistent]
        [ScriptAlias("Configured")]
        [DefaultValue(true)]
        public bool Configured { get; set; } = true;

        [Required]
        [Persistent]
        [ConfigurationKey]
        [ScriptAlias("Path")]
        [DisplayName("File path")]
        public string FilePath { get; set; }

        public List<string> ConfigNames { get; set; }
    }


}
