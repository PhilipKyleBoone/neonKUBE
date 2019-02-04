﻿//-----------------------------------------------------------------------------
// FILE:	    LoginCommand.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft;
using Newtonsoft.Json;

using Neon.Common;
using Neon.Kube;

namespace NeonCli
{
    /// <summary>
    /// Implements the <b>login</b> command.
    /// </summary>
    public class LoginCommand : CommandBase
    {
        private const string usage = @"
Manages Kubernetes contexts for the user on the local workstation.

USAGE:

    neon login              USER@CLUSTER[/NAMESPACE]
    neon login export       USER@CLUSTER[/NAMESPACE] [PATH]
    neon login import       PATH
    neon login list|ls
    neon login remove|rm    USER@CLUSTER[/NAMESPACE]

    neon logout

ARGUMENTS:

    PATH                        - Path to an exported login file.
    USER@CLUSTER[/NAMESPACE]    - Kubernetes user, cluster and optional namespace
";

        /// <inheritdoc/>
        public override string[] Words
        {
            get { return new string[] { "login" }; }
        }

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override void Run(CommandLine commandLine)
        {
            if (commandLine.HasHelpOption || commandLine.Arguments.Length == 0)
            {
                Console.WriteLine(usage);
                Program.Exit(0);
            }

            Console.Error.WriteLine();

            var currentContext = KubeHelper.CurrentContext;
            var newContextName = KubeContextName.Parse(commandLine.Arguments.First());

            // Ensure that the new context exists.

            if (KubeHelper.Config.GetContext(newContextName) == null)
            {
                Console.Error.WriteLine($"*** Context [{newContextName}] not found.");
                Program.Exit(1);
            }

            // Check whether we're already logged into the cluster.

            if (KubeHelper.CurrentContext != null && newContextName == KubeContextName.Parse(KubeHelper.CurrentContext.Name))
            {
                Console.Error.WriteLine($"*** You are already logged into: {newContextName}");
                Program.Exit(0);
            }

            // Logout of the current cluster.

            if (currentContext != null)
            {
                Console.Error.WriteLine($"Logging out of [{currentContext.Name}].");
                KubeHelper.SetCurrentContext((string)null);
            }

            // ...and log into the new context.

            KubeHelper.SetCurrentContext(newContextName);
            Console.WriteLine($"*** Logged into [{newContextName}].");
        }

        /// <inheritdoc/>
        public override DockerShimInfo Shim(DockerShim shim)
        {
            return new DockerShimInfo(shimability: DockerShimability.None, ensureConnection: false);
        }
    }
}