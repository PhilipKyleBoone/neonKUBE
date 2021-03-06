﻿//-----------------------------------------------------------------------------
// FILE:	    ChildWorkflowOptions.cs
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
using System.Reflection;

using Neon.Common;
using Neon.Temporal;
using Neon.Temporal.Internal;

namespace Neon.Temporal
{
    /// <summary>
    /// Specifies the options to use when executing a child workflow.
    /// </summary>
    public class ChildWorkflowOptions
    {
        //---------------------------------------------------------------------
        // Static members

        /// <summary>
        /// <b>INTERNAL USE ONLY:</b> Normalizes the options passed by creating or cloning a new 
        /// instance as required and filling unset properties using default client settings.
        /// </summary>
        /// <param name="client">The associated Temporal client.</param>
        /// <param name="options">The input options or <c>null</c>.</param>
        /// <param name="workflowInterface">Optionally specifies the workflow interface definition.</param>
        /// /// <param name="method">Optionally specifies the target workflow method.</param>
        /// <returns>The normalized options.</returns>
        /// <exception cref="ArgumentNullException">Thrown if a valid task queue is not specified.</exception>
        public static ChildWorkflowOptions Normalize(TemporalClient client, ChildWorkflowOptions options, Type workflowInterface = null, MethodInfo method = null)
        {
            Covenant.Requires<ArgumentNullException>(client != null, nameof(client));

            WorkflowInterfaceAttribute  interfaceAttribute = null;
            WorkflowMethodAttribute     methodAttribute    = null;

            if (options == null)
            {
                options = new ChildWorkflowOptions();
            }
            else
            {
                options = options.Clone();
            }

            if (workflowInterface != null)
            {
                TemporalHelper.ValidateWorkflowInterface(workflowInterface);

                interfaceAttribute = workflowInterface.GetCustomAttribute<WorkflowInterfaceAttribute>();
            }

            if (method != null)
            {
                methodAttribute = method.GetCustomAttribute<WorkflowMethodAttribute>();
            }

            if (string.IsNullOrEmpty(options.Namespace))
            {
                if (!string.IsNullOrEmpty(methodAttribute?.Namespace))
                {
                    options.Namespace = methodAttribute.Namespace;
                }

                if (string.IsNullOrEmpty(options.Namespace) && !string.IsNullOrEmpty(interfaceAttribute?.Namespace))
                {
                    options.Namespace = interfaceAttribute.Namespace;
                }
            }

            if (string.IsNullOrEmpty(options.TaskQueue))
            {
                if (!string.IsNullOrEmpty(methodAttribute?.TaskQueue))
                {
                    options.TaskQueue = methodAttribute.TaskQueue;
                }

                if (string.IsNullOrEmpty(options.TaskQueue) && !string.IsNullOrEmpty(interfaceAttribute?.TaskQueue))
                {
                    options.TaskQueue = interfaceAttribute.TaskQueue;
                }

                if (string.IsNullOrEmpty(options.TaskQueue))
                {
                    options.TaskQueue = client.Settings.DefaultTaskQueue;
                }
            }

            if (options.StartToCloseTimeout <= TimeSpan.Zero)
            {
                if (methodAttribute != null && methodAttribute.StartToCloseTimeoutSeconds > 0)
                {
                    options.StartToCloseTimeout = TimeSpan.FromSeconds(methodAttribute.StartToCloseTimeoutSeconds);
                }

                if (options.StartToCloseTimeout <= TimeSpan.Zero)
                {
                    options.StartToCloseTimeout = client.Settings.WorkflowStartToCloseTimeout;
                }
            }

            if (options.ScheduleToStartTimeout <= TimeSpan.Zero)
            {
                if (methodAttribute != null && methodAttribute.ScheduleToStartTimeoutSeconds > 0)
                {
                    options.ScheduleToStartTimeout = TimeSpan.FromSeconds(methodAttribute.ScheduleToStartTimeoutSeconds);
                }

                if (options.ScheduleToStartTimeout <= TimeSpan.Zero)
                {
                    options.ScheduleToStartTimeout = client.Settings.WorkflowScheduleToStartTimeout;
                }
            }

            if (options.DecisionTaskTimeout <= TimeSpan.Zero)
            {
                if (methodAttribute != null && methodAttribute.DecisionTaskTimeoutSeconds > 0)
                {
                    options.DecisionTaskTimeout = TimeSpan.FromSeconds(methodAttribute.DecisionTaskTimeoutSeconds);
                }

                if (options.DecisionTaskTimeout <= TimeSpan.Zero)
                {
                    options.DecisionTaskTimeout = client.Settings.WorkflowDecisionTaskTimeout;
                }
            }

            if (options.WorkflowIdReusePolicy == Temporal.WorkflowIdReusePolicy.UseDefault)
            {
                if (methodAttribute != null && methodAttribute.WorkflowIdReusePolicy != WorkflowIdReusePolicy.UseDefault)
                {
                    options.WorkflowIdReusePolicy = methodAttribute.WorkflowIdReusePolicy;
                }

                if (options.WorkflowIdReusePolicy == Temporal.WorkflowIdReusePolicy.UseDefault)
                {
                    options.WorkflowIdReusePolicy = client.Settings.WorkflowIdReusePolicy;
                }
            }

            return options;
        }

        //---------------------------------------------------------------------
        // Instance members

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ChildWorkflowOptions()
        {
        }

        /// <summary>
        /// Optionally specifies the target namespace.  This defaults to the namespace
        /// specified by <see cref="WorkflowMethodAttribute.Namespace"/>, 
        /// <see cref="WorkflowInterfaceAttribute.Namespace"/>, or 
        /// to the client's default namespace as specified by <see cref="TemporalSettings.Namespace"/>
        /// (in that order of precedence).
        /// </summary>
        public string Namespace { get; set; } = null;

        /// <summary>
        /// Optionally specifies the workflow ID to assign to the child workflow.
        /// A UUID will be generated by default.
        /// </summary>
        public string WorkflowId { get; set; } = null;

        /// <summary>
        /// Optionally specifies the Temporal task queue where the child workflow will be
        /// scheduled.  This defaults to the namespace specified by <see cref="WorkflowMethodAttribute.TaskQueue"/>
        /// or <see cref="WorkflowInterfaceAttribute.TaskQueue"/>, or the parent workflow's
        /// domain, in that order of precedence.
        /// </summary>
        public string TaskQueue { get; set; } = null;

        /// <summary>
        /// Specifies the maximum time the child workflow may execute from start
        /// to finish.  This defaults to <see cref="TemporalSettings.WorkflowStartToCloseTimeoutSeconds"/>.
        /// </summary>
        public TimeSpan StartToCloseTimeout { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Optionally specifies the default maximum time a workflow can wait between being scheduled
        /// and actually begin executing.  This defaults to <c>24 hours</c>.
        /// </summary>
        public TimeSpan ScheduleToStartTimeout { get; set; }

        /// <summary>
        /// Optionally specifies the decision task timeout for the child workflow.
        /// This defaults to <see cref="TemporalSettings.WorkflowDecisionTaskTimeout"/>.
        /// </summary>
        public TimeSpan DecisionTaskTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Optionally specifies what happens to the child workflow when the parent is terminated.
        /// This defaults to <see cref="ParentClosePolicy.RequestCancel"/>.
        /// </summary>
        public ParentClosePolicy ChildPolicy { get; set; } = ParentClosePolicy.RequestCancel;

        /// <summary>
        /// Optionally specifies whether to wait for the child workflow to finish for any
        /// reason including being: completed, failed, timedout, terminated, or canceled.
        /// </summary>
        public bool WaitUntilFinished { get; set; } = false;

        /// <summary>
        /// Optionally determines how Temporal handles workflows that attempt to reuse workflow IDs.
        /// This generally defaults to <see cref="WorkflowIdReusePolicy.AllowDuplicate"/>
        /// but the default can be customized via the <see cref="WorkflowMethodAttribute"/> tagging
        /// the workflow entry point method or <see cref="TemporalSettings.WorkflowIdReusePolicy"/>
        /// (which also defaults to <see cref="WorkflowIdReusePolicy.AllowDuplicate"/>.
        /// </summary>
        public WorkflowIdReusePolicy WorkflowIdReusePolicy { get; set; } = WorkflowIdReusePolicy.UseDefault;

        /// <summary>
        /// Optionally specifies retry options.
        /// </summary>
        public RetryOptions RetryOptions { get; set; } = null;

        /// <summary>
        /// Optionally specifies a recurring schedule for the workflow.  This can be set to a string specifying
        /// the minute, hour, day of month, month, and day of week scheduling parameters using the standard Linux
        /// CRON format described here: <a href="https://en.wikipedia.org/wiki/Cron">https://en.wikipedia.org/wiki/Cron</a>
        /// </summary>
        /// <remarks>
        /// <para>
        /// Temporal accepts a CRON string formatted as a single line of text with 5 parameters separated by
        /// spaces.  The parameters specified the minute, hour, day of month, month, and day of week values:
        /// </para>
        /// <code>
        /// ┌───────────── minute (0 - 59)
        /// │ ┌───────────── hour (0 - 23)
        /// │ │ ┌───────────── day of the month (1 - 31)
        /// │ │ │ ┌───────────── month (1 - 12)
        /// │ │ │ │ ┌───────────── day of the week (0 - 6) (Sunday to Saturday)
        /// │ │ │ │ │
        /// │ │ │ │ │
        /// * * * * * 
        /// </code>
        /// <para>
        /// Each parameter may be set to one of:
        /// </para>
        /// <list type="table">
        /// <item>
        ///     <term><b>*</b></term>
        ///     <description>
        ///     Matches any value.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>value</b></term>
        ///     <description>
        ///     Matches a specific integer value.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>first-last</b></term>
        ///     <description>
        ///     Matches a range of integer values (inclusive).
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>value1,value2,...</b></term>
        ///     <description>
        ///     Matches a list of integer values.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term><b>first/step</b></term>
        ///     <description>
        ///     Matches values starting at <b>first</b> and then succeeding incremented by <b>step</b>.
        ///     </description>
        /// </item>
        /// </list>
        /// <para>
        /// You can use this handy CRON calculator to see how this works: <a href="https://crontab.guru">https://crontab.guru</a>
        /// </para>
        /// </remarks>
        public string CronSchedule { get; set; }

        /// <summary>
        /// Returns a shallow clone of the current instance.
        /// </summary>
        /// <returns>The cloned <see cref="WorkflowOptions"/>.</returns>
        public ChildWorkflowOptions Clone()
        {
            return new ChildWorkflowOptions()
            {
                Namespace              = this.Namespace,
                CronSchedule           = this.CronSchedule,
                ChildPolicy            = this.ChildPolicy,
                StartToCloseTimeout    = this.StartToCloseTimeout,
                RetryOptions           = this.RetryOptions,
                ScheduleToStartTimeout = this.ScheduleToStartTimeout,
                TaskQueue              = this.TaskQueue,
                DecisionTaskTimeout    = this.DecisionTaskTimeout,
                WaitUntilFinished      = this.WaitUntilFinished,
                WorkflowId             = this.WorkflowId,
                WorkflowIdReusePolicy  = this.WorkflowIdReusePolicy
            };
        }

        /// <summary>
        /// Used internally within generated workflow stubs to convert a <see cref="ChildWorkflowOptions"/>
        /// instance into an equivalent <see cref="WorkflowOptions"/> as a bit of a hack.
        /// </summary>
        /// <returns>The converted <see cref="WorkflowOptions"/>.</returns>
        internal WorkflowOptions ToWorkflowOptions()
        {
            return new WorkflowOptions()
            {
                Namespace              = this.Namespace,
                Memo                   = null,
                RetryOptions           = this.RetryOptions,
                StartToCloseTimeout    = this.StartToCloseTimeout,
                ScheduleToStartTimeout = this.ScheduleToStartTimeout,
                TaskQueue              = this.TaskQueue,
                DecisionTaskTimeout    = this.DecisionTaskTimeout,
                WorkflowId             = this.WorkflowId,
                WorkflowIdReusePolicy  = this.WorkflowIdReusePolicy
            };
        }
    }
}
