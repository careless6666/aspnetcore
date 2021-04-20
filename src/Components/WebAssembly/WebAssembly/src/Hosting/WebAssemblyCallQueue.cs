// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
        private static Queue<WorkItem> _pendingWork = new();

        public static bool HasUnstartedWork => _pendingWork.Count > 0;

        public static Task ScheduleAsync(Action callback)
        {
            if (_isCallInProgress)
            {
                var workItem = new WorkItem(callback);
                _pendingWork.Enqueue(workItem);
                return workItem.CompletionTask;
            }
            else
            {
                _isCallInProgress = true;
                try
                {
                    callback();
                    return Task.CompletedTask;
                }
                finally
                {
                    BeginQueuedWorkItems(); // Does not throw
                    _isCallInProgress = false;
                }
            }
        }

        public static Task ScheduleAsync<T>(T state, Action<T> callback)
        {
            if (_isCallInProgress)
            {
                var workItem = new WorkItem(() => callback(state));
                _pendingWork.Enqueue(workItem);
                return workItem.CompletionTask;
            }
            else
            {
                _isCallInProgress = true;
                try
                {
                    callback(state);
                    return Task.CompletedTask;
                }
                finally
                {
                    BeginQueuedWorkItems(); // Does not throw
                    _isCallInProgress = false;
                }
            }
        }

        public static Task ScheduleAsync<T>(T state, Func<T, Task> callback)
        {
            if (_isCallInProgress)
            {
                var workItem = new WorkItem(() => callback(state));
                _pendingWork.Enqueue(workItem);
                return workItem.CompletionTask;
            }
            else
            {
                _isCallInProgress = true;
                try
                {
                    return callback(state);
                }
                finally
                {
                    BeginQueuedWorkItems(); // Does not throw
                    _isCallInProgress = false;
                }
            }
        }

        private static void BeginQueuedWorkItems()
        {
            while (_pendingWork.TryDequeue(out var nextWorkItem))
            {
                nextWorkItem.Begin(); // Does not throw
            }
        }

        private readonly struct WorkItem
        {
            private readonly TaskCompletionSource _taskCompletionSource;

            public readonly Action? SyncCallback;
            public readonly Func<Task>? AsyncCallback;
            public Task CompletionTask => _taskCompletionSource.Task;

            public WorkItem(Action callback)
            {
                SyncCallback = callback;
                AsyncCallback = null;
                _taskCompletionSource = new();
            }

            public WorkItem(Func<Task> callback)
            {
                SyncCallback = null;
                AsyncCallback = callback;
                _taskCompletionSource = new();
            }

            public void Begin()
            {
                if (SyncCallback != null)
                {
                    RunSync();
                }
                else
                {
                    _ = RunAsync();
                }
            }

            private void RunSync()
            {
                try
                {
                    SyncCallback!();
                    _taskCompletionSource.SetResult();
                }
                catch (Exception ex)
                {
                    _taskCompletionSource.SetException(ex);
                }
            }

            private async Task RunAsync()
            {
                try
                {
                    await AsyncCallback!();
                    _taskCompletionSource.SetResult();
                }
                catch (Exception ex)
                {
                    _taskCompletionSource.SetException(ex);
                }
            }
        }
    }
}
