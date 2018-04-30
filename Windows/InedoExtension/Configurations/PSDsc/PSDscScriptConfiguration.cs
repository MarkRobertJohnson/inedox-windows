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
        [DisplayName("Configured")]
        [Category("Collect and Configure")]
        [DefaultValue(true)]
        [Description("Whether or not the specified DSC configuration should be configured")]
        public bool Configured { get; set; } = true;

        [Persistent]
        [ConfigurationKey]
        [ScriptAlias("DscScriptPath")]
        [DisplayName("DSC Script path")]
        [Category("Collect and Configure")]
        [Description("The full path to an existing PowerShell DSC Configuration (.ps1) script file to ensure.")]
        public string DscScriptPath { get; set; }

        [Persistent]
        [ConfigurationKey]
        [ScriptAlias("DscScriptAsset")]
        [DisplayName("DSC Script asset")]
        [Category("Collect and Configure")]
        [Description("The name of an existing PowerShell DSC Configuration (.ps1) script file asset to ensure.")]
        public string DscScriptAsset { get; set; }

        [Persistent]
        [ConfigurationKey]
        [ScriptAlias("DscConfigDataPath")]
        [DisplayName("DSC Config Data path")]
        [Category("Configuration Data")]
        [Description("The full path to an existing PowerShell DSC Configuration data (.psd1) script file to pass to the configuration.")]
        public string DscConfigDataPath { get; set; }

        [Persistent]
        [ConfigurationKey]
        [ScriptAlias("DscConfigDataAsset")]
        [DisplayName("DSC Config Data asset")]
        [Category("Configuration Data")]
        [Description("The name of an existing PowerShell DSC Configuration data (.psd1) script file asset to pass to the configuration.")]
        public string DscConfigDataAsset { get; set; }

        [Persistent]
        [ScriptAlias("DebugLogging")]
        [DisplayName("Debug Logging")]
        [Category("Logging")]
        [DefaultValue(false)]
        [Description("Provides debug logging output")]
        public bool DebugLogging { get; set; } = false;

        [Persistent]
        [ScriptAlias("VerboseLogging")]
        [DisplayName("Verbose Logging")]
        [Category("Logging")]
        [DefaultValue(false)]
        [Description("Enables verbose log output (NOTE: this adds a lot of logging messages)")]
        public bool VerboseLogging { get; set; } = false;
    }


}
