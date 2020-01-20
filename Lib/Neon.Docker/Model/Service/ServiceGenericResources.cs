﻿//-----------------------------------------------------------------------------
// FILE:	    ServiceGenericResources.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE, LLC.  All rights reserved.
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
using System.Runtime.Serialization;
using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using YamlDotNet.Serialization;

namespace Neon.Docker
{
    /// <summary>
    /// Describes user-defined resource settings.
    /// </summary>
    public class ServiceGenericResources : INormalizable
    {
        /// <summary>
        /// Named setting for a resource.
        /// </summary>
        [JsonProperty(PropertyName = "NamedResourceSpec", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.Populate)]
        [YamlMember(Alias = "NamedResourceSpec", ApplyNamingConventions = false)]
        [DefaultValue(null)]
        public ServiceNamedResourceSpec NamedResourceSpec { get; set; }

        /// <summary>
        /// Discrete setting for a resource.
        /// </summary>
        [JsonProperty(PropertyName = "DiscreteResourceSpec", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.Populate)]
        [YamlMember(Alias = "DiscreteResourceSpec", ApplyNamingConventions = false)]
        [DefaultValue(null)]
        public ServiceDiscreteResourceSpec DiscreteResourceSpec { get; set; }

        /// <inheritdoc/>
        public void Normalize()
        {
            // The presence or absence of the instance properties is
            // important, so we're not going to normalize them.
        }
    }
}
