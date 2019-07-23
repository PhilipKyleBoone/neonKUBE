﻿//-----------------------------------------------------------------------------
// FILE:	    IActivityState.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Neon.Cadence;
using Neon.Cadence.Internal;
using Neon.Common;
using Neon.Diagnostics;

namespace Neon.Cadence
{
    /// <summary>
    /// Provides useful information and functionality for activity implementations.
    /// This will be available as the <see cref="IActivityBase.Activity"/> property.
    /// </summary>
    public interface IActivityState
    {
        /// <summary>
        /// Returns the <see cref="CadenceClient"/> managing this activity.
        /// </summary>
        CadenceClient Client { get; }

        /// <summary>
        /// Returns <c>true</c> for a local activity execution.
        /// </summary>
        bool IsLocal { get; }

        /// <summary>
        /// Returns the activity's cancellation token.  Activities can monitor this
        /// to gracefully handle activity cancellation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// We recommend that all non-local activities that run for relatively long periods,
        /// monitor <see cref="CancellationToken"/> for activity cancellation so that they
        /// can gracefully terminate including potentially calling <see cref="SendHeartbeatAsync(byte[])"/>
        /// to checkpoint the current activity state.
        /// </para>
        /// <para>
        /// Cancelled activities should throw a <see cref="TaskCanceledException"/> from
        /// their entry point method rather than returning a result so that Cadence will 
        /// reschedule the activity if necessary.
        /// </para>
        /// </remarks>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Returns the additional information about the activity and the workflow
        /// that invoked it.  Note that this doesn't work for local activities.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown for local activities.</exception>
        ActivityTask Task { get; }

        /// <summary>
        /// <para>
        /// Sends a heartbeat with optional details to Cadence.
        /// </para>
        /// <note>
        /// <b>IMPORTANT:</b> Heartbeats are not supported for local activities.
        /// </note>
        /// </summary>
        /// <param name="details">Optional heartbeart details.</param>
        /// <returns>The tracking <see cref="System.Threading.Tasks.Task"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown for local activity executions.</exception>
        /// <remarks>
        /// <para>
        /// Long running activities need to send periodic heartbeats back to
        /// Cadence to prove that the activity is still alive.  This can also
        /// be used by activities to implement checkpoints or record other
        /// details.  This method sends a heartbeat with optional details
        /// encoded as a byte array.
        /// </para>
        /// <note>
        /// The maximum allowed time period between heartbeats is specified in 
        /// <see cref="ActivityOptions"/> when activities are executed and it's
        /// also possible to enable automatic heartbeats sent by the Cadence client.
        /// </note>
        /// </remarks>
        Task SendHeartbeatAsync(byte[] details = null);

        /// <summary>
        /// <para>
        /// Determines whether the details from the last recorded heartbeat last
        /// failed attempt exist.
        /// </para>
        /// <note>
        /// <b>IMPORTANT:</b> Heartbeats are not supported for local activities.
        /// </note>
        /// </summary>
        /// <returns>The details from the last heartbeat or <c>null</c>.</returns>
        /// <exception cref="InvalidOperationException">Thrown for local activity executions.</exception>
        Task<bool> HasLastHeartbeatDetailsAsync();

        /// <summary>
        /// <para>
        /// Returns the details from the last recorded heartbeat last failed attempt
        /// at running the activity.
        /// </para>
        /// <note>
        /// <b>IMPORTANT:</b> Heartbeats are not supported for local activities.
        /// </note>
        /// </summary>
        /// <returns>The details from the last heartbeat or <c>null</c>.</returns>
        /// <exception cref="InvalidOperationException">Thrown for local activity executions.</exception>
        Task<byte[]> GetLastHeartbeatDetailsAsync();

        /// <summary>
        /// This method may be called within <see cref="RunAsync(byte[])"/> to indicate that the
        /// activity will be completed externally.
        /// </summary>
        /// <returns>The tracking <see cref="System.Threading.Tasks.Task"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown for local activities.</exception>
        /// <remarks>
        /// <para>
        /// This method works by throwing an <see cref="CadenceActivityExternalCompletionException"/> which
        /// will be caught and handled by the base <see cref="ActivityBase"/> class.  You'll need to allow
        /// this exception to exit your <see cref="RunAsync(byte[])"/> method for this to work.
        /// </para>
        /// <note>
        /// This method doesn't work for local activities.
        /// </note>
        /// </remarks>
        Task CompleteExternallyAsync();
    }
}