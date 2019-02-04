using Inedo.Agents;
using Inedo.Diagnostics;
using Inedo.Documentation;
using Inedo.ExecutionEngine;
using Inedo.Extensibility;
using Inedo.Extensibility.Configurations;
using Inedo.Extensibility.Operations;
using Inedo.Extensions.Windows.Configurations.PSDsc;
using Inedo.Extensions.Windows.PowerShell;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Inedo.Extensions.Windows.Operations
{
    [DisplayName("Ensure DSC Configuration")]
    [Description("Ensures a DSC configuration script file has been enacted")]
    [ScriptAlias("Ensure-DSCConfig")]
    [ScriptNamespace(Namespaces.PowerShell, PreferUnqualified = true)]
    [Tag(Tags.PowerShell)]
    public sealed class EnsurePSDscScriptOperation : EnsureOperation<PSDscScriptConfiguration>
    {
        private PSProgressEventArgs currentProgress;

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var shortDesc = new RichDescription($"Ensures a DSC configuration");
            var longDesc = new RichDescription("Tests whether or not a the specified DSC configuration has been enacted.  If not, then the configuration file is enacted.");
            return new ExtendedRichDescription(shortDesc, longDesc);
        }

        public override async Task ConfigureAsync(IOperationExecutionContext context)
        {
            if (!ValidateConfiguration())
                return;

            var scriptPath = await GetScriptPath();
            var configDataPath = await GetConfigDataPath(scriptPath, Template.DscConfigDataPath, Template.DscConfigDataAsset);
            var scriptContents = System.IO.File.ReadAllText(scriptPath);
            string configDataContents = null;
            if (configDataPath != null)
                configDataContents = System.IO.File.ReadAllText(configDataPath);
            this.LogInformation($"Invoking DSC Configuration '{scriptPath}' (Path: {scriptPath}) ...");
            if (!string.IsNullOrWhiteSpace(configDataPath))
            {
                this.LogInformation($"Using DSC Configuration Data from '{configDataPath}'");
            }
            if (context.Simulation)
            {
                this.LogInformation($"Running simulation, will not configure ...'");
                return;
            }
            this.LogDebug($"Enacting DSC configuration '{scriptPath}' ...");
        


            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();

            var configNames = await GetDscConfigurationNamesFromScript(context.CancellationToken, jobRunner, scriptPath, scriptContents);
            await CompileDscConfigurationsInScript(context.CancellationToken, jobRunner, scriptPath, configNames.ToArray(), configDataPath, configDataContents);

            var dir = System.IO.Path.GetDirectoryName(scriptPath);

            // Start DSC for each configuration in the script file
            foreach (var config in configNames)
            {
                var configDir = System.IO.Path.Combine(dir, config);

                var job = new ExecutePowerShellJob
                {
                    DebugLogging = Template.DebugLogging,
                    VerboseLogging = Template.VerboseLogging,
                    Variables = new Dictionary<string, object>
                {
                    { "configDir", configDir },
                },
                    ScriptText = @"
Start-DscConfiguration -Path $configDir -Wait -Verbose -Force
"
                };
                if (Template.DebugLogging)
                    this.LogDebug(job.ScriptText);

                job.MessageLogged += (s, e) => this.Log(e.Level, e.Message);;
                job.ProgressUpdate += (s, e) => Interlocked.Exchange(ref this.currentProgress, e);

                try
                {
                    var result = await jobRunner.ExecuteJobAsync(job, context.CancellationToken);
                }
                catch (Exception)
                {
                    var newTemplate = Template.DeepCopy() as PSDscScriptConfiguration;
                    newTemplate.Configured = false;
                    var compareResult = Template.Compare(newTemplate);
                    
                   
                    this.Template.Configured = false;
                    throw;
                }
            }
        }

        public override async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context)
        {
            if (!ValidateConfiguration())
            {
                return new PSDscScriptConfiguration();
            }
            string scriptPath = await GetScriptPath();
            var configDataPath = await GetConfigDataPath(scriptPath, Template.DscConfigDataPath, Template.DscConfigDataAsset);
            var scriptContents = System.IO.File.ReadAllText(scriptPath);
            string configDataContents = null;
            if (configDataPath != null)
                configDataContents = System.IO.File.ReadAllText(configDataPath);

            this.LogInformation($"Testing DSC Configuration '{scriptPath}' ...");
            if(!string.IsNullOrWhiteSpace(configDataPath))
            {
                this.LogInformation($"Using DSC Configuration Data from '{configDataPath}'");
            }

            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();

            var configNames = await GetDscConfigurationNamesFromScript(context.CancellationToken, jobRunner, scriptPath, scriptContents);
            await CompileDscConfigurationsInScript(context.CancellationToken, jobRunner, scriptPath, configNames.ToArray(), configDataPath, configDataContents);
            //PSremoting must be enabled to test DSC configurations
            await EnsurePsRemotingEnabled(context, jobRunner);
            bool configured = await TestDscConfigurationsInScript(context, jobRunner, scriptPath, configNames.ToArray());

            return await Complete(new PSDscScriptConfiguration
            {
                Configured = configured,
                DscScriptPath = this.Template.DscScriptPath,
                DscScriptAsset = this.Template.DscScriptAsset,
                DscConfigDataAsset = this.Template.DscConfigDataAsset,
                DscConfigDataPath = this.Template.DscConfigDataPath,
                DebugLogging = this.Template.DebugLogging,
                VerboseLogging = this.Template.VerboseLogging
            });
        }

        public override OperationProgress GetProgress()
        {
            var p = this.currentProgress;
            return new OperationProgress(p?.PercentComplete, p?.Activity);
        }

        private async Task<string> GetScriptPath()
        {
            var scriptPath = Template.DscScriptPath;
            //If script asset is specified, then get the asset contents, write to a temp file location
            if (!string.IsNullOrWhiteSpace(Template.DscScriptAsset))
            {
                var assetName = Template.DscScriptAsset.Split(new[] { "::" }, 2, StringSplitOptions.None).Last();

                var scriptContents = await PSUtil.GetScriptTextAsync(this, Template.DscScriptAsset, null);

                var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), assetName, Hash(scriptContents));
                System.IO.Directory.CreateDirectory(tempDir);

                scriptPath = System.IO.Path.Combine(tempDir, System.IO.Path.GetFileNameWithoutExtension(assetName) + ".ps1");

                
                System.IO.File.WriteAllText(scriptPath, scriptContents, Encoding.UTF8);

            }

            return scriptPath;
        }

        private async Task<string> GetConfigDataPath(string scriptPath, string configDataPath, string configDataAsset)
        {
            var newConfigDataPath = configDataPath;
            //If script asset is specified, then get the asset contents, write to a temp file location
            if (!string.IsNullOrWhiteSpace(configDataAsset))
            {
                var assetName = configDataAsset.Split(new[] { "::" }, 2, StringSplitOptions.None).Last();

                var dir = System.IO.Path.GetDirectoryName(scriptPath);

                newConfigDataPath = System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(assetName) + ".psd1");

                var dataContents = await PSUtil.GetScriptTextAsync(this, configDataAsset, null);
                System.IO.File.WriteAllText(newConfigDataPath, dataContents, Encoding.UTF8);

            }

            return newConfigDataPath;
        }

        private async Task<List<string>> GetDscConfigurationNamesFromScript(CancellationToken cancellationToken, IRemoteJobExecuter jobRunner,
            string scriptPath, string scriptContents)
        {
            var collectJob = new ExecutePowerShellJob
            {
                CollectOutput = true,
                OutVariables = new[] { "results" },
                DebugLogging = Template.DebugLogging,
                VerboseLogging = Template.VerboseLogging,
                Variables = new Dictionary<string, object>
                {
                    {"scriptPath", scriptPath },
                    {"scriptContents", scriptContents }
                },
                LogOutput = true,
                ScriptText = @"
# Source the configuration script so the configurations are available to invoke

$scriptDir = ([IO.Path]::GetDirectoryName($scriptPath))
mkdir $scriptDir -force -erroraction silentlycontinue
cd $scriptDir | out-null
$scriptContents > $scriptPath

# Get the AST of the file
Write-Host $scriptPath
$tokens = $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile(
    $scriptPath,
    [ref]$tokens,
    [ref]$errors)

# Get only configuration definition ASTs
$configDefs = $ast.FindAll({
    param([System.Management.Automation.Language.Ast] $Ast)
    $Ast -is [System.Management.Automation.Language.ConfigurationDefinitionAst]
}, $true)

# Get just the names of the configurations
$results = @()
$configNames = $configDefs | ForEach-Object {
    $results += $_.InstanceName.Extent.Text
}
"
            };

            if(Template.DebugLogging)
                this.LogDebug(collectJob.ScriptText);
            collectJob.MessageLogged += (s, e) => {
                this.Log(e.Level, e.Message);
                }
            ;

            collectJob.ProgressUpdate += (s, e) => Interlocked.Exchange(ref this.currentProgress, e);

            var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(collectJob, cancellationToken);
            var collectValues = ((List<Object>)result.OutVariables["results"]).Cast<string>();

            return collectValues.ToList();
        }


        private async Task EnsurePsRemotingEnabled(IOperationCollectionContext context, IRemoteJobExecuter jobRunner)
        {
            var collectJob = new ExecutePowerShellJob
            {
                CollectOutput = true,
                OutVariables = new[] { ExecutePowerShellJob.CollectOutputAsDictionary },
                DebugLogging = Template.DebugLogging,
                VerboseLogging = Template.VerboseLogging,
                Variables = null,
                LogOutput = true,
                ScriptText = @"
if(!(Test-WsMan -erroraction silentlycontinue)) {
    Enable-PSRemoting -Force -SkipNetworkProfileCheck -Confirm:$false
}
$trustedHosts = get-item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue
if($trustedHosts -and $trustedHosts.Value -ne '*') {
    set-item wsman:\localhost\Client\TrustedHosts -value '<local>' -Concatenate -Force
}
"
            };

            if (Template.DebugLogging)
                this.LogDebug(collectJob.ScriptText);
            collectJob.MessageLogged += (s, e) => this.Log(e.Level, e.Message);
            collectJob.ProgressUpdate += (s, e) => Interlocked.Exchange(ref this.currentProgress, e);

            var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(collectJob, context.CancellationToken);
        }

        private async Task CompileDscConfigurationsInScript(CancellationToken cancellationToken, IRemoteJobExecuter jobRunner,
        string scriptPath, string[] configNames, string configDataPath = null, string configDataContents = null)
        {
            var collectJob = new ExecutePowerShellJob
            {
                CollectOutput = true,
                OutVariables = null,
                DebugLogging = Template.DebugLogging,
                VerboseLogging = Template.VerboseLogging,
                Variables = new Dictionary<string, object>
                {
                    {"scriptPath", scriptPath },
                    {"configNames", configNames },
                    {"ConfigurationData", configDataPath },
                    {"ConfigurationDataContents", configDataContents }
                },
                LogOutput = true,
                ScriptText = @"
# Source the configuration script so the configurations are available to invoke
$scriptDir = ([IO.Path]::GetDirectoryName($scriptPath))
cd $scriptDir | out-null

$configArg = ''
if($ConfigurationData) {
    $ConfigurationDataContents > $ConfigurationData
    $configArg = ""-ConfigurationData '$ConfigurationData'""
}

# Source the configuration script so the configurations are available to invoke
cd ([IO.Path]::GetDirectoryName($scriptPath)) | out-null
. $scriptPath | out-null

Import-Module PSDesiredStateConfiguration | out-null

#Each config name is also the directory where each configuration was compiled to
$results = @{}
$configNames | foreach {
    #Clean any existing directories
    del $_ -Force -Recurse -ErrorAction SilentlyContinue | out-null

    #Now compile the configuration into MOFs (Each configuration will get its own directory)
    Invoke-Expression -Command ""$($_) $configArg"" | out-null
}
"
            };

            if (Template.DebugLogging)
                this.LogDebug(collectJob.ScriptText);
            collectJob.MessageLogged += (s, e) => this.Log(e.Level, e.Message);
            collectJob.ProgressUpdate += (s, e) => Interlocked.Exchange(ref this.currentProgress, e);

            await jobRunner.ExecuteJobAsync(collectJob, cancellationToken);
        }

        /// <summary>
        /// Expects that each configuration has already been compiled
        /// </summary>
        /// <param name="context"></param>
        /// <param name="jobRunner"></param>
        /// <param name="scriptPath"></param>
        /// <param name="configNames"></param>
        /// <returns></returns>
        private async Task<bool> TestDscConfigurationsInScript(IOperationCollectionContext context, IRemoteJobExecuter jobRunner,
           string scriptPath, string[] configNames)
        {
            var collectJob = new ExecutePowerShellJob
            {
                CollectOutput = true,
                OutVariables = new[] { "results" },
                DebugLogging = Template.DebugLogging,
                VerboseLogging = Template.VerboseLogging,
                Variables = new Dictionary<string, object>
                {
                    {"scriptPath", scriptPath },
                    {"configNames", configNames }
                },
                LogOutput = true,
                ScriptText = @"
# Source the configuration script so the configurations are available to invoke
$scriptDir = ([IO.Path]::GetDirectoryName($scriptPath))
mkdir $scriptDir -force -erroraction silentlycontinue
cd $scriptDir | out-null

$configNames | foreach {
    $results += @{$_ = (Test-DscConfiguration -Path $_ -Verbose).InDesiredState}
}
"
            };

            if (Template.DebugLogging)
                this.LogDebug(collectJob.ScriptText);
            collectJob.MessageLogged += (s, e) => this.Log(e.Level, e.Message);
            collectJob.ProgressUpdate += (s, e) => Interlocked.Exchange(ref this.currentProgress, e);

            var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(collectJob, context.CancellationToken);
            var collectValues = ((Dictionary<string, object>)result.OutVariables["results"]);

            var isConfigured = true;
            foreach (var item in collectValues)
            {
                if(!bool.Parse(item.Value?.ToString()))
                {
                    this.LogInformation($"DSC configuration '{item.Key}' was not configured");
                    isConfigured = false;
                }
                

            }
            return isConfigured;
        }


        public override ComparisonResult Compare(PersistedConfiguration other)
        {
            return base.Compare(other);
        }

        private bool ValidateConfiguration()
        {
            bool valid = true;
            if (this.Template == null)
                throw new InvalidOperationException("Template is not set.");

            if (string.IsNullOrWhiteSpace(this.Template.DscScriptAsset) && string.IsNullOrWhiteSpace(this.Template.DscScriptPath))
            {
                this.LogError("DSC Configuration script missing. Specify a value for either \"DscScript Asset\" or \"DscScriptPath\".");
                valid = false;
            }

            return valid;
        }


        static string Hash(string input)
        {
            using (SHA1Managed sha1 = new SHA1Managed())
            {
                var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(hash.Length * 2);

                foreach (byte b in hash)
                {
                    // can be "x2" if you want lowercase
                    sb.Append(b.ToString("X2"));
                }

                return sb.ToString();
            }
        }
    }
}
