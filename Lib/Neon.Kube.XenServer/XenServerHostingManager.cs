﻿//-----------------------------------------------------------------------------
// FILE:	    XenServerHostingManager.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using YamlDotNet.Serialization;

using Neon.Common;
using Neon.Net;
using Neon.XenServer;
using Neon.IO;
using Neon.SSH;

using k8s.Models;

namespace Neon.Kube
{
    /// <summary>
    /// Manages cluster provisioning on the XenServer hypervisor.
    /// </summary>
    [HostingProvider(HostingEnvironment.XenServer)]
    public partial class XenServerHostingManager : HostingManager
    {
        //---------------------------------------------------------------------
        // Private types

        /// <summary>
        /// Used to persist information about downloaded XVA template files.
        /// </summary>
        public class DiskTemplateInfo
        {
            /// <summary>
            /// The downloaded file ETAG.
            /// </summary>
            [JsonProperty(PropertyName = "ETag", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
            [YamlMember(Alias = "etag", ApplyNamingConventions = false)]
            [DefaultValue(null)]
            public string ETag { get; set; }

            /// <summary>
            /// The downloaded file length used as a quick verification that
            /// the complete file was downloaded.
            /// </summary>
            [JsonProperty(PropertyName = "Length", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
            [YamlMember(Alias = "length", ApplyNamingConventions = false)]
            [DefaultValue(-1)]
            public long Length { get; set; }
        }

        //---------------------------------------------------------------------
        // Static members

        /// <summary>
        /// Ensures that the assembly hosting this hosting manager is loaded.
        /// </summary>
        public static void Load()
        {
            // We don't have to do anything here because the assembly is loaded
            // as a byproduct of calling this method.
        }

        //---------------------------------------------------------------------
        // Instance members

        private ClusterProxy        cluster;
        private KubeSetupInfo       setupInfo;
        private string              logFolder;
        private List<XenClient>     xenHosts;
        private int                 maxVmNameWidth;
        private string              secureSshPassword;

        /// <summary>
        /// Creates an instance that is only capable of validating the hosting
        /// related options in the cluster definition.
        /// </summary>
        public XenServerHostingManager()
        {
        }

        /// <summary>
        /// Creates an instance that is capable of provisioning a cluster on XenServer/XCP-ng servers.
        /// </summary>
        /// <param name="cluster">The cluster being managed.</param>
        /// <param name="setupInfo">Specifies the cluster setup information.</param>
        /// <param name="logFolder">
        /// The folder where log files are to be written, otherwise or <c>null</c> or 
        /// empty if logging is disabled.
        /// </param>
        public XenServerHostingManager(ClusterProxy cluster, KubeSetupInfo setupInfo, string logFolder = null)
        {
            Covenant.Requires<ArgumentNullException>(cluster != null, nameof(cluster));
            Covenant.Requires<ArgumentNullException>(setupInfo != null, nameof(setupInfo));

            this.cluster                = cluster;
            this.cluster.HostingManager = this;
            this.setupInfo              = setupInfo;
            this.logFolder              = logFolder;
            this.maxVmNameWidth         = cluster.Definition.Nodes.Max(n => n.Name.Length) + cluster.Definition.Hosting.Vm.GetVmNamePrefix(cluster.Definition).Length;
        }

        /// <inheritdoc/>
        public override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (xenHosts != null)
                {
                    foreach (var xenHost in xenHosts)
                    {
                        xenHost.Dispose();
                    }

                    xenHosts = null;
                }

                GC.SuppressFinalize(this);
            }

            xenHosts = null;
        }

        /// <inheritdoc/>
        public override bool IsProvisionNOP
        {
            get { return false; }
        }

        /// <inheritdoc/>
        public override void Validate(ClusterDefinition clusterDefinition)
        {
            Covenant.Requires<ArgumentNullException>(clusterDefinition != null, nameof(clusterDefinition));
        }

        /// <inheritdoc/>
        public override async Task<bool> ProvisionAsync(ClusterLogin clusterLogin, string secureSshPassword, string orgSshPassword = null)
        {
            Covenant.Requires<ArgumentNullException>(clusterLogin != null, nameof(clusterLogin));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(secureSshPassword), nameof(secureSshPassword));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(orgSshPassword), nameof(orgSshPassword));
            Covenant.Assert(cluster != null, $"[{nameof(XenServerHostingManager)}] was created with the wrong constructor.");

            this.secureSshPassword = secureSshPassword;

            if (IsProvisionNOP)
            {
                // There's nothing to do here.

                return true;
            }

            // We need to ensure that the cluster has at least one ingress node.

            KubeHelper.EnsureIngressNodes(cluster.Definition);

            // We'll call this to be consistent with the cloud hosting managers even though
            // the upstream on-premise router currently needs to be configured manually.

            KubeHelper.EnsureIngressNodes(cluster.Definition);

            // We need to ensure that at least one node will host the OpenEBS
            // cStor block device.

            KubeHelper.EnsureOpenEbsNodes(cluster.Definition);

            // Update the node labels with the actual capabilities of the 
            // virtual machines being provisioned.

            foreach (var node in cluster.Definition.Nodes)
            {
                node.Labels.PhysicalMachine = node.Vm.Host;
                node.Labels.ComputeCores    = node.Vm.GetProcessors(cluster.Definition);
                node.Labels.ComputeRam      = (int)(node.Vm.GetMemory(cluster.Definition) / ByteUnits.MebiBytes);
                node.Labels.StorageSize     = ByteUnits.ToGiB(node.Vm.GetOsDisk(cluster.Definition));
            }

            // Build a list of [LinuxSshProxy] instances that map to the specified XenServer
            // hosts.  We'll use the [XenClient] instances as proxy metadata.

            var xenSshProxies = new List<NodeSshProxy<XenClient>>();

            xenHosts = new List<XenClient>();

            foreach (var host in cluster.Definition.Hosting.Vm.Hosts)
            {
                var hostAddress  = host.Address;
                var hostname     = host.Name;
                var hostUsername = host.Username ?? cluster.Definition.Hosting.Vm.HostUsername;
                var hostPassword = host.Password ?? cluster.Definition.Hosting.Vm.HostPassword;

                if (string.IsNullOrEmpty(hostname))
                {
                    hostname = host.Address;
                }

                var xenHost = new XenClient(hostAddress, hostUsername, hostPassword, name: host.Name, logFolder: logFolder);

                xenHosts.Add(xenHost);
                xenSshProxies.Add(xenHost.SshProxy);
            }

            // We're going to provision the XenServer hosts in parallel to
            // speed up cluster setup.  This works because each XenServer
            // is essentially independent from the others.

            var setupController = new SetupController<XenClient>($"Provisioning [{cluster.Definition.Name}] cluster", xenSshProxies)
            {
                ShowStatus  = this.ShowStatus,
                MaxParallel = this.MaxParallel
            };
             
            setupController.AddWaitUntilOnlineStep();

            setupController.AddNodeStep("verify readiness", (node, stepDelay) => VerifyReady(node));
            setupController.AddNodeStep("virtual machine template", (node, stepDelay) => CheckVmTemplate(node));
            setupController.AddNodeStep("create virtual machines", (node, stepDelay) => ProvisionVM(node));
            setupController.AddGlobalStep(string.Empty, () => Finish(), quiet: true);

            if (!setupController.Run())
            {
                Console.Error.WriteLine("*** ERROR: One or more configuration steps failed.");
                return await Task.FromResult(false);
            }

            return await Task.FromResult(true);
        }

        /// <inheritdoc/>
        public override void AddPostPrepareSteps(SetupController<NodeDefinition> setupController)
        {
            // We need to add any required OpenEBS cStor disks after the node has been otherwise
            // prepared.  We need to do this here because if we created the data and OpenEBS disks
            // when the VM is initially created, the disk setup scripts executed during prepare
            // won't be able to distinguish between the two disks.
            //
            // At this point, the data disk should be partitioned, formatted, and mounted so
            // the OpenEBS disk will be easy to identify as the only unpartitioned disk.

            // IMPLEMENTATION NOTE:
            // --------------------
            // This is a bit tricky.  The essential problem is that the setup controller passed
            // is intended for parallel operations on nodes, not XenServer hosts (like we did
            // above for provisioning).  We still have those XenServer host clients in the [xenHosts]
            // list field.  Note that XenClients are not thread-safe.
            // 
            // We're going to perform these operations in parallel, but require that each node
            // operation acquire a lock on the XenClient for the node's host before proceeding.

            setupController.AddNodeStep("openebs",
                (node, stepDelay) =>
                {
                    var xenClient = xenHosts.Single(client => client.Name == node.Metadata.Vm.Host);

                    node.Status = "openebs: waiting for host...";

                    lock (xenClient)
                    {
                        var vm = xenClient.Machine.List().Single(vm => vm.NameLabel == GetVmName(node));

                        if (xenClient.Machine.DiskCount(vm) < 2)
                        {
                            // We haven't created the cStor disk yet.

                            var disk = new XenVirtualDisk()
                            {
                                Name        = $"{GetVmName(node)}: openebs",
                                Size        = node.Metadata.Vm.GetOpenEbsDisk(cluster.Definition),
                                Description = "OpenEBS cStor"
                            };

                            node.Status = "openebs: stop VM";
                            xenClient.Machine.Shutdown(vm);

                            node.Status = "openebs: add cStor disk";
                            xenClient.Machine.AddDisk(vm, disk);

                            node.Status = "openebs: restart VM";
                            xenClient.Machine.Start(vm);
                        }
                    }
                },
                node => node.Metadata.OpenEBS);
        }

        /// <summary>
        /// Returns the list of <see cref="NodeDefinition"/> instances describing which cluster
        /// nodes are to be hosted by a specific XenServer.
        /// </summary>
        /// <param name="xenHost">The target XenServer.</param>
        /// <returns>The list of nodes to be hosted on the XenServer.</returns>
        private List<NodeSshProxy<NodeDefinition>> GetHostedNodes(XenClient xenHost)
        {
            var nodeDefinitions = cluster.Definition.NodeDefinitions.Values;

            return cluster.Nodes.Where(n => n.Metadata.Vm.Host.Equals(xenHost.Name, StringComparison.InvariantCultureIgnoreCase))
                .OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Returns the name to use when naming the virtual machine hosting the node.
        /// </summary>
        /// <param name="node">The target node.</param>
        /// <returns>The virtual machine name.</returns>
        private string GetVmName(NodeSshProxy<NodeDefinition> node)
        {
            return $"{cluster.Definition.Hosting.Vm.GetVmNamePrefix(cluster.Definition)}{node.Name}";
        }

        /// <summary>
        /// Verify that the XenServer is ready to provision the cluster virtual machines.
        /// </summary>
        /// <param name="xenSshProxy">The XenServer SSH proxy.</param>
        private void VerifyReady(NodeSshProxy<XenClient> xenSshProxy)
        {
            // $todo(jefflill):
            //
            // It would be nice to verify that XenServer actually has enough 
            // resources (RAM, DISK, and perhaps CPU) here as well.

            var xenHost = xenSshProxy.Metadata;
            var nodes   = GetHostedNodes(xenHost);

            xenSshProxy.Status = "check: virtual machines";

            var vmNames = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var vm in xenHost.Machine.List())
            {
                vmNames.Add(vm.NameLabel);
            }

            foreach (var hostedNode in nodes)
            {
                var vmName = GetVmName(hostedNode);

                if (vmNames.Contains(vmName))
                {
                    xenSshProxy.Fault($"XenServer [{xenHost.Name}] already hosts a virtual machine named [{vmName}].");
                    return;
                }
            }
        }

        /// <summary>
        /// Returns the name to use for the node template to be persisted on the XenServers.
        /// </summary>
        /// <returns>The template name.</returns>
        private string GetXenTemplateName()
        {
            return $"neon-{cluster.Definition.LinuxDistribution}-{cluster.Definition.LinuxVersion}";
        }

        /// <summary>
        /// Install the virtual machine template on the XenServer if it's not already present.
        /// </summary>
        /// <param name="xenSshProxy">The XenServer SSH proxy.</param>
        private void CheckVmTemplate(NodeSshProxy<XenClient> xenSshProxy)
        {
            var xenHost      = xenSshProxy.Metadata;
            var templateName = GetXenTemplateName();

            xenSshProxy.Status = "check: template";

            if (xenHost.Template.Find(templateName) == null)
            {
                xenSshProxy.Status = "download: vm template (slow)";
                xenHost.Template.Install(setupInfo.LinuxTemplateUri, templateName, cluster.Definition.Hosting.XenServer.StorageRepository);
            }
        }

        /// <summary>
        /// Formats a nice node status message.
        /// </summary>
        /// <param name="vmName">The name of the virtual machine used to host the cluster node.</param>
        /// <param name="message">The status message.</param>
        /// <returns>The formatted status message.</returns>
        private string FormatVmStatus(string vmName, string message)
        {
            var namePart     = $"[{vmName}]:";
            var desiredWidth = maxVmNameWidth + 3;
            var actualWidth  = namePart.Length;

            if (desiredWidth > actualWidth)
            {
                namePart += new string(' ', desiredWidth - actualWidth);
            }

            return $"{namePart} {message}";
        }

        /// <summary>
        /// Provision the virtual machines on the XenServer.
        /// </summary>
        /// <param name="xenSshProxy">The XenServer SSH proxy.</param>
        private void ProvisionVM(NodeSshProxy<XenClient> xenSshProxy)
        {
            var xenHost  = xenSshProxy.Metadata;
            var hostInfo = xenHost.GetHostInfo();

            if (hostInfo.Version < KubeConst.MinXenServerVersion)
            {
                throw new NotSupportedException($"neonKUBE cannot provision a cluster on a XenServer/XCP-ng host older than [v{KubeConst.MinXenServerVersion}].  [{hostInfo.Params["name-label"]}] is running version [{hostInfo.Version}]. ");
            }

            foreach (var node in GetHostedNodes(xenHost))
            {
                var vmName      = GetVmName(node);
                var processors  = node.Metadata.Vm.GetProcessors(cluster.Definition);
                var memoryBytes = node.Metadata.Vm.GetMemory(cluster.Definition);
                var osDiskBytes = node.Metadata.Vm.GetOsDisk(cluster.Definition);

                xenSshProxy.Status = FormatVmStatus(vmName, "create: virtual machine");

                var vm = xenHost.Machine.Create(vmName, GetXenTemplateName(),
                    processors:                 processors,
                    memoryBytes:                memoryBytes,
                    diskBytes:                  osDiskBytes,
                    snapshot:                   cluster.Definition.Hosting.XenServer.Snapshot,
                    primaryStorageRepository:   cluster.Definition.Hosting.XenServer.StorageRepository);;

                // Create a temporary ISO with the [neon-node-prep.sh] script, mount it
                // to the VM and then boot the VM for the first time.  The script on the
                // ISO will be executed automatically by the [neon-node-prep] service
                // preinstalled on the VM image and the script will configure the secure 
                // SSH password and then the network.
                //
                // This ensures that SSH is not exposed to the network before the secure
                // password has been set.

                var tempIso    = (TempFile)null;
                var xenTempIso = (XenTempIso)null;

                try
                {
                    // Create a temporary ISO with the prep script and insert it
                    // into the node VM.

                    node.Status = $"mount: neon-node-prep iso";

                    tempIso    = KubeHelper.CreateNodePrepIso(node.Cluster.Definition, node.Metadata, secureSshPassword);
                    xenTempIso = xenHost.CreateTempIso(tempIso.Path);

                    xenHost.Invoke($"vm-cd-eject", $"uuid={vm.Uuid}");
                    xenHost.Invoke($"vm-cd-insert", $"uuid={vm.Uuid}", $"cd-name={xenTempIso.CdName}");

                    // Start the VM for the first time with the mounted ISO.  The network
                    // configuration will happen automatically by the time we can connect.

                    node.Status = $"start: virtual machine (first boot)";
                    xenHost.Machine.Start(vm);

                    // Update the node credentials to use the secure password and then wait for the node to boot.

                    node.UpdateCredentials(SshCredentials.FromUserPassword(KubeConst.SysAdminUsername, secureSshPassword));

                    node.Status = $"connecting...";
                    node.WaitForBoot();

                    // Extend the primary partition and file system to fill 
                    // the virtual disk.  Note that we're not going to do
                    // this if the specified disk size is less than or equal
                    // to the node template's disk size (because that
                    // would fail).
                    //
                    // Note that there should only be one unpartitioned disk at
                    // this point: the OS disk.

                    var partitionedDisks = node.ListPartitionedDisks();
                    var osDisk           = partitionedDisks.Single();

                    if (osDiskBytes > KubeConst.NodeTemplateDiskSize)
                    {
                        node.Status = $"resize: OS disk";
                        node.SudoCommand($"growpart {osDisk} 2");
                        node.SudoCommand($"resize2fs {osDisk}2");
                    }
                }
                finally
                {
                    // Be sure to delete the local and remote ISO files so these don't accumulate.

                    tempIso?.Dispose();

                    if (xenTempIso != null)
                    {
                        xenHost.Invoke($"vm-cd-eject", $"uuid={vm.Uuid}");
                        xenHost.RemoveTempIso(xenTempIso);
                    }
                }
            }
        }

        /// <summary>
        /// Perform any necessary global post Hyper-V provisioning steps.
        /// </summary>
        private void Finish()
        {
        }

        /// <inheritdoc/>
        public override (string Address, int Port) GetSshEndpoint(string nodeName)
        {
            return (Address: cluster.GetNode(nodeName).Address.ToString(), Port: NetworkPorts.SSH);
        }

        /// <inheritdoc/>
        public override string GetDataDisk(NodeSshProxy<NodeDefinition> node)
        {
            Covenant.Requires<ArgumentNullException>(node != null, nameof(node));

            // This hosting manager doesn't currently provision a separate data disk.

            return "PRIMARY";
        }
    }
}
