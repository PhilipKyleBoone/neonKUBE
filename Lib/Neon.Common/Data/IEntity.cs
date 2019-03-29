﻿//-----------------------------------------------------------------------------
// FILE:	    IEntity.cs
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
using System.ComponentModel;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Neon.Common;
using Neon.Serialization;

namespace Neon.Data
{
    /// <summary>
    /// Non generic interface describing an entity.  See <see cref="IEntity{T}"/>
    /// for more information.
    /// </summary>
    public interface IEntity
    {
        /// <summary>
        /// <para>
        /// Returns the Couchbase or other database key to be used to persist or retrieve the entity.
        /// By convention for Couchbase, this key includes the entity type plus the unique key
        /// formatted like <b>entity-type</b>::<b>unique-key</b>.  For example:
        /// </para>
        /// <code>
        /// user::122330
        /// </code>
        /// <para>
        /// This identifies the document as a <b>user</b> with unique ID as <b>122330</b>.  Document
        /// IDs are formatted like this so that we'll be able to take advantage of document filtering
        /// by type when we've enabled Couchbase cross datacenter replication.
        /// </para>
        /// </summary>
        /// <returns>The database key for the entity.</returns>
        string GetKey();

        /// <summary>
        /// Identifies the entity type.
        /// </summary>
        string __EntityType { get; set; }
    }

    /// <summary>
    /// Generic interface describing an entity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All entities must implement the <see cref="IEntity.__EntityType"/> property such that it returns
    /// the bucket unique string that identifies the entity type.  This string will be
    /// used to distinguish entity types within a Couchbase bucket.
    /// </para>
    /// <para>
    /// This interface supports the related concepts of entity <b>key</b> and <b>ref</b>.  The
    /// entity key is the string used to persist an entity instance to Couchbase.  By
    /// convention, this string is generally prefixed by the entity type and then is
    /// followed by instance specific properties, a UUID, or a singleton name.
    /// </para>
    /// <para>
    /// Entity <b>ref</b> is the value that other entities can use to reference an entity instance.
    /// This could be the same as the entity <b>key</b> but typically without the entity
    /// type prefix for brevity,
    /// </para>
    /// <para>
    /// As a convention, many <see cref="IEntity{T}"/> implementations also have a <c>static</c>
    /// <b>GetKey(...)</b> method that returns the Couchbase key for an entity based on parameters passed.
    /// </para>
    /// </remarks>
    public interface IEntity<T> : IEntity
        where T : class, IGeneratedDataModel, new()
    {
        /// <summary>
        /// Returns a deep clone of the base object with type <typeparamref name="T"/> from the entity instance.
        /// </summary>
        /// <returns>The base object instance.</returns>
        T ToBase();
    }
}