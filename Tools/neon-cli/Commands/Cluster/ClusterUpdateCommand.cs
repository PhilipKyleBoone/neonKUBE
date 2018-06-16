﻿//-----------------------------------------------------------------------------
// FILE:	    ClusterUpdateCommand.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

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

using Neon.Cluster;
using Neon.Common;

namespace NeonCli
{
    /// <summary>
    /// Implements the <b>cluster update</b> command.
    /// </summary>
    public class ClusterUpdateCommand : CommandBase
    {
        private const string usage = @"
Updates a neonCLUSTER hosts, services, and containers.

USAGE:

    neon cluster update [OPTIONS]

OPTIONS:

    --force             - performs the update without prompting
    --max-parallel=#    - maximum number of host nodes to be updated
                          in parallel (defaults to 1)

REMARKS:

This command updates neonCLUSTER infrastructure related components including
the services, containers, and cluster state.

You can use [--max-parallel=#] to specify the number of cluster host nodes
to be updated in parallel.  This defaults to 1.  For clusters with multiple
cluster managers and enough nodes and service replicas, the update should
have limited or no impact on the cluster workloads.  This will take some time
though for large clusters.  You can use [--max-parallel] to speed this up at
the cost of potentially impacting your workloads.
";
        /// <inheritdoc/>
        public override string[] Words
        {
            get { return new string[] { "cluster", "update" }; }
        }

        /// <inheritdoc/>
        public override string[] ExtendedOptions
        {
            get { return new string[] { "--force", "--max-parallel" }; }
        }

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override void Run(CommandLine commandLine)
        {
            if (commandLine.HasHelpOption)
            {
                Console.WriteLine(usage);
                Program.Exit(0);
            }

            Console.WriteLine();

            if (!commandLine.HasOption("--force") && !Program.PromptYesNo($"*** Are you sure you want to UPDATE this cluster?"))
            {
                Program.Exit(0);
            }

            var clusterLogin = Program.ConnectCluster();
            var cluster      = new ClusterProxy(clusterLogin);
            var controller   = new SetupController<NodeDefinition>("cluster update", cluster.Nodes);

            ClusterUpdateManager.AddUpdateSteps(cluster, controller);

            if (controller.StepCount == 0)
            {
                Console.WriteLine("The cluster is already up-to-date.");
                Program.Exit(0);
            }

            if (!controller.Run())
            {
                Console.Error.WriteLine("*** ERROR: One or more UPDATE steps failed.");
                Program.Exit(1);
            }
        }

        /// <inheritdoc/>
        public override DockerShimInfo Shim(DockerShim shim)
        {
            return new DockerShimInfo(isShimmed: true, ensureConnection: true);
        }
    }
}
