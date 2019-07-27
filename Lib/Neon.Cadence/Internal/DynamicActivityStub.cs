﻿//-----------------------------------------------------------------------------
// FILE:	    DynamicWorkflowStub.cs
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
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Runtime;
using System.Runtime.Loader;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Neon.Cadence;
using Neon.Cadence.Internal;
using Neon.Common;
using System.IO;

namespace Neon.Cadence.Internal
{
    /// <summary>
    /// Manages a dynamically generated type safe activity stub class for an activity interface.
    /// </summary>
    internal class DynamicActivityStub
    {
        private Type                activityInterface;
        private string              className;
        private Assembly            assembly;
        private Type                stubType;
        private ConstructorInfo     executeConstructor;
        private MethodInfo          toUntyped;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="activityInterface">Specifies the activity interface.</param>
        /// <param name="assembly">The assembly holding the generated stub class.</param>
        /// <param name="className">The fully qualified stub class name.</param>
        public DynamicActivityStub(Type activityInterface, Assembly assembly, string className)
        {
            this.activityInterface = activityInterface;
            this.assembly          = assembly;
            this.className         = className;

            // Fetch the stub type and reflect the required constructors and methods.

            this.stubType           = assembly.GetType(className);
            this.executeConstructor = NeonHelper.GetConstructor(stubType, typeof(CadenceClient), typeof(string), typeof(ActivityOptions), typeof(string));
            this.toUntyped          = NeonHelper.GetMethod(stubType, "ToUntyped", Type.EmptyTypes);
        }

        /// <summary>
        /// Creates a activity stub instance suitable for executing an activity.
        /// </summary>
        /// <param name="client">The associated <see cref="CadenceClient"/>.</param>
        /// <param name="workflow">The parent workflow.</param>
        /// <param name="activityTypeName">Specifies the activity type name.</param>
        /// <param name="options">Specifies the <see cref="ActivityOptions"/>.</param>
        /// <param name="domain">Specifies the target domain.</param>
        /// <returns>The activity stub as an <see cref="object"/>.</returns>
        public object Create(CadenceClient client, Workflow workflow, string activityTypeName, ActivityOptions options, string domain)
        {
            return executeConstructor.Invoke(new object[] { client, workflow, activityTypeName, options, domain });
        }

        /// <summary>
        /// Creates a new untyped <see cref="IActivityStub"/> from the dynamic stub.
        /// </summary>
        /// <returns>The new <see cref="IActivityStub"/>.</returns>
        public IActivityStub ToUntyped()
        {
            return (IActivityStub)toUntyped.Invoke(this, Type.EmptyTypes);
        }
    }
}