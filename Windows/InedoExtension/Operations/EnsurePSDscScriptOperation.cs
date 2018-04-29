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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Inedo.Extensions.Windows.Operations
{
    [DisplayName("Ensure DSC Configuration")]
    [Description("Ensures a DSC configuration script file has been enacted")]
    [ScriptAlias("Ensure-DSCConfig")]
    [ScriptNamespace("DSC")]
    [Tag("dsc")]
    [Tag("ensure")]
    [Tag("configuration")]
    [DefaultProperty(nameof(FilePath))]
    public class EnsurePSDscScriptOperation : EnsureOperation<PSDscScriptConfiguration>
    {
        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            var shortDesc = new RichDescription($"Ensures a DSC configuration");
            var longDesc = new RichDescription("Tests whether or not a the specified DSC configuration has been enacted.  If not, then the configuration file is enacted.");
            return new ExtendedRichDescription(shortDesc, longDesc);
        }

        public override async Task ConfigureAsync(IOperationExecutionContext context)
        {
   
            this.LogDebug($"Ensuring DSC configuration {FilePath}...");
        

            if (context.Simulation)
            {
                this.LogInformation("Invoking DscResource...");
                return;
            }

            FilePath = Template.FilePath;

            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();

            var configNames = await GetDscConfigurationNamesFromScript(context.CancellationToken, jobRunner, FilePath);

            var dir = System.IO.Path.GetDirectoryName(Template.FilePath);

            // Start DSC for each configuration in the script file
            foreach (var config in configNames)
            {
                var configDir = System.IO.Path.Combine(dir, config);

                var job = new ExecutePowerShellJob
                {
                    DebugLogging = true,
                    Variables = new Dictionary<string, object>
                {
                    { "configDir", configDir },
                },
                    ScriptText = @"
Start-DscConfiguration -Path $configDir -Wait -Verbose 
"
                };
                this.LogDebug(job.ScriptText);

                job.MessageLogged += (s, e) => this.Log(e.Level, e.Message);

                await jobRunner.ExecuteJobAsync(job, context.CancellationToken);
            }
            


        }

        public override async Task<PersistedConfiguration> CollectAsync(IOperationCollectionContext context)
        {

            if (this.Template == null)
                throw new InvalidOperationException("Template is not set.");

            var fullScriptName = this.FilePath;
            if (this.Template.FilePath == null)
            {
                this.LogError("Bad or missing DSC Script file name.");
                return new PSDscScriptConfiguration();
            }
            FilePath = this.Template.FilePath;

            var jobRunner = await context.Agent.GetServiceAsync<IRemoteJobExecuter>();

            var configNames = await GetDscConfigurationNamesFromScript(context.CancellationToken, jobRunner, FilePath);
            await EnsurePsRemotingEnabled(context, jobRunner);
            bool configured = await TestDscConfigurationsInScript(context, jobRunner, FilePath, configNames.ToArray());

            return await Complete(new PSDscScriptConfiguration {
                Configured = configured,
                FilePath = this.Template.FilePath,
                ConfigNames = configNames
            });
        }

        private async Task<List<string>> GetDscConfigurationNamesFromScript(CancellationToken cancellationToken, IRemoteJobExecuter jobRunner,
            string scriptPath)
        {
            var collectJob = new ExecutePowerShellJob
            {
                CollectOutput = true,
                //OutVariables = new[] { ExecutePowerShellJob.CollectOutputAsDictionary },
                OutVariables = new[] { "results" },
                DebugLogging = true,
                VerboseLogging = true,
                Variables = new Dictionary<string, object>
                {
                    {"scriptPath", scriptPath }
                },
                LogOutput = true,
                ScriptText = @"
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
            //* NOTE: Would be better to do in code
              
            var ast = Parser.ParseFile(scriptPath, out var _, out var errors);
            var configDefinitions = ast.FindAll(x => x is ConfigurationDefinitionAst, true);
            var configNames = configDefinitions.Select(x => (x as ConfigurationDefinitionAst)?.InstanceName?.Extent.Text);

            this.LogDebug(collectJob.ScriptText);
            collectJob.MessageLogged += (s, e) => {
                this.Log(e.Level, e.Message);
                }
            ;
      
            var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(collectJob, cancellationToken);
            //    var collectValues = ((Dictionary<string, object>)result.OutVariables[ExecutePowerShellJob.CollectOutputAsDictionary]).ToDictionary(k => k.Key, k => k.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
            var collectValues = ((List<Object>)result.OutVariables["results"]).Cast<string>();

            return collectValues.ToList();
        }


        private async Task EnsurePsRemotingEnabled(IOperationCollectionContext context, IRemoteJobExecuter jobRunner)
        {
            var collectJob = new ExecutePowerShellJob
            {
                CollectOutput = true,
                OutVariables = new[] { ExecutePowerShellJob.CollectOutputAsDictionary },
                DebugLogging = true,
                Variables = null,
                LogOutput = true,
                ScriptText = @"
Enable-PSRemoting -Force -SkipNetworkProfileCheck -Confirm:$false
$trustedHosts = get-item WSMan:\localhost\Client\TrustedHosts -ErrorAction SilentlyContinue
if($trustedHosts -and $trustedHosts.Value -ne '*') {
    set - item wsman:\localhost\Client\TrustedHosts -value '<local>' -Concatenate
}
"
            };

            this.LogDebug(collectJob.ScriptText);
            collectJob.MessageLogged += (s, e) => this.Log(e.Level, e.Message);

            var result = (ExecutePowerShellJob.Result)await jobRunner.ExecuteJobAsync(collectJob, context.CancellationToken);
        }

        private async Task<bool> TestDscConfigurationsInScript(IOperationCollectionContext context, IRemoteJobExecuter jobRunner,
           string scriptPath, string[] configNames)
        {
            var collectJob = new ExecutePowerShellJob
            {
                CollectOutput = true,
                OutVariables = new[] { "results" },
                DebugLogging = true,
                Variables = new Dictionary<string, object>
                {
                    {"scriptPath", scriptPath },
                    {"configNames", configNames }
                },
                LogOutput = true,
                ScriptText = @"
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
    Invoke-Expression -Command $_ | out-null
    $results += @{$_ = (Test-DscConfiguration -Path $_).InDesiredState}
}
"
            };

            this.LogDebug(collectJob.ScriptText);
            collectJob.MessageLogged += (s, e) => this.Log(e.Level, e.Message);

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

        [Required]
        [ScriptAlias("Path")]
        [DisplayName("File path")]
        [Description("The path to the DSC configuration script to ensure.")]
        public string FilePath { get; set; }
    }
}
