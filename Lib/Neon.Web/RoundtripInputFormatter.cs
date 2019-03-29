﻿//-----------------------------------------------------------------------------
// FILE:	    RoundTripInputFormatter.cs
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
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

using Neon.Common;
using Neon.Serialization;
using Newtonsoft.Json.Linq;

namespace Neon.Web
{
    /// <summary>
    /// <para>
    /// Handles deserialization of JSON objects for noSQL scenarios that supports round 
    /// trips without any property loss, even if one side of the transaction is out 
    /// of data and is not aware of all of the possible JSON properties.
    /// </para>
    /// <para>
    /// This class is designed to support classes generated by the <b>Neon.CodeGen</b>
    /// assembly that implement <see cref="IGeneratedDataModel"/>.
    /// </para>
    /// </summary>
    public sealed class RoundTripInputFormatter : TextInputFormatter
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public RoundTripInputFormatter()
        {
            SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/json"));
            SupportedEncodings.Add(Encoding.UTF8);
        }

        /// <inheritdoc/>
        protected override bool CanReadType(Type type)
        {
            return type.Implements<IGeneratedDataModel>();
        }

        /// <inheritdoc/>
        public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context, Encoding encoding)
        {
            var request = context.HttpContext.Request;

            if (request.Body == null)
            {
                return await InputFormatterResult.SuccessAsync(null);
            }
            else
            {
                return await InputFormatterResult.SuccessAsync(GeneratedClassFactory.CreateFrom(context.ModelType, request.Body));
            }
        }
    }
}
