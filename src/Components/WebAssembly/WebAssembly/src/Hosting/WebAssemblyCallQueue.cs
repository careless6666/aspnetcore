// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Components.WebAssembly.Hosting
{
    // We want the execution ordering semantics to be as similar as possible between WebAssembly
    // and Server hosting models. Since Server queues all incoming messages from JS relative to
    // each other and doesn't execute them nested inside each others' call stacks, we want
    // WebAssembly to behave as much like that as much as possible, but while retaining synchronous
    // execution as much as possible.
    //
    // We could use the whole SynchronizationContext infrastructure, but that would be expensive on
    // payload size and harder to guarantee synchronous execution when possible. The following
    // simplified work queue sufficies, at least until we have true multithreading.
    //
    // Framework code should dispatch incoming async JS->.NET calls via this work queue. Application
    // developers don't need to, because we do it for them.

    internal static class WebAssemblyCallQueue
    {
        private static bool _isCallInProgress;
        private static Queue<Action> _pendingWork = new();

        public static bool HasUnstartedWork => _pendingWork.Count > 0;

        /// <summary>
        /// Runs the supplied callback when possible. If the call queue is empty, the callback is executed
        /// synchronously. If some call is already executing within the queue, the callback is added to the
        /// back of the queue and will be executed in turn.
        /// </summary>
        /// <typeparam name="T">The type of a state parameter for the callback</typeparam>
        /// <param name="state">A state parameter for the callback. If the callback is able to execute synchronously, this allows us to avoid any allocations for the closure.</param>
        /// <param name="callback">The callback to run.</param>
        /// <remarks>
        /// In most cases this should only be used for callbacks that will not throw, because
        /// [1] Unhandled exceptions will be fatal to the application, as the work queue will no longer process
        ///     further items (just like unhandled hub exceptions in Blazor Server)
        /// [2] The exception will be thrown at the point of the top-level caller, which is not necessarily the
        ///     code that scheduled the callback, so you may not be able to observe it.
        ///
        /// We could change this to return a Task and do the necessary try/catch things to direct exceptions back
        /// to the code that scheduled the callback, but it's not required for current use cases and would require
        /// at least an extra allocation and layer of try/catch per call, plus more work to schedule continuations
        /// call site.
        /// </remarks>
        public static void Schedule<T>(T state, Action<T> callback)
        {
            if (_isCallInProgress)
            {
                _pendingWork.Enqueue(() => callback(state));
            }
            else
            {
                _isCallInProgress = true;
                callback(state);

                // Now run any queued work items
                while (_pendingWork.TryDequeue(out var nextWorkItem))
                {
                    nextWorkItem();
                }

                _isCallInProgress = false;
            }
        }
    }
}
