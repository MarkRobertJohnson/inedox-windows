﻿using System.ComponentModel;
using Inedo.Documentation;
using Inedo.Extensibility;
using Inedo.Extensibility.Operations;

namespace Inedo.Extensions.Windows.Operations.IIS.Sites
{
    [DisplayName("Start Site")]
    [Description("Starts an IIS Site.")]
    [ScriptAlias("Start-Site")]
    [ScriptNamespace(Namespaces.IIS)]
    public sealed class StartSiteOperation : SiteOperationBase
    {
        internal override SiteOperationType OperationType => SiteOperationType.Start;

        protected override ExtendedRichDescription GetDescription(IOperationConfiguration config)
        {
            return new ExtendedRichDescription(
                new RichDescription(
                    "Start ",
                    new Hilite(config[nameof(SiteName)]),
                    " IIS Site"
                )
            );
        }
    }
}
