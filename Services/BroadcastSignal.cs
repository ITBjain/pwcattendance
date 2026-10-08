using System;
using System.Threading;
using System.Threading.Tasks;

namespace PwcApi.Services
{
    /// <summary>
    /// Wakes the WhatsApp worker the moment a coach queues an update, so the worker doesn't have to
    /// poll the database every few seconds. (It still checks every 60 s as a safety net.)
    /// </summary>
    public sealed class BroadcastSignal
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0, 1);

        public void Notify()
        {
            try
            {
                if (_semaphore.CurrentCount == 0) _semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // already signalled
            }
        }

        public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _semaphore.WaitAsync(timeout, ct);
    }
}
