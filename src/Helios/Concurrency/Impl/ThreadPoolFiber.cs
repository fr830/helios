﻿using System;
using System.Threading.Tasks;
using Helios.Ops;
using Helios.Ops.Executors;
using Helios.Util;
using Helios.Util.Concurrency;

namespace Helios.Concurrency.Impl
{
    public class ThreadPoolFiber : IFiber
    {
        protected readonly IExecutor Executor;
        protected readonly TaskFactory TF;

        public ThreadPoolFiber(int numThreads) : this((IExecutor) new TryCatchExecutor(), (TaskFactory) TaskRunner.GetTaskFactory(numThreads)) { }

        public ThreadPoolFiber(IExecutor executor, int numThreads) : this(executor, (TaskFactory) TaskRunner.GetTaskFactory(numThreads)) { }

        public ThreadPoolFiber(IExecutor executor) : this(executor, (TaskFactory) TaskRunner.GetTaskFactory()) { }

        public ThreadPoolFiber() : this((IExecutor) new TryCatchExecutor(), (TaskFactory) TaskRunner.GetTaskFactory()) { }

        public ThreadPoolFiber(IExecutor executor, TaskFactory tf)
        {
            Executor = executor;
            TF = tf;
        }

        public bool WasDisposed { get; private set; }

        public void Add(Action op)
        {
            if (Executor.AcceptingJobs)
                TF.StartNew(op);
        }

        public void Shutdown(TimeSpan gracePeriod)
        {
            Executor.Shutdown(gracePeriod);
        }

        public void Stop()
        {
            Executor.Shutdown();
        }

        #region IDisposable members

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Dispose(bool isDisposing)
        {
            if (!WasDisposed)
            {
                if (isDisposing)
                {
                    Executor.Shutdown();
                    var disposableScheduler = TF.Scheduler as IDisposable;
                    disposableScheduler.NotNull(d => d.Dispose()); //collect the threads
                }
            }

            WasDisposed = true;
        }

        #endregion
    }
}