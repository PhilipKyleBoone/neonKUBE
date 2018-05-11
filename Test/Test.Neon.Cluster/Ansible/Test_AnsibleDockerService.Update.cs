﻿//-----------------------------------------------------------------------------
// FILE:	    Test_AnsibleDockerService.Update.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2018 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Docker;
using Neon.Xunit;
using Neon.Xunit.Cluster;

using Xunit;

namespace TestNeonCluster
{
    public partial class Test_AnsibleDockerService : IClassFixture<ClusterFixture>
    {
        /// <summary>
        /// Deploys <b>neoncluster/test:0</b> with default options.
        /// </summary>
        private void DeployTestService()
        {
            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        update_monitor: 1s
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);

            Assert.Equal(serviceName, details.Spec.Name);
            Assert.Equal(serviceImage, details.Spec.TaskTemplate.ContainerSpec.ImageWithoutSHA);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Image()
        {
            DeployTestService();

            // Verify that we can update a service image.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: neoncluster/test:1
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);

            Assert.Equal(serviceName, details.Spec.Name);
            Assert.Equal("neoncluster/test:1", details.Spec.TaskTemplate.ContainerSpec.ImageWithoutSHA);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Args()
        {
            DeployTestService();

            //-----------------------------------------------------------------
            // Verify that we can update service arguments.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        args:
          - one
          - two
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);

            Assert.Equal(new string[] { "one", "two" }, details.Spec.TaskTemplate.ContainerSpec.Args);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Config()
        {
            DeployTestService();

            //-----------------------------------------------------------------
            // Verify that we can add a config.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        config:
          - source: config-1
            target: config
            uid: {TestHelper.TestUID}
            gid: {TestHelper.TestGID}
            mode: 440
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);
            var config = details.Spec.TaskTemplate.ContainerSpec.Configs.FirstOrDefault();

            Assert.NotNull(config);
            Assert.Equal("config-1", config.ConfigName);
            Assert.Equal("config", config.File.Name);
            Assert.Equal(TestHelper.TestUID, config.File.UID);
            Assert.Equal(TestHelper.TestGID, config.File.GID);
            Assert.Equal(Convert.ToInt32("440", 8), config.File.Mode);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);

            details = cluster.InspectService(serviceName);
            config = details.Spec.TaskTemplate.ContainerSpec.Configs.FirstOrDefault();

            Assert.NotNull(config);
            Assert.Equal("config-1", config.ConfigName);
            Assert.Equal("config", config.File.Name);
            Assert.Equal(TestHelper.TestUID, config.File.UID);
            Assert.Equal(TestHelper.TestGID, config.File.GID);
            Assert.Equal(Convert.ToInt32("440", 8), config.File.Mode);

            //-----------------------------------------------------------------
            // Verify that we can remove a config.

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);

            details = cluster.InspectService(serviceName);

            Assert.Empty(details.Spec.TaskTemplate.ContainerSpec.Configs);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Constraint()
        {
            DeployTestService();

            //-----------------------------------------------------------------
            // Verify that we can add placement constraints.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        constraint:
          - node.role==manager
          - node.role!=worker
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);
            var constraints = details.Spec.TaskTemplate.Placement.Constraints;

            Assert.NotNull(constraints);
            Assert.Equal(2, constraints.Count);
            Assert.Contains("node.role==manager", constraints);
            Assert.Contains("node.role!=worker", constraints);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);

            //-----------------------------------------------------------------
            // Verify that we can remove placement constraints.

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        constraint:
          - node.role==manager
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);
            constraints = details.Spec.TaskTemplate.Placement.Constraints;

            Assert.NotNull(constraints);
            Assert.Single(constraints);
            Assert.Contains("node.role==manager", constraints);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_ContainerLabel()
        {
            DeployTestService();

            //-----------------------------------------------------------------
            // Verify that we can add container labels.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        container_label:
          - foo=bar
          - hello=world
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);
            var labels = details.Spec.TaskTemplate.ContainerSpec.Labels;

            Assert.NotNull(labels);
            Assert.Equal(2, labels.Count);
            Assert.Equal("bar", labels["foo"]);
            Assert.Contains("world", labels["hello"]);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);

            //-----------------------------------------------------------------
            // Verify that we can remove container labels.

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        container_label:
          - foo=bar
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);
            labels = details.Spec.TaskTemplate.ContainerSpec.Labels;

            Assert.NotNull(labels);
            Assert.Single(labels);
            Assert.Equal("bar", labels["foo"]);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);

            //-----------------------------------------------------------------
            // Verify that we can edit a container label.

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        container_label:
          - foo=foobar
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);
            labels = details.Spec.TaskTemplate.ContainerSpec.Labels;

            Assert.NotNull(labels);
            Assert.Single(labels);
            Assert.Equal("foobar", labels["foo"]);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Dns()
        {
            DeployTestService();

            //-----------------------------------------------------------------
            // Verify that we can add some DNS settings.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        dns:
          - 8.8.8.8
          - 8.8.4.4
        dns_option:
          - timeout:2
        dns_search:
          - foo.com
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);
            var dnsConfig = details.Spec.TaskTemplate.ContainerSpec.DNSConfig;

            Assert.Equal(2, dnsConfig.Nameservers.Count);
            Assert.Contains("8.8.8.8", dnsConfig.Nameservers);
            Assert.Contains("8.8.4.4", dnsConfig.Nameservers);

            Assert.Equal(new string[] { "timeout:2" }, dnsConfig.Options);
            Assert.Equal(new string[] { "foo.com" }, dnsConfig.Search);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);

            //-----------------------------------------------------------------
            // Verify that we can remove DNS settings labels.

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);
            dnsConfig = details.Spec.TaskTemplate.ContainerSpec.DNSConfig;

            Assert.Empty(dnsConfig.Options);
            Assert.Empty(dnsConfig.Nameservers);
            Assert.Empty(dnsConfig.Search);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_EndpointMode()
        {
            DeployTestService();

            // Verify that we can change the service endpoint mode.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        endpoint_mode: dnsrr
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);

            Assert.Equal(ServiceEndpointMode.DnsRR, details.Spec.EndpointSpec.Mode);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Entrypoint()
        {
            DeployTestService();

            // Verify that we can update the service image entrypoint.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        entrypoint:
          - sleep
          - 7777777
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);

            Assert.Equal(new string[] { "sleep", "7777777" }, details.Spec.TaskTemplate.ContainerSpec.Command);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Env()
        {
            DeployTestService();

            // Verify that we can add environment variables.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        env:
          - FOO=BAR
          - SUDO_USER
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);
            var env = details.Spec.TaskTemplate.ContainerSpec.Env;

            Assert.Equal(2, env.Count);
            Assert.Contains("FOO=BAR", env);
            Assert.Contains("SUDO_USER=sysadmin", env);

            //-----------------------------------------------------------------
            // The module is going to report a change here even though the
            // playbook hasn't changed because SUDO_USER is configured to
            // use the Docker host machine's environment variable.
            //
            // There's no way to know from the service inspection whether
            // a variable value was explicitly specified or if it came
            // from the host, so we have to treat this as change.
            //
            // This is different from how all the other updated collection
            // items work.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);

            //-----------------------------------------------------------------
            // Verify that we can remove environment variables labels.

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        env:
          - FOO=BAR
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);
            env = details.Spec.TaskTemplate.ContainerSpec.Env;

            Assert.Single(env);
            Assert.Contains("FOO=BAR", env);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);

            //-----------------------------------------------------------------
            // Verify that we can edit a environment variable.

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        env:
          - FOO=FOOBAR
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);
            env = details.Spec.TaskTemplate.ContainerSpec.Env;

            Assert.Single(env);
            Assert.Contains("FOO=FOOBAR", env);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Replicas()
        {
            DeployTestService();

            // Verify that we can update the replicas.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        replicas: 2
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);

            Assert.Equal(2, details.Spec.Mode.Replicated.Replicas);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Health()
        {
            DeployTestService();

            // Verify that we can update the health checking options.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        health_cmd: echo OK
        health_interval: 1000000000ns
        health_retries: 3
        health_start_period: 1100000000ns
        health_timeout: 1200000000ns
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);

            Assert.Equal(new string[] { "CMD-SHELL", "echo OK" }, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Test);
            Assert.Equal(1000000000L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Interval);
            Assert.Equal(3L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Retries);
            Assert.Equal(1100000000L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.StartPeriod);
            Assert.Equal(1200000000L, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Timeout);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);

            //-----------------------------------------------------------------
            // Update the service disabling health checks.

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        no_healthcheck: yes
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);

            Assert.Equal(new string[] { "NONE" }, details.Spec.TaskTemplate.ContainerSpec.HealthCheck.Test);
        }

        [Fact(Skip = "DOCKER BUG: https://github.com/moby/moby/issues/37035")]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Host()
        {
            DeployTestService();

            // Verify that we can update the container DNS [/etc/hosts] file.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        host: 
          - ""foo.com:1.1.1.1""
          - ""bar.com:2.2.2.2""
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);
            var hosts = details.Spec.TaskTemplate.ContainerSpec.Hosts;

            Assert.Equal(2, hosts.Count);
            Assert.Contains("1.1.1.1 foo.com", hosts);
            Assert.Contains("2.2.2.2 bar.com", hosts);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);

            //-----------------------------------------------------------------
            // Verify that we can submit updates.

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        host: 
          - ""foo.com:3.3.3.3""
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);
            hosts = details.Spec.TaskTemplate.ContainerSpec.Hosts;

            // This fails due to Docker bug: https://github.com/moby/moby/issues/37035

            Assert.Single(hosts);
            Assert.Contains("3.3.3.3 foo.com", hosts);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_NoResolveImage()
        {
            DeployTestService();

            // Verify that [no_resolve_image: true] doesn't barf.
            // This doesn't actually verify that the setting works
            // but I tested that manually.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        no_resolve_image: yes
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);    // This always return TRUE for this option.
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Limits()
        {
            DeployTestService();

            // Verify that we can update the container resource limits.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        limit_cpu: 1.5
        limit_memory: 64m
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);
            var limits = details.Spec.TaskTemplate.Resources.Limits;

            Assert.Equal(1500000000L, limits.NanoCPUs);
            Assert.Equal(67108864L, limits.MemoryBytes);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);

            details = cluster.InspectService(serviceName);
            limits = details.Spec.TaskTemplate.Resources.Limits;

            Assert.Equal(1500000000L, limits.NanoCPUs);
            Assert.Equal(67108864L, limits.MemoryBytes);
            Assert.False(taskResult.Changed);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Reservations()
        {
            DeployTestService();

            // Verify that we can update the container resource reservations.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        reserve_cpu: 1.5
        reserve_memory: 64m
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);
            var reservations = details.Spec.TaskTemplate.Resources.Reservations;

            Assert.Equal(1500000000L, reservations.NanoCPUs);
            Assert.Equal(67108864L, reservations.MemoryBytes);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);

            details = cluster.InspectService(serviceName);
            reservations = details.Spec.TaskTemplate.Resources.Reservations;

            Assert.Equal(1500000000L, reservations.NanoCPUs);
            Assert.Equal(67108864L, reservations.MemoryBytes);

            Assert.False(taskResult.Changed);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Mount()
        {
            DeployTestService();

            // Verify that we can update service mounts.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        mount:
          - type: volume
            source: test-volume
            target: /mnt/volume
            readonly: no
            volume_label:
              - VOLUME=TEST
            volume_nocopy: yes
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);
            var mounts = details.Spec.TaskTemplate.ContainerSpec.Mounts;

            Assert.Single(mounts);

            var mount = mounts.First();

            Assert.Equal(ServiceMountType.Volume, mount.Type);
            Assert.False(mount.ReadOnly);
            Assert.Equal(ServiceMountConsistency.Default, mount.Consistency);
            Assert.Null(mount.BindOptions);
            Assert.Equal("test-volume", mount.Source);
            Assert.Equal("/mnt/volume", mount.Target);
            Assert.Single(mount.VolumeOptions.Labels);
            Assert.Equal("TEST", mount.VolumeOptions.Labels["VOLUME"]);
            Assert.True(mount.VolumeOptions.NoCopy);

            //-----------------------------------------------------------------
            // BIND mount:

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        mount:
          - type: bind
            source: /tmp
            target: /mnt/volume
            readonly: yes
            consistency: cached
            bind_propagation: slave
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);
            mounts = details.Spec.TaskTemplate.ContainerSpec.Mounts;

            Assert.Single(mounts);

            mount = mounts.First();
            Assert.Equal(ServiceMountType.Bind, mount.Type);
            Assert.True(mount.ReadOnly);

            // $todo(jeff.lill):
            //
            // Not sure why this test isn't working.  I can see the option being
            // specified correctly by CreateService() but [service inspect]
            // doesn't include the property in the [BindOptions].
            //
            // This isn't super important so I'm deferring further investigation.

            //Assert.Equal(ServiceMountConsistency.Cached, mount.Consistency);

            Assert.Equal(ServiceMountBindPropagation.Slave, mount.BindOptions.Propagation);
            Assert.Equal("/tmp", mount.Source);
            Assert.Equal("/mnt/volume", mount.Target);

            //-----------------------------------------------------------------
            // TMPFS mount:

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        constraint:
          - node.role==manager
        mount:
          - type: tmpfs
            target: /mnt/tmpfs
            tmpfs_size: 64m
            tmpfs_mode: 770
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);
            mounts = details.Spec.TaskTemplate.ContainerSpec.Mounts;

            Assert.Single(mounts);

            mount = mounts.First();
            Assert.Equal(ServiceMountType.Tmpfs, mount.Type);
            Assert.False(mount.ReadOnly);
            Assert.Equal("/mnt/tmpfs", mount.Target);
            Assert.Equal(67108864L, mount.TmpfsOptions.SizeBytes);
            Assert.Equal(Convert.ToInt32("770", 8), mount.TmpfsOptions.Mode);

            //-----------------------------------------------------------------
            // Verify that we can detect when no changes were made.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            details = cluster.InspectService(serviceName);
            mounts = details.Spec.TaskTemplate.ContainerSpec.Mounts;

            Assert.Single(mounts);

            mount = mounts.First();
            Assert.Equal(ServiceMountType.Tmpfs, mount.Type);
            Assert.False(mount.ReadOnly);
            Assert.Equal("/mnt/tmpfs", mount.Target);
            Assert.Equal(67108864L, mount.TmpfsOptions.SizeBytes);
            Assert.Equal(Convert.ToInt32("770", 8), mount.TmpfsOptions.Mode);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_Secret()
        {
            DeployTestService();

            //-----------------------------------------------------------------
            // Verify that we can add a secret.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        secret:
          - source: secret-1
            target: secret
            uid: {TestHelper.TestUID}
            gid: {TestHelper.TestGID}
            mode: 440
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);
            var secret = details.Spec.TaskTemplate.ContainerSpec.Secrets.FirstOrDefault();

            Assert.NotNull(secret);
            Assert.Equal("secret-1", secret.SecretName);
            Assert.Equal("secret", secret.File.Name);
            Assert.Equal(TestHelper.TestUID, secret.File.UID);
            Assert.Equal(TestHelper.TestGID, secret.File.GID);
            Assert.Equal(Convert.ToInt32("440", 8), secret.File.Mode);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);

            details = cluster.InspectService(serviceName);
            secret = details.Spec.TaskTemplate.ContainerSpec.Secrets.FirstOrDefault();

            Assert.NotNull(secret);
            Assert.Equal("secret-1", secret.SecretName);
            Assert.Equal("secret", secret.File.Name);
            Assert.Equal(TestHelper.TestUID, secret.File.UID);
            Assert.Equal(TestHelper.TestGID, secret.File.GID);
            Assert.Equal(Convert.ToInt32("440", 8), secret.File.Mode);

            //-----------------------------------------------------------------
            // Verify that we can remove a secret.

            playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
";
            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);

            details = cluster.InspectService(serviceName);

            Assert.Empty(details.Spec.TaskTemplate.ContainerSpec.Secrets);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);
        }

        [Fact]
        [Trait(TestCategory.CategoryTrait, TestCategory.NeonCli)]
        public void Update_UserGroup()
        {
            DeployTestService();

            // Verify that we can update the service user and group.

            var playbook =
$@"
- name: test
  hosts: localhost
  tasks:
    - name: manage service
      neon_docker_service:
        name: {serviceName}
        state: present
        image: {serviceImage}
        user: {TestHelper.TestUID}
        group:
          - {TestHelper.TestGID}
";
            var results = AnsiblePlayer.NeonPlay(playbook);
            var taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.True(taskResult.Changed);
            Assert.Single(cluster.ListServices().Where(s => s.Name == serviceName));

            var details = cluster.InspectService(serviceName);

            Assert.Equal(TestHelper.TestUID, details.Spec.TaskTemplate.ContainerSpec.User);
            Assert.Equal(new string[] { TestHelper.TestGID }, details.Spec.TaskTemplate.ContainerSpec.Groups);

            //-----------------------------------------------------------------
            // Verify that update reports when no change is detected.

            results = AnsiblePlayer.NeonPlay(playbook);
            taskResult = results.GetTaskResult("manage service");

            Assert.True(taskResult.Success);
            Assert.False(taskResult.Changed);
        }
    }
}
