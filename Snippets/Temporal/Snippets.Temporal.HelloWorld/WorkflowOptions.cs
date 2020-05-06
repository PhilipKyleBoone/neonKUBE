﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Temporal;

namespace HelloWorld_WorkflowOptions
{
    [WorkflowInterface(TaskList = "my-tasks")]
    public interface IHelloWorkflow : IWorkflow
    {
        [WorkflowMethod]
        Task<string> HelloAsync(string name);
    }

    public class HelloWorkflow : WorkflowBase, IHelloWorkflow
    {
        public async Task<string> HelloAsync(string name)
        {
            return await Task.FromResult($"Hello {name}!");
        }
    }

    public static class Program
    {
        public static async Task Main(string[] args)
        {
            // Connect to Temporal

            var settings = new TemporalSettings()
            {
                DefaultNamespace = "my-namespace",
                CreateNamespace  = true,
                HostPort         = "localhost:7933"
            };

            using (var client = await TemporalClient.ConnectAsync(settings))
            {
                // Register your workflow implementation to let Temporal
                // know we're open for business.

                await client.RegisterWorkflowAsync<HelloWorkflow>();
                await client.StartWorkerAsync("my-tasks");

                #region code
                // Invoke a workflow with options:

                var stub = client.NewWorkflowStub<IHelloWorkflow>(
                    new WorkflowOptions()
                    {
                         WorkflowId             = "my-ultimate-workflow",
                         ScheduleToStartTimeout = TimeSpan.FromMinutes(5) 
                    });

                Console.WriteLine(await stub.HelloAsync("Jeff"));
                #endregion
            }
        }
    }
}