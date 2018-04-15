﻿//-----------------------------------------------------------------------------
// FILE:	    AnsibleCommand.Module.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Consul;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using ICSharpCode.SharpZipLib.Zip;

using Neon.Cluster;
using Neon.Cryptography;
using Neon.Common;
using Neon.IO;
using Neon.Net;

using NeonCli.Ansible;

namespace NeonCli
{
    public partial class AnsibleCommand : CommandBase
    {
        /// <summary>
        /// Executes a built-in neonCLUSTER Ansible module. 
        /// </summary>
        /// <param name="login">The cluster login.</param>
        /// <param name="commandLine">The module command line: MODULE ARGS...</param>
        private void ExecuteModule(ClusterLogin login, CommandLine commandLine)
        {
            var module = commandLine.Arguments.ElementAtOrDefault(0);

            if (commandLine.HasHelpOption || module == null)
            {
                Console.WriteLine(moduleHelp);
                Program.Exit(0);
            }

            var context = new ModuleContext()
            {
                Module = module
            };

            try
            {
                // Verify that we're running in the context of another Ansible
                // command (probably [exec] or [play]).

                if (Environment.GetEnvironmentVariable("IN_NEON_ANSIBLE_COMMAND") == null)
                {
                    throw new NotSupportedException("Built-in neonCLUSTER Ansible modules can run only within [neon ansible exec] or [play].");
                }

                // Read the Ansible module arguments.

                var argsPath = commandLine.Arguments.ElementAtOrDefault(1);

                if (string.IsNullOrEmpty(argsPath))
                {
                    throw new ArgumentException("Expected a path to the module arguments file.");
                }

                context.Login = login;

                context.SetArguments(argsPath);

                // Connect to the cluster so the NeonClusterHelper methods will work.

                NeonClusterHelper.OpenCluster(login);

                // Run the module.

                switch (module.ToLowerInvariant())
                {
                    case "neon_certificate":

                        new CertificateModule().Run(context);
                        break;

                    case "neon_dashboard":

                        new DashboardModule().Run(context);
                        break;

                    case "neon_docker_service":

                        new DockerServiceModule().Run(context);
                        break;

                    case "neon_dns":

                        new DnsModule().Run(context);
                        break;

                    case "neon_route":

                        new RouteModule().Run(context);
                        break;

                    case "neon_couchbase_import":

                        new CouchbaseImportModule().Run(context);
                        break;

                    case "neon_couchbase_index":

                        new CouchbaseIndexModule().Run(context);
                        break;

                    case "neon_couchbase_query":

                        new CouchbaseQueryModule().Run(context);
                        break;

                    default:

                        throw new ArgumentException($"[{module}] is not a recognized neonCLUSTER Ansible module.");
                }
            }
            catch (Exception e)
            {
                context.Failed  = true;
                context.Message = NeonHelper.ExceptionError(e);

                context.WriteErrorLine(context.Message);
            }

            // Handle non-exception based errors.

            if (context.HasErrors && !context.Failed)
            {
                context.Failed  = true;
                context.Message = context.GetFirstError();
            }

            Console.WriteLine(context.ToString());

            // Exit right now to be sure that nothing else is written to STDOUT.

            Program.Exit(0);
        }
    }
}
