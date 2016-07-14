﻿using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Inedo.Diagnostics;
using Inedo.Documentation;
#if Otter
using Inedo.Otter.Extensibility;
using Inedo.Otter.Extensibility.Configurations;
using Inedo.Otter.Extensibility.Operations;
#elif BuildMaster
using Inedo.BuildMaster.Extensibility;
using Inedo.BuildMaster.Extensibility.Configurations;
using Inedo.BuildMaster.Extensibility.Operations;
#endif
using Inedo.Extensions.Windows.Configurations.IIS;
using Microsoft.Web.Administration;

namespace Inedo.Extensions.Windows.Operations.IIS.Sites
{
    [Serializable]
    [DisplayName("Ensure Site")]
    [Description("Ensures the existence of a site on a server.")]
    [ScriptAlias("Ensure-Site")]
    [Tag(Tags.IIS)]
    [ScriptNamespace(Namespaces.IIS)]
    [Tag(Tags.Sites)]
    [SeeAlso(typeof(AppPools.EnsureIisAppPoolOperation))]
    [Example(@"
# ensures that the Otter web site is present on the web server, and binds the site to the single IP address 192.0.2.100 on port 80 and hostname ""example.com""
IIS::Ensure-Site(
    Name: Otter,
    AppPool: OtterAppPool,
    Path: E:\Websites\Otter,
    Protocol: http,
    Binding: 192.0.2.100:80:example.com 
);

# ensures that the Default Web Site is removed from the web server
IIS::Ensure-Site(
    Name: Default Web Site,
    Exists: false
);
")]
    public sealed class EnsureIisSiteOperation : RemoteEnsureOperation<IisSiteConfiguration>
    {
        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Ensure ",
                    new Hilite(config[nameof(IisSiteConfiguration.Name)])
                )
            );
        }

#if Otter
        protected override Task<PersistedConfiguration> RemoteCollectAsync(IRemoteOperationExecutionContext context)
        {
            this.LogDebug($"Looking for Site \"{this.Template.Name}\"...");
            using (var manager = new ServerManager())
            {
                var site = manager.Sites[this.Template.Name];
                if (site == null)
                {
                    this.LogInformation($"Site \"{this.Template.Name}\" does not exist.");
                    return Task.FromResult<PersistedConfiguration>(new IisSiteConfiguration { Name = this.Template.Name, Exists = false });
                }

                return Task.FromResult<PersistedConfiguration>(IisSiteConfiguration.FromMwaSite(this, site, this.Template));
            }
        }
#endif

        protected override Task RemoteConfigureAsync(IRemoteOperationExecutionContext context)
        {
            this.LogDebug($"Looking for Site \"{this.Template.Name}\"...");
            using (var manager = new ServerManager())
            {
                var site = manager.Sites[this.Template.Name];
                if (this.Template.Exists)
                {
                    if (!string.IsNullOrWhiteSpace(this.Template.ApplicationPoolName))
                    {
                        if (manager.ApplicationPools[this.Template.ApplicationPoolName] == null)
                        {
                            this.LogError($"The specified application pool ({this.Template.ApplicationPoolName}) does not exist.");
                            return Complete();
                        }
                    }

                    if (site == null)
                    {
                        this.LogDebug("Does not exist. Creating...");
                        if (!context.Simulation)
                        {
                            site = manager.Sites.Add(this.Template.Name, this.Template.BindingProtocol, this.Template.BindingInformation, this.Template.VirtualDirectoryPhysicalPath);
                            manager.CommitChanges();
                        }

                        this.LogInformation($"Site \"{this.Template.Name}\" added.");
                        this.LogDebug("Reloading configuration...", this.Template.Name);
                        site = manager.Sites[this.Template.Name];
                    }

                    this.LogDebug("Applying configuration...");
                    if (!context.Simulation)
                        IisSiteConfiguration.SetMwaSite(this, this.Template, site);
                }
                else
                {
                    if (site == null)
                    {
                        this.LogWarning("Site does not exist.");
                        return Complete();
                    }

                    if (!context.Simulation)
                        manager.Sites.Remove(site);
                }

                this.LogDebug("Committing configuration...");
                if (!context.Simulation)
                    manager.CommitChanges();

                this.LogInformation($"Site \"{this.Template.Name}\" {(this.Template.Exists ? "configured" : "removed")}.");
            }

            return Complete();
        }
    }
}