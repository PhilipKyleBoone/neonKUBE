﻿//-----------------------------------------------------------------------------
// FILE:	    GeneratedClassFactory.cs
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
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

using Neon.Diagnostics;

namespace Neon.Serialization
{
    /// <summary>
    /// Used to instantiate code generated classes that implement <see cref="IGeneratedDataModel"/>
    /// as generated by the <c>Neon.CodeGen</c> assembly.
    /// </summary>
    public static class GeneratedClassFactory
    {
        private static Dictionary<string, MethodInfo>   nameToCreateMethod        = new Dictionary<string, MethodInfo>();
        private static Type[]                           createFromJObjectArgTypes = new Type[] { typeof(JObject) };
        private static Type[]                           createFromStreamArgTypes  = new Type[] { typeof(Stream) };

        /// <summary>
        /// Constructs an instance of <typeparamref name="TResult"/> from a <see cref="JObject"/>.
        /// </summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="jObject">The source <see cref="JObject"/>.</param>
        /// <returns>The new <typeparamref name="TResult"/> instance.</returns>
        public static TResult CreateFrom<TResult>(JObject jObject)
            where TResult : IGeneratedDataModel
        {
            return (TResult)CreateFrom(typeof(TResult), jObject);
        }

        /// <summary>
        /// Constructs an instance of <paramref name="resultType"/> from a <see cref="JObject"/>.
        /// </summary>
        /// <param name="resultType">The result type.</param>
        /// <param name="jObject">The source <see cref="JObject"/>.</param>
        /// <returns>The new instance as an <see cref="object"/>.</returns>
        public static object CreateFrom(Type resultType, JObject jObject)
        {
            Covenant.Requires(resultType != null);
            Covenant.Requires(jObject != null);

            MethodInfo createMethod;

            lock (nameToCreateMethod)
            {
                if (!nameToCreateMethod.TryGetValue(resultType.FullName, out createMethod))
                {
                    createMethod = resultType.GetMethod("CreateFrom", BindingFlags.Public | BindingFlags.Static, null, createFromJObjectArgTypes, null);
#if DEBUG
                    Covenant.Assert(createMethod != null, $"Cannot locate generated [{resultType.FullName}.CreateFrom(JObject)] method.");
#endif
                    nameToCreateMethod.Add(resultType.FullName, createMethod);
                }
            }

            return createMethod.Invoke(null, new object[] { jObject });
        }

        /// <summary>
        /// Constructs an instance of <typeparamref name="TResult"/> from a UTF-8 enoded <see cref="Stream"/>.
        /// </summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="stream">The source <see cref="Stream"/>.</param>
        /// <returns>The new <typeparamref name="TResult"/> instance.</returns>
        public static TResult CreateFrom<TResult>(Stream stream)
            where TResult : IGeneratedDataModel
        {
            return (TResult)CreateFrom(typeof(TResult), stream);
        }

        /// <summary>
        /// Constructs an instance of <paramref name="resultType"/> from a UTF-8 enoded <see cref="Stream"/>.
        /// </summary>
        /// <param name="resultType">The result type.</param>
        /// <param name="stream">The source <see cref="Stream"/>.</param>
        /// <returns>The new instance as an <see cref="object"/>.</returns>
        public static object CreateFrom(Type resultType, Stream stream)
        {
            Covenant.Requires(resultType != null);
            Covenant.Requires(stream != null);

            MethodInfo createMethod;

            lock (nameToCreateMethod)
            {
                if (!nameToCreateMethod.TryGetValue(resultType.FullName, out createMethod))
                {
                    createMethod = resultType.GetMethod("CreateFrom", BindingFlags.Public | BindingFlags.Static, null, createFromStreamArgTypes, null);
#if DEBUG
                    Covenant.Assert(createMethod != null, $"Cannot locate generated [{resultType.FullName}.CreateFrom(Stream)] method.");
#endif
                    nameToCreateMethod.Add(resultType.FullName, createMethod);
                }
            }

            return createMethod.Invoke(null, new object[] { stream });
        }
    }
}
