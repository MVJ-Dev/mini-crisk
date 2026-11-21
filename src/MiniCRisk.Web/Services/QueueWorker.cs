using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MiniCRisk.Web.Models;
using Newtonsoft.Json;

namespace MiniCRisk.Web.Services
{
    /// <summary>
    /// Polling consumer of the jobs queue, hosted inside the web app process.
    /// Failed messages become visible again after the visibility timeout; after
    /// JobQueueManager.MaxDequeueCount attempts they are moved to the poison queue.
    /// </summary>
    public static class QueueWorker
    {
        private static readonly TimeSpan Visibility = TimeSpan.FromSeconds(60);
        private static CancellationTokenSource _cts;
        private static Task _loop;

        public static void Start()
        {
            if (_loop != null)
            {
                return;
            }
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loop = Task.Run(() => RunAsync(token));
        }

        public static void Stop()
        {
            try
            {
                if (_cts != null)
                {
                    _cts.Cancel();
                }
                if (_loop != null)
                {
                    _loop.Wait(TimeSpan.FromSeconds(10));
                }
            }
            catch (AggregateException)
            {
                // shutting down
            }
        }

        private static async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var msg = await ServiceRegistry.Queue.DequeueAsync(Visibility).ConfigureAwait(false);
                    if (msg == null)
                    {
                        await Task.Delay(2000, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (msg.DequeueCount > JobQueueManager.MaxDequeueCount)
                    {
                        Trace.TraceError("Moving poison message {0} after {1} attempts", msg.Id, msg.DequeueCount);
                        await ServiceRegistry.Queue.MoveToPoisonAsync(msg).ConfigureAwait(false);
                        continue;
                    }

                    var job = JsonConvert.DeserializeObject<JobMessage>(msg.AsString);
                    await ServiceRegistry.Processor.ProcessAsync(job).ConfigureAwait(false);
                    await ServiceRegistry.Queue.CompleteAsync(msg).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("QueueWorker error: " + ex);
                    try
                    {
                        await Task.Delay(5000, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
    }
}
