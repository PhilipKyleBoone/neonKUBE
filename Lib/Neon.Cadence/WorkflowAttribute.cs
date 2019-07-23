﻿//-----------------------------------------------------------------------------
// FILE:	    WorkflowAttribute.cs
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
using System.Reflection;

using Neon.Cadence;
using Neon.Common;

namespace Neon.Cadence
{
    /// <summary>
    /// Use this to tag workflow implementations that inherit from
    /// <see cref="IWorkflowBase"/> to customize the how the workflow is
    /// registered.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class WorkflowAttribute : Attribute
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="typeName">
        /// Optionally specifies the workflow type name to override the
        /// tagged type's fully qualified name as the workflow type
        /// name used to register the type with Cadence.
        /// </param>
        public WorkflowAttribute(string typeName = null)
        {
            Covenant.Requires<ArgumentException>(typeName == null || typeName.Length > 0, $"[{nameof(typeName)}] cannot be empty.");

            this.TypeName = typeName;
        }

        /// <summary>
        /// Returns the type name.  This defaults to the fully qualified name
        /// of the tagged workflow type.
        /// </summary>
        public string TypeName { get; private set; } = null;

        /// <summary>
        /// Indicates that <see cref="CadenceClient.RegisterAssemblyWorkflowsAsync(Assembly)"/> will
        /// automatically register the tagged workflow implementation for the specified assembly.
        /// This defaults to <c>false</c>
        /// </summary>
        public bool AutoRegister { get; private set; } = false;
    }
}