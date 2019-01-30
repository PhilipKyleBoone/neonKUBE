﻿//-----------------------------------------------------------------------------
// FILE:	    ClusterSetupCommand.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Neon.Common;
using Neon.Cryptography;
using Neon.IO;
using Neon.Kube;
using Neon.Net;
using Neon.Retry;
using Neon.Time;

namespace NeonCli
{
    /// <summary>
    /// Implements the <b>cluster setup</b> command.
    /// </summary>
    public class ClusterSetupCommand : CommandBase
    {
        //---------------------------------------------------------------------
        // Private types

        /// <summary>
        /// Holds information about a remote file we'll need to download.
        /// </summary>
        private class RemoteFile
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            /// <param name="path">The file path.</param>
            /// <param name="permissions">Optional file permissions.</param>
            /// <param name="owner">Optional file owner.</param>
            public RemoteFile(string path, string permissions = "600", string owner = "root:root")
            {
                this.Path        = path;
                this.Permissions = permissions;
                this.Owner       = owner;
            }

            /// <summary>
            /// Returns the file path.
            /// </summary>
            public string Path { get; private set; }

            /// <summary>
            /// Returns the file permissions.
            /// </summary>
            public string Permissions { get; private set; }

            /// <summary>
            /// Returns the file owner formatted as: USER:GROUP.
            /// </summary>
            public string Owner { get; private set; }
        }

        //---------------------------------------------------------------------
        // Implementation

        private const string usage = @"
Configures a neonHIVE as described in the cluster definition file.

USAGE: 

    neon cluster setup [OPTIONS] root@CLUSTER-NAME  

OPTIONS:

    --unredacted        - Runs Vault and other commands with potential
                          secrets without redacting logs.  This is useful 
                          for debugging cluster setup  issues.  Do not
                          use for production hives.
";
        private const string logBeginMarker  = "# CLUSTER-BEGIN-SETUP ############################################################";
        private const string logEndMarker    = "# CLUSTER-END-SETUP-SUCCESS ######################################################";
        private const string logFailedMarker = "# CLUSTER-END-SETUP-FAILED #######################################################";

        private KubeConfig              kubeConfig;
        private KubeConfigContext       kubeContext;
        private KubeContextExtension    kubeContextExtension;
        private ClusterProxy            cluster;
        private KubeSetupInfo           kubeSetupInfo;

        /// <inheritdoc/>
        public override string[] Words
        {
            get { return new string[] { "cluster", "setup" }; }
        }

        /// <inheritdoc/>
        public override string[] ExtendedOptions
        {
            get { return new string[] { "--unredacted" }; }
        }

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override void Run(CommandLine commandLine)
        {
            if (commandLine.Arguments.Length < 1)
            {
                Console.Error.WriteLine("*** ERROR: [root@CLUSTER-NAME] argument is required.");
                Program.Exit(1);
            }

            var contextName = KubeConfigName.Parse(commandLine.Arguments[0]);

            kubeContextExtension = KubeHelper.GetContextExtension(contextName);

            if (kubeContextExtension == null)
            {
                Console.Error.WriteLine($"*** ERROR: Be sure to prepare the cluster first via [neon cluster prepare...].");
                Program.Exit(1);
            }
            else if (!kubeContextExtension.SetupDetails.SetupPending)
            {
                Console.Error.WriteLine($"*** ERROR: Cluster [{contextName.Cluster}] has already been setup.");
            }

            kubeConfig                       = KubeConfig.Load();
            kubeContext                      = new KubeConfigContext(contextName);
            kubeContext.Properties.Extension = kubeContextExtension;

            if (kubeConfig.GetCluster(contextName.Cluster) != null)
            {
                Console.Error.WriteLine($"*** ERROR: You already have a deployed cluster named [{contextName.Cluster}].");
                Program.Exit(1);
            }

            KubeHelper.SetKubeContext(kubeContext);

            // Note that cluster setup appends to existing log files.

            cluster = new ClusterProxy(kubeContext, Program.CreateNodeProxy<NodeDefinition>, appendLog: true, defaultRunOptions: RunOptions.LogOutput | RunOptions.FaultOnError);

            // Configure global options.

            if (commandLine.HasOption("--unredacted"))
            {
                cluster.SecureRunOptions = RunOptions.None;
            }

            // Perform the setup operations.

            var controller =
                new SetupController<NodeDefinition>(new string[] { "cluster", "setup", $"[{cluster.Name}]" }, cluster.Nodes)
                {
                    ShowStatus = !Program.Quiet,
                    MaxParallel = Program.MaxParallel
                };

            controller.AddGlobalStep("setup details",
                () =>
                {
                    if (kubeContextExtension.SetupDetails?.SetupInfo != null)
                    {
                        kubeSetupInfo = kubeContextExtension.SetupDetails.SetupInfo;
                    }
                    else
                    {
                        using (var client = new HeadendClient())
                        {
                            kubeSetupInfo = client.GetSetupInfoAsync(cluster.Definition).Result;

                            kubeContextExtension.SetupDetails.SetupInfo = kubeSetupInfo;
                            kubeContextExtension.Save();
                        }
                    }
                });

            controller.AddGlobalStep("workstation binaries", () => WorkstationBinaries());
            controller.AddWaitUntilOnlineStep("connect");
            controller.AddStep("ssh certificate", GenerateClientSshCert, node => node == cluster.FirstMaster);
            controller.AddStep("verify OS", CommonSteps.VerifyOS);

            // Write the operation begin marker to all cluster node logs.

            cluster.LogLine(logBeginMarker);

            // Perform common configuration for the bootstrap node first.
            // We need to do this so the the package cache will be running
            // when the remaining nodes are configured.

            var configureFirstMasterStepLabel = cluster.Definition.Masters.Count() > 1 ? "setup first master" : "setup master";

            controller.AddStep(configureFirstMasterStepLabel,
                (node, stepDelay) =>
                {
                    SetupCommon(node, stepDelay);
                    node.InvokeIdempotentAction("setup/common-restart", () => RebootAndWait(node));
                    SetupNode(node);
                },
                node => node == cluster.FirstMaster,
                stepStaggerSeconds: cluster.Definition.Setup.StepStaggerSeconds);

            // Perform common configuration for the remaining nodes (if any).

            if (cluster.Definition.Nodes.Count() > 1)
            {
                controller.AddStep("setup other nodes",
                    (node, stepDelay) =>
                    {
                        SetupCommon(node, stepDelay);
                        node.InvokeIdempotentAction("setup/common-restart", () => RebootAndWait(node));
                        SetupNode(node);
                    },
                    node => node != cluster.FirstMaster,
                    stepStaggerSeconds: cluster.Definition.Setup.StepStaggerSeconds);
            }

            //-----------------------------------------------------------------
            // Kubernetes configuration.

            controller.AddStep("setup kubernetes", SetupKubernetes);
            controller.AddGlobalStep("setup cluster", SetupCluster);
            controller.AddGlobalStep("label nodes", LabelNodes);

            //-----------------------------------------------------------------
            // Verify the cluster.

            controller.AddStep("check masters",
                (node, stepDelay) =>
                {
                    ClusterDiagnostics.CheckMaster(node, cluster.Definition);
                },
                node => node.Metadata.IsMaster);

            controller.AddStep("check workers",
                (node, stepDelay) =>
                {
                    ClusterDiagnostics.CheckWorker(node, cluster.Definition);
                },
                node => node.Metadata.IsWorker);

            //-----------------------------------------------------------------
            // Update the node security to use a strong password and also 
            // configure the SSH client certificate.

            // $todo(jeff.lill):
            //
            // Note that this step isn't entirely idempotent.  The problem happens
            // when the password change fails on one or more of the nodes and succeeds
            // on others.  This will result in SSH connection failures for the nodes
            // that had their passwords changes.
            //
            // One solution would be to store credentials in the node definitions
            // rather than using common credentials across all nodes.
            //
            //      https://github.com/jefflill/NeonForge/issues/397

            kubeContextExtension.SetupDetails.SshStrongPassword = NeonHelper.GetRandomPassword(cluster.Definition.NodeOptions.PasswordLength);
            kubeContextExtension.Save();

            controller.AddStep("set strong password",
                (node, stepDelay) =>
                {
                    // $todo(jeff.lill): RESTORE THIS!

                    // SetStrongPassword(node, TimeSpan.Zero);
                });

            controller.AddGlobalStep("passwords set",
                () =>
                {
                    // This hidden step sets the SSH provisioning password to NULL to 
                    // indicate that the final password has been set for all of the nodes.

                    // $todo(jeff.lill): RESTORE THIS!

                    //kubeContextExtension.SetupDetails.HasStrongSshPassword = true;
                    //kubeContextExtension.Save();
                },
                quiet: true);

            controller.AddGlobalStep("set ssh certs", () => ConfigureSshCerts());

            // This needs to be run last because it will likely disable
            // SSH username/password authentication which may block
            // connection attempts.
            //
            // It's also handy to do this last so it'll be possible to 
            // manually login with the original credentials to diagnose
            // setup issues.

            controller.AddStep("ssh secured", ConfigureSsh);

            // Start setup.

            if (!controller.Run())
            {
                // Write the operation end/failed to all cluster node logs.

                cluster.LogLine(logFailedMarker);

                Console.Error.WriteLine("*** ERROR: One or more configuration steps failed.");
                Program.Exit(1);
            }

            // Persist the new strong password and indicate that setup is complete.

            //kubeContextExtension.SshPassword       = kubeContextExtension.SshStrongPassword;
            //kubeContextExtension.SshStrongPassword = null;
            kubeContextExtension.SetupDetails.SetupPending = false;
            kubeContextExtension.Save();

            // Write the operation end marker to all cluster node logs.

            cluster.LogLine(logEndMarker);

            // Update the kubeconfig.

            var kubeConfigPath = KubeHelper.KubeConfigPath;

            if (!File.Exists(kubeConfigPath))
            {
                File.WriteAllText(kubeConfigPath, kubeContextExtension.SetupDetails.MasterFiles["/etc/kubernetes/admin.conf"].Text);
            }
            else
            {
                // The user already has an existing kubeconfig, so we need
                // to merge in the new config.

                var newConfig      = NeonHelper.YamlDeserialize<KubeConfig>(kubeContextExtension.SetupDetails.MasterFiles["/etc/kubernetes/admin.conf"].Text);
                var existingConfig = KubeHelper.KubeConfig;

                // Remove any existing user, context, and cluster with the same names.
                // Note that we're assuming that there's only one of each in the config
                // we dornloaded from the cluster.

                var newCluster = newConfig.Clusters.Single();
                var newContext = newConfig.Contexts.Single();
                var newUser    = newConfig.Users.Single();

                var existingCluster = existingConfig.GetCluster(newCluster.Name);
                var existingContext = existingConfig.GetContext(newContext.Name);
                var existingUser    = existingConfig.GetUser(newUser.Name);

                if (existingConfig != null)
                {
                    existingConfig.Clusters.Remove(existingCluster);
                }

                if (existingContext != null)
                {
                    existingConfig.Contexts.Remove(existingContext);
                }

                if (existingUser != null)
                {
                    existingConfig.Users.Remove(existingUser);
                }

                existingConfig.Clusters.Add(newCluster);
                existingConfig.Contexts.Add(newContext);
                existingConfig.Users.Add(newUser);

                existingConfig.CurrentContext = newContext.Name;

                KubeHelper.SetKubeConfig(existingConfig);
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Downloads and installs any required binaries to the workstation cache if they're not already present.
        /// </summary>
        private async void WorkstationBinaries()
        {
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            var firstMaster       = cluster.FirstMaster;
            var hostPlatform      = KubeHelper.HostPlatform;
            var cachedKubeCtlPath = KubeHelper.GetCachedComponentPath(hostPlatform, "kubectl", kubeSetupInfo.Versions.Kubernetes);
            var cachedHelmPath    = KubeHelper.GetCachedComponentPath(hostPlatform, "helm", kubeSetupInfo.Versions.Helm);

            string kubeCtlUri;
            string helmUri;

            switch (hostPlatform)
            {
                case KubeHostPlatform.Linux:

                    kubeCtlUri = kubeSetupInfo.KubeCtlLinuxUri;
                    helmUri    = kubeSetupInfo.HelmLinuxUri;
                    break;

                case KubeHostPlatform.Osx:

                    kubeCtlUri = kubeSetupInfo.KubeCtlOsxUri;
                    helmUri    = kubeSetupInfo.HelmOsxUri;
                    break;

                case KubeHostPlatform.Windows:

                    kubeCtlUri = kubeSetupInfo.KubeCtlWindowsUri;
                    helmUri    = kubeSetupInfo.HelmWindowsUri;
                    break;

                default:

                    throw new NotSupportedException($"Unsupported workstation platform [{hostPlatform}]");
            }

            // Download the components if they're not already cached.

            using (var client = new HttpClient(handler, disposeHandler: true))
            {
                if (!File.Exists(cachedKubeCtlPath))
                {
                    firstMaster.Status = "download: kubectl";

                    using (var response = await client.GetStreamAsync(kubeCtlUri))
                    {
                        using (var output = new FileStream(cachedKubeCtlPath, FileMode.Create, FileAccess.ReadWrite))
                        {
                            await response.CopyToAsync(output);
                        }
                    }
                }

                if (!File.Exists(cachedHelmPath))
                {
                    firstMaster.Status = "download: Helm";

                    using (var response = await client.GetStreamAsync(helmUri))
                    {
                        // This is a [zip] file for Windows and a [tar.gz] file for Linux and OS/X.
                        // We're going to download to a temporary file so we can extract just the
                        // Helm binary.

                        var cachedTempHelmPath = cachedHelmPath + ".tmp";

                        try
                        {
                            using (var output = new FileStream(cachedTempHelmPath, FileMode.Create, FileAccess.ReadWrite))
                            {
                                await response.CopyToAsync(output);
                            }

                            switch (hostPlatform)
                            {
                                case KubeHostPlatform.Linux:
                                case KubeHostPlatform.Osx:

                                    throw new NotImplementedException($"Unsupported workstation platform [{hostPlatform}]");

                                case KubeHostPlatform.Windows:

                                    // The downloaded file is a ZIP archive for Windows.  We're going
                                    // to extract the [windows-amd64/helm.exe] file.

                                    using (var input = new FileStream(cachedTempHelmPath, FileMode.Open, FileAccess.ReadWrite))
                                    {
                                        using (var zip = new ZipFile(input))
                                        {
                                            foreach (ZipEntry zipEntry in zip)
                                            {
                                                if (!zipEntry.IsFile)
                                                {
                                                    continue;
                                                }

                                                if (zipEntry.Name == "windows-amd64/helm.exe")
                                                {
                                                    using (var zipStream = zip.GetInputStream(zipEntry))
                                                    {
                                                        using (var output = new FileStream(cachedHelmPath, FileMode.Create, FileAccess.ReadWrite))
                                                        {
                                                            zipStream.CopyTo(output);
                                                        }
                                                    }
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    break;

                                default:

                                    throw new NotSupportedException($"Unsupported workstation platform [{hostPlatform}]");
                            }
                        }
                        finally
                        {
                            if (File.Exists(cachedTempHelmPath))
                            {
                                File.Delete(cachedTempHelmPath);
                            }
                        }
                    }
                }
            }

            // We're going to assume that the workstation tools are backwards 
            // compatible with older versions of Kubernetes and other infrastructure
            // components and simply compare the installed tool (if present) version
            // with the requested tool version and overwrite the installed tool if
            // the new one is more current.

            KubeHelper.InstallKubeCtl(kubeSetupInfo);
            KubeHelper.InstallHelm(kubeSetupInfo);

            firstMaster.Status = string.Empty;
        }

        /// <summary>
        /// Basic configuration that will happen every time if DEBUG setup
        /// mode is ENABLED or else will be invoked idempotently (if that's 
        /// a word).
        /// </summary>
        /// <param name="node">The target node.</param>
        private void ConfigureBasic(SshProxy<NodeDefinition> node)
        {
            // Configure the node's environment variables.

            CommonSteps.ConfigureEnvironmentVariables(node, cluster.Definition);

            // Upload the setup and configuration files.

            node.CreateHostFolders();
            node.UploadConfigFiles(cluster.Definition, kubeSetupInfo);
            node.UploadResources(cluster.Definition, kubeSetupInfo);
        }

        /// <summary>
        /// Performs common node configuration.
        /// </summary>
        /// <param name="node">The target node.</param>
        /// <param name="stepDelay">The step delay if the operation hasn't already been completed.</param>
        private void SetupCommon(SshProxy<NodeDefinition> node, TimeSpan stepDelay)
        {
            //-----------------------------------------------------------------
            // NOTE: 
            //
            // We're going to perform the following steps outside of the
            // idempotent check to make it easier to debug and modify 
            // scripts and tools when cluster setup has been partially
            // completed.  These steps are implicitly idempotent and
            // complete pretty quickly.

            if (Program.Debug)
            {
                ConfigureBasic(node);
            }

            //-----------------------------------------------------------------
            // Ensure the following steps are executed only once.

            node.InvokeIdempotentAction("setup/common",
                () =>
                {
                    Thread.Sleep(stepDelay);

                    if (!Program.Debug)
                    {
                        ConfigureBasic(node);
                    }

                    // Ensure that the node has been prepared for setup.

                    CommonSteps.PrepareNode(node, cluster.Definition, kubeSetupInfo);

                    // Create the [/mnt-data] folder if it doesn't already exist.  This folder
                    // is where we're going to host the Docker containers and volumes that should
                    // have been initialized to link to any data drives attached to the machine
                    // or simply be located on the OS drive.  This may not be initialized for
                    // some prepared nodes, so we'll create this on the OS drive if necessary.

                    if (!node.DirectoryExists("/mnt-data"))
                    {
                        node.SudoCommand("mkdir -p /mnt-data");
                    }

                    // Configure the APT proxy server settings early.

                    node.Status = "configure: package proxy";
                    node.SudoCommand("setup-package-proxy.sh");

                    // Perform basic node setup including changing the hostname.

                    UploadHostname(node);

                    node.Status = "configure: node basics";
                    node.SudoCommand("setup-node.sh");

                    // Tune Linux for SSDs, if enabled.

                    node.Status = "tune: disks";
                    node.SudoCommand("setup-ssd.sh");
                });
        }

        /// <summary>
        /// Performs basic node configuration.
        /// </summary>
        /// <param name="node">The target node.</param>
        private void SetupNode(SshProxy<NodeDefinition> node)
        {
            node.InvokeIdempotentAction($"setup/{node.Metadata.Role}",
                () =>
                {
                    // Configure the APT package proxy on the masters
                    // and configure the proxy selector for all nodes.

                    node.Status = "configure: package proxy";
                    node.SudoCommand("setup-package-proxy.sh");

                    // Upgrade Linux packages if requested.  We're doing this after
                    // deploying the APT package proxy so it'll be faster.

                    switch (cluster.Definition.NodeOptions.Upgrade)
                    {
                        case OsUpgrade.Partial:

                            node.Status = "upgrade: partial";

                            node.SudoCommand("safe-apt-get upgrade -yq");
                            break;

                        case OsUpgrade.Full:

                            node.Status = "upgrade: full";

                            node.SudoCommand("safe-apt-get dist-upgrade -yq");
                            break;
                    }

                    // Check to see whether the upgrade requires a reboot and
                    // do that now if necessary.

                    if (node.FileExists("/var/run/reboot-required"))
                    {
                        node.Status = "restarting...";
                        node.Reboot();
                    }

                    // Setup NTP.

                    node.Status = "configure: NTP";
                    node.SudoCommand("setup-ntp.sh");

                    node.Status = "install: docker";

                    var dockerRetry = new LinearRetryPolicy(typeof(TransientException), maxAttempts: 5, retryInterval: TimeSpan.FromSeconds(5));

                    dockerRetry.InvokeAsync(
                        async () =>
                        {
                            var response = node.SudoCommand("setup-docker.sh", node.DefaultRunOptions & ~RunOptions.FaultOnError);

                            if (response.ExitCode != 0)
                            {
                                throw new TransientException(response.ErrorText);
                            }

                            await Task.CompletedTask;

                        }).Wait();

                    // Clean up any cached APT files.

                    node.Status = "clean up";
                    node.SudoCommand("safe-apt-get clean -yq");
                    node.SudoCommand("rm -rf /var/lib/apt/lists");
                });
        }

        /// <summary>
        /// Reboots the cluster nodes.
        /// </summary>
        /// <param name="node">The cluster node.</param>
        private void RebootAndWait(SshProxy<NodeDefinition> node)
        {
            node.Status = "restarting...";
            node.Reboot(wait: true);
        }

        /// <summary>
        /// Updates the node hostname and related configuration.
        /// </summary>
        /// <param name="node">The target node.</param>
        private void UploadHostname(SshProxy<NodeDefinition> node)
        {
            // Update the hostname.

            node.SudoCommand($"hostnamectl set-hostname {node.Name}");

            // We need to edit [/etc/cloud/cloud.cfg] to preserve the hostname change.

            var cloudCfg = node.DownloadText("/etc/cloud/cloud.cfg");

            cloudCfg = cloudCfg.Replace("preserve_hostname: false", "preserve_hostname: true");

            node.UploadText("/etc/cloud/cloud.cfg", cloudCfg);

            // Update the [/etc/hosts] file to resolve the new hostname.

            var sbHosts = new StringBuilder();

            var nodeAddress = node.PrivateAddress.ToString();
            var separator   = new string(' ', Math.Max(16 - nodeAddress.Length, 1));

            sbHosts.Append(
$@"
127.0.0.1	    localhost
{nodeAddress}{separator}{node.Name}
::1             localhost ip6-localhost ip6-loopback
ff02::1         ip6-allnodes
ff02::2         ip6-allrouters
");
            node.UploadText("/etc/hosts", sbHosts, 4, Encoding.UTF8);
        }

        /// <summary>
        /// Installs the required Kubernetes related components on a node.
        /// </summary>
        /// <param name="node">The target node.</param>
        /// <param name="stepDelay">The step delay if the operation hasn't already been completed.</param>
        private void SetupKubernetes(SshProxy<NodeDefinition> node, TimeSpan stepDelay)
        {
            node.InvokeIdempotentAction("setup/setup-install-kubernetes",
                () =>
                {
                    Thread.Sleep(stepDelay);

                    node.Status = "setup: kubernetes repo";

                    var bundle = CommandBundle.FromScript(
$@"#!/bin/bash
curl {Program.CurlOptions} https://packages.cloud.google.com/apt/doc/apt-key.gpg | apt-key add -
echo ""deb https://apt.kubernetes.io/ kubernetes-xenial main"" > /etc/apt/sources.list.d/kubernetes.list
safe-apt-get update
");
                    node.SudoCommand(bundle);

                    node.Status = "install: kubeadm";
                    node.SudoCommand($"safe-apt-get install -yq kubeadm={kubeSetupInfo.KubeAdmPackageUbuntuVersion}");

                    node.Status = "install: kubectl";
                    node.SudoCommand($"safe-apt-get install -yq kubectl={kubeSetupInfo.KubeCtlPackageUbuntuVersion}");

                    node.Status = "install: kubelet";
                    node.SudoCommand($"safe-apt-get install -yq kubelet={kubeSetupInfo.KubeletPackageUbuntuVersion}");

                    node.Status = "hold: packages";
                    node.SudoCommand("apt-mark hold kubeadm kubectl kubelet");
                    
                    // Download and install the Helm client:

                    node.InvokeIdempotentAction("setup/cluster-helm",
                        () =>
                        {
                            node.Status = "install: helm";

                            var helmInstallScript =
$@"#!/bin/bash
cd /tmp
curl {Program.CurlOptions} {kubeSetupInfo.HelmLinuxUri} > helm.tar.gz
tar xvf helm.tar.gz
cp linux-amd64/helm /usr/local/bin
chmod 770 /usr/local/bin/helm
rm -f helm.tar.gz
rm -rf helm
";
                            node.SudoCommand(CommandBundle.FromScript(helmInstallScript));
                        });
                });
        }

        /// <summary>
        /// Initializes the cluster on the first manager, then joins the remaining
        /// masters and workers to the cluster.
        /// </summary>
        private void SetupCluster()
        {
            var firstMaster = cluster.FirstMaster;

            firstMaster.InvokeIdempotentAction("setup/cluster",
                () =>
                {
                    //---------------------------------------------------------
                    // Initialize the cluster on the first master:

                    firstMaster.Status = "create: cluster";

                    // Pull the Kubernetes images:

                    firstMaster.InvokeIdempotentAction("setup/cluster-images",
                        () =>
                        {
                            firstMaster.Status = "pull: kubernetes images...";
                            firstMaster.SudoCommand("kubeadm config images pull");
                        });

                    firstMaster.InvokeIdempotentAction("setup/cluster-init",
                        () =>
                        {
                            firstMaster.Status = "initializing...";

                            // It's possible that a previous cluster initialization operation
                            // was interrupted.  This command resets the state.

                            firstMaster.SudoCommand("kubeadm reset --force");

                            // Configure the control plane's API server endpoint and initialize
                            // the certificate SAN names to include each master IP address as well
                            // as the HOSTNAME/ADDRESS of the API load balancer (if any).

                            var controlPlaneEndpoint = $"{cluster.FirstMaster.PrivateAddress}:{KubeHostPorts.ApiServerProxy}";
                            var sbCertSANs           = new StringBuilder();

                            if (!string.IsNullOrEmpty(cluster.Definition.Kubernetes.ApiLoadBalancer))
                            {
                                controlPlaneEndpoint = cluster.Definition.Kubernetes.ApiLoadBalancer;

                                var fields = cluster.Definition.Kubernetes.ApiLoadBalancer.Split(':');

                                sbCertSANs.AppendLine($"  - \"{fields[0]}\"");
                            }

                            // $todo(jeff.lill): DELETE THIS!
                            controlPlaneEndpoint = $"{cluster.FirstMaster.PrivateAddress}:{KubeHostPorts.KubeApiServer}";

                            foreach (var node in cluster.Masters)
                            {
                                sbCertSANs.AppendLine($"  - \"{node.PrivateAddress}\"");
                            }

                            var clusterConfig =
$@"
apiVersion: kubeadm.k8s.io/v1beta1
kind: ClusterConfiguration
clusterName: {cluster.Name}
kubernetesVersion: ""v{kubeSetupInfo.Versions.Kubernetes}""
apiServer:
  certSANs:
{sbCertSANs}
controlPlaneEndpoint: ""{controlPlaneEndpoint}""
networking:
  podSubnet: ""{cluster.Definition.Network.PodSubnet}""
  serviceSubnet: ""{cluster.Definition.Network.ServiceSubnet}""
";
                            firstMaster.UploadText("/tmp/cluster.yaml", clusterConfig);

                            var response = firstMaster.SudoCommand($"kubeadm init --config /tmp/cluster.yaml");

                            firstMaster.SudoCommand("rm /tmp/cluster.yaml");

                            // Extract the cluster join command from the response.  We'll need this to join
                            // other nodes to the cluster.

                            var output = response.OutputText;
                            var pStart = output.IndexOf("kubeadm join");

                            if (pStart == -1)
                            {
                                throw new KubeException("Cannot locate the [kubadm join ...] command in the [kubeadm init ...] response.");
                            }

                            var pEnd = output.IndexOf('\n', pStart);

                            if (pEnd == -1)
                            {
                                kubeContextExtension.SetupDetails.ClusterJoinCommand = output.Substring(pStart).Trim();
                            }
                            else
                            {
                                kubeContextExtension.SetupDetails.ClusterJoinCommand = output.Substring(pStart, pEnd - pStart).Trim();
                            }
                        });

                    // kubectl config:

                    firstMaster.InvokeIdempotentAction("setup/cluster-kubectl",
                        () =>
                        {
                            // Edit the Kubernetes configuration file to rename the context:
                            //
                            //       CLUSTERNAME-admin@kubernetes --> root@CLUSTERNAME
                            //
                            // rename the user:
                            //
                            //      CLUSTERNAME-admin --> CLUSTERNAME-root 

                            var adminConfig = firstMaster.DownloadText("/etc/kubernetes/admin.conf");

                            adminConfig = adminConfig.Replace($"kubernetes-admin@{cluster.Definition.Name}", $"root@{cluster.Definition.Name}");
                            adminConfig = adminConfig.Replace("kubernetes-admin", $"root@{cluster.Definition.Name}");

                            firstMaster.UploadText("/etc/kubernetes/admin.conf", adminConfig, permissions: "600", owner: "root:root");
                        });

                    // Download the boot master files that will need to be provisioned on
                    // the remaining masters and may also be needed for other purposes
                    // (if we haven't already downloaded these).

                    if (kubeContextExtension.SetupDetails.MasterFiles != null)
                    {
                        kubeContextExtension.SetupDetails.MasterFiles = new Dictionary<string, KubeFileDetails>();
                    }

                    if (kubeContextExtension.SetupDetails.MasterFiles.Count == 0)
                    {
                        // I'm hardcoding the permissions and owner here.  It would be nice to
                        // scrape this from the source files in the future but this was not
                        // worth the bother at this point.

                        var files = new RemoteFile[]
                        {
                            new RemoteFile("/etc/kubernetes/admin.conf", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/ca.crt", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/ca.key", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/sa.pub", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/sa.key", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/front-proxy-ca.crt", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/front-proxy-ca.key", "600", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/etcd/ca.crt", "644", "root:root"),
                            new RemoteFile("/etc/kubernetes/pki/etcd/ca.key", "600", "root:root"),
                        };

                        foreach (var file in files)
                        {
                            var text = firstMaster.DownloadText(file.Path);

                            kubeContextExtension.SetupDetails.MasterFiles[file.Path] = new KubeFileDetails(text, permissions: file.Permissions, owner: file.Owner);
                        }
                    }

                    // Persist the cluster join command and downloaded master files.

                    kubeContextExtension.Save();

                    firstMaster.Status = "joined";

                    //---------------------------------------------------------
                    // Join the remaining masters to the cluster:

                    foreach (var master in cluster.Masters.Where(m => m != firstMaster))
                    {
                        try
                        {
                            master.InvokeIdempotentAction("setup/cluster-kubectl",
                                () =>
                                {
                                    // It's possible that a previous cluster join operation
                                    // was interrupted.  This command resets the state.

                                    master.SudoCommand("kubeadm reset --force");

                                    // The other (non-boot) masters need files downloaded from the boot master.

                                    master.Status = "upload: master files";

                                    foreach (var file in kubeContextExtension.SetupDetails.MasterFiles)
                                    {
                                        master.UploadText(file.Key, file.Value.Text, permissions: file.Value.Permissions, owner: file.Value.Owner);
                                    }

                                    // Join the cluster:

                                    master.InvokeIdempotentAction("setup/cluster-join",
                                            () =>
                                            {
                                                master.Status = "join: as master";
                                                master.SudoCommand(kubeContextExtension.SetupDetails.ClusterJoinCommand + " --experimental-control-plane");
                                            });

                                    // Pull the Kubernetes images:

                                    master.InvokeIdempotentAction("setup/cluster-images",
                                            () =>
                                            {
                                                master.Status = "pull: kubernetes images";
                                                master.SudoCommand("kubeadm config images pull");
                                            });
                                });
                        }
                        catch (Exception e)
                        {
                            master.Fault(NeonHelper.ExceptionError(e));
                            master.LogException(e);
                        }

                        master.Status = "joined";
                    }

                    //---------------------------------------------------------
                    // Join the remaining workers to the cluster:

                    foreach (var worker in cluster.Workers)
                    {
                        try
                        {
                            worker.InvokeIdempotentAction("setup/cluster-join",
                                () =>
                                {
                                    worker.Status = "join: as worker";
                                    worker.SudoCommand(kubeContextExtension.SetupDetails.ClusterJoinCommand);
                                });
                        }
                        catch (Exception e)
                        {
                            worker.Fault(NeonHelper.ExceptionError(e));
                            worker.LogException(e);
                        }

                        worker.Status = "joined";
                    }
                });

            //-----------------------------------------------------------------
            // Configure the cluster.

            firstMaster.InvokeIdempotentAction("setup/cluster-configure",
                () =>
                {
                    foreach (var node in cluster.Nodes)
                    {
                        node.Status = string.Empty;
                    }

                    // Allow pods to be scheduled on master nodes if enabled.

                    firstMaster.InvokeIdempotentAction("setup/cluster-master-pods",
                        () =>
                        {
                            var allowPodsOnMasters = false;

                            if (cluster.Definition.Kubernetes.AllowPodsOnMasters.HasValue)
                            {
                                allowPodsOnMasters = cluster.Definition.Kubernetes.AllowPodsOnMasters.Value;
                            }
                            else
                            {
                                allowPodsOnMasters = cluster.Definition.Workers.Count() > 0;
                            }

                            // The [kubectl taint] command looks like it can return a non-zero exit code.
                            // We'll ignore this.

                            firstMaster.SudoCommand("kubectl taint nodes --all node-role.kubernetes.io/master-", firstMaster.DefaultRunOptions & ~RunOptions.FaultOnError);
                        });

                    // Install the network CNI.

                    // $todo(jeff.lill): Not supporting the integrated Istio CNI yet.

                    switch (cluster.Definition.Network.Cni)
                    {
                        case NetworkCni.Calico:

                            DeployCalicoCni(firstMaster);
                            break;

                        case NetworkCni.Istio:
                        default:

                            throw new NotImplementedException($"The [{cluster.Definition.Network.Cni}] CNI support is not implemented.");
                    }

                    // Install the Helm/Tiller service.  This will install the latest stable version.

                    firstMaster.InvokeIdempotentAction("setup/cluster-deploy-helm",
                        () =>
                        {
                            firstMaster.Status = "deploy: Helm/Tiller";
                            firstMaster.SudoCommand("helm init --service-account tiller");
                        });

                    // Install the Kubernetes dashboard:

                    firstMaster.InvokeIdempotentAction("setup/cluster-deploy-kubernetes-dashboard",
                        () =>
                        {
                            firstMaster.Status = "deploy: Kubernetes dashboard";
                            firstMaster.SudoCommand($"kubectl apply -f {kubeSetupInfo.KubeDashboardUri}");
                        });
                });
        }

        /// <summary>
        /// Installs the Calico CNI.
        /// </summary>
        /// <param name="master">The master node.</param>
        private void DeployCalicoCni(SshProxy<NodeDefinition> master)
        {
            master.InvokeIdempotentAction("setup/cluster-deploy-cni",
                () =>
                {
                    // Deploy Calico

                    var script =
$@"#!/bin/bash

# Configure RBAC:

kubectl apply -f {kubeSetupInfo.CalicoRbacYamlUri}

# We need to edit the setup manifest to specify the 
# cluster subnet before applying it.

curl {Program.CurlOptions} {kubeSetupInfo.CalicoSetupYamUri} > /tmp/calico.yaml
sed -i 's;192.168.0.0/16;{cluster.Definition.Network.PodSubnet};' /tmp/calico.yaml
kubectl apply -f /tmp/calico.yaml
rm /tmp/calico.yaml
";
                    master.SudoCommand(CommandBundle.FromScript(script));

                    // Wait for Calico and CoreDNS pods to report that they're running.

                    // $todo(jeff.lill):
                    //
                    // This is a horrible hack.  I'm going to examine the [kubectl get pods]
                    // response by skipping the column headers and then ensuring that each
                    // remaining line includes a " Running " string.  If one or more lines
                    // don't include this then we're not ready.
                    //
                    // [kubectl wait] as an experimental command that we should investigate
                    // in the future:
                    //
                    //      https://github.com/jefflill/NeonForge/issues/424
                    //
                    // We're going to wait a maximum of 120 seconds.

                    NeonHelper.WaitFor(
                        () =>
                        {
                            var response = master.SudoCommand("kubectl get pods --all-namespaces", master.DefaultRunOptions | RunOptions.LogOnErrorOnly);

                            using (var reader = new StringReader(response.OutputText))
                            {
                                foreach (var line in reader.Lines().Skip(1))
                                {
                                    if (!line.Contains(" Running "))
                                    {
                                        return false;
                                    }
                                }
                            }

                            return true;
                        },
                        timeout: TimeSpan.FromSeconds(120),
                        pollTime: TimeSpan.FromSeconds(1));
                });
        }

        /// <summary>
        /// Installs Istio without its CNI.
        /// </summary>
        /// <param name="master">The master node.</param>
        private void InstallIstio(SshProxy<NodeDefinition> master)
        {
        }

        /// <summary>
        /// Installs Istio with its integrated CNI.
        /// </summary>
        /// <param name="master">The master node.</param>
        private void InstallIstioWithCni(SshProxy<NodeDefinition> master)
        {
            // $todo(jeff.lill): This doesn't work.  Waiting for a stable release of Istio with integrated CNI.

            throw new NotImplementedException("Istio installation with integrated CNI doesn't work yet.");

/*
            // Configure Helm and Istio: https://preliminary.istio.io/docs/setup/kubernetes/helm-install/ (option 1)

            master.InvokeIdempotentAction("setup/cluster-deploy-helm",
                () =>
                {
                    master.Status = "deploy: Istio";

                    var mutualTls = cluster.Definition.Network.IstioMutualTls ? "true" : "false";
                    var istioScript =
$@"#!/bin/bash

# Download and extract the Istio binaries:

cd /tmp
curl {Program.CurlOptions} {kubeSetupInfo.IstioLinuxUri} > istio.tar.gz
tar xvf /tmp/istio.tar.gz
mv istio-{kubeSetupInfo.Versions.Istio} istio
cd istio

# Copy the tools:

chmod 330 bin/*
cp bin/* /usr/local/bin

#------------------------------------------------------------------------------
# Update Helm dependencies:

helm repo add istio.io ""https://storage.googleapis.com/istio-prerelease/daily-build/master-latest-daily/charts""
helm dep update install/kubernetes/helm/istio

#------------------------------------------------------------------------------
# Option 1: Install with Helm via [helm template]
# https://preliminary.istio.io/docs/setup/kubernetes/helm-install/

# Step 1: Install Istio CRDs:

for i in install/kubernetes/helm/istio/templates/crd*yaml; do kubectl apply -f $i; done
sleep 10

# Step 2: Generate the Istio Kubernetes manifest:

cat install/kubernetes/namespace.yaml > /tmp/istio.yaml
helm template install/kubernetes/helm/istio --name istio --namespace istio-system --set global.mtls.enabled={mutualTls} >> /tmp/istio.yaml

# Step 3: Install the components via the manifest:

kubectl apply -f /tmp/istio.yaml

# Cleanup:

# rm istio.tar.gz
# rm -r istio
# rm istio.yaml
";
                    master.SudoCommand(CommandBundle.FromScript(istioScript));
                });
*/
        }

        /// <summary>
        /// Adds the node labels.
        /// </summary>
        private void LabelNodes()
        {
            var master = cluster.FirstMaster;

            master.InvokeIdempotentAction("setup/cluster-label-nodes",
                () =>
                {
                    master.Status = "label: nodes";

                    try
                    {
                        // Generate a Bash script we'll submit to the first master
                        // that initializes the labels for all nodes.

                        var sbScript = new StringBuilder();
                        var sbArgs   = new StringBuilder();

                        sbScript.AppendLineLinux("#!/bin/bash");

                        foreach (var node in cluster.Nodes)
                        {
                            var labelDefinitions = new List<string>();

                            if (node.Metadata.IsWorker)
                            {
                                // Kubernetes doesn't set the role for worker nodes so we'll do that here.

                                labelDefinitions.Add("kubernetes.io/role=worker");
                            }

                            labelDefinitions.Add($"{NodeLabels.LabelDatacenter}={cluster.Definition.Datacenter.ToLowerInvariant()}");
                            labelDefinitions.Add($"{NodeLabels.LabelEnvironment}=\"{cluster.Definition.Environment.ToString().ToLowerInvariant()}\"");

                            foreach (var label in node.Metadata.Labels.All)
                            {
                                labelDefinitions.Add($"{label.Key}=\"{label.Value}\"");
                            }

                            sbArgs.Clear();

                            foreach (var label in labelDefinitions)
                            {
                                sbArgs.AppendWithSeparator(label);
                            }

                            sbScript.AppendLineLinux($"kubectl label nodes {node.Name} {sbArgs}");
                        }

                        master.SudoCommand(CommandBundle.FromScript(sbScript));
                    }
                    finally
                    {
                        master.Status = string.Empty;
                    }
                });
        }

        /// <summary>
        /// Generates the SSH key to be used for authenticating SSH client connections.
        /// </summary>
        /// <param name="master">A cluster manager node.</param>
        /// <param name="stepDelay">The step delay if the operation hasn't already been completed.</param>
        private void GenerateClientSshCert(SshProxy<NodeDefinition> master, TimeSpan stepDelay)
        {
            // Here's some information explaining what how I'm doing this:
            //
            //      https://help.ubuntu.com/community/SSH/OpenSSH/Configuring
            //      https://help.ubuntu.com/community/SSH/OpenSSH/Keys

            if (kubeContextExtension.SshClientKey != null)
            {
                return; // Key has already been created.
            }

            Thread.Sleep(stepDelay);

            kubeContextExtension.SshClientKey = new SshClientKey();

            // $hack(jeff.lill): 
            //
            // We're going to generate a 2048 bit key pair on one of the
            // master nodes and then download and then delete it.  This
            // means that the private key will be persisted to disk (tmpfs)
            // for a moment but I'm going to worry about that too much
            // since we'll be rebooting the master later on during setup.
            //
            // Technically, I could have installed OpenSSL or something
            // on Windows or figured out the .NET Crypto libraries but
            // but OpenSSL didn't support generating the PUB format
            // SSH expects for the client public key.

            const string keyGenScript =
@"
# Generate a 2048-bit key without a passphrase (the -N option).

rm -f /run/ssh-key*
ssh-keygen -t rsa -b 2048 -N """" -C ""neonhive"" -f /run/ssh-key

# Relax permissions so we can download the key parts.

chmod 666 /run/ssh-key*
";
            master.SudoCommand(CommandBundle.FromScript(keyGenScript));

            using (var stream = new MemoryStream())
            {
                master.Download("/run/ssh-key.pub", stream);

                kubeContextExtension.SshClientKey.PublicPUB = NeonHelper.ToLinuxLineEndings(Encoding.UTF8.GetString(stream.ToArray()));
            }

            using (var stream = new MemoryStream())
            {
                master.Download("/run/ssh-key", stream);

                kubeContextExtension.SshClientKey.PrivatePEM = NeonHelper.ToLinuxLineEndings(Encoding.UTF8.GetString(stream.ToArray()));
            }

            master.SudoCommand("rm /run/ssh-key*");

            // We're going to use WinSCP to convert the OpenSSH PEM formatted key
            // to the PPK format PuTTY/WinSCP require.  Note that this won't work
            // when the tool is running in a Docker Linux container.  We're going
            // to handle the conversion in the outer shim as a post run action.

            if (NeonHelper.IsWindows)
            {
                var pemKeyPath = Path.Combine(KubeHelper.TempFolder, Guid.NewGuid().ToString("D"));
                var ppkKeyPath = Path.Combine(KubeHelper.TempFolder, Guid.NewGuid().ToString("D"));

                try
                {
                    File.WriteAllText(pemKeyPath, kubeContextExtension.SshClientKey.PrivatePEM);

                    ExecuteResult result;

                    try
                    {
                        result = NeonHelper.ExecuteCapture("winscp.com", $@"/keygen ""{pemKeyPath}"" /comment=""{cluster.Definition.Name} Key"" /output=""{ppkKeyPath}""");
                    }
                    catch (Win32Exception)
                    {
                        return; // Tolerate when WinSCP isn't installed.
                    }

                    if (result.ExitCode != 0)
                    {
                        Console.WriteLine(result.OutputText);
                        Console.Error.WriteLine(result.ErrorText);
                        Program.Exit(result.ExitCode);
                    }

                    kubeContextExtension.SshClientKey.PrivatePPK = NeonHelper.ToLinuxLineEndings(File.ReadAllText(ppkKeyPath));

                    // Persist the SSH client key.

                    kubeContextExtension.Save();
                }
                finally
                {
                    if (File.Exists(pemKeyPath))
                    {
                        File.Delete(pemKeyPath);
                    }

                    if (File.Exists(ppkKeyPath))
                    {
                        File.Delete(ppkKeyPath);
                    }
                }
            }
        }

        /// <summary>
        /// Changes the admin account's password on a node.
        /// </summary>
        /// <param name="node">The target node.</param>
        /// <param name="stepDelay">The step delay if the operation hasn't already been completed.</param>
        private void SetStrongPassword(SshProxy<NodeDefinition> node, TimeSpan stepDelay)
        {
            node.InvokeIdempotentAction("setup/strong-password",
                () =>
                {
                    Thread.Sleep(stepDelay);

                    node.Status = "strong password";

                    var script =
$@"
echo '{kubeContextExtension.SshUsername}:{kubeContextExtension.SetupDetails.SshStrongPassword}' | chpasswd
";
                    var response = node.SudoCommand(CommandBundle.FromScript(script));

                    if (response.ExitCode != 0)
                    {
                        Console.Error.WriteLine($"*** ERROR: Unable to set a strong password [exitcode={response.ExitCode}].");
                        Program.Exit(response.ExitCode);
                    }

                    node.UpdateCredentials(SshCredentials.FromUserPassword(kubeContextExtension.SshUsername, kubeContextExtension.SetupDetails.SshStrongPassword));
                });
        }

        /// <summary>
        /// Generates the private key that will be used to secure SSH on the cluster nodes.
        /// </summary>
        private void ConfigureSshCerts()
        {
            cluster.FirstMaster.InvokeIdempotentAction("setup/ssh-server-key",
                () =>
                {
                    cluster.FirstMaster.Status = "generate: server SSH key";

                    var configScript =
@"
# Generate the SSH server key and fingerprint.

mkdir -p /dev/shm/ssh

# For idempotentcy, ensure that the key file doesn't already exist to
# avoid having the [ssh-keygen] command prompt and wait for permission
# to overwrite it.

if [ -f /dev/shm/ssh/ssh_host_rsa_key ] ; then
    rm /dev/shm/ssh/ssh_host_rsa_key
fi

ssh-keygen -f /dev/shm/ssh/ssh_host_rsa_key -N '' -t rsa

# Extract the host's SSL RSA key fingerprint to a temporary file
# so [neon-cli] can download it.

ssh-keygen -l -E md5 -f /dev/shm/ssh/ssh_host_rsa_key > /dev/shm/ssh/ssh.fingerprint

# The files need to have user permissions so we can download them.

chmod 777 /dev/shm/ssh/
chmod 666 /dev/shm/ssh/ssh_host_rsa_key
chmod 666 /dev/shm/ssh/ssh_host_rsa_key.pub
chmod 666 /dev/shm/ssh/ssh.fingerprint
";
                    cluster.FirstMaster.SudoCommand(CommandBundle.FromScript(configScript));

                    cluster.FirstMaster.Status = "download: server SSH key";

                    kubeContextExtension.SshNodePrivateKey  = cluster.FirstMaster.DownloadText("/dev/shm/ssh/ssh_host_rsa_key");
                    kubeContextExtension.SshNodePublicKey   = cluster.FirstMaster.DownloadText("/dev/shm/ssh/ssh_host_rsa_key.pub");
                    kubeContextExtension.SshNodeFingerprint = cluster.FirstMaster.DownloadText("/dev/shm/ssh/ssh.fingerprint");

                    // Delete the SSH key files for security.

                    cluster.FirstMaster.SudoCommand("rm -r /dev/shm/ssh");

                    // Persist the server SSH key and fingerprint.

                    kubeContextExtension.Save();
                });
        }

        /// <summary>
        /// Configures SSH on a node.
        /// </summary>
        /// <param name="node">The target node.</param>
        /// <param name="stepDelay">Ignored.</param>
        private void ConfigureSsh(SshProxy<NodeDefinition> node, TimeSpan stepDelay)
        {
            // Configure the SSH credentials on all cluster nodes.

            node.InvokeIdempotentAction("setup/ssh",
                () =>
                {
                    CommandBundle bundle;

                    // Here's some information explaining what how I'm doing this:
                    //
                    //      https://help.ubuntu.com/community/SSH/OpenSSH/Configuring
                    //      https://help.ubuntu.com/community/SSH/OpenSSH/Keys

                    node.Status = "setup: client SSH key";

                    // Enable the public key by appending it to [$HOME/.ssh/authorized_keys],
                    // creating the file if necessary.  Note that we're allowing only a single
                    // authorized key.

                    var addKeyScript =
$@"
chmod go-w ~/
mkdir -p $HOME/.ssh
chmod 700 $HOME/.ssh
touch $HOME/.ssh/authorized_keys
cat ssh-key.pub > $HOME/.ssh/authorized_keys
chmod 600 $HOME/.ssh/authorized_keys
";
                    bundle = new CommandBundle("./addkeys.sh");

                    bundle.AddFile("addkeys.sh", addKeyScript, isExecutable: true);
                    bundle.AddFile("ssh-key.pub", kubeContextExtension.SshClientKey.PublicPUB);

                    // NOTE: I'm explictly not running the bundle as [sudo] because the OpenSSH
                    //       server is very picky about the permissions on the user's [$HOME]
                    //       and [$HOME/.ssl] folder and contents.  This took me a couple 
                    //       hours to figure out.

                    node.RunCommand(bundle);

                    // These steps are required for both password and public key authentication.

                    // Upload the server key and edit the [sshd] config to disable all host keys 
                    // except for RSA.

                    var configScript =
@"
# Copy the server key.

cp ssh_host_rsa_key /etc/ssh/ssh_host_rsa_key

# Disable all host keys except for RSA.

sed -i 's!^\HostKey /etc/ssh/ssh_host_dsa_key$!#HostKey /etc/ssh/ssh_host_dsa_key!g' /etc/ssh/sshd_config
sed -i 's!^\HostKey /etc/ssh/ssh_host_ecdsa_key$!#HostKey /etc/ssh/ssh_host_ecdsa_key!g' /etc/ssh/sshd_config
sed -i 's!^\HostKey /etc/ssh/ssh_host_ed25519_key$!#HostKey /etc/ssh/ssh_host_ed25519_key!g' /etc/ssh/sshd_config

# Restart SSHD to pick up the changes.

systemctl restart sshd
";
                    bundle = new CommandBundle("./config.sh");

                    bundle.AddFile("config.sh", configScript, isExecutable: true);
                    bundle.AddFile("ssh_host_rsa_key", kubeContextExtension.SshNodePrivateKey);
                    node.SudoCommand(bundle);
                });
        }

        /// <inheritdoc/>
        public override DockerShimInfo Shim(DockerShim shim)
        {
            return new DockerShimInfo(shimability: DockerShimability.None);
        }
    }
}