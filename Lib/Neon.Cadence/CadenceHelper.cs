﻿//-----------------------------------------------------------------------------
// FILE:	    CadenceHelper.cs
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
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Runtime.Loader;
using System.Threading.Tasks;

using Neon.Cadence;
using Neon.Cadence.Internal;
using Neon.Common;
using Neon.Diagnostics;

namespace Neon.Cadence.Internal
{
    /// <summary>
    /// Cadence helper methods and constants.
    /// </summary>
    public static class CadenceHelper
    {
        private static readonly object      syncLock = new object();
        private static readonly string      genericTaskNamePrefix;

        /// <summary>
        /// The optional separator string used to separate the base workflow type
        /// name from the optional workflow method name.  This string may not be
        /// embedded in a normal workflow type name.
        /// </summary>
        internal const string WorkflowTypeMethodSeparator = "::";

        /// <summary>
        /// The optional separator string used to separate the base activity type
        /// name from the optional activity method name.  This string may not be
        /// embedded in a normal activity type name.
        /// </summary>
        internal const string ActivityTypeMethodSeparator = "::";

        /// <summary>
        /// Number of nanoseconds per second (spoiler alert: it's 1 billion).
        /// </summary>
        internal const long NanosecondsPerSecond = 1000000000L;

        /// <summary>
        /// Returns the maximum timespan supported by Cadence.
        /// </summary>
        internal static TimeSpan MaxTimespan { get; private set; } = TimeSpan.FromTicks(long.MaxValue / 100);

        /// <summary>
        /// Returns the minimum timespan supported by Cadence.
        /// </summary>
        internal static TimeSpan MinTimespan { get; private set; } = TimeSpan.FromTicks(long.MinValue / 100);

        /// <summary>
        /// Static constructor.
        /// </summary>
        static CadenceHelper()
        {
            var fullName = typeof(Task<string>).FullName;
            var tickPos  = fullName.IndexOf('`');

            genericTaskNamePrefix = fullName.Substring(0, tickPos + 1);
        }

        /// <summary>
        /// Determines whether the type passed is a <see cref="Task"/>.
        /// </summary>
        /// <param name="type">The type being tested.</param>
        /// <returns><c>true</c> if the type is a <see cref="Task"/>.</returns>
        internal static bool IsTask(Type type)
        {
            return type == typeof(Task);
        }

        /// <summary>
        /// Determines whether the type passed is a <see cref="Task{T}"/>.
        /// </summary>
        /// <param name="type">The type being tested.</param>
        /// <returns><c>true</c> if the type is a <see cref="Task{T}"/>.</returns>
        internal static bool IsTaskT(Type type)
        {
            return type.IsGenericType && type.FullName.StartsWith(genericTaskNamePrefix);
        }

        /// <summary>
        /// Ensures that a workflow type name is valid.
        /// </summary>
        /// <param name="name">The workflow type name being checked.</param>
        /// <exception cref="WorkflowTypeException">Thrown if the name passed is not valid.</exception>
        internal static void ValidateWorkflowTypeName(string name)
        {
            if (name != null && name.Contains(CadenceHelper.WorkflowTypeMethodSeparator))
            {
                throw new WorkflowTypeException($"Workflow type names cannot include: \"{CadenceHelper.WorkflowTypeMethodSeparator}\".");
            }
        }

        /// <summary>
        /// Converts a .NET type name into a form suitable for using in generated C# source code.
        /// This handles the replacement of any embedded <b>(+)</b> characters that indicate
        /// a nested type into <b>(.)</b> characters compatible with C#. 
        /// </summary>
        /// <param name="typeName">The type name.</param>
        /// <returns>The normalized type name.</returns>
        internal static string TypeNameToSource(string typeName)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(typeName));

            return typeName.Replace('+', '.');
        }

        /// <summary>
        /// Returns the fully qualified name of the type passed, converting it into a form 
        /// suitable for using in generated C# source code. This handles the replacement of 
        /// any embedded <b>(+)</b> characters that indicate a nested type into <b>(.)</b> 
        /// characters compatible with C#. 
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The normalized fully qualified type name.</returns>
        internal static string TypeNameToSource(Type type)
        {
            Covenant.Requires<ArgumentNullException>(type != null);

            return TypeNameToSource(TypeToCSharp(type));
        }

        /// <summary>
        /// Returns the Cadence workflow type name to be used for a workflow interface or
        /// implementation class.
        /// </summary>
        /// <param name="workflowType">The workflow interface or implementation type.</param>
        /// <param name="workflowAttribute">Specifies the <see cref="WorkflowAttribute"/>.</param>
        /// <returns>The type name.</returns>
        /// <remarks>
        /// <para>
        /// If <paramref name="workflowAttribute"/> is passed and <see cref="WorkflowAttribute.Name"/>
        /// is not <c>null</c> or empty, then the name specified in the attribute is returned.
        /// </para>
        /// <para>
        /// Otherwise, we'll return the fully qualified name of the workflow interface
        /// with the leadting "I" removed.
        /// </para>
        /// </remarks>
        internal static string GetWorkflowTypeName(Type workflowType, WorkflowAttribute workflowAttribute)
        {
            Covenant.Requires<ArgumentNullException>(workflowType != null, nameof(workflowType));

            if (workflowAttribute != null && !string.IsNullOrEmpty(workflowAttribute.Name))
            {
                return workflowAttribute.Name;
            }

            if (workflowType.IsClass)
            {
                CadenceHelper.ValidateWorkflowImplementation(workflowType);

                workflowType = CadenceHelper.GetWorkflowInterface(workflowType);
            }
            else
            {
                CadenceHelper.ValidateWorkflowInterface(workflowType);
            }

            var fullName = workflowType.FullName;
            var name     = workflowType.Name;

            if (name.StartsWith("I") && name != "I")
            {
                // We're going to strip the leading "I" from the unqualified
                // type name (unless that's the only character).

                fullName  = fullName.Substring(0, fullName.Length - name.Length);
                fullName += name.Substring(1);
            }

            return TypeNameToSource(fullName);
        }

        /// <summary>
        /// Returns the Cadence activity type name to be used for a activity interface or
        /// implementation class.
        /// </summary>
        /// <param name="activityType">The activity interface or implementation type.</param>
        /// <param name="activityAttribute">Specifies the <see cref="ActivityAttribute"/>.</param>
        /// <returns>The type name.</returns>
        /// <remarks>
        /// <para>
        /// If <paramref name="activityAttribute"/> is passed and <see cref="ActivityAttribute.Name"/>
        /// is not <c>null</c> or empty, then the name specified in the attribute is returned.
        /// </para>
        /// <para>
        /// Otherwise, we'll return the fully qualified name of the activity interface
        /// with the leadting "I" removed.
        /// </para>
        /// </remarks>
        internal static string GetActivityTypeName(Type activityType, ActivityAttribute activityAttribute)
        {
            Covenant.Requires<ArgumentNullException>(activityType != null, nameof(activityType));

            if (activityAttribute != null && !string.IsNullOrEmpty(activityAttribute.Name))
            {
                return activityAttribute.Name;
            }

            if (activityType.IsClass)
            {
                CadenceHelper.ValidateActivityImplementation(activityType);

                activityType = CadenceHelper.GetActivityInterface(activityType);
            }
            else
            {
                CadenceHelper.ValidateActivityInterface(activityType);
            }

            var fullName = activityType.FullName;
            var name     = activityType.Name;

            if (name.StartsWith("I") && name != "I")
            {
                // We're going to strip the leading "I" from the unqualified
                // type name (unless that's the only character).

                fullName = fullName.Substring(0, fullName.Length - name.Length);
                fullName += name.Substring(1);
            }

            return TypeNameToSource(fullName);
        }

        /// <summary>
        /// Ensures that the type passed is a valid workflow interface.
        /// </summary>
        /// <param name="workflowInterface">The type being tested.</param>
        /// <exception cref="ActivityTypeException">Thrown when the interface is not valid.</exception>
        internal static void ValidateWorkflowInterface(Type workflowInterface)
        {
            Covenant.Requires<ArgumentNullException>(workflowInterface != null, nameof(workflowInterface));

            if (!workflowInterface.IsInterface)
            {
                throw new WorkflowTypeException($"[{workflowInterface.FullName}] is not an interface.");
            }

            if (!workflowInterface.Implements<IWorkflow>())
            {
                throw new WorkflowTypeException($"[{workflowInterface.FullName}] does not implement [{typeof(IWorkflow).FullName}].");
            }

            if (workflowInterface.IsGenericType)
            {
                throw new WorkflowTypeException($"[{workflowInterface.FullName}] has generic type parameters.  Workflow interfaces cannot be generic.");
            }

            if (!workflowInterface.IsPublic && !workflowInterface.IsNestedPublic)
            {
                throw new WorkflowTypeException($"Workflow interface [{workflowInterface.FullName}] is not public.");
            }

            if (workflowInterface.GetCustomAttribute<ActivityInterfaceAttribute>() != null)
            {
                throw new WorkflowTypeException($"Workflow interface [{workflowInterface.FullName}] cannot be tagged with [ActivityInterface] because it doesn't define an activity.");
            }

            if (workflowInterface.GetCustomAttribute<WorkflowAttribute>() != null)
            {
                throw new WorkflowTypeException($"Workflow interface [{workflowInterface.FullName}] cannot not be tagged with [Workflow] because that is valid only for activity implementation classes.");
            }

            // Validate the entrypoint method names and result types.

            var workflowNames = new HashSet<string>();

            foreach (var method in workflowInterface.GetMethods())
            {
                var workflowMethodAttribute = method.GetCustomAttribute<WorkflowMethodAttribute>();

                if (workflowMethodAttribute == null)
                {
                    continue;
                }

                if (!(CadenceHelper.IsTask(method.ReturnType) || CadenceHelper.IsTaskT(method.ReturnType)))
                {
                    throw new WorkflowTypeException($"Workflow workflow method [{workflowInterface.FullName}.{method.Name}()] must return a Task.");
                }

                var name = workflowMethodAttribute.Name ?? string.Empty;

                if (workflowNames.Contains(name))
                {
                    throw new WorkflowTypeException($"Multiple workflow methods are tagged by [WorkflowMethod(Name = \"{name}\")].");
                }

                workflowNames.Add(name);
            }

            if (workflowNames.Count == 0)
            {
                throw new ActivityTypeException($"Workflow interface [{workflowInterface.FullName}] does not define any methods tagged with [WorkflowMethod].");
            }

            // Validate the signal method names and return types.

            var signalNames = new HashSet<string>();

            foreach (var method in workflowInterface.GetMethods())
            {
                var signalMethodAttribute = method.GetCustomAttribute<SignalMethodAttribute>();

                if (signalMethodAttribute == null)
                {
                    continue;
                }

                if (signalMethodAttribute.Synchronous)
                {
                    if (!CadenceHelper.IsTask(method.ReturnType) && !CadenceHelper.IsTaskT(method.ReturnType))
                    {
                        throw new WorkflowTypeException($"Synchronous workflow signal method [{workflowInterface.FullName}.{method.Name}()] must return a [Task] or [Task<T>].");
                    }
                }
                else
                {
                    if (!CadenceHelper.IsTask(method.ReturnType))
                    {
                        throw new WorkflowTypeException($"Fire-and-forget signal method [{workflowInterface.FullName}.{method.Name}()] must return a Task.");
                    }

                    if (CadenceHelper.IsTaskT(method.ReturnType))
                    {
                        throw new WorkflowTypeException($"Fire-and-forget workflow signal method [{workflowInterface.FullName}.{method.Name}()] cannot return a result via a [Task<T>].  Use [SignalMethod(Synchronous = true)] to enable this.");
                    }
                }

                var name = signalMethodAttribute.Name ?? string.Empty;

                if (signalNames.Contains(name))
                {
                    throw new WorkflowTypeException($"Multiple signal methods are tagged by [SignalMethod(name:\"{name}\")].");
                }

                signalNames.Add(name);
            }

            // Validate the query method names and return types.

            var queryNames = new HashSet<string>();

            foreach (var method in workflowInterface.GetMethods())
            {
                var queryMethodAttribute = method.GetCustomAttribute<QueryMethodAttribute>();

                if (queryMethodAttribute == null)
                {
                    continue;
                }

                if (!(CadenceHelper.IsTask(method.ReturnType) || CadenceHelper.IsTaskT(method.ReturnType)))
                {
                    throw new WorkflowTypeException($"Workflow query method [{workflowInterface.FullName}.{method.Name}()] must return a Task.");
                }

                var name = queryMethodAttribute.Name ?? string.Empty;

                if (queryNames.Contains(name))
                {
                    throw new WorkflowTypeException($"Multiple query methods are tagged by [QueryMethod(name:\"{name}\")].");
                }

                queryNames.Add(name);
            }
        }

        /// <summary>
        /// Ensures that the type passed is a valid workflow implementation.
        /// </summary>
        /// <param name="workflowType">The type being tested.</param>
        /// <exception cref="WorkflowTypeException">Thrown when the interface is not valid.</exception>
        internal static void ValidateWorkflowImplementation(Type workflowType)
        {
            Covenant.Requires<ArgumentNullException>(workflowType != null, nameof(workflowType));

            if (workflowType.IsInterface)
            {
                throw new WorkflowTypeException($"[{workflowType.FullName}] workflow implementation cannot be an interface.");
            }

            if (workflowType.IsValueType)
            {
                throw new ActivityTypeException($"[{workflowType.FullName}] is a [struct].  Workflows must be implemented as a [class].");
            }

            if (workflowType.IsGenericType)
            {
                throw new WorkflowTypeException($"[{workflowType.FullName}] has generic type parameters.  Workflow implementations cannot be generic.");
            }

            if (workflowType.BaseType != typeof(WorkflowBase))
            {
                if (workflowType.BaseType == typeof(ActivityBase))
                {
                    throw new WorkflowTypeException($"[{workflowType.FullName}] does not inherit [{typeof(WorkflowBase).FullName}].  Did you mean to use [Activity]?");
                }
                else
                {
                    throw new WorkflowTypeException($"[{workflowType.FullName}] does not inherit [{typeof(WorkflowBase).FullName}].");
                }
            }

            if (workflowType == typeof(WorkflowBase))
            {
                throw new WorkflowTypeException($"The base [{nameof(WorkflowBase)}] class cannot be a workflow implementation.");
            }

            var workflowInterfaces = new List<Type>();

            foreach (var @interface in workflowType.GetInterfaces())
            {
                if (@interface.Implements<IWorkflow>())
                {
                    workflowInterfaces.Add(@interface);
                    ValidateWorkflowInterface(@interface);
                }
            }

            if (workflowInterfaces.Count == 0)
            {
                throw new WorkflowTypeException($"Workflow class [{workflowType.FullName}] does not implement an interface that derives from [{typeof(IWorkflow).FullName}].");
            }
            else if (workflowInterfaces.Count > 1)
            {
                throw new WorkflowTypeException($"Workflow class [{workflowType.FullName}] implements multiple workflow interfaces that derive from [{typeof(IWorkflow).FullName}].  This is not supported.");
            }

            if (workflowType.GetCustomAttribute<ActivityAttribute>() != null)
            {
                throw new WorkflowTypeException($"Workflow class [{workflowType.FullName}] cannot be tagged with [Activity] because it doesn't implement a workflow.");
            }

            if (workflowType.GetCustomAttribute<WorkflowInterfaceAttribute>() != null)
            {
                throw new WorkflowTypeException($"Workflow class [{workflowType.FullName}] cannot not be tagged with [WorkflowInterface] because that is valid only for workflow definition interfaces.");
            }
        }

        /// <summary>
        /// Returns the workflow interface for a workflow implementation class.
        /// </summary>
        /// <param name="workflowType">The workflow implementation class.</param>
        /// <returns>The workflow interface type.</returns>
        internal static Type GetWorkflowInterface(Type workflowType)
        {
            Covenant.Requires<ArgumentNullException>(workflowType != null, nameof(workflowType));
            Covenant.Requires<ArgumentException>(workflowType.IsClass, nameof(workflowType));

            foreach (var @interface in workflowType.GetInterfaces())
            {
                if (@interface.Implements<IWorkflow>())
                {
                    return @interface;
                }
            }

            throw new ArgumentException($"Workflow implementation class [{workflowType.FullName}] does not implement a workflow interface.", nameof(workflowType));
        }

        /// <summary>
        /// Ensures that an activity type name is valid.
        /// </summary>
        /// <param name="name">The activity type name being checked.</param>
        /// <exception cref="ActivityTypeException">Thrown if the name passed is not valid.</exception>
        internal static void ValidateActivityTypeName(string name)
        {
            if (name != null && name.Contains(CadenceHelper.ActivityTypeMethodSeparator))
            {
                throw new ActivityTypeException($"Activity type names cannot include: \"{CadenceHelper.ActivityTypeMethodSeparator}\".");
            }
        }

        /// <summary>
        /// Ensures that the type passed is a valid activity interface.
        /// </summary>
        /// <param name="activityInterface">The type being tested.</param>
        /// <exception cref="ActivityTypeException">Thrown when the interface is not valid.</exception>
        internal static void ValidateActivityInterface(Type activityInterface)
        {
            Covenant.Requires<ArgumentNullException>(activityInterface != null, nameof(activityInterface));

            if (!activityInterface.IsInterface)
            {
                throw new ActivityTypeException($"[{activityInterface.FullName}] is not an interface.");
            }

            if (!activityInterface.Implements<IActivity>())
            {
                throw new ActivityTypeException($"[{activityInterface.FullName}] does not implement [{typeof(IActivity).FullName}].");
            }

            if (activityInterface.IsGenericType)
            {
                throw new ActivityTypeException($"[{activityInterface.FullName}] has generic type parameters.  Activity interfaces cannot be generic.");
            }

            if (!activityInterface.IsPublic && !activityInterface.IsNestedPublic)
            {
                throw new ActivityTypeException($"Activity interface [{activityInterface.FullName}] is not public.");
            }

            if (activityInterface.GetCustomAttribute<WorkflowInterfaceAttribute>() != null)
            {
                throw new WorkflowTypeException($"Workflow interface [{activityInterface.FullName}] cannot be tagged with [WorkflowInterface] because it doesn't define a workflow.");
            }

            if (activityInterface.GetCustomAttribute<ActivityAttribute>() != null)
            {
                throw new WorkflowTypeException($"Activity interface [{activityInterface.FullName}] cannot not be tagged with [Activity] because that is valid only for activity implementation classes.");
            }

            // Validate the activity methods.

            var activityNames = new HashSet<string>();

            foreach (var method in activityInterface.GetMethods())
            {
                var activityMethodAttribute = method.GetCustomAttribute<ActivityMethodAttribute>();

                if (activityMethodAttribute == null)
                {
                    continue;
                }

                if (!(CadenceHelper.IsTask(method.ReturnType) || CadenceHelper.IsTaskT(method.ReturnType)))
                {
                    throw new WorkflowTypeException($"Activity interface method [{activityInterface.FullName}.{method.Name}()] must return a [Task].");
                }

                var name = activityMethodAttribute.Name ?? string.Empty;

                if (activityNames.Contains(name))
                {
                    throw new ActivityTypeException($"Multiple [{activityInterface.FullName}] activity methods are tagged by [ActivityMethod(Name = \"{name}\")].");
                }

                activityNames.Add(name);
            }

            if (activityNames.Count == 0)
            {
                throw new ActivityTypeException($"Activity interface [{activityInterface.FullName}] does not define any methods tagged with [ActivityMethod].");
            }
        }

        /// <summary>
        /// Ensures that the type passed is a valid activity implementation.
        /// </summary>
        /// <param name="activityType">The type being tested.</param>
        /// <exception cref="ActivityTypeException">Thrown when the interface is not valid.</exception>
        internal static void ValidateActivityImplementation(Type activityType)
        {
            Covenant.Requires<ArgumentNullException>(activityType != null, nameof(activityType));

            if (activityType.IsInterface)
            {
                throw new ActivityTypeException($"[{activityType.FullName}] implementation cannot be an interface.");
            }

            if (activityType.IsValueType)
            {
                throw new ActivityTypeException($"[{activityType.FullName}] is a [struct].  Activities must be implemented as a [class].");
            }

            if (activityType.IsGenericType)
            {
                throw new ActivityTypeException($"[{activityType.FullName}] has generic type parameters.  Activity implementations cannot be generic.");
            }

            if (activityType.BaseType != typeof(ActivityBase))
            {
                if (activityType.BaseType != typeof(ActivityBase))
                {
                    if (activityType.BaseType == typeof(WorkflowBase))
                    {
                        throw new WorkflowTypeException($"[{activityType.FullName}] does not inherit [{typeof(ActivityBase).FullName}].  Did you mean to use [Workflow]?");
                    }
                    else
                    {
                        throw new WorkflowTypeException($"[{activityType.FullName}] does not inherit [{typeof(ActivityBase).FullName}].");
                    }
                }
            }

            if (activityType == typeof(ActivityBase))
            {
                throw new ActivityTypeException($"[{nameof(ActivityBase)}] cannot be used to define an activity.");
            }

            var activityInterfaces = new List<Type>();

            foreach (var @interface in activityType.GetInterfaces())
            {
                if (@interface.Implements<IActivity>())
                {
                    ValidateActivityInterface(@interface);
                    activityInterfaces.Add(@interface);
                }
            }

            if (activityInterfaces.Count == 0)
            {
                throw new ActivityTypeException($"Activity class [{activityType.FullName}] does not implement an interface that derives from [{typeof(IActivity).FullName}].");
            }
            else if (activityInterfaces.Count > 1)
            {
                throw new ActivityTypeException($"Activity class [{activityType.FullName}] implements multiple workflow interfaces that derive from [{typeof(IActivity).FullName}].  This is not supported.");
            }

            if (activityType.GetCustomAttribute<WorkflowAttribute>() != null)
            {
                throw new WorkflowTypeException($"Activity class [{activityType.FullName}] cannot be tagged with [Workflow] because it doesn't implement a workflow.");
            }

            if (activityType.GetCustomAttribute<ActivityInterfaceAttribute>() != null)
            {
                throw new WorkflowTypeException($"Activity class [{activityType.FullName}] cannot not be tagged with [ActivityInterface] because that is valid only for activity definition interfaces.");
            }

            // Validate the methods.

            var activityNames = new HashSet<string>();

            foreach (var method in activityType.GetMethods())
            {
                var activityMethodAttribute = method.GetCustomAttribute<ActivityMethodAttribute>();

                if (activityMethodAttribute == null)
                {
                    continue;
                }

                var name = activityMethodAttribute.Name ?? string.Empty;

                if (activityNames.Contains(name))
                {
                    throw new ActivityTypeException($"Multiple [{activityType.FullName}] activity methods are tagged by [ActivityMethod(Name = \"{name}\")].");
                }

                activityNames.Add(name);
            }
        }


        /// <summary>
        /// Returns the activity interface for a activity implementation class.
        /// </summary>
        /// <param name="activityType">The activity implementation class.</param>
        /// <returns>The activity interface type.</returns>
        internal static Type GetActivityInterface(Type activityType)
        {
            Covenant.Requires<ArgumentNullException>(activityType != null, nameof(activityType));
            Covenant.Requires<ArgumentException>(activityType.IsClass, nameof(activityType));

            foreach (var @interface in activityType.GetInterfaces())
            {
                if (@interface.Implements<IActivity>())
                {
                    return @interface;
                }
            }

            throw new ArgumentException($"Workflow implementation class [{activityType.FullName}] does not implement a workflow interface.", nameof(activityType));
        }

        /// <summary>
        /// Ensures that the timespan passed doesn't exceed the minimum or maximum
        /// supported by Cadence/GOLANG.
        /// </summary>
        /// <param name="timespan">The input.</param>
        /// <returns>The adjusted output.</returns>
        internal static TimeSpan Normalize(TimeSpan timespan)
        {
            if (timespan > MaxTimespan)
            {
                return MaxTimespan;
            }
            else if (timespan < MinTimespan)
            {
                return MinTimespan;
            }
            else
            {
                return timespan;
            }
        }

        /// <summary>
        /// Converts a .NET <see cref="TimeSpan"/> into a Cadence/GOLANG duration
        /// (aka a <c>long</c> specifying the interval in nanoseconds.
        /// </summary>
        /// <param name="timespan">The input .NET timespan.</param>
        /// <returns>The duration in nanoseconds.</returns>
        internal static long ToCadence(TimeSpan timespan)
        {
            timespan = Normalize(timespan);

            return timespan.Ticks * 100;
        }

        /// <summary>
        /// Parses a Cadence timestamp string and converts it to a UTC
        /// <see cref="DateTime"/>.
        /// </summary>
        /// <param name="timestamp">The timestamp string.</param>
        /// <returns>The parsed <see cref="DateTime"/>.</returns>
        internal static DateTime ParseCadenceTimestamp(string timestamp)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(timestamp), nameof(timestamp));

            var dateTimeOffset = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture);

            return new DateTime(dateTimeOffset.ToUniversalTime().Ticks, DateTimeKind.Utc);
        }

        /// <summary>
        /// Converts UNIX nano time (UTC) to a <see cref="DateTime"/>.
        /// </summary>
        /// <param name="nanoseconds">Nano seconds from midnight 1-1-1970 (UTC)</param>
        /// <returns>The corresponding <see cref="DateTime"/>.</returns>
        internal static DateTime UnixNanoToDateTimeUtc(long nanoseconds)
        {
            var ticks = nanoseconds / 100;

            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc) + TimeSpan.FromTicks(ticks);
        }

        /// <summary>
        /// Returns the name we'll use for a type when generating type references.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>The type name.</returns>
        private static string GetTypeName(Type type)
        {
            // Convert common types into their C# equivents:

            var typeName = type.FullName;

            switch (typeName)
            {
                case "System.Byte":     return "byte";
                case "System.SByte":    return "sbyte";
                case "System.Int16":    return "short";
                case "System.UInt16":   return "ushort";
                case "System.Int32":    return "int";
                case "System.UInt32":   return "uint";
                case "System.Int64":    return "long";
                case "System.UInt64":   return "ulong";
                case "System.Float":    return "float";
                case "System.Double":   return "double";
                case "System.String":   return "string";
                case "System.Boolean":  return "bool";
                case "System.Decimal":  return "decimal";
            }

            if (type.IsGenericType)
            {
                // Strip the backtick and any text after it.

                var tickPos = typeName.IndexOf('`');

                if (tickPos != -1)
                {
                    typeName = typeName.Substring(0, tickPos);
                }
            }

            // We're going to use the global namespace to avoid namespace conflicts.

            return TypeNameToSource($"global::{typeName}");
        }

        /// <summary>
        /// Resolves the type passed into a nice string taking generic types 
        /// and arrays into account.  This is used when generating workflow
        /// and activity stubs.
        /// </summary>
        /// <param name="type">The referenced type.</param>
        /// <returns>The type reference as a string or <c>null</c> if the type is not valid.</returns>
        internal static string TypeToCSharp(Type type)
        {
            if (type == typeof(void))
            {
                return "void";
            }

            if (type.IsPrimitive || (!type.IsArray && !type.IsGenericType))
            {
                return GetTypeName(type);
            }

            if (type.IsArray)
            {
                // We need to handle jagged arrays where the element type 
                // is also an array.  We'll accomplish this by walking down
                // the element types until we get to a non-array element type,
                // counting how many subarrays there were.

                var arrayDepth  = 0;
                var elementType = type.GetElementType();

                while (elementType.IsArray)
                {
                    arrayDepth++;
                    elementType = elementType.GetElementType();
                }

                var arrayRef = TypeToCSharp(elementType);

                for (int i = 0; i <= arrayDepth; i++)
                {
                    arrayRef += "[]";
                }

                return arrayRef;
            }
            else if (type.IsGenericType)
            {
                var genericRef    = GetTypeName(type);
                var genericParams = string.Empty;

                foreach (var genericParamType in type.GetGenericArguments())
                {
                    if (genericParams.Length > 0)
                    {
                        genericParams += ", ";
                    }

                    genericParams += TypeToCSharp(genericParamType);
                }

                return $"{genericRef}<{genericParams}>";
            }

            Covenant.Assert(false); // We should never get here.            
            return null;
        }

        /// <summary>
        /// Loads the assembly from a stream into current <see cref="AssemblyLoadContext"/> or
        /// <see cref="AppDomain"/>, depending on whether we're running on .NET Core or
        /// .NET Frtamework.
        /// </summary>
        /// <param name="stream">The stream with the assembly bytes.</param>
        /// <returns>The loaded <see cref="Assembly"/>.</returns>
        internal static Assembly LoadAssembly(Stream stream)
        {
            Covenant.Requires<ArgumentNullException>(stream != null, nameof(stream));

            switch (NeonHelper.Framework)
            {
                case NetFramework.Core:

                    return LoadAssemblyNetCore(stream);

                case NetFramework.Framework:

                    return LoadAssemblyNetFramework(stream);

                default:

                    throw new NotSupportedException($"Framework [{NeonHelper.Framework}] is not supported.");
            }
        }

        /// <summary>
        /// <b>.NET CORE ONLY:</b> Loads the assembly from a stream into the current <see cref="AssemblyLoadContext"/>.
        /// </summary>
        /// <param name="stream">The stream with the assembly bytes.</param>
        /// <returns>The loaded <see cref="Assembly"/>.</returns>
        private static Assembly LoadAssemblyNetCore(Stream stream)
        {
            var orgPos = stream.Position;

            try
            {
                return AssemblyLoadContext.Default.LoadFromStream(stream);
            }
            finally
            {
                stream.Position = orgPos;
            }
        }

        /// <summary>
        /// <b>.NET FRAMEWORK ONLY:</b> Loads the assembly from a stream into the current <see cref="AppDomain"/>.
        /// </summary>
        /// <param name="stream">The stream with the assembly bytes.</param>
        /// <returns>The loaded <see cref="Assembly"/>.</returns>
        private static Assembly LoadAssemblyNetFramework(Stream stream)
        {
            var orgPos = stream.Position;

            try
            {
                return AppDomain.CurrentDomain.Load(stream.ReadToEnd());
            }
            finally
            {
                stream.Position = orgPos;
            }
        }

        /// <summary>
        /// Converts a Neon <see cref="LogLevel"/> value into a <b>cadence-proxy</b> compatible
        /// log level string.
        /// </summary>
        /// <param name="logLevel">The input log level.</param>
        /// <returns>The <b>cadence-proxy</b> compatable level string.</returns>
        internal static string ToCadenceLogLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Critical: 
                    
                    return "fatal";

                case LogLevel.Debug:    
                    
                    return "debug";

                case LogLevel.Error:    
                case LogLevel.SError:   
                    
                    return "error";

                case LogLevel.Info:
                case LogLevel.SInfo:
                default:

                    return "info";

                case LogLevel.None:     
                    
                    return "none";

                case LogLevel.Warn:

                    return "warn";
            }
        }

        /// <summary>
        /// Returns the activity type and method information for an activity interface and 
        /// an optional target method name.
        /// </summary>
        /// <param name="activityInterface">The target activity interface.</param>
        /// <param name="methodName">
        /// Optionally specifies the target method name (as specified in the <c>[ActivityMethod]</c>
        /// attribiute tagging the activity method within the interface.
        /// </param>
        /// <returns>The activity type name for the activity interface as well as the method information and attribute.</returns>
        /// <exception cref="ArgumentException">Thrown if target method does not exist.</exception>
        /// <remarks>
        /// <paramref name="methodName"/> is optional.  When this is passed as <c>null</c>
        /// or empty, the default activity method will be targeted (if any).
        /// </remarks>
        internal static (string ActivityTypeName, MethodInfo TargetMethod, ActivityMethodAttribute MethodAttribute) GetActivityTarget(Type activityInterface, string methodName = null)
        {
            Covenant.Requires<ArgumentNullException>(activityInterface != null);

            CadenceHelper.ValidateActivityInterface(activityInterface);

            var activityAttribute = activityInterface.GetCustomAttribute<ActivityAttribute>();
            var methodAttribute   = (ActivityMethodAttribute)null;
            var targetMethod      = (MethodInfo)null;

            if (string.IsNullOrEmpty(methodName))
            {
                // Look for the entrypoint method with a null or empty method name.

                foreach (var method in activityInterface.GetMethods())
                {
                    methodAttribute = method.GetCustomAttribute<ActivityMethodAttribute>();

                    if (methodAttribute != null)
                    {
                        if (string.IsNullOrEmpty(methodAttribute.Name))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }
            }
            else
            {
                // Look for the entrypoint method with the matching method name.

                foreach (var method in activityInterface.GetMethods())
                {
                    methodAttribute = method.GetCustomAttribute<ActivityMethodAttribute>();

                    if (methodAttribute != null)
                    {
                        if (methodName == methodAttribute.Name)
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }
            }

            if (targetMethod == null)
            {
                throw new ArgumentException($"Activity interface [{activityInterface.FullName}] does not have a method tagged by [ActivityMethod(Name = {methodName})].", nameof(activityInterface));
            }

            var activityTypeName = CadenceHelper.GetActivityTypeName(activityInterface, activityAttribute);

            if (!string.IsNullOrEmpty(methodAttribute.Name))
            {
                activityTypeName += $"::{methodAttribute.Name}";
            }

            return (activityTypeName, targetMethod, methodAttribute);
        }

        /// <summary>
        /// Returns the workflow type and method information for a workflow interface and 
        /// an optional target method name.
        /// </summary>
        /// <param name="workflowInterface">The target workflow interface.</param>
        /// <param name="methodName">
        /// Optionally specifies the target method name (as specified in the <c>[WorkflowMethod]</c>
        /// attribiute tagging the workflow method within the interface.
        /// </param>
        /// <returns>The workflow type name for the workflow interface as well as the method information and attribute.</returns>
        /// <exception cref="ArgumentException">Thrown if target method does not exist.</exception>
        /// <remarks>
        /// <paramref name="methodName"/> is optional.  When this is passed as <c>null</c>
        /// or empty, the default workflow method will be targeted (if any).
        /// </remarks>
        internal static (string WorkflowTypeName, MethodInfo TargetMethod, WorkflowMethodAttribute MethodAttribute) GetWorkflowTarget(Type workflowInterface, string methodName = null)
        {
            Covenant.Requires<ArgumentNullException>(workflowInterface != null);

            CadenceHelper.ValidateWorkflowInterface(workflowInterface);

            var workflowAttribute = workflowInterface.GetCustomAttribute<WorkflowAttribute>();
            var methodAttribute   = (WorkflowMethodAttribute)null;
            var targetMethod      = (MethodInfo)null;

            if (string.IsNullOrEmpty(methodName))
            {
                // Look for the entrypoint method with a null or empty method name.

                foreach (var method in workflowInterface.GetMethods())
                {
                    methodAttribute = method.GetCustomAttribute<WorkflowMethodAttribute>();

                    if (methodAttribute != null)
                    {
                        if (string.IsNullOrEmpty(methodAttribute.Name))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }
            }
            else
            {
                // Look for the entrypoint method with the matching method name.

                foreach (var method in workflowInterface.GetMethods())
                {
                    methodAttribute = method.GetCustomAttribute<WorkflowMethodAttribute>();

                    if (methodAttribute != null)
                    {
                        if (methodName == methodAttribute.Name)
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }
            }

            if (targetMethod == null)
            {
                throw new ArgumentException($"Workflow interface [{workflowInterface.FullName}] does not have a method tagged by [WorkflowMethod(Name = {methodName})].", nameof(workflowInterface));
            }

            var workflowTypeName = CadenceHelper.GetWorkflowTypeName(workflowInterface, workflowAttribute);

            if (!string.IsNullOrEmpty(methodAttribute.Name))
            {
                workflowTypeName += $"::{methodAttribute.Name}";
            }

            return (workflowTypeName, targetMethod, methodAttribute);
        }

        /// <summary>
        /// Returns the workflow type name for a workflow interface and target method.
        /// </summary>
        /// <typeparam name="TWorkflowInterface">The workflow interface.</typeparam>
        /// <param name="methodName">
        /// Optionally specifies the target method name (as specified in the <c>[WorkflowMethod]</c>
        /// attribiute tagging the workflow method within the interface.
        /// </param>
        /// <returns>The workflow type name for the workflow interface and target method.</returns>
        /// <exception cref="ArgumentException">Thrown if target method does not exist.</exception>
        /// <remarks>
        /// <paramref name="methodName"/> is optional.  When this is passed as <c>null</c>
        /// or empty, the default workflow method will be targeted (if any).
        /// </remarks>
        public static string GetWorkflowTypeName<TWorkflowInterface>(string methodName = null)
        {
            return GetWorkflowTarget(typeof(TWorkflowInterface), methodName).WorkflowTypeName;
        }

        /// <summary>
        /// <para>
        /// Used to convert an argument value being passed to a workflow or activity from
        /// its current type to the target parameter type.  For example, if an <c>int</c>
        /// argument is being passed to a <c>double</c> parameter, this method will convert
        /// the <c>int</c> to a <c>double</c> and return the <c>double</c>.
        /// </para>
        /// <para>
        /// This mimics the behavior of the C# complier which which will also perform these
        /// implicit conversions so the workflow developer won't have to do this explicitly
        /// (which would be really annoying).
        /// </para>
        /// </summary>
        /// <param name="parameterType">The parameter type we'll be casting <paramref name="arg"/> to.</param>
        /// <param name="arg">The argument value being passed.</param>
        /// <returns>The converted argument.</returns>
        internal static object ConvertArg(Type parameterType, object arg)
        {
            // Just return the original argument when no casting is required.

            if (arg == null || arg.GetType() == parameterType)
            {
                return arg;
            }

            // Perform the conversion.

            try
            {
                var argType = arg.GetType();

                return TypeDescriptor.GetConverter(argType).ConvertTo(arg, parameterType);
            }
            catch
            {
                // We're going to return the argument as-is if it can't be converted.
                // This will probably fail when we try to pass it while invoking the
                // target method later, but that will result in a nicer exception.

                return arg;
            }
        }

        /// <summary>
        /// <b>INTERNAL USE ONLY:</b> Appends a line of text to the debug log which is
        /// used internally to debug generated code like stubs.  This works only for
        /// DEBUG builds and hardcodes its output to <b>C:\Temp\cadence-debug.log</b>
        /// so this only works on Windows.
        /// </summary>
        /// <param name="line">The line of text to be written.</param>
        public static void DebugLog(string line)
        {
            const string logPath = @"C:\Temp\cadence-debug.log";

            var timestamp = DateTime.Now.ToString(NeonHelper.DateFormatTZ);

            if (!string.IsNullOrEmpty(line) && !line.StartsWith("----"))
            {
                line = timestamp + ": " + line;
            }

            lock (syncLock)
            {
                if (NeonHelper.IsWindows)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));

                    if (line == null)
                    {
                        File.AppendAllText(logPath, "\r\n");
                    }
                    else
                    {
                        File.AppendAllText(logPath, line + "\r\n");
                    }
                }
            }
        }
    }
}
