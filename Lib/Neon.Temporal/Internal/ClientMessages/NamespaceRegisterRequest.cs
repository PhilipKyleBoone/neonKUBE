﻿//-----------------------------------------------------------------------------
// FILE:	    NamespaceRegisterRequest.cs
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

using Neon.Common;
using Neon.Temporal;

// $todo(jefflill):
//
// There are several more parameters we could specify but these
// don't seem critical at this point.

namespace Neon.Temporal.Internal
{
    /// <summary>
    /// <b>client --> proxy:</b> Requests that the proxy register a Temporal namespace.
    /// </summary>
    [InternalProxyMessage(InternalMessageTypes.NamespaceRegisterRequest)]
    internal class NamespaceRegisterRequest : ProxyRequest
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public NamespaceRegisterRequest()
        {
            Type = InternalMessageTypes.NamespaceRegisterRequest;
        }

        /// <inheritdoc/>
        public override InternalMessageTypes ReplyType => InternalMessageTypes.NamespaceRegisterReply;

        /// <summary>
        /// Name for the new namespace.
        /// </summary>
        public string Name
        {
            get => GetStringProperty(PropertyNames.Name);
            set => SetStringProperty(PropertyNames.Name, value);
        }

        /// <summary>
        /// Human readable description for the namespace.
        /// </summary>
        public string Description
        {
            get => GetStringProperty(PropertyNames.Description);
            set => SetStringProperty(PropertyNames.Description, value);
        }

        /// <summary>
        /// Owner email address.
        /// </summary>
        public string OwnerEmail
        {
            get => GetStringProperty(PropertyNames.OwnerEmail);
            set => SetStringProperty(PropertyNames.OwnerEmail, value);
        }

        /// <summary>
        /// Enable metrics.
        /// </summary>
        public bool EmitMetrics
        {
            get => GetBoolProperty(PropertyNames.EmitMetrics);
            set => SetBoolProperty(PropertyNames.EmitMetrics, value);
        }

        /// <summary>
        /// The complete workflow history retention period in days.
        /// </summary>
        public int RetentionDays
        {
            get => GetIntProperty(PropertyNames.RetentionDays);
            set => SetIntProperty(PropertyNames.RetentionDays, value);
        }

        /// <summary>
        /// Optional security token.
        /// </summary>
        public string SecurityToken
        {
            get => GetStringProperty(PropertyNames.SecurityToken);
            set => SetStringProperty(PropertyNames.SecurityToken, value);
        }

        /// <inheritdoc/>
        internal override ProxyMessage Clone()
        {
            var clone = new NamespaceRegisterRequest();

            CopyTo(clone);

            return clone;
        }

        /// <inheritdoc/>
        protected override void CopyTo(ProxyMessage target)
        {
            base.CopyTo(target);

            var typedTarget = (NamespaceRegisterRequest)target;

            typedTarget.Name          = this.Name;
            typedTarget.Description   = this.Description;
            typedTarget.OwnerEmail    = this.OwnerEmail;
            typedTarget.EmitMetrics   = this.EmitMetrics;
            typedTarget.RetentionDays = this.RetentionDays;
            typedTarget.SecurityToken = this.SecurityToken;
        }
    }
}
