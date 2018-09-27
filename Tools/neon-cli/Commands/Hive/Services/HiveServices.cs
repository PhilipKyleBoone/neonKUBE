﻿//-----------------------------------------------------------------------------
// FILE:	    HiveServices.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Consul;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Neon.Common;
using Neon.IO;
using Neon.Hive;
using Neon.Net;
using Neon.Time;

namespace NeonCli
{
    /// <summary>
    /// Handles the provisioning of the global hive proxy services including: 
    /// <b>neon-hive-manager</b>, <b>neon-proxy-manager</b>, <b>neon-varnish</b>,
    /// <b>neon-proxy-public</b> and <b>neon-proxy-private</b>, <b>neon-dns</b>, 
    /// <b>neon-dns-mon</b> as well as the <b>neon-proxy-public-bridge</b> and
    /// <b>neon-proxy-private-bridge</b> containers on any pet nodes.
    /// </summary>
    public class HiveServices : ServicesBase
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="hive">The hive proxy.</param>
        public HiveServices(HiveProxy hive)
            : base(hive)
        {
        }

        /// <summary>
        /// Configures the hive services.
        /// </summary>
        /// <param name="firstManager">The first hive proxy manager.</param>
        public void Configure(SshProxy<NodeDefinition> firstManager)
        {
            firstManager.InvokeIdempotentAction("setup/hive-services",
                () =>
                {
                    // Ensure that Vault has been initialized.

                    if (!Hive.HiveLogin.HasVaultRootCredentials)
                    {
                        throw new InvalidOperationException("Vault has not been initialized yet.");
                    }

                    //---------------------------------------------------------
                    // Deploy DNS related services.

                    // Deploy: neon-dns-mon

                    StartService("neon-dns-mon", Hive.Definition.Image.DnsMon,
                        new CommandBundle(
                            "docker service create",
                            "--name", "neon-dns-mon",
                            "--detach=false",
                            "--mount", "type=bind,src=/etc/neon/host-env,dst=/etc/neon/host-env,readonly=true",
                            "--mount", "type=bind,src=/usr/local/share/ca-certificates,dst=/mnt/host/ca-certificates,readonly=true",
                            "--env", "POLL_INTERVAL=5s",
                            "--env", "LOG_LEVEL=INFO",
                            "--constraint", "node.role==manager",
                            "--replicas", "1",
                            "--restart-delay", Hive.Definition.Docker.RestartDelay,
                            ImagePlaceholderArg));

                    // Deploy: neon-dns

                    StartService("neon-dns", Hive.Definition.Image.Dns,
                        new CommandBundle(
                            "docker service create",
                            "--name", "neon-dns",
                            "--detach=false",
                            "--mount", "type=bind,src=/etc/neon/host-env,dst=/etc/neon/host-env,readonly=true",
                            "--mount", "type=bind,src=/usr/local/share/ca-certificates,dst=/mnt/host/ca-certificates,readonly=true",
                            "--mount", "type=bind,src=/etc/powerdns/hosts,dst=/etc/powerdns/hosts",
                            "--mount", "type=bind,src=/dev/shm/neon-dns,dst=/neon-dns",
                            "--env", "POLL_INTERVAL=5s",
                            "--env", "VERIFY_INTERVAL=5m",
                            "--env", "LOG_LEVEL=INFO",
                            "--constraint", "node.role==manager",
                            "--mode", "global",
                            "--restart-delay", Hive.Definition.Docker.RestartDelay,
                            ImagePlaceholderArg));

                    //---------------------------------------------------------
                    // Deploy [neon-hive-manager] as a service constrained to manager nodes.

                    string unsealSecretOption = null;

                    if (Hive.Definition.Vault.AutoUnseal)
                    {
                        var vaultCredentials = NeonHelper.JsonClone<VaultCredentials>(Hive.HiveLogin.VaultCredentials);

                        // We really don't want to include the root token in the credentials
                        // passed to [neon-hive-manager], which needs the unseal keys so 
                        // we'll clear that here.

                        vaultCredentials.RootToken = null;

                        Hive.Docker.Secret.Set("neon-hive-manager-vaultkeys", Encoding.UTF8.GetBytes(NeonHelper.JsonSerialize(vaultCredentials, Formatting.Indented)));

                        unsealSecretOption = "--secret=neon-hive-manager-vaultkeys";
                    }

                    StartService("neon-hive-manager", Hive.Definition.Image.HiveManager,
                        new CommandBundle(
                            "docker service create",
                            "--name", "neon-hive-manager",
                            "--detach=false",
                            "--mount", "type=bind,src=/etc/neon/host-env,dst=/etc/neon/host-env,readonly=true",
                            "--mount", "type=bind,src=/usr/local/share/ca-certificates,dst=/mnt/host/ca-certificates,readonly=true",
                            "--mount", "type=bind,src=/var/run/docker.sock,dst=/var/run/docker.sock",
                            "--env", "LOG_LEVEL=INFO",
                            "--secret", "neon-ssh-credentials",
                            unsealSecretOption,
                            "--constraint", "node.role==manager",
                            "--replicas", 1,
                            "--restart-delay", Hive.Definition.Docker.RestartDelay,
                            ImagePlaceholderArg
                        ),
                        Hive.SecureRunOptions | RunOptions.FaultOnError);

                    //---------------------------------------------------------
                    // Deploy proxy related services

                    // Obtain the AppRole credentials from Vault for the proxy manager as well as the
                    // public and private proxy services and persist these as Docker secrets.

                    firstManager.Status = "secrets: proxy services";

                    Hive.Docker.Secret.Set("neon-proxy-manager-credentials", NeonHelper.JsonSerialize(Hive.Vault.Client.GetAppRoleCredentialsAsync("neon-proxy-manager").Result, Formatting.Indented));
                    Hive.Docker.Secret.Set("neon-proxy-public-credentials", NeonHelper.JsonSerialize(Hive.Vault.Client.GetAppRoleCredentialsAsync("neon-proxy-public").Result, Formatting.Indented));
                    Hive.Docker.Secret.Set("neon-proxy-private-credentials", NeonHelper.JsonSerialize(Hive.Vault.Client.GetAppRoleCredentialsAsync("neon-proxy-private").Result, Formatting.Indented));

                    // Initialize the public and private proxies.

                    Hive.PublicLoadBalancer.UpdateSettings(
                        new LoadBalancerSettings()
                        {
                            ProxyPorts = HiveConst.PublicProxyPorts
                        });

                    Hive.PrivateLoadBalancer.UpdateSettings(
                        new LoadBalancerSettings()
                        {
                            ProxyPorts = HiveConst.PrivateProxyPorts
                        });

                    // Deploy the proxy manager service.

                    StartService("neon-proxy-manager", Hive.Definition.Image.ProxyManager,
                        new CommandBundle(
                            "docker service create",
                            "--name", "neon-proxy-manager",
                            "--detach=false",
                            "--mount", "type=bind,src=/etc/neon/host-env,dst=/etc/neon/host-env,readonly=true",
                            "--mount", "type=bind,src=/usr/local/share/ca-certificates,dst=/mnt/host/ca-certificates,readonly=true",
                            "--mount", "type=bind,src=/var/run/docker.sock,dst=/var/run/docker.sock",
                            "--env", "VAULT_CREDENTIALS=neon-proxy-manager-credentials",
                            "--env", "LOG_LEVEL=INFO",
                            "--secret", "neon-proxy-manager-credentials",
                            "--constraint", "node.role==manager",
                            "--replicas", 1,
                            "--restart-delay", Hive.Definition.Docker.RestartDelay,
                            ImagePlaceholderArg));

                    // Docker mesh routing seemed unstable on versions so we're going
                    // to provide an option to work around this by running the PUBLIC, 
                    // PRIVATE and VAULT proxies on all nodes and  publishing the ports
                    // to the host (not the mesh).
                    //
                    //      https://github.com/jefflill/NeonForge/issues/104
                    //
                    // Note that this mode feature is documented (somewhat poorly) here:
                    //
                    //      https://docs.docker.com/engine/swarm/services/#publish-ports

                    var publicPublishArgs   = new List<string>();
                    var privatePublishArgs  = new List<string>();
                    var proxyConstraintArgs = new List<string>();
                    var proxyReplicasArgs   = new List<string>();
                    var proxyModeArgs       = new List<string>();

                    if (Hive.Definition.Docker.GetAvoidIngressNetwork(Hive.Definition))
                    {
                        // The parameterized [docker service create --publish] option doesn't handle port ranges so we need to 
                        // specify multiple publish options.

                        foreach (var port in HiveConst.PublicProxyPorts.Ports)
                        {
                            publicPublishArgs.Add($"--publish");
                            publicPublishArgs.Add($"mode=host,published={port},target={port}");
                        }

                        for (int port = HiveConst.PublicProxyPorts.PortRange.FirstPort; port <= HiveConst.PublicProxyPorts.PortRange.LastPort; port++)
                        {
                            publicPublishArgs.Add($"--publish");
                            publicPublishArgs.Add($"mode=host,published={port},target={port}");
                        }

                        foreach (var port in HiveConst.PrivateProxyPorts.Ports)
                        {
                            privatePublishArgs.Add($"--publish");
                            privatePublishArgs.Add($"mode=host,published={port},target={port}");
                        }

                        for (int port = HiveConst.PrivateProxyPorts.PortRange.FirstPort; port <= HiveConst.PrivateProxyPorts.PortRange.LastPort; port++)
                        {
                            privatePublishArgs.Add($"--publish");
                            privatePublishArgs.Add($"mode=host,published={port},target={port}");
                        }

                        proxyModeArgs.Add("--mode");
                        proxyModeArgs.Add("global");
                    }
                    else
                    {
                        // The parameterized [docker run --publish] option doesn't handle port ranges so we need to 
                        // specify multiple publish options.

                        foreach (var port in HiveConst.PublicProxyPorts.Ports)
                        {
                            publicPublishArgs.Add($"--publish");
                            publicPublishArgs.Add($"{port}:{port}");
                        }

                        publicPublishArgs.Add($"--publish");
                        publicPublishArgs.Add($"{HiveConst.PublicProxyPorts.PortRange.FirstPort}-{HiveConst.PublicProxyPorts.PortRange.LastPort}:{HiveConst.PublicProxyPorts.PortRange.FirstPort}-{HiveConst.PublicProxyPorts.PortRange.LastPort}");

                        foreach (var port in HiveConst.PrivateProxyPorts.Ports)
                        {
                            privatePublishArgs.Add($"--publish");
                            privatePublishArgs.Add($"{port}:{port}");
                        }

                        privatePublishArgs.Add($"--publish");
                        privatePublishArgs.Add($"{HiveConst.PrivateProxyPorts.PortRange.FirstPort}-{HiveConst.PrivateProxyPorts.PortRange.LastPort}:{HiveConst.PrivateProxyPorts.PortRange.FirstPort}-{HiveConst.PrivateProxyPorts.PortRange.LastPort}");

                        proxyConstraintArgs.Add($"--constraint");
                        proxyReplicasArgs.Add("--replicas");

                        if (Hive.Definition.Workers.Count() > 0)
                        {
                            // Constrain proxies to worker nodes if there are any.

                            proxyConstraintArgs.Add($"node.role!=manager");

                            if (Hive.Definition.Workers.Count() == 1)
                            {
                                proxyReplicasArgs.Add("1");
                            }
                            else
                            {
                                proxyReplicasArgs.Add("2");
                            }
                        }
                        else
                        {
                            // Constrain proxies to manager nodes nodes if there are no workers.

                            proxyConstraintArgs.Add($"node.role==manager");

                            if (Hive.Definition.Managers.Count() == 1)
                            {
                                proxyReplicasArgs.Add("1");
                            }
                            else
                            {
                                proxyReplicasArgs.Add("2");
                            }
                        }

                        proxyModeArgs.Add("--mode");
                        proxyModeArgs.Add("replicated");
                    }

                    // Deploy: neon-proxy-public

                    StartService("neon-proxy-public", Hive.Definition.Image.Proxy,
                        new CommandBundle(
                            "docker service create",
                            "--name", "neon-proxy-public",
                            "--detach=false",
                            "--mount", "type=bind,src=/etc/neon/host-env,dst=/etc/neon/host-env,readonly=true",
                            "--mount", "type=bind,src=/usr/local/share/ca-certificates,dst=/mnt/host/ca-certificates,readonly=true",
                            "--env", "CONFIG_KEY=neon/service/neon-proxy-manager/proxies/public/proxy-conf",
                            "--env", "CONFIG_HASH_KEY=neon/service/neon-proxy-manager/proxies/public/proxy-hash",
                            "--env", "VAULT_CREDENTIALS=neon-proxy-public-credentials",
                            "--env", "WARN_SECONDS=300",
                            "--env", "POLL_SECONDS=15",
                            "--env", "START_SECONDS=10",
                            "--env", "LOG_LEVEL=INFO",
                            "--env", "DEBUG=false",
                            "--env", "VAULT_SKIP_VERIFY=true",
                            "--secret", "neon-proxy-public-credentials",
                            publicPublishArgs,
                            proxyConstraintArgs,
                            proxyReplicasArgs,
                            proxyModeArgs,
                            "--restart-delay", Hive.Definition.Docker.RestartDelay,
                            "--network", HiveConst.PublicNetwork,
                            ImagePlaceholderArg));

                    // Deploy: neon-proxy-private

                    StartService("neon-proxy-private", Hive.Definition.Image.Proxy,
                        new CommandBundle(
                            "docker service create",
                            "--name", "neon-proxy-private",
                            "--detach=false",
                            "--mount", "type=bind,src=/etc/neon/host-env,dst=/etc/neon/host-env,readonly=true",
                            "--mount", "type=bind,src=/usr/local/share/ca-certificates,dst=/mnt/host/ca-certificates,readonly=true",
                            "--env", "CONFIG_KEY=neon/service/neon-proxy-manager/proxies/private/proxy-conf",
                            "--env", "CONFIG_HASH_KEY=neon/service/neon-proxy-manager/proxies/private/proxy-hash",
                            "--env", "VAULT_CREDENTIALS=neon-proxy-private-credentials",
                            "--env", "WARN_SECONDS=300",
                            "--env", "POLL_SECONDS=15",
                            "--env", "START_SECONDS=10",
                            "--env", "LOG_LEVEL=INFO",
                            "--env", "DEBUG=false",
                            "--env", "VAULT_SKIP_VERIFY=true",
                            "--secret", "neon-proxy-private-credentials",
                            privatePublishArgs,
                            proxyConstraintArgs,
                            proxyReplicasArgs,
                            proxyModeArgs,
                            "--restart-delay", Hive.Definition.Docker.RestartDelay,
                            "--network", HiveConst.PrivateNetwork,
                            ImagePlaceholderArg));
                });

                //---------------------------------------------------------
                // Deploy the RabbitMQ cluster.

                Hive.FirstManager.InvokeIdempotentAction("setup/hivemq-cluster",
                    () =>
                    {
                        // We're going to list the hive nodes that will host the
                        // RabbitMQ cluster and sort them by node name.  Then we're
                        // going to ensure that the first RabbitMQ node/container
                        // is started and ready before configuring the rest of the
                        // cluster so that it will bootstrap properly.

                        var hiveMQNodes = Hive.Nodes
                            .Where(n => n.Metadata.Labels.HiveMQ)
                            .OrderBy(n => n.Name)
                            .ToList();

                        DeployHiveMQ(hiveMQNodes.First());

                        // Start the remaining nodes in parallel.

                        var actions = new List<Action>();

                        foreach (var node in hiveMQNodes.Skip(1))
                        {
                            actions.Add(() => DeployHiveMQ(node));
                        }

                        NeonHelper.WaitForParallel(actions);

                        // The RabbitMQ cluster is created with the [/] vhost and the
                        // [sysadmin] user by default.  We need to create the [neon]
                        // and [app] vhosts along with the [neon] and [app] users
                        // and then set the appropriate permissions.
                        //
                        // We're going to run [rabbitmqctl] within the first RabbitMQ
                        // to accomplish this.

                        var hiveMQNode = hiveMQNodes.First();

                        // Create the vhosts.

                        Hive.FirstManager.InvokeIdempotentAction("setup/hivemq-cluster-vhost-app", () => hiveMQNode.SudoCommand($"docker exec neon-hivemq rabbitmqctl add_vhost {Hive.Definition.HiveMQ.AppVHost}"));
                        Hive.FirstManager.InvokeIdempotentAction("setup/hivemq-cluster-vhost-neon", () => hiveMQNode.SudoCommand($"docker exec neon-hivemq rabbitmqctl add_vhost {Hive.Definition.HiveMQ. NeonVHost}"));

                        // Create the users.

                        Hive.FirstManager.InvokeIdempotentAction("setup/hivemq-cluster-user-app", () => hiveMQNode.SudoCommand($"docker exec neon-hivemq rabbitmqctl add_user {Hive.Definition.HiveMQ.AppUser} {Hive.Definition.HiveMQ.AppUser}"));
                        Hive.FirstManager.InvokeIdempotentAction("setup/hivemq-cluster-user-neon", () => hiveMQNode.SudoCommand($"docker exec neon-hivemq rabbitmqctl add_user {Hive.Definition.HiveMQ.NeonUser} {Hive.Definition.HiveMQ.NeonPassword}"));

                        // Grant the [app] account full access to the [app] vhost, the [neon] account full
                        // access to the [neon] vhost.  Note that this doesn't need to be idempotent.

                        hiveMQNode.SudoCommand($"docker exec neon-hivemq rabbitmqctl set_permissions -p {Hive.Definition.HiveMQ.AppVHost} {Hive.Definition.HiveMQ.AppUser} \".*\" \".*\" \".*\"");
                        hiveMQNode.SudoCommand($"docker exec neon-hivemq rabbitmqctl set_permissions -p {Hive.Definition.HiveMQ. NeonVHost} {Hive.Definition.HiveMQ.NeonUser} \".*\" \".*\" \".*\"");

                        // Clear the UX status for the HiveMQ nodes.

                        foreach (var node in hiveMQNodes)
                        {
                            node.Status = string.Empty;
                        }

                        // Deploy private load balancer for the AMPQ endpoints.

                        var rule = new LoadBalancerHttpRule()
                        {
                            Name     = "neon-hivemq-ampq",
                            System   = true,
                            Resolver = null
                        };

                        // Initialize the frontends and backends.

                        rule.Frontends.Add(
                            new LoadBalancerHttpFrontend()
                            {
                                ProxyPort = HiveHostPorts.ProxyPrivateHiveMQAMPQ
                            });

                        rule.Backends.Add(
                            new LoadBalancerHttpBackend()
                            {
                                Group      = HiveHostGroups.HiveMQ,
                                GroupLimit = 5,
                                Port       = HiveHostPorts.HiveMQAMPQ
                            });

                        Hive.PrivateLoadBalancer.SetRule(rule);

                        // Deploy private load balancer for the management endpoints.

                        rule = new LoadBalancerHttpRule()
                        {
                            Name     = "neon-hivemq-management",
                            System   = true,
                            Resolver = null
                        };

                        // Initialize the frontends and backends.

                        rule.Frontends.Add(
                            new LoadBalancerHttpFrontend()
                            {
                                ProxyPort = HiveHostPorts.ProxyPrivateHiveMQAdmin
                            });

                        rule.Backends.Add(
                            new LoadBalancerHttpBackend()
                            {
                                Group      = HiveHostGroups.HiveMQManagers,
                                GroupLimit = 5,
                                Port       = HiveHostPorts.HiveMQManagement
                            });

                        Hive.PrivateLoadBalancer.SetRule(rule);
                    });

                //---------------------------------------------------------
                // Deploy the Varnish HTTP caching service
#if TODO
                if (Hive.Definition.Varnish.Enabled)
                {
                    var constraintArgs = new List<string>();

                    if (Hive.Workers.Count() > 0)
                    {
                        constraintArgs.Add("--constraint");
                        constraintArgs.Add("node.role!=manager");
                    }

                    StartService("neon-varnish", Hive.Definition.VarnishImage,
                        new CommandBundle(
                            "docker service create",
                            "--name", "neon-varnish",
                            "--detach=false",
                            "--mount", "type=bind,src=/etc/neon/host-env,dst=/etc/neon/host-env,readonly=true",
                            "--env", "LOG_LEVEL=INFO",
                            constraintArgs,
                            "--replicas", 1,
                            "--restart-delay", Hive.Definition.Docker.RestartDelay,
                            ImagePlaceholderArg
                        ));
                }
#endif
            // Log the hive into any Docker registries with credentials.

            firstManager.InvokeIdempotentAction("setup/registry-login",
                () =>
                {
                    foreach (var credential in Hive.Definition.Docker.Registries
                        .Where(r => !string.IsNullOrEmpty(r.Username)))
                    {
                        Hive.Registry.Login(credential.Registry, credential.Username, credential.Password);
                    }
                });
        }

        /// <summary>
        /// Deploys RabbitMQ to a cluster node as a container.
        /// </summary>
        /// <param name="node">The target hive node.</param>
        private void DeployHiveMQ(SshProxy<NodeDefinition> node)
        {
            // Deploy RabbitMQ only on the labeled nodes.

            if (node.Metadata.Labels.HiveMQ)
            {
                // Build a comma separated list of fully qualified RabbitMQ hostnames so we
                // can pass them as the CLUSTER environment variable.

                var rabbitNodes = Hive.Definition.SortedNodes.Where(n => n.Labels.HiveMQ).ToList();
                var sbCluster   = new StringBuilder();

                foreach (var rabbitNode in rabbitNodes)
                {
                    sbCluster.AppendWithSeparator($"{rabbitNode.Name}@{rabbitNode.Name}.{Hive.Definition.Hostnames.HiveMQ}", ",");
                }

                var hipeCompileArgs = new List<string>();

                if (Hive.Definition.HiveMQ.Precompile)
                {
                    hipeCompileArgs.Add("--env");
                    hipeCompileArgs.Add("RABBITMQ_HIPE_COMPILE=1");
                }

                var managementPluginArgs = new List<string>();

                if (node.Metadata.Labels.HiveMQManager)
                {
                    hipeCompileArgs.Add("--env");
                    hipeCompileArgs.Add("MANAGEMENT_PLUGIN=true");
                }

                // $todo(jeff.lill):
                //
                // I was unable to get TLS working correctly for RabbitMQ.  I'll come back
                // and revisit this later:
                //
                //      https://github.com/jefflill/NeonForge/issues/319

                StartContainer(node, "neon-hivemq", Hive.Definition.Image.HiveMQ, RunOptions.FaultOnError,
                    new CommandBundle(
                        "docker run",
                        "--detach",
                        "--name", "neon-hivemq",
                        "--env", $"CLUSTER_NAME={Hive.Definition.Name}",
                        "--env", $"CLUSTER_NODES={sbCluster}",
                        "--env", $"CLUSTER_PARTITION_MODE=autoheal",
                        "--env", $"NODENAME={node.Name}@{node.Name}.{Hive.Definition.Hostnames.HiveMQ}",
                        "--env", $"RABBITMQ_USE_LONGNAME=true",
                        "--env", $"RABBITMQ_DEFAULT_USER=sysadmin",
                        "--env", $"RABBITMQ_DEFAULT_PASS=password",
                        "--env", $"RABBITMQ_NODE_PORT={HiveHostPorts.HiveMQAMPQ}",
                        "--env", $"RABBITMQ_DIST_PORT={HiveHostPorts.HiveMQDIST}",
                        "--env", $"RABBITMQ_MANAGEMENT_PORT={HiveHostPorts.HiveMQManagement}",
                        "--env", $"RABBITMQ_ERLANG_COOKIE={Hive.Definition.HiveMQ.ErlangCookie}",
                        "--env", $"RABBITMQ_VM_MEMORY_HIGH_WATERMARK={Hive.Definition.HiveMQ.RamHighWatermark}",
                        hipeCompileArgs,
                        managementPluginArgs,
                        "--env", $"RABBITMQ_DISK_FREE_LIMIT={HiveDefinition.ValidateSize(Hive.Definition.HiveMQ.DiskFreeLimit, typeof(HiveMQOptions), nameof(Hive.Definition.HiveMQ.DiskFreeLimit))}",
                        //"--env", $"RABBITMQ_SSL_CERTFILE=/etc/neon/certs/hive.crt",
                        //"--env", $"RABBITMQ_SSL_KEYFILE=/etc/neon/certs/hive.key",
                        "--env", $"ERL_EPMD_PORT={HiveHostPorts.HiveMQEPMD}",
                        "--mount", "type=volume,source=neon-hivemq,target=/var/lib/rabbitmq",
                        "--mount", "type=bind,source=/etc/neon/certs,target=/etc/neon/certs,readonly",
                        "--publish", $"{HiveHostPorts.HiveMQEPMD}:{HiveHostPorts.HiveMQEPMD}",
                        "--publish", $"{HiveHostPorts.HiveMQAMPQ}:{HiveHostPorts.HiveMQAMPQ}",
                        "--publish", $"{HiveHostPorts.HiveMQDIST}:{HiveHostPorts.HiveMQDIST}",
                        "--publish", $"{HiveHostPorts.HiveMQManagement}:{HiveHostPorts.HiveMQManagement}",
                        "--memory", HiveDefinition.ValidateSize(Hive.Definition.HiveMQ.RamLimit, typeof(HiveMQOptions), nameof(Hive.Definition.HiveMQ.RamLimit)),
                        "--restart", "always",
                        ImagePlaceholderArg));

                // Wait for the RabbitMQ node to report that it's ready.

                var timeout  = TimeSpan.FromMinutes(4);
                var pollTime = TimeSpan.FromSeconds(2);

                node.Status = "hivemq: waiting";

                try
                {
                    NeonHelper.WaitFor(
                    () =>
                    {
                        var readyReponse = node.SudoCommand($"docker exec neon-hivemq rabbitmqctl node_health_check -n {node.Name}@{node.Name}.{Hive.Definition.Hostnames.HiveMQ}", node.DefaultRunOptions & ~RunOptions.FaultOnError);

                        return readyReponse.ExitCode == 0;
                    },
                    timeout: timeout,
                    pollTime: pollTime);
                }
                catch (TimeoutException)
                {
                    node.Fault($"RabbitMQ not ready after waiting [{timeout}].");
                    return;
                }

                node.Status = "hivemq: ready";
            }
        }

        /// <summary>
        /// Deploys hive containers to a node.
        /// </summary>
        /// <param name="node">The target hive node.</param>
        /// <param name="stepDelay">The step delay if the operation hasn't already been completed.</param>
        public void DeployContainers(SshProxy<NodeDefinition> node, TimeSpan stepDelay)
        {
            Thread.Sleep(stepDelay);

            // NOTE: We only need to deploy the proxy bridges to the pet nodes, 
            //       because these will be deployed as global services on the 
            //       swarm nodes.

            if (node.Metadata.IsPet)
            {
                StartContainer(node, "neon-proxy-public-bridge", Hive.Definition.Image.Proxy, RunOptions.FaultOnError,
                    new CommandBundle(
                        "docker run",
                        "--detach",
                        "--name", "neon-proxy-public-bridge",
                        "--mount", "type=bind,src=/etc/neon/host-env,dst=/etc/neon/host-env,readonly=true",
                        "--mount", "type=bind,src=/usr/local/share/ca-certificates,dst=/mnt/host/ca-certificates,readonly=true",
                        "--env", "CONFIG_KEY=neon/service/neon-proxy-manager/proxies/public-bridge/proxy-conf",
                        "--env", "CONFIG_HASH_KEY=neon/service/neon-proxy-manager/proxies/public-bridge/proxy-hash",
                        "--env", "VAULT_CREDENTIALS=neon-proxy-private-credentials",
                        "--env", "WARN_SECONDS=300",
                        "--env", "POLL_SECONDS=15",
                        "--env", "START_SECONDS=10",
                        "--env", "LOG_LEVEL=INFO",
                        "--env", "DEBUG=false",
                        "--env", "VAULT_SKIP_VERIFY=true",
                        "--network", "host",
                        "--restart", "always",
                        ImagePlaceholderArg));

                StartContainer(node, "neon-proxy-private-bridge", Hive.Definition.Image.Proxy, RunOptions.FaultOnError,
                    new CommandBundle(
                        "docker run",
                        "--detach",
                        "--name", "neon-proxy-private-bridge",
                        "--mount", "type=bind,src=/etc/neon/host-env,dst=/etc/neon/host-env,readonly=true",
                        "--mount", "type=bind,src=/usr/local/share/ca-certificates,dst=/mnt/host/ca-certificates,readonly=true",
                        "--env", "CONFIG_KEY=neon/service/neon-proxy-manager/proxies/private-bridge/proxy-conf",
                        "--env", "CONFIG_HASH_KEY=neon/service/neon-proxy-manager/proxies/private-bridge/proxy-hash",
                        "--env", "VAULT_CREDENTIALS=neon-proxy-private-credentials",
                        "--env", "WARN_SECONDS=300",
                        "--env", "POLL_SECONDS=15",
                        "--env", "START_SECONDS=10",
                        "--env", "LOG_LEVEL=INFO",
                        "--env", "DEBUG=false",
                        "--env", "VAULT_SKIP_VERIFY=true",
                        "--network", "host",
                        "--restart", "always",
                        ImagePlaceholderArg));
            }
        }
    }
}
